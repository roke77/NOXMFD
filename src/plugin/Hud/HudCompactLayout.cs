using System;
using System.Collections.Generic;
using UnityEngine;

namespace NOXMFD
{
    // Pulls the native nose-following HUD readouts into an F-16-style cluster (HudCompactLayoutMath
    // holds the preset). The readouts are prefab children of FlightHud's HUDCenter and of the
    // aircraft's HUDAppManager, and no game code writes their positions, so a localPosition set here
    // sticks. Mission-scoped like HudDeclutter; nothing outlives the HUD.
    //
    // Original positions are captured once per HUDAppManager instance. The compass belongs to
    // FlightHud and survives an aircraft respawn, so everything is restored before a re-capture —
    // otherwise the moved position would be captured as the original.
    internal class HudCompactLayout : MonoBehaviour
    {
        private const float Interval = 0.5f;
        private float _timer = Interval;

        private sealed class Moved
        {
            internal Transform T = null!;
            internal Vector3 Orig;
        }

        private sealed class BlockState
        {
            internal HudCompactLayoutMath.Block Def;
            internal Vector3 RefOrig;
            internal readonly List<Moved> Members = new List<Moved>();
        }

        private readonly List<BlockState> _blocks = new List<BlockState>();
        private HUDAppManager? _apps;
        private float _canvasHeight;
        private float _appliedFov = -1f;
        private bool _loggedFailure;

        private void Update()
        {
            try
            {
                _timer += Time.unscaledDeltaTime;
                if (_timer >= Interval)
                {
                    _timer = 0f;
                    Sync();
                }
                // Per frame so zooming (an FOV change) doesn't leave the blocks lagging the ladder.
                if (_blocks.Count > 0) Apply();
            }
            catch (Exception ex)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Plugin.Log?.LogWarning($"[NOXMFD] Compact HUD layout failed; restoring the native layout: {ex}");
                }
                Restore();
                enabled = false;
            }
        }

        private void OnDestroy() => Restore();

        private void Sync()
        {
            HUDAppManager apps = SceneSingleton<HUDAppManager>.i;
            if (!HudDeclutterConfig.CompactLayout || apps == null)
            {
                Restore();
                return;
            }
            if (apps == _apps && _blocks.Count > 0) return;
            Restore();
            Capture(apps);
        }

        private void Capture(HUDAppManager apps)
        {
            FlightHud hud = SceneSingleton<FlightHud>.i;
            if (hud == null) return;
            Transform center = hud.GetHUDCenter();
            Canvas canvas = center.GetComponentInParent<Canvas>().rootCanvas;
            _canvasHeight = ((RectTransform)canvas.transform).rect.height;
            _apps = apps;
            _appliedFov = -1f;

            foreach (HudCompactLayoutMath.Block def in HudCompactLayoutMath.F16Preset)
            {
                Transform? reference = Resolve(center, apps.transform, def.Reference);
                if (reference == null) continue;
                var block = new BlockState { Def = def, RefOrig = center.InverseTransformPoint(reference.position) };
                foreach (string path in def.Members)
                {
                    Transform? t = Resolve(center, apps.transform, path);
                    if (t != null) block.Members.Add(new Moved { T = t, Orig = t.localPosition });
                }
                _blocks.Add(block);
            }
        }

        private static Transform? Resolve(Transform center, Transform apps, string path) =>
            center.Find(path) ?? apps.Find(path);

        private void Apply()
        {
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null || cam.mainCamera == null) return;
            float fov = cam.mainCamera.fieldOfView;
            if (Mathf.Abs(fov - _appliedFov) < 0.01f) return;
            _appliedFov = fov;

            foreach (BlockState b in _blocks)
            {
                var target = new Vector3(
                    HudCompactLayoutMath.DegToUnits(b.Def.XDeg, _canvasHeight, fov),
                    HudCompactLayoutMath.DegToUnits(b.Def.YDeg, _canvasHeight, fov), 0f);
                // ponytail: assumes each member's parent is unrotated and unscaled relative to
                // HUDCenter, true for every member in the preset; a member under a rotating pivot
                // would need the delta transformed into its parent's space.
                Vector3 delta = target - new Vector3(b.RefOrig.x, b.RefOrig.y, 0f);
                foreach (Moved m in b.Members)
                    if (m.T != null) m.T.localPosition = m.Orig + delta;
            }
        }

        private void Restore()
        {
            foreach (BlockState b in _blocks)
                foreach (Moved m in b.Members)
                    if (m.T != null) m.T.localPosition = m.Orig;
            _blocks.Clear();
            _apps = null;
            _appliedFov = -1f;
        }
    }
}
