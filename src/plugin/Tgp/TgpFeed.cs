using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace NOXMFD
{
    // The TGP (targeting-pod) camera feed, peeled out of TelemetryReader. Unlike AssetCapture's
    // one-shot extraction, this is a CONTINUOUS feed: each tick it reads the game's own TargetCam
    // render texture, GPU-downscales it, reads it back asynchronously, and hands raw pixels to one
    // bounded JPEG worker before pushing the result to the MJPEG endpoint. Buffers and the worker
    // are created lazily, so the cost is zero until the TGP page opens a subscriber.
    //
    // LOW reads the game's native TargetCam RT. MID/HIGH use an independent mirror camera, leaving
    // the game's camera, UI canvas, and cockpit screen untouched.
    //
    // A plain object (not a MonoBehaviour): TelemetryReader owns one, drives it via Tick(dt) each
    // frame, reads Active for the snapshot, and calls Shutdown() from its OnDestroy. Generation
    // checks make a readback or encode completion after teardown harmless.
    //
    // Owns only the capture/GPU pipeline (source resolution, downscale, async readback, JPEG encode,
    // IR grayscale). The text/status overlay data captures derive alongside each frame lives in
    // TgpOverlay.cs instead (TgpFeed.Overlay) — a distinct concern from how the picture gets there.
    internal class TgpFeed
    {
        // Not a const: RatesConfig.SetTgpHz (rates.set command) writes this live from the TGP CFG
        // page's rate slider.
        internal static float Interval    = 1f / 15f;   // 15 Hz — enough for a small MFD pane, keeps readback+encode rate low
        internal static TgpResolution Resolution = TgpResolution.Native;
        internal static TgpJpegQuality JpegQuality = TgpJpegQuality.Medium;
        internal static bool       SuppressNativeDisplay;
        private static int         _settingsGeneration;

        internal static void SetResolution(TgpResolution resolution)
        {
            if (Resolution == resolution) return;
            Resolution = resolution;
            Interlocked.Increment(ref _settingsGeneration);
            TelemetryServer.ClearTgpFrame();
        }

        internal static void SetJpegQuality(TgpJpegQuality quality)
        {
            if (JpegQuality == quality) return;
            JpegQuality = quality;
            Interlocked.Increment(ref _settingsGeneration);
            TelemetryServer.ClearTgpFrame();
        }

        private float          _timer;
        private RenderTexture? _rt;                    // Blit destination, source for AsyncGPUReadback
        private FieldInfo?     _camField;              // TargetCam.cam (Camera) — private, cached
        private FieldInfo?     _screenRendererField;   // TargetCam.targetScreenRenderer — private, cached
        private FieldInfo?     _onCamToggleField;      // TargetCam.onCamToggle event backing field — private, cached
        private bool           _reflectionTried;
        private volatile bool  _engaged;               // true while we're actively capturing (for clean disengage logging)
        private volatile bool  _active;                // last capture pushed a frame — mirrored into the snapshot
        private bool           _readbackInFlight;      // an AsyncGPUReadback is outstanding — skip new captures until it completes
        private TgpMirrorCam?  _mirror;                // only allocated for a mirror resolution
        private int            _captureGeneration;
        private int            _lastSourceWidth = -1;
        private int            _lastSourceHeight = -1;
        private TgpResolution  _lastSourceResolution = (TgpResolution)(-1);
        private readonly object _encoderGate = new object();
        private readonly AutoResetEvent _encoderSignal = new AutoResetEvent(false);
        private EncodeWork?    _pendingEncode;
        private bool           _encoderStarted;
        private volatile bool  _encoderStopping;
        private int            _encoderDrops;
        private bool           _cockpitDisplaySuppressed;
        private bool           _toggleMissingLogged;
        private bool           _lastDiagWantsTgp;
        private bool           _lastDiagSuppressSetting;
        private bool           _lastDiagCockpitSuppressed;
        private int            _lastDiagTargetCount = -2;
        private TgpResolution  _lastDiagResolution;

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
                if (_engaged || _rt != null) Disengage();
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

            TgpCaptureSettings settings = TgpFeedSettings.Resolve(Resolution, JpegQuality);
            Texture? src;
            if (!settings.UsesMirror)
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
                // Mirror path: read from TgpMirrorCam's own higher-res RenderTexture instead of the
                // game's native TargetCam RT. The mirror camera is enabled+Base once Engage()'d, so
                // the pipeline renders a fresh frame into it every Unity frame on its own — nothing
                // here needs to trigger that render.
                _mirror ??= new TgpMirrorCam();
                _mirror.Engage(tc, settings.Width, settings.Height);
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
                // Manual mode never has a real lock (Tick() auto-exits the instant one exists), so
                // this and the real-lock Populate() path never compete — the external web TGP page
                // gets the same RNG/ALT/REL/CLO/GRID/MODE/MAG/EL data the in-cockpit overlay already
                // shows (TgpNativeOverlay), instead of the overlay just going blank while pointing
                // manually (docs/tgp-manual-control.md's "In-cockpit overlay" / web parity).
                if (!hasTargets && TgpManualControl.ManualMode)
                {
                    Overlay.PopulateManual(tc, tc.GetCamMount(), ac);
                }
                else
                {
                    Func<Vector3, Vector3> project = Resolution == TgpResolution.Native
                        ? (Func<Vector3, Vector3>)(pos => cam.WorldToViewportPoint(pos))
                        : (pos => _mirror != null ? _mirror.WorldToViewport(pos) : new Vector3(0f, 0f, -1f));
                    Overlay.Populate(tc, targets, ac, project);
                }
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
            (int targetW, int targetH) = TgpFeedSettings.FitWithinMaxDimension(sw, sh, settings.MaxDimension);

            if (_lastSourceWidth != sw || _lastSourceHeight != sh || _lastSourceResolution != Resolution)
            {
                _lastSourceWidth = sw;
                _lastSourceHeight = sh;
                _lastSourceResolution = Resolution;
                Plugin.Log?.LogInfo($"[NOXMFD] TGP {Resolution} source {sw}x{sh}; capturing at {targetW}x{targetH}, JPEG {settings.JpegQuality}.");
            }

            // Don't stack readbacks if the GPU is still working on the previous one — drop
            // this tick instead. At the 15 Hz default AsyncGPUReadback usually completes in
            // 1–3 frames, so this skips rarely; at higher rates (TGP CFG page's rate slider) the
            // GPU can fall behind and skip much more often (docs/performance.md).
            if (_readbackInFlight) return;

            // (Re)allocate the downscale RT when the source dimensions change.
            if (_rt == null || _rt.width != targetW || _rt.height != targetH)
            {
                if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
                _rt = new RenderTexture(targetW, targetH, 0, RenderTextureFormat.ARGB32);
                _rt.Create();
            }
            // GPU downscale, then ASYNC readback. AsyncGPUReadback dispatches the readback to
            // the GPU and returns immediately — no main-thread stall waiting on a pipeline
            // flush (a synchronous ReadPixels here would be the dominant per-frame cost).
            // The callback fires on the main thread once the GPU has the bytes ready (typically
            // 1–3 frames later); we then copy the request-owned bytes for the encoder worker.
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
            bool ir = settings.UsesMirror && tc.UsingIR();
            int captureGeneration = _captureGeneration;
            int settingsGeneration = Volatile.Read(ref _settingsGeneration);
            int jpegQuality = settings.JpegQuality;
            AsyncGPUReadback.Request(_rt, 0, request =>
                OnReadbackComplete(request, captureW, captureH, ir, jpegQuality,
                                   captureGeneration, settingsGeneration));
        }

        // The callback copies request-owned memory before returning, then hands ownership to the
        // bounded encoder worker. At most one frame waits behind the frame currently encoding.
        private void OnReadbackComplete(AsyncGPUReadbackRequest request, int w, int h, bool ir,
                                        int jpegQuality, int captureGeneration,
                                        int settingsGeneration)
        {
            if (captureGeneration != _captureGeneration) return;
            _readbackInFlight = false;
            if (request.hasError) return;
            if (!TelemetryServer.WantsTgpFrames) return;              // disengaged while in flight
            if (settingsGeneration != Volatile.Read(ref _settingsGeneration)) return;

            byte[] data = request.GetData<byte>().ToArray();
            EnqueueEncode(new EncodeWork(data, w, h, ir, jpegQuality,
                                         captureGeneration, settingsGeneration));
        }

        private void EnqueueEncode(EncodeWork work)
        {
            bool startWorker = false;
            lock (_encoderGate)
            {
                if (_encoderStopping) return;
                if (_pendingEncode != null)
                    Interlocked.Increment(ref _encoderDrops);
                _pendingEncode = work;
                if (!_encoderStarted)
                {
                    _encoderStarted = true;
                    startWorker = true;
                }
            }
            if (startWorker)
            {
                var worker = new Thread(EncoderLoop)
                {
                    IsBackground = true,
                    Name = "NOXMFD TGP JPEG",
                };
                worker.Start();
            }
            _encoderSignal.Set();
        }

        private void EncoderLoop()
        {
            while (true)
            {
                _encoderSignal.WaitOne();
                if (_encoderStopping) return;

                while (true)
                {
                    EncodeWork work;
                    lock (_encoderGate)
                    {
                        if (_pendingEncode == null) break;
                        work = _pendingEncode;
                        _pendingEncode = null;
                    }

                    try
                    {
                        if (work.CaptureGeneration != Volatile.Read(ref _captureGeneration) ||
                            work.SettingsGeneration != Volatile.Read(ref _settingsGeneration) ||
                            !TelemetryServer.WantsTgpFrames)
                            continue;

                        if (work.IR)
                        {
                            if (_irCaptureGeneration != work.CaptureGeneration ||
                                _irSettingsGeneration != work.SettingsGeneration)
                            {
                                _irMinEma = _irMaxEma = -1f;
                                _irCaptureGeneration = work.CaptureGeneration;
                                _irSettingsGeneration = work.SettingsGeneration;
                            }
                            Grayscale(work.Data);
                        }

                        byte[] jpg = ImageConversion.EncodeArrayToJPG(
                            work.Data, GraphicsFormat.R8G8B8A8_UNorm,
                            (uint)work.Width, (uint)work.Height, 0, work.JpegQuality);
                        if (work.CaptureGeneration != Volatile.Read(ref _captureGeneration) ||
                            work.SettingsGeneration != Volatile.Read(ref _settingsGeneration) ||
                            !TelemetryServer.WantsTgpFrames)
                            continue;

                        TelemetryServer.PushTgpFrame(jpg);
                        _active = true;
                        _engaged = true;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.LogWarning($"[NOXMFD] TGP JPEG encode failed: {ex.Message}");
                    }
                }
            }
        }

        private sealed class EncodeWork
        {
            internal EncodeWork(byte[] data, int width, int height, bool ir, int jpegQuality,
                                int captureGeneration, int settingsGeneration)
            {
                Data = data;
                Width = width;
                Height = height;
                IR = ir;
                JpegQuality = jpegQuality;
                CaptureGeneration = captureGeneration;
                SettingsGeneration = settingsGeneration;
            }

            internal byte[] Data { get; }
            internal int Width { get; }
            internal int Height { get; }
            internal bool IR { get; }
            internal int JpegQuality { get; }
            internal int CaptureGeneration { get; }
            internal int SettingsGeneration { get; }
        }

        // The actual auto-levels algorithm (and its ponytail/design-rationale notes) lives in
        // TgpFeedSettings.ApplyIrAutoLevels — pure byte-array math, linked into tools/tests. This
        // class only owns the smoothing state across frames and when to reset it (a resolution/
        // quality change or a fresh capture generation re-seeds from that frame's own min/max
        // instead of easing in from the previous scene's brightness).
        private const float IrLevelsSmoothing = 0.25f;
        private float _irMinEma = -1f;
        private float _irMaxEma = -1f;
        private int _irCaptureGeneration = -1;
        private int _irSettingsGeneration = -1;

        private void Grayscale(byte[] px) =>
            TgpFeedSettings.ApplyIrAutoLevels(px, ref _irMinEma, ref _irMaxEma, IrLevelsSmoothing);

        // Shared by every early-return guard in CaptureFrame() (no aircraft, no TGP component,
        // reflection failed, cam disabled/timed out) — clearing Overlay alongside _active keeps the
        // HQ overlay (driven by Overlay, not by Active) from showing a stale lock once the feed
        // itself has gone dark. Doesn't touch the buffers Disengage() releases — those guards fire
        // far more often than an actual disengage (every tick with no lock at all), so reallocating
        // them each time would be wasteful.
        private void ClearFeed(bool showTargetCam, bool restoreCockpit = true)
        {
            InvalidatePendingWork();
            _mirror?.Disengage();
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
            _mirror?.Disengage();

            bool wasEngaged    = _engaged;
            _engaged           = false;
            _active            = false;
            InvalidatePendingWork();
            _lastSourceWidth = _lastSourceHeight = -1;
            _lastSourceResolution = (TgpResolution)(-1);
            Overlay.Clear();              // stale overlay data shouldn't linger once the feed goes idle
            TelemetryServer.ClearTgpFrame();
            if (wasEngaged)
                Plugin.Log?.LogInfo($"[NOXMFD] TGP: disengaged (no subscribers, encoderDrops={Volatile.Read(ref _encoderDrops)}).");
        }

        public void Shutdown()
        {
            Disengage();
            _encoderStopping = true;
            _encoderSignal.Set();
        }

        private void InvalidatePendingWork()
        {
            Interlocked.Increment(ref _captureGeneration);
            _readbackInFlight = false;
            lock (_encoderGate) _pendingEncode = null;
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
                Plugin.Log?.LogInfo($"[NOXMFD] TGP cockpit hide: ON (resolution={Resolution}).");
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
                           Resolution != _lastDiagResolution;
            if (!changed) return;

            Plugin.Log?.LogInfo(
                $"[NOXMFD] TGP cockpit hide state ({reason}): wants={wants}, setting={SuppressNativeDisplay}, resolution={Resolution}, targets={(targetCount < 0 ? "n/a" : targetCount.ToString())}, suppressed={_cockpitDisplaySuppressed}.");

            _lastDiagWantsTgp = wants;
            _lastDiagSuppressSetting = SuppressNativeDisplay;
            _lastDiagCockpitSuppressed = _cockpitDisplaySuppressed;
            _lastDiagTargetCount = targetCount;
            _lastDiagResolution = Resolution;
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
