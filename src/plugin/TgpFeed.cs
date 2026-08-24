using System;
using System.Collections.Generic;
using System.Reflection;
using NuclearOption.Networking;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace NOXMFD
{
    // The TGP (targeting-pod) camera feed, peeled out of TelemetryReader. Unlike AssetCapture's
    // one-shot extraction, this is a CONTINUOUS feed: each tick it reads the game's own TargetCam
    // render texture, GPU-downscales + JPEG-encodes it (async readback, off the frame's critical
    // path) and pushes it to the server's MJPEG endpoint. Buffers are allocated lazily and freed on
    // disengage, so the cost is zero until the MFD's TGP page actually opens a subscriber.
    //
    // We let the game render the TargetCam at its prefab-native resolution (~360×240) and just READ
    // that RT, rather than swapping in a larger RT for a higher-quality feed. A bigger RT would (a)
    // quadruple per-frame render cost for cam + UICam, and (b) reposition UI canvas-anchored
    // elements (the targeting box/crosshair) on the in-cockpit screen, since the canvas snaps to
    // the RT edges. Reading the native RT avoids both — the cockpit screen is undisturbed.
    //
    // A plain object (not a MonoBehaviour): TelemetryReader owns one, drives it via Tick(dt) each
    // frame, reads Active for the snapshot, and calls Disengage() from its OnDestroy. Disengage()
    // nulls the buffers, so a readback callback that lands after teardown bails on its own guards.
    //
    // Owns only the capture/GPU pipeline (source resolution, downscale, async readback, JPEG encode,
    // IR grayscale). The text/status overlay data captures derive alongside each frame lives in
    // TgpOverlay.cs instead (TgpFeed.Overlay) — a distinct concern from how the picture gets there.
    internal class TgpFeed
    {
        // Not a const: RatesConfig.SetTgpHz (rates.set command) writes this live from the TGP CFG
        // page's rate slider.
        internal static float Interval    = 1f / 15f;   // 15 Hz — enough for a small MFD pane, keeps readback+encode rate low
        private  const int   MaxDim      = 720;        // cap for the encoded frame — a no-op at Native res, an exact match at Hq res (below)
        private  const int   JpegQuality = 50;         // JPEG quality 0–100; 50 is visually fine for a small MFD pane
        // RatesConfig.SetTgpQuality (rates.set command, group "tgpQuality") writes this live from
        // the TGP CFG page. HqWidth/HqHeight match the native source's ~3:2 aspect (docs/tgp-high-
        // quality-mode.md's stated default) so the existing letterbox-avoidance math below needs no
        // change for either HQ mode.
        internal static TgpQuality Quality  = TgpQuality.Native;
        internal static bool       SuppressNativeDisplay;
        private  const int         HqWidth  = 720;
        private  const int         HqHeight = 480;

        private float          _timer;
        private RenderTexture? _rt;                    // Blit destination, source for AsyncGPUReadback
        private Texture2D?     _tex;                   // CPU-side buffer the readback writes into via LoadRawTextureData
        private FieldInfo?     _camField;              // TargetCam.cam (Camera) — private, cached
        private FieldInfo?     _screenRendererField;   // TargetCam.targetScreenRenderer — private, cached
        private FieldInfo?     _onCamToggleField;      // TargetCam.onCamToggle event backing field — private, cached
        private bool           _reflectionTried;
        private bool           _engaged;               // true while we're actively capturing (for clean disengage logging)
        private bool           _active;                // last capture pushed a frame — mirrored into the snapshot
        private bool           _srcLogged;             // logged the source texture dimensions once
        private bool           _readbackInFlight;      // an AsyncGPUReadback is outstanding — skip new captures until it completes
        private TgpMirrorCam?  _mirror;                // only allocated once Quality != Native (see CaptureFrame)
        private bool           _cockpitDisplaySuppressed;
        private bool           _toggleMissingLogged;
        private bool           _lastDiagWantsTgp;
        private bool           _lastDiagSuppressSetting;
        private bool           _lastDiagCockpitSuppressed;
        private int            _lastDiagTargetCount = -2;
        private TgpQuality     _lastDiagQuality;

        // Last capture pushed a frame — mirrored into the snapshot's TgpActive.
        public bool Active => _active;

        // Text/status overlay data, mirrored into the snapshot's Tgp* fields each capture tick —
        // see TgpOverlay.cs for what each means and how it's derived.
        public TgpOverlay Overlay { get; } = new TgpOverlay();

        // Accumulate frame time and capture at Interval. Called every Update from the reader.
        public void Tick(float dt)
        {
            if (!TelemetryServer.WantsTgpFrames || !SuppressNativeDisplay)
            {
                LogSuppressionState("gate-off", targetCount: -1);
                RestoreNativeScreen(showTargetCam: true);
            }
            else
            {
                UpdateNativeSuppressionGate();
            }

            _timer += dt;
            if (_timer < Interval) return;
            _timer = 0f;
            CaptureFrame();
        }

        // The game's own TargetCam tracks the player's current target, including IR mode and
        // zoom-on-target FOV. While a target is locked we nudge it active each tick; when the
        // last target disappears we STOP calling SetTargetCam — the TargetCam's own Update()
        // keeps the cam aimed at the final target position for ~3 s via its camTimeout, then
        // disables itself. We mirror that lifetime by reading frames while cam.enabled is true
        // and clearing as soon as it goes false. Net effect: the feed lingers exactly as long
        // as the in-cockpit screen does.
        private void CaptureFrame()
        {
            // Gate on /tgp.mjpg subscribers. When the MFD's TGP page is not open, no client
            // is subscribed, and there's no point running the capture pipeline (or calling
            // SetTargetCam, which keeps the in-cockpit TargetCam alive). Free our buffers
            // on the transition out so we leave nothing allocated while idle.
            if (!TelemetryServer.WantsTgpFrames)
            {
                RestoreNativeScreen(showTargetCam: true);
                if (_engaged) Disengage();
                return;
            }

            // No mission / no aircraft / no TGP component → drop any cached frame and bail.
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null) { ClearFeed(showTargetCam: false, restoreCockpit: !SuppressNativeDisplay); return; }
            TargetCam? tc = ac.targetCam;
            if (tc == null) { ClearFeed(showTargetCam: false, restoreCockpit: !SuppressNativeDisplay); return; }

            // Cache private fields once. cam = scene camera; targetScreenRenderer = the in-cockpit
            // display material fallback for Native capture; onCamToggle is what TacScreen listens
            // to when it shows/hides the cockpit TGP overlay over the normal radar screen.
            if (!_reflectionTried)
            {
                CacheTargetCamFields();
            }
            if (_camField == null || _screenRendererField == null) { ClearFeed(showTargetCam: false, restoreCockpit: !SuppressNativeDisplay); return; }

            Camera? cam = _camField.GetValue(tc) as Camera;
            Renderer? screenRenderer = _screenRendererField.GetValue(tc) as Renderer;

            // Only refresh the camTimeout while a target is actually locked — SetTargetCam
            // would crash on an empty list, and not calling it is what gives us the 3-second
            // post-loss hold (game's Update keeps aiming at the last targetPosition until
            // camTimeout expires).
            List<Unit>? targets = ac.weaponManager != null ? ac.weaponManager.GetTargetList() : null;
            bool hasTargets = targets != null && targets.Count > 0;
            if (hasTargets)
            {
                try { tc.SetTargetCam(); }
                catch (Exception ex)
                {
                    // SetTargetCam touches a lot of game state. If anything throws (e.g. the player
                    // just disabled / detached), skip this tick rather than killing Update.
                    Plugin.Log?.LogDebug($"[NOXMFD] TGP SetTargetCam threw: {ex.Message}");
                    return;
                }
            }

            // After the game's 3-second timeout expires, cam.enabled flips to false. Stop
            // pushing then so MJPEG clients see "no feed" and fall back to NO TARGET.
            cam = _camField.GetValue(tc) as Camera;
            screenRenderer = _screenRendererField.GetValue(tc) as Renderer;
            if (cam == null || !cam.enabled) { ClearFeed(showTargetCam: false, restoreCockpit: !SuppressNativeDisplay); return; }

            Texture? src;
            if (Quality == TgpQuality.Native)
            {
                _mirror?.Disengage();

                // Prefer the camera's own targetTexture; fall back to the cockpit renderer's
                // material (which the game points at the same RT) if the prefab puts the
                // assignment there instead of on the Camera.
                src = cam.targetTexture;
                if (src == null)
                {
                    if (screenRenderer != null && screenRenderer.material != null)
                        src = screenRenderer.material.mainTexture;
                }
                if (src == null) { ClearFeed(showTargetCam: hasTargets, restoreCockpit: !SuppressNativeDisplay); return; }
            }
            else
            {
                // HQ path: read from TgpMirrorCam's own higher-res RenderTexture instead of the
                // game's native TargetCam RT. The mirror camera is enabled+Base once Engage()'d, so
                // the pipeline renders a fresh frame into it every Unity frame on its own — nothing
                // here needs to trigger that render.
                _mirror ??= new TgpMirrorCam();
                _mirror.Engage(tc, HqWidth, HqHeight);
                _mirror.SyncFromSource(cam);
                src = _mirror.Texture;
                if (src == null) { ClearFeed(showTargetCam: hasTargets, restoreCockpit: !SuppressNativeDisplay); return; }
            }
            Texture source = src;

            // Overlay data (including the per-target lock box) projects through whichever camera
            // is actually producing the picture — cam itself for Native, the mirror camera for HQ
            // — so this runs after src is resolved, once that camera is guaranteed ready.
            try
            {
                Func<Vector3, Vector3> project = Quality == TgpQuality.Native
                    ? (Func<Vector3, Vector3>)(pos => cam.WorldToViewportPoint(pos))
                    : (pos => _mirror != null ? _mirror.WorldToViewport(pos) : new Vector3(0f, 0f, -1f));
                Overlay.Populate(tc, targets, ac, project);
            }
            catch (Exception ex)
            {
                // Touches a lot of game state (pilots, faction HQ, radar jam state) — if anything
                // throws, keep the video capture path alive rather than losing the feed over a
                // supplementary field. Fail CLOSED though: Overlay.Clear() hides the overlay
                // client-side (see TgpBlock in TelemetryJson.cs), so a mid-update throw can't leave
                // stale target data on screen until the next successful tick or disengage.
                Overlay.Clear();
                Plugin.Log?.LogDebug($"[NOXMFD] TGP overlay update threw: {ex.Message}");
            }

            // Suppress only the cockpit's TargetCam overlay, not TargetCam.cam/UICam or any screen
            // renderer/material. TacScreen receives the same toggle event the game uses and falls
            // back to its normal radar/time content, while HQ still syncs from the live TargetCam.
            if (SuppressNativeDisplay && hasTargets)
                SuppressNativeScreen(tc);
            else if (!SuppressNativeDisplay)
                RestoreNativeScreen(showTargetCam: hasTargets);

            // Match the captured frame to the source's aspect ratio. Forcing a square output
            // squashed the in-game (wider-than-tall) feed; capturing at the native aspect lets
            // the MFD's object-fit:contain letterbox naturally, so the visible cam rectangle
            // shrinks and pixelation drops without distorting the picture. Cap at source size
            // — upsampling here adds no detail, just bytes.
            int sw = Mathf.Max(1, source.width);
            int sh = Mathf.Max(1, source.height);
            int targetW, targetH;
            int maxSide = Mathf.Max(sw, sh);
            if (maxSide <= MaxDim)
            {
                targetW = sw; targetH = sh;
            }
            else if (sw >= sh)
            {
                targetW = MaxDim;
                targetH = Mathf.Max(1, Mathf.RoundToInt(MaxDim * (float)sh / sw));
            }
            else
            {
                targetH = MaxDim;
                targetW = Mathf.Max(1, Mathf.RoundToInt(MaxDim * (float)sw / sh));
            }

            if (!_srcLogged)
            {
                _srcLogged = true;
                Plugin.Log?.LogInfo($"[NOXMFD] TGP source texture {sw}x{sh} (aspect {(float)sw/sh:0.000}); capturing at {targetW}x{targetH}.");
            }

            // Don't stack readbacks if the GPU is still working on the previous one — drop
            // this tick instead. At the 15 Hz default AsyncGPUReadback usually completes in
            // 1–3 frames, so this skips rarely; at higher rates (TGP CFG page's rate slider) the
            // GPU can fall behind and skip much more often (docs/performance.md).
            if (_readbackInFlight) return;

            // (Re)allocate the downscale RT + readback texture when the source dimensions change.
            // RGBA32 on both sides so the bytes from AsyncGPUReadback can be fed straight into
            // LoadRawTextureData without a format conversion.
            if (_rt == null || _rt.width != targetW || _rt.height != targetH)
            {
                if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
                _rt = new RenderTexture(targetW, targetH, 0, RenderTextureFormat.ARGB32);
                _rt.Create();
            }
            if (_tex == null || _tex.width != targetW || _tex.height != targetH)
            {
                if (_tex != null) UnityEngine.Object.Destroy(_tex);
                _tex = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
            }

            // GPU downscale, then ASYNC readback. AsyncGPUReadback dispatches the readback to
            // the GPU and returns immediately — no main-thread stall waiting on a pipeline
            // flush (a synchronous ReadPixels here would be the dominant per-frame cost).
            // The callback fires on the main thread once the GPU has the bytes ready (typically
            // 1–3 frames later); we then copy into _tex, encode, and push.
            Graphics.Blit(source, _rt);
            _readbackInFlight = true;
            int captureW = targetW;
            int captureH = targetH;
            // Native bakes the game's own thermal look into the video for free (it's really
            // TargetCam's own camera, with TargetCam's own local IR post-process volume already
            // applied upstream). The HQ mirror camera has no such volume, so without this the HQ
            // feed stays full-color even when the pod is in IR mode. Cheapest correct fix: convert
            // to grayscale on the CPU after readback, on the bytes we already have in hand for JPEG
            // encoding — no shader/volume/layer-mask plumbing, and it can't leak onto any other
            // camera since it only touches our own capture buffer.
            bool ir = Quality == TgpQuality.HighQuality && tc.UsingIR();
            AsyncGPUReadback.Request(_rt, 0, request => OnReadbackComplete(request, captureW, captureH, ir));
        }

        // Async readback callback — runs on the Unity main thread. Bail cleanly if the GPU errored
        // or the user disengaged the TGP page mid-flight. Disengage() nulls _tex on teardown, so the
        // size check below also covers "the reader was destroyed while this readback was in flight".
        private void OnReadbackComplete(AsyncGPUReadbackRequest request, int w, int h, bool ir)
        {
            _readbackInFlight = false;
            if (request.hasError) return;
            if (!TelemetryServer.WantsTgpFrames) return;              // disengaged while in flight
            if (_tex == null || _tex.width != w || _tex.height != h) return;

            var data = request.GetData<byte>();
            _tex.LoadRawTextureData(data);
            if (ir) Grayscale(_tex.GetRawTextureData<byte>());
            _tex.Apply(false, false);

            byte[] jpg = _tex.EncodeToJPG(JpegQuality);
            TelemetryServer.PushTgpFrame(jpg);
            _active  = true;
            _engaged = true;
        }

        // ponytail: stretches the frame's own min..max luma to fill 0..255 (auto-levels) rather than
        // a flat luma conversion or a fixed contrast pivot around mid-gray — a bright daytime scene's
        // luma already sits well above 128, so pushing away from a fixed pivot just clips almost
        // everything to white. Self-adjusting to the scene's actual brightness avoids that blowout;
        // it's still not the real thermal shader's simulated heat curve, way past what a "basic
        // black/white cam" needs, but much closer without the blowout risk.
        // Ceiling: a flat-scene frame (all sky, no ground) has a near-zero luma range, so the
        // stretch divisor floors at 1 rather than dividing by ~0 — that just leaves it unstretched
        // rather than amplifying sensor-noise-level differences into full contrast.
        //
        // The min/max feeding that stretch is smoothed across frames (_irMinEma/_irMaxEma) rather
        // than trusting each frame's raw values outright — a bare per-frame min/max jumps whenever a
        // bright/dark pixel enters or leaves the picture (a highlight, camera jitter), which reads as
        // a visible brightness/contrast pulse between consecutive frames; more noticeable at high
        // capture rates, where shown frames are more likely to differ from each other and dropped
        // readbacks (docs/performance.md) widen the gap between them further.
        // ponytail: fixed smoothing factor, tuned by feel rather than measured — revisit (e.g. a
        // faster factor, or reset-on-large-jump) if it visibly lags a fast real brightness change
        // such as an HQ zoom snapping onto a new target.
        private const float IrLevelsSmoothing = 0.25f;
        private float _irMinEma = -1f;
        private float _irMaxEma = -1f;

        private void Grayscale(NativeArray<byte> px)
        {
            byte rawMin = 255, rawMax = 0;
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                byte luma = (byte)(0.299f * px[i] + 0.587f * px[i + 1] + 0.114f * px[i + 2]);
                if (luma < rawMin) rawMin = luma;
                if (luma > rawMax) rawMax = luma;
            }

            if (_irMinEma < 0f) { _irMinEma = rawMin; _irMaxEma = rawMax; }
            else
            {
                _irMinEma += (rawMin - _irMinEma) * IrLevelsSmoothing;
                _irMaxEma += (rawMax - _irMaxEma) * IrLevelsSmoothing;
            }

            float range = Mathf.Max(1f, _irMaxEma - _irMinEma);
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                float luma = 0.299f * px[i] + 0.587f * px[i + 1] + 0.114f * px[i + 2];
                byte gray = (byte)Mathf.Clamp((luma - _irMinEma) / range * 255f, 0f, 255f);
                px[i] = px[i + 1] = px[i + 2] = gray;
            }
        }

        // Shared by every early-return guard in CaptureFrame() (no aircraft, no TGP component,
        // reflection failed, cam disabled/timed out) — clearing Overlay alongside _active keeps the
        // HQ overlay (driven by Overlay, not by Active) from showing a stale lock once the feed
        // itself has gone dark. Doesn't touch the buffers Disengage() releases — those guards fire
        // far more often than an actual disengage (every tick with no lock at all), so reallocating
        // them each time would be wasteful.
        private void ClearFeed(bool showTargetCam, bool restoreCockpit = true)
        {
            if (restoreCockpit)
                RestoreNativeScreen(showTargetCam);
            TelemetryServer.ClearTgpFrame();
            _active = false;
            Overlay.Clear();
        }

        // Release the buffers we lazily allocate during capture, restore any native cockpit-screen
        // state this instance suppressed, and clear the published frame. Safe to call from the
        // gating fast-path or from the reader's OnDestroy.
        public void Disengage()
        {
            RestoreNativeScreen(showTargetCam: true);
            if (_rt  != null) { _rt.Release();  UnityEngine.Object.Destroy(_rt);  _rt  = null; }
            if (_tex != null) {                 UnityEngine.Object.Destroy(_tex); _tex = null; }
            _mirror?.Disengage();

            bool wasEngaged    = _engaged;
            _engaged           = false;
            _active            = false;
            _srcLogged         = false;
            _readbackInFlight  = false;   // any in-flight callback will see !WantsTgpFrames / null _tex and bail
            _irMinEma = _irMaxEma = -1f;  // re-seed from the next lock's own scene, not this one's brightness
            Overlay.Clear();              // stale overlay data shouldn't linger once the feed goes idle
            TelemetryServer.ClearTgpFrame();
            if (wasEngaged) Plugin.Log?.LogInfo("[NOXMFD] TGP: disengaged (no subscribers).");
        }

        private void CacheTargetCamFields()
        {
            if (_reflectionTried) return;
            _reflectionTried = true;
            var t = typeof(TargetCam);
            _camField            = t.GetField("cam",                  BindingFlags.NonPublic | BindingFlags.Instance);
            _screenRendererField = t.GetField("targetScreenRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
            _onCamToggleField    = t.GetField("onCamToggle",          BindingFlags.NonPublic | BindingFlags.Instance);
            if (_camField == null || _screenRendererField == null)
                Plugin.Log?.LogWarning("[NOXMFD] TGP: could not locate TargetCam private fields — feed disabled.");
        }

        private void UpdateNativeSuppressionGate()
        {
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.targetCam == null)
            {
                RestoreNativeScreen(showTargetCam: false);
                return;
            }

            TargetCam tc = ac.targetCam;
            List<Unit>? targets = ac.weaponManager != null ? ac.weaponManager.GetTargetList() : null;
            int targetCount = targets != null ? targets.Count : 0;
            LogSuppressionState("gate", targetCount);
            SuppressNativeScreen(tc);
        }

        private void SuppressNativeScreen(TargetCam tc)
        {
            if (!_cockpitDisplaySuppressed)
                Plugin.Log?.LogInfo($"[NOXMFD] TGP cockpit hide: ON (quality={Quality}).");
            InvokeTargetCamToggle(tc, enabled: false);
            _cockpitDisplaySuppressed = true;
        }

        private void RestoreNativeScreen(bool showTargetCam)
        {
            if (!_cockpitDisplaySuppressed)
                return;
            GameManager.GetLocalAircraft(out Aircraft ac);
            TargetCam? tc = ac != null ? ac.targetCam : null;
            Plugin.Log?.LogInfo($"[NOXMFD] TGP cockpit hide: OFF (showTargetCam={showTargetCam}, hasTargetCam={tc != null}).");
            if (showTargetCam && tc != null)
                InvokeTargetCamToggle(tc, enabled: true);
            _cockpitDisplaySuppressed = false;
        }

        private void LogSuppressionState(string reason, int targetCount)
        {
            bool wants = TelemetryServer.WantsTgpFrames;
            bool changed = wants != _lastDiagWantsTgp ||
                           SuppressNativeDisplay != _lastDiagSuppressSetting ||
                           _cockpitDisplaySuppressed != _lastDiagCockpitSuppressed ||
                           targetCount != _lastDiagTargetCount ||
                           Quality != _lastDiagQuality;
            if (!changed) return;

            Plugin.Log?.LogInfo(
                $"[NOXMFD] TGP cockpit hide state ({reason}): wants={wants}, setting={SuppressNativeDisplay}, quality={Quality}, targets={(targetCount < 0 ? "n/a" : targetCount.ToString())}, suppressed={_cockpitDisplaySuppressed}.");

            _lastDiagWantsTgp = wants;
            _lastDiagSuppressSetting = SuppressNativeDisplay;
            _lastDiagCockpitSuppressed = _cockpitDisplaySuppressed;
            _lastDiagTargetCount = targetCount;
            _lastDiagQuality = Quality;
        }

        private void InvokeTargetCamToggle(TargetCam tc, bool enabled)
        {
            if (_onCamToggleField?.GetValue(tc) is Action<TargetCam.OnCamToggle> toggle)
            {
                toggle.Invoke(new TargetCam.OnCamToggle
                {
                    enabled = enabled,
                    camMode = TargetCam.CamMode.targetForward
                });
            }
            else
            {
                if (!_toggleMissingLogged)
                {
                    _toggleMissingLogged = true;
                    Plugin.Log?.LogDebug("[NOXMFD] TGP: TargetCam onCamToggle event not found; cockpit suppression skipped.");
                }
            }
        }
    }
}
