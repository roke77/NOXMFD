using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOXMFD
{
    // Per-engine and per-tank state for the ENG page (docs/eng-page.md). Main thread only: called
    // from TelemetryReader.PushSnapshot.
    //
    // Engine status comes from the same IEngine.OnEngineDamage / OnEngineDisable events the game's
    // cockpit EngineTelemetry widget listens to — no engine type exposes a public "is damaged" flag.
    // Every engine seen on the current aircraft stays listed for its lifetime: one that drops out
    // of Aircraft.engineStates or is destroyed (blown off) keeps its card, reading inoperable.
    internal sealed class PropulsionReader
    {
        internal readonly struct Frame
        {
            public readonly EngineInfo[] Engines;
            public readonly TankInfo[] Tanks;
            public readonly float FuelKg, FuelCapKg, BurnKgS, GroundSpeed;

            public Frame(EngineInfo[] engines, TankInfo[] tanks, float fuelKg, float fuelCapKg, float burnKgS, float groundSpeed)
            {
                Engines = engines; Tanks = tanks;
                FuelKg = fuelKg; FuelCapKg = fuelCapKg; BurnKgS = burnKgS; GroundSpeed = groundSpeed;
            }
        }

        private sealed class TrackedEngine
        {
            public IEngine Engine = null!;
            public float X;
            public byte Status;
            public Action OnDamage = null!;
            public Action OnDisable = null!;
        }

        private Aircraft? _aircraft;
        private readonly List<TrackedEngine> _engines = new List<TrackedEngine>(4);
        private readonly FuelBurnEstimator _burn = new FuelBurnEstimator();

        // Engine health is each type's private `condition` (0..1) — Turbojet, Turbofan,
        // TurbineEngine, PropFan and RotorShaft have it; DuctedFan and ConstantSpeedProp do not
        // (null entry, reported as -1).
        private static readonly Dictionary<Type, FieldInfo?> _conditionFields = new Dictionary<Type, FieldInfo?>();
        private static readonly FieldInfo? _tankLeakRateField =
            typeof(FuelTank).GetField("leakRate", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? _tankOnFireField =
            typeof(FuelTank).GetField("onFire", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool _tankFlagsWarned, _conditionWarned;

        public Frame Read(Aircraft ac)
        {
            if (!ReferenceEquals(ac, _aircraft))
            {
                Unsubscribe();
                _aircraft = ac;
                _burn.Reset();
            }
            TrackNewEngines(ac);

            var engines = new EngineInfo[_engines.Count];
            for (int i = 0; i < _engines.Count; i++) engines[i] = ReadEngine(_engines[i]);

            TankInfo[] tanks = ReadTanks(ac, out float fuelKg, out float capKg);
            _burn.Sample(fuelKg, Time.time);

            Vector3 v = ac.rb != null ? ac.rb.velocity : Vector3.zero;
            float gs = new Vector2(v.x, v.z).magnitude;
            return new Frame(engines, tanks, fuelKg, capKg, _burn.RateKgPerSec, Finite(gs));
        }

        // Engines register themselves into engineStates on Awake/OnEnable, so the list can still be
        // growing during the first frames after spawn; anything not tracked yet gets subscribed.
        private void TrackNewEngines(Aircraft ac)
        {
            List<IEngine> states = ac.engineStates;
            if (states == null) return;
            for (int i = 0; i < states.Count; i++)
            {
                IEngine e = states[i];
                if (e == null || IsDestroyed(e) || IsTracked(e)) continue;

                var t = new TrackedEngine { Engine = e, X = LateralOffset(ac, e.transform) };
                float cond = ReadCondition(e);
                if (cond >= 0f && cond < 1f) t.Status = cond <= 0f ? EngineStatus.Inoperable : EngineStatus.Fault;
                t.OnDamage  = () => { if (t.Status < EngineStatus.Fault) t.Status = EngineStatus.Fault; };
                t.OnDisable = () => t.Status = EngineStatus.Inoperable;
                e.OnEngineDamage  += t.OnDamage;
                e.OnEngineDisable += t.OnDisable;
                _engines.Add(t);
            }
            // Left-to-right, so the page can split the cards around its centre column.
            _engines.Sort((a, b) => a.X.CompareTo(b.X));
        }

        private bool IsTracked(IEngine e)
        {
            for (int i = 0; i < _engines.Count; i++)
                if (ReferenceEquals(_engines[i].Engine, e)) return true;
            return false;
        }

        private void Unsubscribe()
        {
            foreach (TrackedEngine t in _engines)
            {
                // C# events live on the managed object, which outlives a destroyed Unity component.
                t.Engine.OnEngineDamage  -= t.OnDamage;
                t.Engine.OnEngineDisable -= t.OnDisable;
            }
            _engines.Clear();
        }

        private EngineInfo ReadEngine(TrackedEngine t)
        {
            var info = new EngineInfo { X = t.X, Power = -1f, MaxPower = -1f, Condition = -1f, Status = t.Status };
            IEngine e = t.Engine;
            if (IsDestroyed(e) || _aircraft == null || !_aircraft.engineStates.Contains(e))
            {
                info.Status = EngineStatus.Inoperable;
                info.Condition = 0f;
                return info;
            }
            info.Rpm       = Mathf.Clamp01(Finite(e.GetRPMRatio()));
            info.Thrust    = Mathf.Max(0f, Finite(e.GetThrust()));
            info.MaxThrust = Mathf.Max(0f, Finite(e.GetMaxThrust()));
            if (e is IPowerSource ps)
            {
                info.Power    = Mathf.Max(0f, Finite(ps.GetPower()));
                info.MaxPower = Mathf.Max(0f, Finite(ps.GetMaxPower()));
            }
            info.Condition = ReadCondition(e);
            return info;
        }

        private static float ReadCondition(IEngine e)
        {
            Type type = e.GetType();
            if (!_conditionFields.TryGetValue(type, out FieldInfo? field))
            {
                field = type.GetField("condition", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null && field.FieldType != typeof(float)) field = null;
                _conditionFields[type] = field;
            }
            if (field == null) return -1f;
            try { return Mathf.Clamp01(Finite((float)field.GetValue(e))); }
            catch (Exception ex)
            {
                if (!_conditionWarned)
                {
                    _conditionWarned = true;
                    Plugin.Log?.LogWarning($"[NOXMFD] Reading {type.Name}.condition failed; ENG shows engine health as unknown: {ex.Message}");
                }
                _conditionFields[type] = null;
                return -1f;
            }
        }

        // Fresh array per tick (the snapshot is serialized off-thread; see TelemetryReader.BuildParts).
        private static TankInfo[] ReadTanks(Aircraft ac, out float fuelKg, out float capKg)
        {
            fuelKg = 0f;
            capKg = 0f;
            List<FuelTank> list = ac.GetFuelTanks();
            if (list == null || list.Count == 0) return Array.Empty<TankInfo>();

            var buf = new List<TankInfo>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                FuelTank tank = list[i];
                if (tank == null) continue;
                var info = new TankInfo
                {
                    Name  = tank.gameObject.name,
                    X     = LateralOffset(ac, tank.transform),
                    Kg    = Mathf.Max(0f, Finite(tank.GetLevel())),
                    CapKg = Mathf.Max(0f, Finite(tank.GetCapacity())),
                };
                ReadTankFlags(tank, ref info);
                fuelKg += info.Kg;
                capKg  += info.CapKg;
                buf.Add(info);
            }
            buf.Sort((a, b) => a.X.CompareTo(b.X));
            return buf.ToArray();
        }

        private static void ReadTankFlags(FuelTank tank, ref TankInfo info)
        {
            if (_tankLeakRateField == null || _tankOnFireField == null)
            {
                if (!_tankFlagsWarned)
                {
                    _tankFlagsWarned = true;
                    Plugin.Log?.LogWarning("[NOXMFD] FuelTank.leakRate/onFire not found; ENG won't flag leaking or burning tanks.");
                }
                return;
            }
            info.Leak = (float)_tankLeakRateField.GetValue(tank) > 0f;
            info.Fire = (bool)_tankOnFireField.GetValue(tank);
        }

        // Aircraft-local x of a part (+ = right of the centreline). Both positions are in the same
        // floating-origin frame, so the offset is unaffected by world re-centering.
        private static float LateralOffset(Aircraft ac, Transform part) =>
            Finite(ac.transform.InverseTransformPoint(part.position).x);

        private static bool IsDestroyed(IEngine e) => e is UnityEngine.Object o && o == null;

        private static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
    }
}
