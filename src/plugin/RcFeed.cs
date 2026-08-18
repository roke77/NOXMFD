using UnityEngine;
using UnityEngine.Rendering;

namespace NOXMFD
{
    // The MissileCamera: Remote Control feed — same continuous-capture pipeline as TgpFeed.cs, but
    // sourced from RcBridge.FeedCamera (a soft dependency on another mod) instead of the game's
    // own TargetCam. Gated the same way: zero cost while nobody has the /rc page open, via
    // TelemetryServer.WantsRcFrames — see docs/rc-page.md for the full protocol this feeds.
    //
    // A plain object (not a MonoBehaviour), owned by TelemetryReader exactly like _tgp: driven via
    // Tick(dt) each frame, Active mirrored into the snapshot, torn down from OnDestroy.
    internal class RcFeed
    {
        // 20 Hz: this is the pilot's direct aiming view (vs. TGP's 15 Hz targeting reference), so
        // it gets a bit more headroom for smooth tracking of a fast-moving reticle.
        internal const float Interval    = 1f / 20f;
        private  const int   MaxDim      = 720;
        private  const int   JpegQuality = 55;

        private float          _timer;
        private RenderTexture? _rt;
        private Texture2D?     _tex;
        private bool           _engaged;
        private bool           _active;
        private bool           _srcLogged;
        private bool           _pixelDiagLogged;
        private bool           _hadSrc;
        private bool           _readbackInFlight;

        public bool Active => _active;

        public void Tick(float dt)
        {
            _timer += dt;
            if (_timer < Interval) return;
            _timer = 0f;
            CaptureFrame();
        }

        private void CaptureFrame()
        {
            // Gate on /rc.mjpg subscribers — same reasoning as TgpFeed: no point reading RcBridge
            // or touching a Camera every tick when no client has the RC page open. This gate is
            // also what drives McBridge.RequestCapture below — see that call for why it must run
            // even on the "no subscribers" branch, not just here.
            if (!TelemetryServer.WantsRcFrames)
            {
                McBridge.RequestCapture(false);
                if (_engaged) Disengage();
                return;
            }

            // Ask the BASE "Missile Camera" mod to keep its feed pipeline live for us — this is
            // what removes the need for the pilot to enter fullscreen or have the cockpit MFD panel
            // bound (docs/rc-page.md). Level-triggered, so this runs every tick we still want
            // frames; safe no-op if that mod isn't installed or is a version without Bridge.
            McBridge.RequestCapture(true);

            // Prefer the base mod's own Bridge texture (works headless, per above — and is the
            // actual authoritative output, not read off the camera mid-render — see McBridge.cs).
            // Fall back to RcBridge's fullscreen-gated camera.targetTexture for older MissileCamera
            // installs without a Bridge — same picture, just requires the pilot to actually be in
            // fullscreen for it to be non-null/valid.
            Texture? src;
            bool haveCam;   // only meaningful on the fallback path — see the enabled check below
            if (McBridge.Available)
            {
                src = McBridge.FeedTexture;
                haveCam = true;
            }
            else
            {
                Camera? cam = RcBridge.FeedCamera;
                haveCam = cam != null && cam.enabled;
                src = haveCam ? cam!.targetTexture : null;
            }
            if (!haveCam || src == null)
            {
                TelemetryServer.ClearRcFrame();
                _active = false;
                if (_hadSrc)
                {
                    // Transition log (not a one-shot like _srcLogged below) — if capture silently
                    // stops mid-session, this is what tells us when/why on the next log, instead of
                    // total silence after the one-time startup dump.
                    _hadSrc = false;
                    Plugin.Log?.LogWarning("[NOXMFD] RC: feed became unavailable "
                        + $"(McBridge.Available={McBridge.Available}, HasTrackableMissile={McBridge.HasTrackableMissile}, "
                        + $"haveCam={haveCam}).");
                }
                return;
            }
            if (!_hadSrc)
            {
                _hadSrc = true;
                Plugin.Log?.LogInfo("[NOXMFD] RC: feed (re)available.");
            }

            // Match the captured frame to the source's aspect ratio — see TgpFeed for why (avoids
            // squashing a wider-than-tall feed; the MFD page letterboxes with object-fit:contain).
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
                Plugin.Log?.LogInfo($"[NOXMFD] RC source texture {sw}x{sh} (aspect {(float)sw / sh:0.000}); capturing at {targetW}x{targetH}.");

                // Diagnostic: camera state itself, when we have one to inspect (McBridge only
                // exposes the texture, so fetch its Camera separately just for this one-time log —
                // harmless extra reflection call, not on the hot path since _srcLogged guards it).
                Camera? diagCam = McBridge.Available ? McBridge.FeedCamera : null;
                if (diagCam != null)
                {
                    Plugin.Log?.LogInfo($"[NOXMFD] RC feed camera diag: enabled={diagCam.enabled}, "
                        + $"cullingMask={diagCam.cullingMask}, clip=[{diagCam.nearClipPlane:0.###},{diagCam.farClipPlane:0.#}], "
                        + $"fov={diagCam.fieldOfView:0.#}, targetTexture={(diagCam.targetTexture != null ? diagCam.targetTexture.GetInstanceID().ToString() : "null")}, "
                        + $"feedTexture={src.GetInstanceID()}, sameRT={(diagCam.targetTexture == src)}.");
                }
            }

