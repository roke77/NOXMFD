using System;
using System.Collections.Generic;
using System.Reflection;
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
    // that RT. Earlier we tried swapping in a 720×480 RT to get a higher-quality feed, but it (a)
    // quadrupled per-frame render cost for cam + UICam, and (b) repositioned UI canvas-anchored
    // elements (the targeting box/crosshair) on the in-cockpit screen because the canvas snapped to
    // the new RT edges. Reading the native RT side-steps both — the cockpit screen is undisturbed.
    //
    // A plain object (not a MonoBehaviour): TelemetryReader owns one, drives it via Tick(dt) each
    // frame, reads Active for the snapshot, and calls Disengage() from its OnDestroy. Disengage()
    // nulls the buffers, so a readback callback that lands after teardown bails on its own guards.
    internal class TgpFeed
    {
        internal const float Interval    = 1f / 15f;   // 15 Hz — halved from 30 Hz to cut readback+encode rate
        private  const int   MaxDim      = 720;        // cap for the encoded frame (native source is smaller, so this is a no-op today)
        private  const int   JpegQuality = 50;         // JPEG quality 0–100; 50 is visually fine for a small MFD pane

        private float          _timer;
        private RenderTexture? _rt;                    // Blit destination, source for AsyncGPUReadback
        private Texture2D?     _tex;                   // CPU-side buffer the readback writes into via LoadRawTextureData
        private FieldInfo?     _camField;              // TargetCam.cam (Camera) — private, cached
        private FieldInfo?     _screenRendererField;   // TargetCam.targetScreenRenderer — private, cached
        private bool           _reflectionTried;
        private bool           _engaged;               // true while we're actively capturing (for clean disengage logging)
        private bool           _active;                // last capture pushed a frame — mirrored into the snapshot
        private bool           _srcLogged;             // logged the source texture dimensions once
        private bool           _readbackInFlight;      // an AsyncGPUReadback is outstanding — skip new captures until it completes

        // Last capture pushed a frame — mirrored into the snapshot's TgpActive.
        public bool Active => _active;

        // Accumulate frame time and capture at Interval. Called every Update from the reader.
        public void Tick(float dt)
        {
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
                if (_engaged) Disengage();
                return;
            }

            // No mission / no aircraft / no TGP component → drop any cached frame and bail.
            GameManager.GetLocalAircraft(out Aircraft ac);
            TargetCam? tc = ac != null ? ac.targetCam : null;
            if (tc == null) { TelemetryServer.ClearTgpFrame(); _active = false; return; }

            // Cache the three private fields once. cam = scene camera, UICam = overlay canvas
            // camera, targetScreenRenderer = the in-cockpit display whose material is bound
            // to the camera's render texture.
            if (!_reflectionTried)
            {
                _reflectionTried = true;
                var t = typeof(TargetCam);
                _camField            = t.GetField("cam",                  BindingFlags.NonPublic | BindingFlags.Instance);
                _screenRendererField = t.GetField("targetScreenRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_camField == null || _screenRendererField == null)
                    Plugin.Log?.LogWarning("[NOXMFD] TGP: could not locate TargetCam private fields — feed disabled.");
            }
            if (_camField == null || _screenRendererField == null) { TelemetryServer.ClearTgpFrame(); _active = false; return; }

            // Only refresh the camTimeout while a target is actually locked — SetTargetCam
            // would crash on an empty list, and not calling it is what gives us the 3-second
            // post-loss hold (game's Update keeps aiming at the last targetPosition until
            // camTimeout expires).
            List<Unit> targets = ac!.weaponManager != null ? ac.weaponManager.GetTargetList() : null;
            if (targets != null && targets.Count > 0)
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
            Camera cam = _camField.GetValue(tc) as Camera;
            if (cam == null || !cam.enabled) { TelemetryServer.ClearTgpFrame(); _active = false; return; }

            // Prefer the camera's own targetTexture; fall back to the cockpit renderer's
            // material (which the game points at the same RT) if the prefab puts the
            // assignment there instead of on the Camera.
            Texture src = cam.targetTexture;
            if (src == null)
            {
                if (_screenRendererField.GetValue(tc) is Renderer rend && rend.material != null)
                    src = rend.material.mainTexture;
            }
            if (src == null) { TelemetryServer.ClearTgpFrame(); _active = false; return; }

            // Match the captured frame to the source's aspect ratio. Forcing a square output
            // squashed the in-game (wider-than-tall) feed; capturing at the native aspect lets
            // the MFD's object-fit:contain letterbox naturally, so the visible cam rectangle
            // shrinks and pixelation drops without distorting the picture. Cap at source size
            // — upsampling here adds no detail, just bytes.
            int sw = Mathf.Max(1, src.width);
            int sh = Mathf.Max(1, src.height);
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
            // this tick instead. With AsyncGPUReadback completing in 1–3 frames we'll only
            // skip one or two ticks per second under load, no visible stutter on the feed.
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
            // flush, which was the dominant per-frame cost of the synchronous ReadPixels path.
            // The callback fires on the main thread once the GPU has the bytes ready (typically
            // 1–3 frames later); we then copy into _tex, encode, and push.
            Graphics.Blit(src, _rt);
            _readbackInFlight = true;
            int captureW = targetW;
            int captureH = targetH;
            AsyncGPUReadback.Request(_rt, 0, request => OnReadbackComplete(request, captureW, captureH));
        }

        // Async readback callback — runs on the Unity main thread. Bail cleanly if the GPU errored
        // or the user disengaged the TGP page mid-flight. Disengage() nulls _tex on teardown, so the
        // size check below also covers "the reader was destroyed while this readback was in flight".
        private void OnReadbackComplete(AsyncGPUReadbackRequest request, int w, int h)
        {
            _readbackInFlight = false;
            if (request.hasError) return;
            if (!TelemetryServer.WantsTgpFrames) return;              // disengaged while in flight
            if (_tex == null || _tex.width != w || _tex.height != h) return;

            var data = request.GetData<byte>();
            _tex.LoadRawTextureData(data);
            _tex.Apply(false, false);

            byte[] jpg = _tex.EncodeToJPG(JpegQuality);
            TelemetryServer.PushTgpFrame(jpg);
            _active  = true;
            _engaged = true;
        }

        // Release the buffers we lazily allocate during capture and clear the published
        // frame. Safe to call from the gating fast-path or from the reader's OnDestroy. We never
        // swap any game-side RTs, so there's nothing to restore.
        public void Disengage()
        {
            if (_rt  != null) { _rt.Release();  UnityEngine.Object.Destroy(_rt);  _rt  = null; }
            if (_tex != null) {                 UnityEngine.Object.Destroy(_tex); _tex = null; }

            bool wasEngaged    = _engaged;
            _engaged           = false;
            _active            = false;
            _srcLogged         = false;
            _readbackInFlight  = false;   // any in-flight callback will see !WantsTgpFrames / null _tex and bail
            TelemetryServer.ClearTgpFrame();
            if (wasEngaged) Plugin.Log?.LogInfo("[NOXMFD] TGP: disengaged (no subscribers).");
        }
    }
}
