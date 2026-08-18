using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOXMFD
{
    // Soft dependency on MissileCameraRemoteControl's public Bridge (Bridge/McRcBridge.cs there),
    // if that mod is installed. Located purely via reflection at runtime — NOXMFD does not
    // reference RC's assembly, compiles and runs identically whether or not RC is present, exactly
    // like MissileCameraFsAccess does on the RC side for MissileCamera itself.
    //
    // Resolved once into cached delegates (Delegate.CreateDelegate — a direct call afterward, not
    // a per-invocation Invoke), retried on a cooldown since BepInEx plugin load order between two
    // separate mods isn't guaranteed. Every member below no-ops / returns a safe default until
    // resolved, so callers (RcFeed, TelemetryReader, CommandDispatcher) never need to check
    // Available themselves first.
    internal static class RcBridge
    {
        // Bump if McRcBridge's ApiVersion moves past what this file's Bind* calls assume.
        private const int MinApiVersion = 1;

        private static bool  _resolved;
        private static float _nextAttempt;

        private static Func<bool>?    _isFullscreenActive;
        private static Func<bool>?    _isControlling;
        private static Func<Camera>?  _feedCamera;
        private static Func<string>?  _controlledMissileName;
        private static Func<float>?   _throttle01;
        private static Func<bool>?    _boostActive;
        private static Func<string>?  _linkQuality;
        private static Func<Vector2>? _reticleViewport;
        private static Func<bool>?    _formationFollowActive;
        private static Func<IReadOnlyList<string>>? _controllablePool;

        private static Action?             _refreshPool;
        private static Func<bool>?         _takeNearest;
        private static Func<int, bool>?    _takeAt;
        private static Action?             _release;
        private static Action<float, float>? _injectAimDelta;
        private static Action<float>?      _setThrottle01;
        private static Action<float>?      _adjustThrottle;
        private static Action<bool>?       _setBoostHeld;
        private static Action?             _toggleFormationFollow;
        private static Func<bool>?         _manualDetonate;

        internal static bool Available => EnsureResolved();

        // ── State ──────────────────────────────────────────────────────────────
        internal static bool    IsFullscreenActive     => Available && _isFullscreenActive!();
        internal static bool    IsControlling           => Available && _isControlling!();
        internal static Camera? FeedCamera               => Available ? _feedCamera!() : null;
        internal static string  ControlledMissileName    => Available ? _controlledMissileName!() : string.Empty;
        internal static float   Throttle01                => Available ? _throttle01!() : 0f;
        internal static bool    BoostActive               => Available && _boostActive!();
        internal static string  LinkQuality                => Available ? _linkQuality!() : string.Empty;
        internal static Vector2 ReticleViewport             => Available ? _reticleViewport!() : new Vector2(0.5f, 0.5f);
        internal static bool    FormationFollowActive        => Available && _formationFollowActive!();
        internal static IReadOnlyList<string> ControllablePool =>
            Available ? _controllablePool!() : Array.Empty<string>();

        // ── Commands (all safe no-ops when unavailable) ──────────────────────────
        internal static void RefreshPool()                        { if (Available) _refreshPool!(); }
        internal static bool TakeNearest()                        => Available && _takeNearest!();
        internal static bool TakeAt(int index)                    => Available && _takeAt!(index);
        internal static void Release()                            { if (Available) _release!(); }
        internal static void InjectAimDelta(float yawDeg, float pitchDeg)
                                                                    { if (Available) _injectAimDelta!(yawDeg, pitchDeg); }
        internal static void SetThrottle01(float value01)         { if (Available) _setThrottle01!(value01); }
        internal static void AdjustThrottle(float delta)          { if (Available) _adjustThrottle!(delta); }
        internal static void SetBoostHeld(bool held)              { if (Available) _setBoostHeld!(held); }
        internal static void ToggleFormationFollow()              { if (Available) _toggleFormationFollow!(); }
        internal static bool ManualDetonate()                     => Available && _manualDetonate!();

        private static bool EnsureResolved()
        {
            if (_resolved) return true;
            if (Time.unscaledTime < _nextAttempt) return false;
            _nextAttempt = Time.unscaledTime + 2f;   // RC may load after us — keep retrying, cheaply

            try
            {
                Assembly? asm = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == "MissileCameraRemoteControl") { asm = a; break; }
                }
                if (asm == null) return false;   // RC not installed — normal, not an error

                Type? t = asm.GetType("MissileCameraRemoteControl.Bridge.McRcBridge");
                if (t == null)
                {
                    Plugin.Log?.LogWarning("[NOXMFD] MissileCameraRemoteControl found but has no Bridge — too old? RC page disabled.");
                    return false;
                }

                FieldInfo? verField = t.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                int ver = verField != null ? (int)verField.GetValue(null) : 0;
                if (ver < MinApiVersion)
                {
                    Plugin.Log?.LogWarning($"[NOXMFD] MissileCameraRemoteControl Bridge ApiVersion {ver} < {MinApiVersion} — RC page disabled.");
                    return false;
                }

                _isFullscreenActive    = BindGet<bool>(t, "IsFullscreenActive");
                _isControlling         = BindGet<bool>(t, "IsControlling");
                _feedCamera            = BindGet<Camera>(t, "FeedCamera");
                _controlledMissileName = BindGet<string>(t, "ControlledMissileName");
                _throttle01            = BindGet<float>(t, "Throttle01");
                _boostActive           = BindGet<bool>(t, "BoostActive");
                _linkQuality           = BindGet<string>(t, "LinkQuality");
                _reticleViewport       = BindGet<Vector2>(t, "ReticleViewport");
                _formationFollowActive = BindGet<bool>(t, "FormationFollowActive");
                _controllablePool      = BindGet<IReadOnlyList<string>>(t, "ControllablePool");

                _refreshPool           = BindAction(t, "RefreshPool");
                _takeNearest           = BindFunc<bool>(t, "TakeNearest");
                _takeAt                = BindFuncArg<int, bool>(t, "TakeAt");
                _release               = BindAction(t, "Release");
                _injectAimDelta        = BindAction2<float, float>(t, "InjectAimDelta");
                _setThrottle01         = BindAction1<float>(t, "SetThrottle01");
                _adjustThrottle        = BindAction1<float>(t, "AdjustThrottle");
                _setBoostHeld          = BindAction1<bool>(t, "SetBoostHeld");
                _toggleFormationFollow = BindAction(t, "ToggleFormationFollow");
                _manualDetonate        = BindFunc<bool>(t, "ManualDetonate");

                // Sanity: bail (retry later) if reflection didn't find everything we need — a half
                // -bound bridge is worse than none, since Available would gate real calls but a
                // null delegate would still throw NullReferenceException at the call site.
                _resolved = _isFullscreenActive != null && _isControlling != null && _feedCamera != null
                    && _controlledMissileName != null && _throttle01 != null && _boostActive != null
                    && _linkQuality != null && _reticleViewport != null && _formationFollowActive != null
                    && _controllablePool != null && _refreshPool != null && _takeNearest != null
                    && _takeAt != null && _release != null && _injectAimDelta != null
                    && _setThrottle01 != null && _adjustThrottle != null && _setBoostHeld != null
                    && _toggleFormationFollow != null && _manualDetonate != null;

                if (_resolved)
                    Plugin.Log?.LogInfo("[NOXMFD] MissileCameraRemoteControl Bridge found (v" + ver + ") — RC page enabled.");
                else
                    Plugin.Log?.LogWarning("[NOXMFD] MissileCameraRemoteControl Bridge shape mismatch — RC page disabled.");
                return _resolved;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] RC bridge resolve failed: {ex.Message}");
                return false;
            }
        }

        // ── Delegate binding helpers ──────────────────────────────────────────────
        private static Func<TRet>? BindGet<TRet>(Type t, string propName)
        {
            MethodInfo? get = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
            return get == null ? null : (Func<TRet>)Delegate.CreateDelegate(typeof(Func<TRet>), get);
        }

        private static Action? BindAction(Type t, string methodName)
        {
            MethodInfo? m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            return m == null ? null : (Action)Delegate.CreateDelegate(typeof(Action), m);
        }

        private static Action<T1>? BindAction1<T1>(Type t, string methodName)
        {
            MethodInfo? m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(T1) }, null);
            return m == null ? null : (Action<T1>)Delegate.CreateDelegate(typeof(Action<T1>), m);
        }

        private static Action<T1, T2>? BindAction2<T1, T2>(Type t, string methodName)
        {
            MethodInfo? m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(T1), typeof(T2) }, null);
            return m == null ? null : (Action<T1, T2>)Delegate.CreateDelegate(typeof(Action<T1, T2>), m);
        }

        private static Func<TRet>? BindFunc<TRet>(Type t, string methodName)
        {
            MethodInfo? m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            return m == null ? null : (Func<TRet>)Delegate.CreateDelegate(typeof(Func<TRet>), m);
        }

        private static Func<T1, TRet>? BindFuncArg<T1, TRet>(Type t, string methodName)
        {
            MethodInfo? m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(T1) }, null);
            return m == null ? null : (Func<T1, TRet>)Delegate.CreateDelegate(typeof(Func<T1, TRet>), m);
        }
    }
}
