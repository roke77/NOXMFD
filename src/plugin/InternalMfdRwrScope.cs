using System;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Native RWR scope for the internal-MFD POC's left (RWR) half: concentric range rings, live
    // contact blips (RwrContact, positioned/colored by tier), and inbound-missile bearing spears
    // (MwContact) — the first native replication of an actual NOXMFD page's core visual, not just a
    // text summary, following the FUEL dial's precedent (procedural sprites, no shipped art).
    //
    // Azimuth/distance math is deliberately identical to telemetry-source.js's own RWR plot (az =
    // bearing-relative-to-heading via atan2, d = 1-power clamped to a 0.06 floor so a contact never
    // renders exactly on the ownship dot) — same underlying data mapping as the real page, even
    // though the rendering itself is a native approximation, not its SVG. Own-ship position/heading
    // and every contact/missile position come from ONE TelemetrySnapshot
    // (TelemetryServer.TryGetLatestSnapshot) so the bearing math can't mix floating-origin frames.
    internal sealed class InternalMfdRwrScope
    {
        private const int MaxContacts = 8;
        private const int MaxMissiles = 4;
        private const float Diameter = 280f;
        private const float Radius = Diameter / 2f;
        private const float MinDistFrac = 0.06f; // matches telemetry-source.js's own floor

        // Index 0/1/2 = search/track/lock, matching rwr.js's own RWR_COL palette.
        private static readonly Color[] TierColor =
        {
            new Color(0.86f, 0.86f, 0.86f),
            new Color(1f, 0.82f, 0.12f),
            new Color(1f, 0.23f, 0.19f),
        };
        private static readonly Color MissileColor = new Color(1f, 0.4f, 0.1f);
        private static readonly Color RingColor = new Color(0.3f, 1f, 0.4f, 0.35f);
        private static readonly Color TextColor = new Color(0.3f, 1f, 0.4f);

        private readonly RectTransform _center;
        private readonly Image[] _contactMarkers = new Image[MaxContacts];
        private readonly Text[] _contactLabels = new Text[MaxContacts];
        private readonly RectTransform[] _missileMarkers = new RectTransform[MaxMissiles];
        private readonly Image[] _missileImages = new Image[MaxMissiles];

        internal InternalMfdRwrScope(RectTransform parent, Font? font)
        {
            int layer = parent.gameObject.layer;

            var titleGo = NewUi("Title", parent, layer, typeof(Text));
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 0.85f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;
            var title = titleGo.GetComponent<Text>();
            title.font = font;
            title.fontSize = 16;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = TextColor;
            title.text = "RWR";
            title.raycastTarget = false;

            var scopeGo = NewUi("Scope", parent, layer, typeof(RectTransform));
            _center = scopeGo.GetComponent<RectTransform>();
            _center.anchorMin = _center.anchorMax = new Vector2(0.5f, 0.42f); // below the title band
            _center.sizeDelta = new Vector2(Diameter, Diameter);

            Sprite ring = ResolveRingSprite();
            foreach (float frac in new[] { 0.34f, 0.67f, 1f })
                BuildRing(_center, layer, ring, Diameter * frac);

            var dotGo = NewUi("Ownship", _center, layer, typeof(Image));
            var dotRt = dotGo.GetComponent<RectTransform>();
            dotRt.anchorMin = dotRt.anchorMax = new Vector2(0.5f, 0.5f);
            dotRt.sizeDelta = new Vector2(6f, 6f);
            var dotImg = dotGo.GetComponent<Image>();
            dotImg.color = TextColor;
            dotImg.raycastTarget = false;

            for (int i = 0; i < MaxContacts; i++)
            {
                var go = NewUi($"Contact{i}", _center, layer, typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(12f, 12f);
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f); // a plain square, rotated to a diamond
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                go.SetActive(false);
                _contactMarkers[i] = img;

                var labelGo = NewUi($"Contact{i}Label", _center, layer, typeof(Text));
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.5f, 0.5f);
                labelRt.sizeDelta = new Vector2(90f, 16f);
                var label = labelGo.GetComponent<Text>();
                label.font = font;
                label.fontSize = 11;
                label.alignment = TextAnchor.MiddleCenter;
                label.raycastTarget = false;
                labelGo.SetActive(false);
                _contactLabels[i] = label;
            }

            for (int i = 0; i < MaxMissiles; i++)
            {
                var go = NewUi($"Missile{i}", _center, layer, typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f); // extends outward from centre along local +Y
                rt.sizeDelta = new Vector2(4f, Radius * 0.85f);
                var img = go.GetComponent<Image>();
                img.color = MissileColor;
                img.raycastTarget = false;
                go.SetActive(false);
                _missileMarkers[i] = rt;
                _missileImages[i] = img;
            }
        }

        internal void Refresh(TelemetrySnapshot snap)
        {
            RwrContact[] contacts = snap.Rwr ?? Array.Empty<RwrContact>();
            int shown = Mathf.Min(contacts.Length, MaxContacts);
            for (int i = 0; i < MaxContacts; i++)
            {
                bool active = i < shown;
                _contactMarkers[i].gameObject.SetActive(active);
                _contactLabels[i].gameObject.SetActive(active);
                if (!active) continue;

                RwrContact c = contacts[i];
                float az = Azimuth(snap, c.X, c.Z);
                // Same mapping telemetry-source.js uses: distance fraction is 1-power (closest
                // contact reads as CLOSEST to the ownship dot, not furthest), floored so nothing
                // ever renders exactly on top of the ownship marker.
                float distFrac = Mathf.Max(MinDistFrac, Mathf.Min(1f, 1f - c.Power));
                Vector2 pos = PolarToLocal(az, distFrac);

                Color color = TierColor[Mathf.Clamp(c.Tier, 0, TierColor.Length - 1)];
                _contactMarkers[i].rectTransform.anchoredPosition = pos;
                _contactMarkers[i].color = color;
                _contactLabels[i].rectTransform.anchoredPosition = pos + new Vector2(0f, -14f);
                _contactLabels[i].color = color;
                _contactLabels[i].text = ShortName(c.Name);
            }

            MwContact[] missiles = snap.Mw ?? Array.Empty<MwContact>();
            int shownM = Mathf.Min(missiles.Length, MaxMissiles);
            // A "flickering spear" (rwr.js's own description of the missile indicator) — a plain
            // sine alpha oscillation reads as an alert without needing a real animation clip.
            float flickerAlpha = 0.5f + 0.5f * Mathf.Sin(Time.time * 10f);
            for (int i = 0; i < MaxMissiles; i++)
            {
                bool active = i < shownM;
                _missileMarkers[i].gameObject.SetActive(active);
                if (!active) continue;

                MwContact m = missiles[i];
                float az = Azimuth(snap, m.X, m.Z);
                _missileMarkers[i].localRotation = Quaternion.Euler(0f, 0f, -az);
                Color c = MissileColor;
                c.a = flickerAlpha;
                _missileImages[i].color = c;
            }
        }

        // Degrees clockwise from the nose — matches telemetry-source.js's own
        // `Math.atan2(dx, dz) * 180/PI - hdg` exactly (dx/dz = contact minus ownship). Not
        // normalized to 0..360 here: Sin/Cos in PolarToLocal don't need it, only a "BRG NNN"
        // text readout would (see InternalMfdPoc.RefreshRwr, which does normalize its own copy).
        private static float Azimuth(TelemetrySnapshot snap, float x, float z)
            => HudWaypointCueMath.BearingDeg(snap.WorldX, snap.WorldZ, x, z) - snap.Heading;

        private static Vector2 PolarToLocal(float azDeg, float distFrac)
        {
            float rad = azDeg * Mathf.Deg2Rad;
            float r = distFrac * Radius;
            return new Vector2(Mathf.Sin(rad) * r, Mathf.Cos(rad) * r);
        }

        // Mirrors rwr.js's own rwrShort(): first word, upper-cased, capped at 7 chars.
        private static string ShortName(string? n)
        {
            if (string.IsNullOrEmpty(n)) return string.Empty;
            string s = n!.Split(' ')[0].ToUpperInvariant();
            return s.Length > 7 ? s.Substring(0, 7) : s;
        }

        private static void BuildRing(RectTransform parent, int layer, Sprite ring, float diameter)
        {
            var go = NewUi("Ring", parent, layer, typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(diameter, diameter);
            var img = go.GetComponent<Image>();
            img.sprite = ring;
            img.color = RingColor;
            img.raycastTarget = false;
        }

        private static GameObject NewUi(string name, Transform parent, int layer, Type extraComponent)
        {
            var go = new GameObject(name, typeof(RectTransform), extraComponent);
            go.layer = layer;
            go.GetComponent<RectTransform>().SetParent(parent, false);
            return go;
        }

        private static Sprite? _ringSprite;

        // Procedural antialiased ring (annulus): alpha=1 only in a thin band near the edge, alpha=0
        // inside/outside — same runtime-generated-texture approach as InternalMfdPoc's FUEL dial
        // circle, so range rings need no shipped art either. One sprite reused at 3 sizes (stroke
        // width scales with each ring's own radius, which reads fine for concentric range rings).
        private static Sprite ResolveRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;

            const int size = 128;
            const float r = size / 2f;
            const float band = 3f; // stroke thickness in texture pixels
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            var center = new Vector2(r, r);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float distFromRing = Mathf.Abs(d - (r - band));
                    byte alpha = (byte)(Mathf.Clamp01(1f - distFromRing / (band * 0.5f)) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _ringSprite;
        }
    }
}
