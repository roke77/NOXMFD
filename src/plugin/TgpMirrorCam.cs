using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NOXMFD
{
    // Native is the default and unchanged path (TgpFeed.cs reads the game's own
    // TargetCam.cam.targetTexture directly — no mirror camera involved). HighQuality reads from
    // TgpMirrorCam's own higher-resolution RenderTexture instead, rendered every Unity frame like a
    // normal camera (enabled + URP Base) rather than only at the TGP capture rate.
    //
    // An earlier build of this branch also had a cheaper "Performance" tier (camera disabled/URP
    // Overlay, driven by an explicit Camera.Render() only on capture ticks) that traded away tree/
    // grass rendering for a lower cost. Live A/B testing (docs/performance.md, 2026-08-23) found
    // its main-thread frame cost was actually *higher* on average than just rendering every frame —
    // the manual Camera.Render() call's cost showed up synchronously in that tick instead of being
    // absorbed into the normal render loop — so it bought a real visual downgrade (no tree/grass
    // detail) for no measured benefit. Dropped; HighQuality is now the only HQ tier.
    internal enum TgpQuality { Native, HighQuality }

    // A second camera, parented to TargetCam's active mount (docs/tgp-high-quality-mode.md), used
    // only when TgpFeed.Quality != Native. Never touches TargetCam.cam, its UICam, or anything
    // CameraStateManager owns — see Mursisru/MissileCamera's Fullscreen/CAMERA_SAFETY.md, which
    // documents a real incident from a *different* approach (reparenting/overlaying the vanilla
    // camera) that this design avoids entirely by creating an independent camera instead.
    //
    // Deliberately does NOT replicate that reference mod's MissileCameraRenderPrep.cs (terrain
    // shader-global sync + a private-field DetailRenderer.camera hijack that redirects tree/grass
    // culling to the manual camera) — live testing (2026-08-23) already shows correct tree/grass
    // rendering without it, so there's nothing currently known to port. BeforeRender/AfterRender
    // stay as an explicit extension point in case a future case turns up where it's needed.
    internal sealed class TgpMirrorCam
    {
        private const float NearClip = 2f;
        private const float FarClip  = 60000f;

        private GameObject? _root;
        private Camera?     _cam;
        private RenderTexture? _rt;
        private int _texW, _texH;

        internal Texture? Texture => _rt;

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

        // The camera is enabled + Base once Engage()'d, so the pipeline renders it every Unity
        // frame on its own — this is only the hook for any per-tick prep a future case needs
        // (terrain/shader-global sync etc.), currently a no-op (see class comment).
        internal void RenderTick()
        {
            if (_cam != null) BeforeRender(_cam);
        }

        private static void BeforeRender(Camera feedCamera) { }

        internal void Disengage()
        {
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); _rt = null; }
            if (_root != null) { Object.Destroy(_root); _root = null; _cam = null; }
            _texW = _texH = 0;
        }
    }
}
