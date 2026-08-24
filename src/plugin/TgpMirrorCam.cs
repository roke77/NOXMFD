using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NOXMFD
{
    // Native is the default and unchanged path (TgpFeed.cs reads the game's own
    // TargetCam.cam.targetTexture directly — no mirror camera involved). HighQuality reads from
    // TgpMirrorCam's own higher-resolution RenderTexture instead, rendered every Unity frame like a
    // normal camera (enabled + URP Base) rather than only at the TGP capture rate — the only tier
    // this camera supports; a cheaper disabled/manual-render tier measured no cheaper on average
    // (docs/performance.md) while losing tree/grass detail, so it isn't worth the extra mode.
    internal enum TgpQuality { Native, HighQuality }

    // A second camera, parented to TargetCam's active mount (docs/tgp-high-quality-mode.md), used
    // only when TgpFeed.Quality != Native. Never touches TargetCam.cam, its UICam, or anything
    // CameraStateManager owns — see Mursisru/MissileCamera's Fullscreen/CAMERA_SAFETY.md, which
    // documents a real incident from a *different* approach (reparenting/overlaying the vanilla
    // camera) that this design avoids entirely by creating an independent camera instead.
    //
    // Renders correct tree/grass detail with no terrain shader-global sync or DetailRenderer.camera
    // hijack (unlike that reference mod's MissileCameraRenderPrep.cs) — a plain enabled+Base camera
    // is enough on its own.
    internal sealed class TgpMirrorCam
    {
        private const float NearClip = 2f;
        private const float FarClip  = 60000f;

        private GameObject? _root;
        private Camera?     _cam;
        private RenderTexture? _rt;
        private int _texW, _texH;

        internal Texture? Texture => _rt;

        // WorldToViewportPoint, not WorldToScreenPoint — the box overlay needs a resolution-
        // independent [0,1] fraction it can position a CSS box with directly, not render-texture
        // pixels. z is a behind-camera/inactive sentinel (<=0) when there's no camera yet.
        internal Vector3 WorldToViewport(Vector3 worldPos) =>
            _cam != null ? _cam.WorldToViewportPoint(worldPos) : new Vector3(0f, 0f, -1f);

        // Creates the rig on first use, reparents if the active mount changed (forward/rear/
        // landing), and reallocates the RT if the requested size changed. Cheap to call every
        // capture tick — all the real work is gated on an actual difference.
        internal void Engage(TargetCam tc, int width, int height)
        {
            Transform? mount = tc.GetCamMount();
            if (mount == null) return;

            if (_root == null)
            {
                _root = new GameObject("NOXMFD.TgpMirrorCam");
                _cam = _root.AddComponent<Camera>();
                _cam.stereoTargetEye = StereoTargetEyeMask.None;
                _cam.useOcclusionCulling = false;
                _cam.nearClipPlane = NearClip;
                _cam.farClipPlane  = FarClip;
                UniversalAdditionalCameraData urp = _cam.GetUniversalAdditionalCameraData();
                urp.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
                urp.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;
                urp.renderType = CameraRenderType.Base;
                // A freshly created UniversalAdditionalCameraData defaults this to false, which
                // skips the scene's global post-process Volume (tonemapping/color grading)
                // entirely — the washed-out, low-contrast look the HQ feed had next to the real
                // TargetCam picture (which, being a normal in-scene camera, already gets it).
                urp.renderPostProcessing = true;
                _cam.enabled = true;
            }

            if (_cam!.transform.parent != mount)
            {
                _cam.transform.SetParent(mount, worldPositionStays: false);
                _cam.transform.localPosition = Vector3.zero;
                _cam.transform.localRotation = Quaternion.identity;
            }

            if (_rt == null || _texW != width || _texH != height)
            {
                if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
                _rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
                {
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear,
                };
                _rt.Create();
                _texW = width;
                _texH = height;
                _cam.targetTexture = _rt;
            }
        }

        // Copies the source TargetCam's per-tick state — zoom FOV above all, since that's what
        // gives the feed its "zoom on target" behavior. Cheap; called every capture tick.
        internal void SyncFromSource(Camera src)
        {
            if (_cam == null) return;
            _cam.fieldOfView = src.fieldOfView;
            if (_cam.cullingMask != src.cullingMask) _cam.cullingMask = src.cullingMask;
            if (_cam.allowHDR    != src.allowHDR)    _cam.allowHDR    = src.allowHDR;
            if (_cam.allowMSAA   != src.allowMSAA)   _cam.allowMSAA   = src.allowMSAA;
            if (_cam.clearFlags  != src.clearFlags)  _cam.clearFlags  = src.clearFlags;
        }

        internal void Disengage()
        {
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); _rt = null; }
            if (_root != null) { Object.Destroy(_root); _root = null; _cam = null; }
            _texW = _texH = 0;
        }
    }
}
