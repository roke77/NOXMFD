using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Native RWR scope for the internal-MFD POC's left (RWR) half: concentric range rings, cardinal
    // ticks, a heading triangle, an ownship caret, live contact blips, and inbound-missile bearing
    // indicators — matched against the real page's actual SVG (src/web/pages/rwr/rwr.html/rwr.js),
    // not eyeballed: ring radii (460/304/152 of a 1000 viewBox), colors (white family, not this
    // mod's HUD green — rwr.html's rings/caret are rgba(255,255,255,*), contacts are rwr.js's own
    // RWR_COL hex triplet), the outer ring solid vs the two inner rings dashed, and the ownship
    // caret's exact 4-point polygon are all taken directly from that source.
    //
    // Azimuth/distance math is likewise identical to telemetry-source.js's own RWR plot (az =
    // bearing-relative-to-heading via atan2, d = 1-power clamped to a 0.06 floor) — same data
    // mapping as the real page, even though the *rendering* is a native approximation of its SVG,
    // not the SVG itself (see docs/internal-mfd.md's "Why native, not screen-scraped" for why that
    // gap can't fully close). Own-ship position/heading and every contact/missile position come from
    // ONE TelemetrySnapshot (TelemetryServer.TryGetLatestSnapshot) so the bearing math can't mix
    // floating-origin frames.
    internal sealed class InternalMfdRwrScope
    {
        private const float MinDistFrac = 0.06f; // matches telemetry-source.js's own floor
        // How much of the smaller container dimension the outer ring fills — the ring itself
        // touches top/bottom exactly. The heading triangle extends ~4.8% of the radius beyond the
        // outer ring (see BuildHeadingTriangle) and rides slightly past that edge as a result, same
        // as the real page's own tight fit against its bezel.
        private const float FillFrac = 1f;

        // Ring radii as fractions of the outer ring (rwr.html: r=460/304/152 of a 1000 viewBox).
        private const float MidRingFrac = 304f / 460f;
        private const float InnerRingFrac = 152f / 460f;
        // Cardinal ticks span from the ring edge inward by (78-40)=38 units (rwr.html) — as a
        // fraction of the outer radius, the inner end sits at 1 - 38/460.
        private const float TickInnerFrac = 1f - 38f / 460f;

        // rwr.html's own rgba(255,255,255,*) values — this page's whole scope is the same white
        // family AVN's gauge dials use (theme.css: "Neutral instrument white... AVN's gauge
        // dials"), not this mod's HUD green.
        private static readonly Color RingColor = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color TickColor = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color HeadingTriColor = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color OwnshipColor = new Color(1f, 1f, 1f, 0.65f);

        // rwr.js's own RWR_COL palette — index 0/1/2 = search/track/lock.
        private static readonly Color[] TierColor =
        {
            new Color32(0xdc, 0xdc, 0xdc, 0xff),
            new Color32(0xff, 0xd2, 0x1e, 0xff),
            new Color32(0xff, 0x3b, 0x30, 0xff),
        };
        // rwr.js's renderThreats(): the missile dart is fixed #ff3b30 red, and only the separate
        // flicker layer (mwFlip, ~3.8Hz) toggles red<->yellow — a discrete swap, not a fade.
        private static readonly Color MissileRed = new Color32(0xff, 0x3b, 0x30, 0xff);
        private static readonly Color MissileAmber = new Color32(0xff, 0xd2, 0x1e, 0xff);

        private readonly RectTransform _center;
        private readonly int _layer;
        private readonly Font? _font;
        private readonly float _diameter;
        private readonly float _radius;

        // Grow-on-demand pools, not a fixed cap — TelemetryReader's own _rwrEmitters dictionary and
        // rwr.js's renderer have no size limit at all, so an artificial "MaxContacts"/"MaxMissiles"
        // ceiling here would silently drop real contacts/missiles past whatever number was picked,
        // a genuine behavior difference from the real page, not just a rendering-style one. Never
        // shrinks once grown (a dense furball leaves the pool at its peak size rather than
        // reallocating up and down every refresh) — inactive entries just sit deactivated.
        private readonly List<Image> _contactMarkers = new List<Image>();
        private readonly List<Text> _contactLabels = new List<Text>();
        private readonly List<RectTransform> _missileMarkers = new List<RectTransform>();
        private readonly List<Image> _missileImages = new List<Image>();

        internal InternalMfdRwrScope(RectTransform parent, Font? font)
        {
            int layer = parent.gameObject.layer;
            _layer = layer;
            _font = font;

            // Fills as much of the available panel as the smaller dimension allows, rather than a
            // fixed pixel size that leaves dead space above/below when the container is taller than
            // that fixed size — parent.rect is already resolved here (plain anchor-stretch math,
            // no Layout Group/ContentSizeFitter deferring it a frame).
            _diameter = Mathf.Min(parent.rect.width, parent.rect.height) * FillFrac;
            _radius = _diameter / 2f;

            // No title text — the real page has none; the scope fills the whole panel. Centered
            // in the full half, not offset to leave room for a header.
            var scopeGo = NewUi("Scope", parent, layer, typeof(RectTransform));
            _center = scopeGo.GetComponent<RectTransform>();
            _center.anchorMin = _center.anchorMax = new Vector2(0.5f, 0.5f);
            _center.sizeDelta = new Vector2(_diameter, _diameter);

            Sprite solidRing = ResolveRingSprite(dashed: false);
            Sprite dashedRing = ResolveRingSprite(dashed: true);
            BuildRing(_center, layer, solidRing, _diameter); // outer — solid
            BuildRing(_center, layer, dashedRing, _diameter * MidRingFrac); // dashed
            BuildRing(_center, layer, dashedRing, _diameter * InnerRingFrac); // dashed

            foreach (float angle in new[] { 0f, 90f, 180f, 270f })
                PlaceRadialBar(_center, layer, "Tick", angle, TickInnerFrac, 1f, 2.5f, TickColor);

            BuildHeadingTriangle(_center, layer);
            BuildOwnshipCaret(_center, layer);
            // Contact/missile markers are created on demand in Refresh (EnsureContactPool/
            // EnsureMissilePool below), not pre-built here — there's no fixed count to build.
        }

        // Grows the contact pool to at least `count` entries, creating new marker+label pairs only
        // when the pool isn't already big enough (never shrinks or reallocates existing entries).
        private void EnsureContactPool(int count)
        {
            while (_contactMarkers.Count < count)
            {
                var go = NewUi($"Contact{_contactMarkers.Count}", _center, _layer, typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(10f, 10f);
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f); // a plain square, rotated to a diamond
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                go.SetActive(false);
                _contactMarkers.Add(img);

                var labelGo = NewUi($"Contact{_contactLabels.Count}Label", _center, _layer, typeof(Text));
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.5f, 0.5f);
                labelRt.sizeDelta = new Vector2(90f, 16f);
                var label = labelGo.GetComponent<Text>();
                label.font = _font;
                label.fontSize = 11;
                label.alignment = TextAnchor.MiddleCenter;
                label.raycastTarget = false;
                labelGo.SetActive(false);
                _contactLabels.Add(label);
            }
        }

        // Grows the missile pool to at least `count` entries. Pivoted and anchored at the TRUE
        // centre (not an angle-0 offset like the fixed cardinal ticks) — a missile's angle changes
        // every Refresh, so its pivot has to stay at centre for the live rotation to sweep
        // correctly instead of orbiting around wherever it was first created pointing.
        private void EnsureMissilePool(int count)
        {
            while (_missileMarkers.Count < count)
            {
                var go = NewUi($"Missile{_missileMarkers.Count}", _center, _layer, typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(4f, 0.85f * _radius);
                var img = go.GetComponent<Image>();
                img.color = MissileRed;
                img.raycastTarget = false;
                go.SetActive(false);
                _missileMarkers.Add(rt);
                _missileImages.Add(img);
            }
        }

        internal void Refresh(TelemetrySnapshot snap)
        {
            RwrContact[] contacts = snap.Rwr ?? Array.Empty<RwrContact>();
            EnsureContactPool(contacts.Length);
            for (int i = 0; i < _contactMarkers.Count; i++)
            {
                bool active = i < contacts.Length;
                _contactMarkers[i].gameObject.SetActive(active);
                _contactLabels[i].gameObject.SetActive(active);
                if (!active) continue;

                RwrContact c = contacts[i];
                float az = Azimuth(snap, c.X, c.Z);
                // Same mapping telemetry-source.js uses: distance fraction is 1-power (closest
                // contact reads as CLOSEST to the ownship marker, not furthest), floored so nothing
                // ever renders exactly on top of it.
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
            EnsureMissilePool(missiles.Length);
            // rwr.js flickers the missile layer red<->yellow on its own ~3.8Hz timer, independent
            // of the data rate — a discrete color swap, not an alpha fade.
            bool flip = Mathf.FloorToInt(Time.time * 3.8f) % 2 == 0;
            Color flickerColor = flip ? MissileRed : MissileAmber;
            for (int i = 0; i < _missileMarkers.Count; i++)
            {
                bool active = i < missiles.Length;
                _missileMarkers[i].gameObject.SetActive(active);
                if (!active) continue;

                MwContact m = missiles[i];
                float az = Azimuth(snap, m.X, m.Z);
                _missileMarkers[i].localRotation = Quaternion.Euler(0f, 0f, -az);
                _missileImages[i].color = flickerColor;
            }
        }

        // Degrees clockwise from the nose — matches telemetry-source.js's own
        // `Math.atan2(dx, dz) * 180/PI - hdg` exactly (dx/dz = contact minus ownship). Not
        // normalized to 0..360 here: Sin/Cos in PolarToLocal don't need it, only a "BRG NNN"
        // text readout would.
        private static float Azimuth(TelemetrySnapshot snap, float x, float z)
            => HudWaypointCueMath.BearingDeg(snap.WorldX, snap.WorldZ, x, z) - snap.Heading;

        private Vector2 PolarToLocal(float azDeg, float distFrac)
        {
            float rad = azDeg * Mathf.Deg2Rad;
            float r = distFrac * _radius;
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

        // A thin bar from innerFrac*Radius to outerFrac*Radius along angleDeg (clockwise from the
        // nose, same convention as contact/missile azimuth) — shared by the cardinal ticks (fixed
        // angle) and the missile indicators (rotated live to the missile's bearing).
        private RectTransform PlaceRadialBar(RectTransform parent, int layer, string name,
            float angleDeg, float innerFrac, float outerFrac, float widthPx, Color color)
        {
            var go = NewUi(name, parent, layer, typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f); // extends outward along local +Y from its anchored point
            rt.sizeDelta = new Vector2(widthPx, (outerFrac - innerFrac) * _radius);
            rt.anchoredPosition = PolarToLocal(angleDeg, innerFrac);
            rt.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        // rwr.html's downward-pointing filled triangle straddling the top of the outer ring
        // (polygon 480,18 520,18 500,50 of the 1000 viewBox) — the heading/lubber-line reference
        // mark visible just above the scope.
        private void BuildHeadingTriangle(RectTransform parent, int layer)
        {
            var go = NewUi("HeadingTri", parent, layer, typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            // Triangle spans viewBox y 18..50 (450..482 units above the 500,500 centre); as a
            // fraction of the outer radius (460) that's a band centred at ~1.013*_radius.
            const float apexFrac = 450f / 460f;
            const float baseFrac = 482f / 460f;
            float width = (40f / 460f) * _radius;
            float height = (baseFrac - apexFrac) * _radius;
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(0f, (apexFrac + baseFrac) * 0.5f * _radius);
            var img = go.GetComponent<Image>();
            img.sprite = ResolveTriangleSprite();
            img.color = HeadingTriColor;
            img.raycastTarget = false;
        }

        // rwr.html's ownship caret: an unfilled 4-point outline (polygon 500,460 475,548 500,528
        // 525,548 of the 1000 viewBox) — an upward arrowhead with a concave notch at the back,
        // not a plain triangle or a dot.
        private void BuildOwnshipCaret(RectTransform parent, int layer)
        {
            var go = NewUi("Ownship", parent, layer, typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            const float shapeWidth = 50f;  // 525-475
            const float shapeHeight = 88f; // 548-460
            float scale = (0.16f * _radius) / shapeHeight; // caret height ~16% of the scope radius
            rt.sizeDelta = new Vector2(shapeWidth * scale, shapeHeight * scale);
            var img = go.GetComponent<Image>();
            img.sprite = ResolveOwnshipCaretSprite();
            img.color = OwnshipColor;
            img.raycastTarget = false;
        }

        private static GameObject NewUi(string name, Transform parent, int layer, Type extraComponent)
        {
            var go = new GameObject(name, typeof(RectTransform), extraComponent);
            go.layer = layer;
            go.GetComponent<RectTransform>().SetParent(parent, false);
            return go;
        }

        private static Sprite? _solidRingSprite;
        private static Sprite? _dashedRingSprite;

        // Procedural antialiased ring (annulus): alpha=1 only in a thin band near the edge. The
        // dashed variant additionally zeroes alpha in angular gaps (rwr.html's two inner rings use
        // stroke-dasharray; the outer ring is solid) — same runtime-generated-texture approach as
        // InternalMfdPoc's FUEL dial circle, so range rings need no shipped art either.
        private static Sprite ResolveRingSprite(bool dashed)
        {
            if (dashed && _dashedRingSprite != null) return _dashedRingSprite;
            if (!dashed && _solidRingSprite != null) return _solidRingSprite;

            const int size = 128;
            const float r = size / 2f;
            const float band = 3f; // stroke thickness in texture pixels
            const int dashCount = 14; // visual dash count, not rwr.html's exact "12 16"/"10 16" spacing
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
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Vector2.Distance(p, center);
                    float distFromRing = Mathf.Abs(d - (r - band));
                    float alpha01 = Mathf.Clamp01(1f - distFromRing / (band * 0.5f));

                    if (dashed && alpha01 > 0f)
                    {
                        float angle01 = (Mathf.Atan2(p.x - center.x, p.y - center.y) + Mathf.PI) / (2f * Mathf.PI);
                        float dashPhase = (angle01 * dashCount) % 1f;
                        if (dashPhase > 0.55f) alpha01 = 0f; // ~55% on, 45% gap
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha01 * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            if (dashed) _dashedRingSprite = sprite; else _solidRingSprite = sprite;
            return sprite;
        }

        private static Sprite? _triangleSprite;

        // Filled downward-pointing triangle (edge-function inside/outside test, ~1px antialiased
        // edges) — the heading reference mark.
        private static Sprite ResolveTriangleSprite()
        {
            if (_triangleSprite != null) return _triangleSprite;

            // Texture2D.SetPixels32 stores row 0 as the BOTTOM of the resulting texture (Unity's
            // standard bottom-up convention) — so the base (meant to render furthest from the ring,
            // i.e. visually at the TOP of the sprite) needs the HIGH y fraction, and the apex
            // (pointing down, toward the ring) the LOW one. Getting this backwards is exactly what
            // shipped first: the ownship caret below hit the identical bug (arrow pointing down
            // instead of up) for the same reason.
            const int w = 80, h = 64;
            var p0 = new Vector2(0.05f * w, 0.95f * h);
            var p1 = new Vector2(0.95f * w, 0.95f * h);
            var p2 = new Vector2(0.50f * w, 0.05f * h);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float e0 = EdgeSigned(p0, p1, p);
                    float e1 = EdgeSigned(p1, p2, p);
                    float e2 = EdgeSigned(p2, p0, p);
                    float inside = Mathf.Min(e0, Mathf.Min(e1, e2));
                    byte alpha = (byte)(Mathf.Clamp01(inside + 0.5f) * 255f);
                    pixels[y * w + x] = new Color32(255, 255, 255, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            _triangleSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _triangleSprite;
        }

        // Signed distance (pixels) of point p from the left side of directed edge a->b: positive
        // = inside (for a consistently-wound triangle), used as one term of a 3-edge inside test.
        private static float EdgeSigned(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float cross = ab.x * (p.y - a.y) - ab.y * (p.x - a.x);
            return cross / ab.magnitude;
        }

        private static Sprite? _ownshipCaretSprite;

        // Stroke-only outline of rwr.html's exact 4-point ownship polygon (500,460 / 475,548 /
        // 500,528 / 525,548 — an upward arrowhead with a concave back notch), via point-to-segment
        // distance to each of the 4 edges. Not a filled shape: rwr.html's caret is fill="none".
        private static Sprite ResolveOwnshipCaretSprite()
        {
            if (_ownshipCaretSprite != null) return _ownshipCaretSprite;

            const int w = 80, h = 140; // ~50:88 aspect, matching the polygon's own bounding box
            // Polygon points normalized into a padded [0.1, 0.9] box, same shape rwr.html draws.
            // Y already flipped (1 - svgY) here: Texture2D.SetPixels32 stores row 0 as the BOTTOM of
            // the resulting texture, so the apex (meant to render at the TOP, pointing toward the
            // nose) needs the HIGH y fraction, not the low one a naive SVG-Y copy would give it —
            // the un-flipped version is exactly what shipped first and rendered the caret pointing
            // down instead of up.
            Vector2[] pts =
            {
                new Vector2(0.5f, 0.9f),
                new Vector2(0.1f, 0.1f),
                new Vector2(0.5f, 0.282f),
                new Vector2(0.9f, 0.1f),
            };
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(pts[i].x * w, pts[i].y * h);

            const float strokePx = 3.5f;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float minDist = float.MaxValue;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float d = DistancePointSegment(p, pts[i], pts[(i + 1) % pts.Length]);
                        if (d < minDist) minDist = d;
                    }
                    byte alpha = (byte)(Mathf.Clamp01(strokePx * 0.5f - minDist + 0.5f) * 255f);
                    pixels[y * w + x] = new Color32(255, 255, 255, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            _ownshipCaretSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _ownshipCaretSprite;
        }

        private static float DistancePointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            Vector2 closest = a + ab * t;
            return Vector2.Distance(p, closest);
        }
    }
}
