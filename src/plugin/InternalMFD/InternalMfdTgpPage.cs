using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Placeholder for the TGP pane's native content — not yet implemented, just a labeled panel.
    // Exists to prove a pane can host arbitrary future page content via IInternalMfdPage, the same
    // way InternalMfdRwrPage already does for RWR; the real TGP page (target camera feed / manual
    // control readout) is a separate, not-yet-scoped follow-up.
    internal sealed class InternalMfdTgpPage : IInternalMfdPage
    {
        private static readonly Color TextColor = new Color(1f, 1f, 1f, 0.5f);

        internal InternalMfdTgpPage(RectTransform parent, Font? font)
        {
            var go = InternalMfdUi.NewUi("TgpPlaceholder", parent, parent.gameObject.layer, typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = TextColor;
            text.text = "TGP";
            text.raycastTarget = false;
        }

        // Nothing live yet — placeholder only.
        public void Refresh(TelemetrySnapshot snap)
        {
        }
    }
}
