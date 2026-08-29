using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NOXMFD
{
    // MID and HIGH use this camera at different RenderTexture sizes. It stays enabled as a URP Base
    // camera because manual Camera.Render() loses tree/grass detail in this game.
    // A second camera, parented to TargetCam's active mount (docs/tgp-high-quality-mode.md), used
    // only when TgpFeed.Resolution != Native. It must not reparent or alter TargetCam.cam, UICam,
    // CameraStateManager.mainCamera, or cameraPivot: those objects own the game's active camera
    // state, and changing their hierarchy or pose can corrupt cockpit-camera restoration.
    //
    // A continuously enabled URP Base camera renders the required tree and grass detail without
    // terrain shader-global synchronization or replacing DetailRenderer.camera.
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
