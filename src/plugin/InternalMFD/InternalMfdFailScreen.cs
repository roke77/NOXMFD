using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Battle damage FAIL screen for an internal MFD pane (docs/battle-damage.md): an opaque cover
    // with a red "<SYSTEM> FAIL" banner, the in-cockpit twin of services/battle-damage.js. It
    // flickers for ~3 s after going up — sampled at the controller's 10 Hz refresh — then holds.
    internal sealed class InternalMfdFailScreen
    {
        internal static readonly Color FailColor = new Color(1f, 0.25f, 0.25f, 0.9f);
        private const float FlickerSeconds = 3f;

        private readonly GameObject _root;
        private readonly Image _cover;
        private readonly Text _banner;
        private float _since = -1f;   // Time.time the screen went up; -1 = down

        internal InternalMfdFailScreen(RectTransform parent, Font? font, string banner)
        {
            _root = InternalMfdUi.NewUi("FailScreen", parent, parent.gameObject.layer, typeof(Image));
            InternalMfdUi.Stretch(_root.GetComponent<RectTransform>());
            _cover = _root.GetComponent<Image>();
            _cover.color = Color.black;
            _cover.raycastTarget = false;

            var textGo = InternalMfdUi.NewUi("FailBanner", _root.transform, parent.gameObject.layer, typeof(Text));
            InternalMfdUi.Stretch(textGo.GetComponent<RectTransform>());
            _banner = textGo.GetComponent<Text>();
            _banner.font = font;
            _banner.fontSize = 22;
            _banner.alignment = TextAnchor.MiddleCenter;
            _banner.color = FailColor;
            _banner.text = banner;
            _banner.raycastTarget = false;
            _root.SetActive(false);
        }

        // Returns whether the screen is up, so the page can skip drawing what it covers. While the
        // flicker runs, the off phases show the page's last drawn frame underneath.
        internal bool Update(bool failed)
        {
            if (!failed)
            {
                if (_since >= 0f) { _since = -1f; _root.SetActive(false); }
                return false;
            }
            if (_since < 0f)
            {
                _since = Time.time;
                _root.SetActive(true);
                _root.transform.SetAsLastSibling();   // above the page's own content
            }
            float t = Time.time - _since;
            _cover.enabled = _banner.enabled = t >= FlickerSeconds || Mathf.Sin(t * 37f) + Mathf.Sin(t * 11f) > 0f;
            return true;
        }
    }
}
