using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // TGP camera feed for the internal MFD's left pane. Points the RawImage straight at
    // TargetCam.cam's own targetTexture — the exact RenderTexture the physical in-cockpit TGP
    // screen already displays every frame (TgpFeed.cs's own "Native" capture path reads this same
    // field first, for the same reason: it's the native camera the game itself renders, smoothly
    // zooms, and already bakes its IR look into). A RenderTexture is a live GPU resource — once
    // the reference is set, the picture updates on its own every frame in lockstep with the native
    // screen, no per-tick work needed here at all.
    //
    // Deliberately not a second, independent mirror camera with its FOV copied from the native one
    // each Refresh: Refresh only runs at the controller's 10Hz page-refresh rate, while the native
    // camera's own zoom animates smoothly every frame — sampling that animation 10 times a second
    // and holding it steady in between reads as laggy, stuttering zoom next to the native screen's
    // always-live picture, on top of rendering the same scene a second time for no benefit. Reading
    // the native RT directly has neither problem: no second render, no sync to fall behind.
    //
    // Real lock and TgpManualControl's manual pan/tilt/zoom both drive this exact same
    // TargetCam.cam, so nothing here needs to branch between the two.
    internal sealed class InternalMfdTgpPage : IInternalMfdPage
    {
        private static readonly Color NoFeedColor = new Color(1f, 1f, 1f, 0.5f);

        private readonly RawImage _feedImage;
        private readonly Text _statusText;

        internal InternalMfdTgpPage(RectTransform parent, Font? font)
        {
            var feedGo = InternalMfdUi.NewUi("TgpFeed", parent, parent.gameObject.layer, typeof(RawImage));
            var feedRt = feedGo.GetComponent<RectTransform>();
            feedRt.anchorMin = Vector2.zero;
            feedRt.anchorMax = Vector2.one;
            feedRt.offsetMin = feedRt.offsetMax = Vector2.zero;
            _feedImage = feedGo.GetComponent<RawImage>();
            _feedImage.color = Color.white;
            _feedImage.raycastTarget = false;
            _feedImage.enabled = false;

            var textGo = InternalMfdUi.NewUi("TgpStatus", parent, parent.gameObject.layer, typeof(Text));
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            _statusText = textGo.GetComponent<Text>();
            _statusText.font = font;
            _statusText.fontSize = 22;
            _statusText.alignment = TextAnchor.MiddleCenter;
            _statusText.color = NoFeedColor;
            _statusText.text = "TGP";
            _statusText.raycastTarget = false;
        }

        public void Refresh(TelemetrySnapshot snap)
        {
            if (!GameManager.GetLocalAircraft(out Aircraft ac) || ac == null || ac.targetCam == null)
            {
                ShowNoFeed();
                return;
            }

            TargetCam tc = ac.targetCam;
            if (!TgpManualTargetCamAccess.Ensure() || TgpManualTargetCamAccess.IsLandingMode(tc))
            {
                ShowNoFeed();
                return;
            }

            Camera? cam = TgpManualTargetCamAccess.GetCamera(tc);
            if (cam == null || !cam.enabled || cam.targetTexture == null)
            {
                ShowNoFeed();
                return;
            }

            _feedImage.texture = cam.targetTexture;
            _feedImage.enabled = true;
            _statusText.enabled = false;
        }

        private void ShowNoFeed()
        {
            _feedImage.enabled = false;
            _statusText.enabled = true;
        }
    }
}
