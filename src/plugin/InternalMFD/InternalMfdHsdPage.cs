using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Native HSD plan view for the internal MFD's left pane (default content — InternalMfdController
    // swaps this pane over to InternalMfdTgpPage instead while the TGP has a lock or manual mode is
    // engaged, then back to this once neither is true). Matched against the real page's own SVG
    // (src/web/pages/hsd/hsd.html/hsd.js): grid ring color/opacity (theme.css --no-hsd-pink-rgb),
    // contact colors (--no-purple datalink / --no-red own-radar / --no-white stale / --no-amber
    // focused-lock), the AA threat rings (--no-hsd-yellow), and the notched contact/ownship icon
    // polygon (hsd.js's own 'M0 -9 L-6 7 L0 4 L6 7 Z' — reused verbatim for both, filled, unlike
    // RWR's stroke-only ownship caret) are all taken directly from that source.
    //
    // Deliberately simplified from the real page in ways that need a cursor/bezel input this pane
    // doesn't have wired up yet: always CEN mode (no DEP), a fixed range (RangeM below, not the
    // R+/R- range ladder), no radar cone overlay, no active-route line, and no PAD acquisition
    // cursor (contacts can't be selected/deselected from here). Upgrade path if any of these turn
    // out to matter live: DEP/range would need their own keybinds routed through
    // InternalMfdController the way TGP's toggle already is; the cursor would need a HOTAS axis
    // mapped to a screen-space position the way pad-cursor.js's onMove does client-side.
    //
    // Azimuth/distance math mirrors telemetry-source.js's own HSD plot (az = bearing-relative-to-
    // heading via atan2, distFrac = dist/RangeM) — same "ownship-relative, nose-up screen
    // coordinate" frame InternalMfdRwrPage already uses for RWR, off the same single
    // TelemetrySnapshot InternalMfdController passes into Refresh.
    internal sealed class InternalMfdHsdPage : IInternalMfdPage
    {
        // Fixed at CEN mode's default range index (hsd.js: CEN_RANGE_NM[2] = 40) — see the
        // simplification note above.
        private const float RangeM = 40f * 1852f;
        private const float FillFrac = 1f;

        // gridFractions() for CEN mode (hsd.js) — four quarter-range rings, all non-dashed, the
        // outermost drawn brighter/thicker (hsd.js: stroke-opacity 0.70 vs 0.36 for f===1).
        private static readonly float[] GridFractions = { 0.25f, 0.5f, 0.75f, 1f };

        // theme.css --no-hsd-pink-rgb (121,21,81) — hsd.js's own grid ring color, not the brighter
        // --no-purple contact color below (same file, two different tokens for two different uses).
        private static readonly Color32 GridColorDim = new Color32(121, 21, 81, 92);   // 0.36 alpha
        private static readonly Color32 GridColorBright = new Color32(121, 21, 81, 178); // 0.70 alpha
        private static readonly Color OwnshipColor = new Color(1f, 1f, 1f, 0.78f);
        private static readonly Color HeadingTickColor = new Color(1f, 1f, 1f, 0.45f);

        // hsd.js's contactColor(): focused-locked overrides everything else, then stale, then own-
        // radar, then plain datalink purple.
        private static readonly Color ContactPurple = new Color32(179, 136, 255, 255);
        private static readonly Color ContactRed = new Color32(255, 64, 64, 255);
        private static readonly Color ContactStaleWhite = new Color32(230, 235, 239, 255);
        private static readonly Color ContactAmber = new Color32(255, 170, 0, 255);
        private static readonly Color ThreatYellow = new Color32(224, 194, 0, 191); // stroke-opacity 0.75

        private readonly RectTransform _center;
        private readonly int _layer;
        private readonly Font? _font;
        private readonly float _diameter;
        private readonly float _radius;

        private readonly Text _rangeText;
        private readonly Text _linkText;
        private readonly Text _lockText;
        private readonly Text _focusedNameText;
        private readonly Text _focusedDetailText;

        // Grow-on-demand pools — same reasoning as InternalMfdRwrPage's contact/missile pools: HSD
        // has no fixed contact/threat cap either (hsd.js's own renderContacts/renderThreats iterate
        // whatever state.items/threats hold), so an artificial ceiling here would silently drop real
        // contacts past whatever number was picked.
        private readonly List<Image> _contactIcons = new List<Image>();
        private readonly List<RectTransform> _contactVectors = new List<RectTransform>();
        private readonly List<Image> _lockRings = new List<Image>();
        private readonly List<Image> _threatRings = new List<Image>();

        internal InternalMfdHsdPage(RectTransform parent, Font? font)
        {
            int layer = parent.gameObject.layer;
            _layer = layer;
            _font = font;

            _diameter = Mathf.Min(parent.rect.width, parent.rect.height) * FillFrac;
            _radius = _diameter / 2f;

            var scopeGo = InternalMfdUi.NewUi("Scope", parent, layer, typeof(RectTransform));
            _center = scopeGo.GetComponent<RectTransform>();
            _center.anchorMin = _center.anchorMax = new Vector2(0.5f, 0.5f);
            _center.sizeDelta = new Vector2(_diameter, _diameter);

            Sprite ring = InternalMfdUi.ResolveRingSprite(dashed: false);
            for (int i = 0; i < GridFractions.Length; i++)
            {
                bool outer = i == GridFractions.Length - 1;
                BuildRing(ring, _diameter * GridFractions[i], outer ? GridColorBright : GridColorDim);
            }

            BuildOwnship();

            _rangeText = BuildCornerText("RangeText", new Vector2(1f, 1f), TextAnchor.UpperRight, 16);
            _linkText = BuildCornerText("LinkText", new Vector2(1f, 0f), TextAnchor.LowerRight, 14);
            _lockText = BuildCornerText("LockText", new Vector2(1f, 0f), TextAnchor.LowerRight, 14);
            _lockText.rectTransform.anchoredPosition += new Vector2(0f, 18f); // stacked above LinkText
            _focusedNameText = BuildCornerText("FocusedName", new Vector2(0f, 0f), TextAnchor.LowerLeft, 15);
            _focusedNameText.rectTransform.anchoredPosition += new Vector2(0f, 18f);
            _focusedDetailText = BuildCornerText("FocusedDetail", new Vector2(0f, 0f), TextAnchor.LowerLeft, 13);
            _focusedNameText.color = _focusedDetailText.color = ContactAmber;

            _rangeText.text = "CEN " + Mathf.RoundToInt(RangeM / 1852f) + "NM";
        }

        private void EnsureContactPool(int count)
        {
            while (_contactIcons.Count < count)
            {
                var iconGo = InternalMfdUi.NewUi($"HsdContact{_contactIcons.Count}", _center, _layer, typeof(Image));
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                iconRt.sizeDelta = new Vector2(0.045f * _diameter, 0.06f * _diameter); // ~12:16 aspect
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = ResolveIconSprite();
                iconImg.raycastTarget = false;
                iconGo.SetActive(false);
                _contactIcons.Add(iconImg);

                var vecGo = InternalMfdUi.NewUi($"HsdContactVec{_contactVectors.Count}", _center, _layer, typeof(Image));
                var vecRt = vecGo.GetComponent<RectTransform>();
                vecRt.anchorMin = vecRt.anchorMax = new Vector2(0.5f, 0.5f);
                vecRt.pivot = new Vector2(0.5f, 0f);
                vecRt.sizeDelta = new Vector2(0.006f * _diameter, 0.06f * _diameter); // hsd.js: 18/600 of viewBox
                var vecImg = vecGo.GetComponent<Image>();
                vecImg.raycastTarget = false;
                vecGo.SetActive(false);
                _contactVectors.Add(vecRt);

                var lockGo = InternalMfdUi.NewUi($"HsdLock{_lockRings.Count}", _center, _layer, typeof(Image));
                var lockRt = lockGo.GetComponent<RectTransform>();
                lockRt.anchorMin = lockRt.anchorMax = new Vector2(0.5f, 0.5f);
                lockRt.sizeDelta = new Vector2(0.033f * _diameter, 0.033f * _diameter); // hsd.js: r=10 of 600
                var lockImg = lockGo.GetComponent<Image>();
                lockImg.sprite = InternalMfdUi.ResolveRingSprite(dashed: false);
                lockImg.color = ContactAmber;
                lockImg.raycastTarget = false;
                lockGo.SetActive(false);
                _lockRings.Add(lockImg);
            }
        }

        private void EnsureThreatPool(int count)
        {
            while (_threatRings.Count < count)
            {
                var go = InternalMfdUi.NewUi($"HsdThreat{_threatRings.Count}", _center, _layer, typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                var img = go.GetComponent<Image>();
                img.sprite = InternalMfdUi.ResolveRingSprite(dashed: false);
                img.color = ThreatYellow;
                img.raycastTarget = false;
                go.SetActive(false);
                _threatRings.Add(img);
            }
        }

        public void Refresh(TelemetrySnapshot snap)
        {
            HsdThreat[] threats = snap.HsdThreats ?? Array.Empty<HsdThreat>();
            EnsureThreatPool(threats.Length);
            for (int i = 0; i < _threatRings.Count; i++)
            {
                bool active = i < threats.Length;
                _threatRings[i].gameObject.SetActive(active);
                if (!active) continue;

                HsdThreat t = threats[i];
                float dx = t.X - snap.WorldX, dz = t.Z - snap.WorldZ;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (t.Range <= 0f || dist > RangeM)
                {
                    _threatRings[i].gameObject.SetActive(false);
                    continue;
                }
                Vector2 pos = PolarToLocal(Azimuth(snap, t.X, t.Z), dist / RangeM);
                float ringDiameter = 2f * _radius * (t.Range / RangeM);
                _threatRings[i].rectTransform.anchoredPosition = pos;
                _threatRings[i].rectTransform.sizeDelta = new Vector2(ringDiameter, ringDiameter);
            }

            HsdContact[] contacts = snap.Hsd ?? Array.Empty<HsdContact>();
            EnsureContactPool(contacts.Length);
            int linkCount = 0, lockCount = 0;
            HsdContact? focused = null;
            float focusedDist = 0f;
            for (int i = 0; i < _contactIcons.Count; i++)
            {
                bool active = i < contacts.Length;
                _contactIcons[i].gameObject.SetActive(active);
                _contactVectors[i].gameObject.SetActive(active);
                if (!active)
                {
                    _lockRings[i].gameObject.SetActive(false);
                    continue;
                }

                HsdContact c = contacts[i];
                float dx = c.X - snap.WorldX, dz = c.Z - snap.WorldZ;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > RangeM)
                {
                    _contactIcons[i].gameObject.SetActive(false);
                    _contactVectors[i].gameObject.SetActive(false);
                    _lockRings[i].gameObject.SetActive(false);
                    continue;
                }
                linkCount++;

                bool isFocused = c.Targeted && c.Id == snap.FocusedTargetId;
                if (c.Targeted)
                {
                    lockCount++;
                    if (isFocused) { focused = c; focusedDist = dist; }
                }
                Color color = isFocused ? ContactAmber
                    : c.Stale ? ContactStaleWhite
                    : c.Radar ? ContactRed
                    : ContactPurple;

                Vector2 pos = PolarToLocal(Azimuth(snap, c.X, c.Z), dist / RangeM);
                float rot = ((c.Heading - snap.Heading) % 360f + 360f) % 360f;

                _contactIcons[i].rectTransform.anchoredPosition = pos;
                _contactIcons[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -rot);
                _contactIcons[i].color = color;

                _contactVectors[i].anchoredPosition = pos;
                _contactVectors[i].localRotation = Quaternion.Euler(0f, 0f, -rot);
                _contactVectors[i].GetComponent<Image>().color = color;

                _lockRings[i].gameObject.SetActive(c.Targeted);
                if (c.Targeted) _lockRings[i].rectTransform.anchoredPosition = pos;
            }

            _linkText.text = linkCount > 0 ? "LINK " + linkCount : "LINK 0";
            _lockText.text = lockCount > 0 ? "LOCK " + lockCount : "";

            if (focused.HasValue)
            {
                HsdContact c = focused.Value;
                _focusedNameText.text = ShortName(c.Name);
                _focusedDetailText.text = "RNG " + UnitConverter.DistanceReading(focusedDist) +
                                           "   ALT " + UnitConverter.AltitudeReading(c.Alt) +
                                           "   HDG " + Pad3(c.Heading);
            }
            else
            {
                _focusedNameText.text = "";
                _focusedDetailText.text = "";
            }
        }

        // Degrees clockwise from the nose — same convention/helper as InternalMfdRwrPage's own
        // Azimuth (see that file's comment); duplicated rather than shared since each page ties it
        // to its own _radius-scaled PolarToLocal.
        private static float Azimuth(TelemetrySnapshot snap, float x, float z)
            => HudWaypointCueMath.BearingDeg(snap.WorldX, snap.WorldZ, x, z) - snap.Heading;

        private Vector2 PolarToLocal(float azDeg, float distFrac)
        {
            float rad = azDeg * Mathf.Deg2Rad;
            float r = distFrac * _radius;
            return new Vector2(Mathf.Sin(rad) * r, Mathf.Cos(rad) * r);
        }

        // hsd.js's short(): upper-cased, capped at 18 chars (BOGEY fallback for an empty name).
        private static string ShortName(string? n)
        {
            string s = (string.IsNullOrEmpty(n) ? "BOGEY" : n!).ToUpperInvariant();
            return s.Length > 18 ? s.Substring(0, 18) : s;
        }

        // hsd.js's pad3(): zero-padded 3-digit heading, wrapped into 0..359.
        private static string Pad3(float headingDeg)
        {
            int h = ((Mathf.RoundToInt(headingDeg) % 360) + 360) % 360;
            return h.ToString("000");
        }

        private void BuildRing(Sprite sprite, float diameter, Color color)
        {
            var go = InternalMfdUi.NewUi("Ring", _center, _layer, typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(diameter, diameter);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
        }

        // hsd.js's renderOwnship(): the same notched-arrow icon contacts use, filled white, fixed at
        // screen centre pointing straight up (the whole view is already heading-up), plus a short
        // forward tick above the nose.
        private void BuildOwnship()
        {
            var go = InternalMfdUi.NewUi("Ownship", _center, _layer, typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0.05f * _diameter, 0.067f * _diameter);
            var img = go.GetComponent<Image>();
            img.sprite = ResolveIconSprite();
            img.color = OwnshipColor;
            img.raycastTarget = false;

            var tickGo = InternalMfdUi.NewUi("OwnshipTick", _center, _layer, typeof(Image));
            var tickRt = tickGo.GetComponent<RectTransform>();
            tickRt.anchorMin = tickRt.anchorMax = new Vector2(0.5f, 0.5f);
            tickRt.pivot = new Vector2(0.5f, 0f);
            tickRt.sizeDelta = new Vector2(0.006f * _diameter, 0.03f * _diameter);
            tickRt.anchoredPosition = new Vector2(0f, 0.03f * _diameter);
            var tickImg = tickGo.GetComponent<Image>();
            tickImg.color = HeadingTickColor;
            tickImg.raycastTarget = false;
        }

        private Text BuildCornerText(string name, Vector2 corner, TextAnchor align, int fontSize)
        {
            var go = InternalMfdUi.NewUi(name, _center, _layer, typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = corner;
            rt.pivot = corner;
            rt.sizeDelta = new Vector2(0.4f * _diameter, 20f);
            const float pad = 10f;
            rt.anchoredPosition = new Vector2((corner.x - 0.5f) * -2f * pad, (corner.y - 0.5f) * -2f * pad);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = align;
            text.color = new Color(1f, 1f, 1f, 0.85f);
            text.raycastTarget = false;
            return text;
        }

        private static Sprite? _iconSprite;

        // hsd.js's shared notched-arrow polygon ('M0 -9 L-6 7 L0 4 L6 7 Z', SVG-space, origin at the
        // icon's own centre) — used FILLED here for both ownship and every contact (unlike RWR's
        // ownship caret, which is stroke-only). Split into two triangles (apex/back-left/notch,
        // apex/notch/back-right) and tested with a winding-independent point-in-triangle check
        // (rather than InternalMfdRwrPage's edge-function-plus-known-sign approach) specifically to
        // avoid re-deriving a sign convention by hand a second time — RWR's own triangle sprite
        // shipped inverted the first time that was tried. 2x2 sub-pixel supersampling gives cheap
        // antialiasing without needing a signed distance at all.
        private static Sprite ResolveIconSprite()
        {
            if (_iconSprite != null) return _iconSprite;

            const int w = 72, h = 96; // 12:16 aspect, upscaled for a crisp edge once supersampled
            // SVG-space points, normalized into the texture box (minX=-6,maxX=6,minY=-9,maxY=7).
            Vector2 apex = new Vector2(0.5f * w, 0f * h);
            Vector2 backLeft = new Vector2(0f * w, 1f * h);
            Vector2 notch = new Vector2(0.5f * w, 0.8125f * h); // (4-(-9))/16 = 0.8125
            Vector2 backRight = new Vector2(1f * w, 1f * h);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[w * h];
            Vector2[] offsets = { new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f), new Vector2(0.25f, 0.75f), new Vector2(0.75f, 0.75f) };
            for (int y = 0; y < h; y++)
            {
                // Texture2D.SetPixels32 stores row 0 as the BOTTOM of the resulting texture — the
                // apex (meant to render at the TOP, toward the nose) is at SVG y=-9 (texture-space
                // y=0 above), so this loop's row index has to be flipped to sample the right SVG row.
                int svgRow = h - 1 - y;
                byte alpha = 0;
                for (int x = 0; x < w; x++)
                {
                    int hits = 0;
                    foreach (Vector2 off in offsets)
                    {
                        Vector2 p = new Vector2(x + off.x, svgRow + off.y);
                        if (InTriangle(p, apex, backLeft, notch) || InTriangle(p, apex, notch, backRight)) hits++;
                    }
                    alpha = (byte)(hits * 255 / offsets.Length);
                    pixels[y * w + x] = new Color32(255, 255, 255, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            _iconSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _iconSprite;
        }

        // Standard winding-independent point-in-triangle test (same-sign-on-all-three-edges), so the
        // vertex order passed in doesn't need a pre-verified winding the way an edge-function-with-
        // fixed-sign approach would.
        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b) =>
            (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
    }
}
