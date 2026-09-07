using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // TGP camera feed for the internal MFD's left pane. Reads the same TargetCam.cam the game
    // itself drives — a real weapon lock and TgpManualControl's manual pan/tilt/zoom both end up
    // writing to that one field, so this needs no branching between the two: whichever owns the
    // camera right now is simply what's on screen, exactly like TgpFullScreen.cs's own Tick()
    // already does for the cinematic full-screen view. Unlike TgpFeed.cs's web pipeline, there's
    // no JPEG round-trip here — a RawImage can point straight at the mirror camera's own
    // RenderTexture, since both live in the same process.
    //
    // Owns a dedicated TgpMirrorCam instance: TgpFeed's (web /tgp page) and TgpFullScreen's own
    // are each sized and engaged for their own consumer, and three consumers fighting over one
    // camera/RT's size each tick would thrash it.
    internal sealed class InternalMfdTgpPage : IInternalMfdPage
    {
        // Small on purpose — this pane is already a fraction of the T/A-30's center screen (itself
        // cropped from a shared 1024x512 cockpit texture, docs/internal-mfd.md), so anything past
        // "looks sharp at that final size" is wasted GPU/readback-free but still real render cost.
        private const int FeedWidth = 512;
        private const int FeedHeight = 384;

        private static readonly Color NoFeedColor = new Color(1f, 1f, 1f, 0.5f);

        private readonly TgpMirrorCam _mirror = new TgpMirrorCam();
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
            Transform? mount = TgpManualTargetCamAccess.GetMount(tc);
            if (cam == null || !cam.enabled || mount == null)
            {
                ShowNoFeed();
                return;
            }

            _mirror.Engage(tc, FeedWidth, FeedHeight);
            _mirror.SyncFromSource(cam);
            _mirror.SetInfrared(tc.UsingIR());

            _feedImage.texture = _mirror.Texture;
            _feedImage.enabled = true;
            _statusText.enabled = false;
        }

        private void ShowNoFeed()
        {
            _feedImage.enabled = false;
            _statusText.enabled = true;
        }

        // The mirror camera's rig is parented to the TargetCam mount, outside this page's own UI
        // subtree — Destroy(_overlay) alone would leak it, so the controller calls this before
        // dropping its reference to the page.
        public void Teardown()
        {
            _mirror.Disengage();
        }
    }
}
