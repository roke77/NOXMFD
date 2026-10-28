using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Pulls the native nose-following HUD readouts into an F-16-style cluster (HudCompactLayoutMath
    // holds the preset, HudCompactConfig the sliders). The readouts are prefab children of FlightHud's
    // HUDCenter and of the aircraft's HUDAppManager, and no game code writes their positions or
    // scales, so values set here stick. Mission-scoped like HudDeclutter; nothing outlives the HUD.
    //
    // Original transforms are captured once per HUDAppManager instance. The compass, ladder and
    // waterline belong to FlightHud and survive an aircraft respawn, so everything is restored before
    // a re-capture — otherwise the moved values would be captured as the originals.
    internal class HudCompactLayout : MonoBehaviour
    {
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
        // FlightHud's pitchCompass (the ladder) and waterline. The ladder is a RawImage window onto a
        // tall ladder texture; FlightHud rewrites its uvRect and localScale every frame, but never its
        // sizeDelta, and never touches the waterline's scale.
        private RawImage? _ladder;
        private Vector2 _ladderOrigSize;
        private Rect _ladderLastUv;
        private float _ladderUvHeight;   // the game's uncropped uv height, from its latest write
        private Moved? _waterline;
        private HUDAppManager? _apps;
        private float _canvasHeight;
        private float _appliedFov = -1f;
        private int _appliedVersion = -1;
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
                // Per frame so zooming (an FOV change) or dragging a slider doesn't leave the blocks
                // lagging.
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
            if (!HudCompactConfig.Enabled || apps == null)
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
            if (_ladder != null) _ladderOrigSize = _ladder.rectTransform.sizeDelta;
            if (hud.waterline != null) _waterline = Snapshot(hud.waterline.transform, center);
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
            if (Mathf.Abs(fov - _appliedFov) < 0.01f && HudCompactConfig.Version == _appliedVersion) return;
            _appliedFov = fov;
            _appliedVersion = HudCompactConfig.Version;

            float spread = HudCompactConfig.Spread;
            HudCompactConfig.Adjust all = HudCompactConfig.All;
            foreach (BlockState b in _blocks)
            {
                HudCompactConfig.Adjust own = HudCompactConfig.ForGroup(b.Def.Group);
                float size = all.Size * own.Size;
                var target = new Vector3(
                    HudCompactLayoutMath.DegToUnits(b.Def.XDeg * spread + all.OffsetX + own.OffsetX, _canvasHeight, fov),
                    HudCompactLayoutMath.DegToUnits(b.Def.YDeg * spread + all.OffsetY + own.OffsetY, _canvasHeight, fov), 0f);
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

            float ladder = LadderFactor;
            if (_ladder != null) _ladder.rectTransform.sizeDelta = _ladderOrigSize * ladder;
            if (_waterline != null && _waterline.T != null)
                _waterline.T.localScale = _waterline.OrigScale * ladder;
        }

        private static float LadderFactor => HudCompactConfig.All.Size * HudCompactConfig.LadderSize;

        // The ladder window shrinks evenly (sizeDelta in Apply), and its uvRect is cropped by the same
        // factor about its center so the rungs keep their spacing — 10° of pitch still spans 10° on
        // screen, the window just shows fewer rungs. Runs after FlightHud.Update has written this
        // frame's uvRect; if FlightHud skipped a frame (no cockpit rigidbody) the uvRect is still the
        // one cropped here, so it's left alone rather than cropped twice.
        // The ladder's localScale is replaced the same way with the true conformal scale (FlightHud
        // writes its 50/fov approximation each frame), computed from the uncropped window so the crop
        // doesn't change rung spacing.
        private void LateUpdate()
        {
            if (_ladder == null || _blocks.Count == 0) return;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam != null && cam.mainCamera != null && _ladderUvHeight > 0f)
                _ladder.transform.localScale = Vector3.one *
                    HudCompactLayoutMath.TrueLadderScale(_ladderUvHeight, _ladderOrigSize.y, _canvasHeight, cam.mainCamera.fieldOfView);

            Rect uv = _ladder.uvRect;
            if (uv == _ladderLastUv) return;
            _ladderUvHeight = uv.height;
            float h = uv.height * LadderFactor;
            uv.y += (uv.height - h) * 0.5f;
            uv.height = h;
            _ladder.uvRect = uv;
            _ladderLastUv = uv;
        }

        private void Restore()
        {
            foreach (BlockState b in _blocks)
                foreach (Moved m in b.Members)
                    Reset(m);
            // The uvRect needs no restore: FlightHud rewrites it next frame.
            if (_ladder != null) _ladder.rectTransform.sizeDelta = _ladderOrigSize;
            if (_waterline != null) Reset(_waterline);
            _blocks.Clear();
            _ladder = null;
            _ladderUvHeight = 0f;
            _waterline = null;
            _apps = null;
            _appliedFov = -1f;
        }

        private static void Reset(Moved m)
        {
            if (m.T == null) return;
            m.T.localPosition = m.Orig;
            m.T.localScale = m.OrigScale;
        }
    }
}