            // Don't stack readbacks — drop this tick if the GPU is still finishing the last one.
            if (_readbackInFlight) return;

            if (_rt == null || _rt.width != targetW || _rt.height != targetH)
            {
                if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
                _rt = new RenderTexture(targetW, targetH, 0, RenderTextureFormat.ARGB32);
                _rt.Create();
            }
            if (_tex == null || _tex.width != targetW || _tex.height != targetH)
            {
                if (_tex != null) Object.Destroy(_tex);
                _tex = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
            }

            Graphics.Blit(src, _rt);
            _readbackInFlight = true;
            int captureW = targetW;
            int captureH = targetH;
            AsyncGPUReadback.Request(_rt, 0, request => OnReadbackComplete(request, captureW, captureH));
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest request, int w, int h)
        {
            _readbackInFlight = false;
            if (request.hasError) return;
            if (!TelemetryServer.WantsRcFrames) return;                 // disengaged while in flight
            if (_tex == null || _tex.width != w || _tex.height != h) return;

            var data = request.GetData<byte>();

            // One-time diagnostic: is the captured buffer actually black, or does it have real
            // content that's getting lost downstream (JPEG encode / MJPEG serving / browser)?
            // Sampled every 97th byte (prime, avoids landing on the same channel every time) so
            // this stays cheap even though it only ever runs once per RcFeed instance.
            if (!_pixelDiagLogged)
            {
                _pixelDiagLogged = true;
                long sum = 0; int n = 0;
                for (int i = 0; i < data.Length; i += 97) { sum += data[i]; n++; }
                double avg = n > 0 ? (double)sum / n : -1;
                Plugin.Log?.LogInfo($"[NOXMFD] RC frame diag: {w}x{h}, avg sampled byte ≈ {avg:0.0} (0=black, 255=white/full).");
            }

            _tex.LoadRawTextureData(data);
            _tex.Apply(false, false);

            byte[] jpg = _tex.EncodeToJPG(JpegQuality);
            TelemetryServer.PushRcFrame(jpg);
            _active  = true;
            _engaged = true;
        }

        public void Disengage()
        {
            McBridge.RequestCapture(false);   // e.g. OnDestroy calling this directly, not via CaptureFrame
            if (_rt  != null) { _rt.Release();  Object.Destroy(_rt);  _rt  = null; }
            if (_tex != null) {                 Object.Destroy(_tex); _tex = null; }

            bool wasEngaged   = _engaged;
            _engaged          = false;
            _active           = false;
            _srcLogged        = false;
            _hadSrc           = false;
            _readbackInFlight = false;
            TelemetryServer.ClearRcFrame();
            if (wasEngaged) Plugin.Log?.LogInfo("[NOXMFD] RC: disengaged (no subscribers).");
        }
    }
}
