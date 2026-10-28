using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Battle damage FAIL screen for an internal MFD pane (docs/battle-damage.md): an opaque cover
    // with a red "<SYSTEM> FAIL" banner, the in-cockpit twin of services/battle-damage.js.
    internal sealed class InternalMfdFailScreen
    {
        internal static readonly Color FailColor = new Color(1f, 0.25f, 0.25f, 0.9f);

        private readonly GameObject _root;
        private bool _up;

        internal InternalMfdFailScreen(RectTransform parent, Font? font, string banner)
        {
            _root = InternalMfdUi.NewUi("FailScreen", parent, parent.gameObject.layer, typeof(Image));
            InternalMfdUi.Stretch(_root.GetComponent<RectTransform>());
            var cover = _root.GetComponent<Image>();
            cover.color = Color.black;
            cover.raycastTarget = false;

            var textGo = InternalMfdUi.NewUi("FailBanner", _root.transform, parent.gameObject.layer, typeof(Text));
            InternalMfdUi.Stretch(textGo.GetComponent<RectTransform>());
            var text = textGo.GetComponent<Text>();
            text.font = font;
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = FailColor;
            text.text = banner;
            text.raycastTarget = false;
            _root.SetActive(false);
        }

        // Returns whether the screen is up, so the page can skip drawing what it covers.
        internal bool Update(bool failed)
        {
            if (failed == _up) return failed;
            _up = failed;
            _root.SetActive(failed);
            if (failed) _root.transform.SetAsLastSibling();   // above the page's own content
            return failed;
        }
    }
}
