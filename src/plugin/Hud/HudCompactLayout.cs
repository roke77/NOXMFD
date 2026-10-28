using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Compact HUD: pulls the native nose-following HUD readouts into an F-16-style cluster
    // (HudCompactLayoutMath holds the tuned preset) and draws the pitch ladder at true scale. The
    // readouts are prefab children of FlightHud's HUDCenter and of the aircraft's HUDAppManager, and
    // no game code writes their positions or scales, so values set here stick. Mission-scoped like
    // HudDeclutter; nothing outlives the HUD.
    //
    // Original transforms are captured once per HUDAppManager instance. The compass belongs to
    // FlightHud and survives an aircraft respawn, so everything is restored before a re-capture —
    // otherwise the moved values would be captured as the originals.
    internal class HudCompactLayout : MonoBehaviour
    {
        private static ConfigEntry<bool>? _enabled;
        private static bool Enabled => _enabled?.Value ?? false;

        // The one F1-menu checkbox; read every Sync, so a toggle applies or restores within 0.5 s.
        public static void Bind(ConfigFile config) =>
            _enabled = config.Bind("HUD", "Compact HUD", false,
                "Pull the nose-following HUD readouts into a compact F-16-style cluster around the pitch ladder, and draw the ladder at true scale.");

        private const float Interval = 0.5f;
        private float _timer = Interval;

        private sealed class Moved
        {
            internal Transform T = null!;
            internal Vector3 Orig;          // localPosition in its parent
            internal Vector3 OrigCenter;    // position in HUDCenter space
            internal Vector3 OrigScale;
        }

        private sealed class BlockState
        {
            internal HudCompactLayoutMath.Block Def;
            internal Vector3 RefOrig;
            internal readonly List<Moved> Members = new List<Moved>();
        }

        private readonly List<BlockState> _blocks = new List<BlockState>();
        // FlightHud's pitchCompass: a RawImage window onto the ladder texture, whose uvRect and
        // localScale FlightHud rewrites every frame.
        private RawImage? _ladder;
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
            catch (Exception ex) { Fail(ex); }
        }

        private void Fail(Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.Log?.LogWarning($"[NOXMFD] Compact HUD layout failed; restoring the native layout: {ex}");
            }
            Restore();
            enabled = false;
        }

        private void OnDestroy() => Restore();

        private void Sync()
        {
            HUDAppManager apps = SceneSingleton<HUDAppManager>.i;
            if (!Enabled || apps == null)
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
                    if (t != null) block.Members.Add(Snapshot(t, center));
                }
                _blocks.Add(block);
            }

            _ladder = center.Find("pitchCompassCenter/pitchCompass")?.GetComponent<RawImage>();
        }

        private static Moved Snapshot(Transform t, Transform center) => new Moved
        {
            T = t,
            Orig = t.localPosition,
            OrigCenter = center.InverseTransformPoint(t.position),
            OrigScale = t.localScale,
        };

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
                float size = b.Def.Size;
                var target = new Vector3(
                    HudCompactLayoutMath.DegToUnits(b.Def.XDeg, _canvasHeight, fov),
                    HudCompactLayoutMath.DegToUnits(b.Def.YDeg, _canvasHeight, fov), 0f);
                var refOrig = new Vector3(b.RefOrig.x, b.RefOrig.y, 0f);
                // Each member keeps its prefab offset from the block's anchor, scaled with the block,
                // so the anchor lands on target at any size. Offsets are taken in HUDCenter space and
                // added to the member's own localPosition, so a member under an offset parent (Mach
                // sits under a machIndicator 25 units down) lands correctly.
                // ponytail: assumes every parent between a member and HUDCenter is unrotated and
                // unscaled, true for the preset; a member under a rotating pivot would need the
                // offset transformed into its parent's space.
                foreach (Moved m in b.Members)
                {
                    if (m.T == null) continue;
                    Vector3 desired = target + (m.OrigCenter - refOrig) * size;
                    m.T.localPosition = m.Orig + (desired - m.OrigCenter);
                    m.T.localScale = m.OrigScale * size;
                }
            }
        }

        // Replaces FlightHud's 50/fov ladder scale, written each frame in its Update, with the true
        // conformal one, so the 10° rung sits 10° above the horizon at any FOV. No restore needed:
        // FlightHud rewrites the scale on its next frame.
        private void LateUpdate()
        {
            if (_ladder == null || _blocks.Count == 0) return;
            try
            {
                CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
                if (cam == null || cam.mainCamera == null) return;
                _ladder.transform.localScale = Vector3.one * HudCompactLayoutMath.TrueLadderScale(
                    _ladder.uvRect.height, _ladder.rectTransform.sizeDelta.y, _canvasHeight, cam.mainCamera.fieldOfView);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void Restore()
        {
            foreach (BlockState b in _blocks)
                foreach (Moved m in b.Members)
                    if (m.T != null)
                    {
                        m.T.localPosition = m.Orig;
                        m.T.localScale = m.OrigScale;
                    }
            _blocks.Clear();
            _ladder = null;
            _apps = null;
            _appliedFov = -1f;
        }
    }
}
