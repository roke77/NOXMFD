using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Native RWR scope for the internal-MFD's RWR pane: concentric range rings, cardinal ticks, a
    // heading triangle, an ownship caret, live contact blips, and inbound-missile bearing
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
    // ONE TelemetrySnapshot (TelemetryServer.TryGetLatestSnapshot, passed into Refresh by
    // InternalMfdController) so the bearing math can't mix floating-origin frames.
    internal sealed class InternalMfdRwrPage : IInternalMfdPage
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

        // rwr.js's renderThreats(): RMAX=6 (km mapped to the rim), RIN=60 (fixed inner radius the
        // line never crosses), a further +35 offset before the line's outer/dart end starts moving.
        // rng isn't a wire field — rwr.js computes it client-side as
        // Math.hypot(dx,dz)/1000 from world positions, same as here.
        private const float MissileRangeMaxKm = 6f;
        // 0, not rwr.html's own RIN=60/460 (~13%) — asked to close that gap so the line visually
        // touches the ownship caret instead of stopping short of it, a deliberate deviation from
        // the source's own small gap there.
        private const float MissileInnerFrac = 0f;
        private const float MissileAnchorFrac = (60f + 35f) / 460f; // the outer end's position when rng=0

        // rwr.js's dart polygon proportions (HL=36 apex length + HB=8 back offset, HW*2=20 full
        // width) as fractions of the outer radius.
        private const float DartLengthFrac = (36f + 8f) / 460f;
        private const float DartWidthFrac = 20f / 460f;

        // rwr.html/rwr.js use stroke-width="3" (out of a 1000 viewBox) for both the cardinal ticks
        // and the missile line — as a fraction of the outer radius (460), not a fixed pixel count
        // that stays the same regardless of how big the scope itself ends up (InternalMfdController sizes
        // it differently per aircraft/layout).
        private const float StrokeWidthFrac = 3f / 460f;

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
        private float _lastMissileDiagLog = float.NegativeInfinity;

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
        private readonly List<Image> _missileDarts = new List<Image>();
        private readonly List<Image> _notchLines = new List<Image>();

        internal InternalMfdRwrPage(RectTransform parent, Font? font)
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
            // in the full pane, not offset to leave room for a header.
            var scopeGo = InternalMfdUi.NewUi("Scope", parent, layer, typeof(RectTransform));
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
                var go = InternalMfdUi.NewUi($"Contact{_contactMarkers.Count}", _center, _layer, typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(10f, 10f);
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f); // a plain square, rotated to a diamond
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                go.SetActive(false);
                _contactMarkers.Add(img);

                var labelGo = InternalMfdUi.NewUi($"Contact{_contactLabels.Count}Label", _center, _layer, typeof(Text));
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

        // Grows the missile pool to at least `count` entries — a line (fixed inner end at
        // MissileInnerFrac, outer end/length recomputed live from range each Refresh) plus a dart
        // marker at the outer end (rwr.js draws both; the line alone, with no dart and a length
        // that never changes, was the reported bug). Both pivoted/anchored at the TRUE centre, not
        // an angle-0 offset like the fixed cardinal ticks — a missile's angle, and now its length
        // and position too, change every Refresh, so anchoredPosition has to be recomputed live
        // rather than baked in once at construction.
        private void EnsureMissilePool(int count)
        {
            while (_missileMarkers.Count < count)
            {
                var go = InternalMfdUi.NewUi($"Missile{_missileMarkers.Count}", _center, _layer, typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f);
                var img = go.GetComponent<Image>();
                img.color = MissileRed;
                img.raycastTarget = false;
                go.SetActive(false);
                _missileMarkers.Add(rt);
                _missileImages.Add(img);

                var dartGo = InternalMfdUi.NewUi($"MissileDart{_missileDarts.Count}", _center, _layer, typeof(Image));
                var dartRt = dartGo.GetComponent<RectTransform>();
                dartRt.anchorMin = dartRt.anchorMax = new Vector2(0.5f, 0.5f);
                dartRt.sizeDelta = new Vector2(DartWidthFrac * _radius, DartLengthFrac * _radius);
                var dartImg = dartGo.GetComponent<Image>();
                // Its own sprite, not ResolveTriangleSprite() — that one is proportioned for the
                // heading marker (80x64, wider than tall); the dart is narrow and tall (~20:44), and
                // stretching a mismatched-aspect sprite into that box distorted the shape rather than
                // just shrinking it.
                dartImg.sprite = ResolveDartSprite();
                dartImg.color = MissileRed;
                dartImg.raycastTarget = false;
                dartGo.SetActive(false);
                _missileDarts.Add(dartImg);

                // Radar-seeker beam-notch axis (rwr.js: a dashed yellow line spanning the FULL
                // diameter through the player, static for as long as Notch stays valid — not tied
                // to the missile's own closing range like the line/dart above). Pivot at centre
                // (not the bottom like the range line) since it extends both ways from the player.
                var notchGo = InternalMfdUi.NewUi($"Notch{_notchLines.Count}", _center, _layer, typeof(Image));
                var notchRt = notchGo.GetComponent<RectTransform>();
                notchRt.anchorMin = notchRt.anchorMax = new Vector2(0.5f, 0.5f);
                notchRt.sizeDelta = new Vector2(StrokeWidthFrac * _radius, _diameter);
                var notchImg = notchGo.GetComponent<Image>();
                notchImg.sprite = ResolveDashedLineSprite();
                notchImg.color = MissileAmber;
                notchImg.raycastTarget = false;
                notchGo.SetActive(false);
                _notchLines.Add(notchImg);
            }
        }

        public void Refresh(TelemetrySnapshot snap)
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
                _missileDarts[i].gameObject.SetActive(active);
                if (!active)
                {
                    _notchLines[i].gameObject.SetActive(false);
                    continue;
                }

                MwContact m = missiles[i];
                float az = Azimuth(snap, m.X, m.Z);

                // rng isn't a wire field — computed the same way telemetry-source.js does, straight
                // from world positions already in the shared frame (snap.WorldX/Z).
                float dx = m.X - snap.WorldX, dz = m.Z - snap.WorldZ;
                float rngKm = Mathf.Sqrt(dx * dx + dz * dz) / 1000f;
                float frac = Mathf.Clamp01(rngKm / MissileRangeMaxKm);
                // The outer end (and the dart riding it) moves from MissileAnchorFrac (missile right
                // on top of the player) out to the rim (frac=1) — THIS moving, not a fixed-length
                // line, is what reads as "closing in" (the reported bug: a static line that never
                // shortened as the missile approached).
                float outerFrac = Mathf.Lerp(MissileAnchorFrac, 1f, frac);

                Vector2 innerPos = PolarToLocal(az, MissileInnerFrac);
                _missileMarkers[i].anchoredPosition = innerPos;
                _missileMarkers[i].sizeDelta = new Vector2(StrokeWidthFrac * _radius, (outerFrac - MissileInnerFrac) * _radius);
                _missileMarkers[i].localRotation = Quaternion.Euler(0f, 0f, -az);
                _missileImages[i].color = flickerColor;

                // Traced through rwr.js's own vector math rather than assumed: its dart uses
                // (ux,uy)=(-sn,cs), the exact NEGATIVE of (sn,-cs) — the same outward-at-this-
                // azimuth vector (mx,my) itself is placed with. So the apex sits INWARD from the
                // line's outer end, toward the player. Same rotation as the line itself, no extra
                // 180 - the dart's own apex already points local -Y unrotated (see
                // ResolveTriangleSprite), i.e. already inward once rotated by -az the same way the
                // line is.
                _missileDarts[i].rectTransform.anchoredPosition = PolarToLocal(az, outerFrac);
                _missileDarts[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -az);
                _missileDarts[i].color = flickerColor;

                // Radar-seeker beam-notch axis — rwr.js only draws this when Notch is a valid
                // heading (>=0; -1 means no seeker/no notch to show). Same relative-to-heading
                // conversion telemetry-source.js applies to it, not just to az.
                bool hasNotch = m.Notch >= 0f;
                _notchLines[i].gameObject.SetActive(hasNotch);
                if (hasNotch)
                {
                    float notchAz = m.Notch - snap.Heading;
                    _notchLines[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -notchAz);
                }

                if (Time.time - _lastMissileDiagLog > 2f)
                {
                    _lastMissileDiagLog = Time.time;
                    Plugin.Log?.LogInfo(
                        $"[NOXMFD] Internal MFD POC RWR missile[{i}]: az={az:F1} rngKm={rngKm:F2} " +
                        $"outerFrac={outerFrac:F3} dartPos={_missileDarts[i].rectTransform.anchoredPosition} " +
                        $"dartSize={_missileDarts[i].rectTransform.sizeDelta} dartActive={_missileDarts[i].gameObject.activeSelf} " +
                        $"notch={m.Notch:F1} notchActive={hasNotch}.");
                }
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
            var go = InternalMfdUi.NewUi("Ring", parent, layer, typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(diameter, diameter);
            var img = go.GetComponent<Image>();
            img.sprite = ring;
            img.color = RingColor;
            img.raycastTarget = false;
        }

        // A thin bar from innerFrac*Radius to outerFrac*Radius along angleDeg (clockwise from the
        // nose, same convention as contact/missile azimuth) — used by the fixed-angle cardinal
        // ticks. (Missiles need their own inline construction in EnsureMissilePool/Refresh instead
        // of this helper — their angle changes every frame, and this helper bakes anchoredPosition
        // in at the angle passed at construction time, which only holds for elements that never
        // rotate again.)
        private RectTransform PlaceRadialBar(RectTransform parent, int layer, string name,
            float angleDeg, float innerFrac, float outerFrac, float widthPx, Color color)
        {
            var go = InternalMfdUi.NewUi(name, parent, layer, typeof(Image));
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
            var go = InternalMfdUi.NewUi("HeadingTri", parent, layer, typeof(Image));
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
            var go = InternalMfdUi.NewUi("Ownship", parent, layer, typeof(Image));
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

        private static Sprite? _dashedLineSprite;

        // A vertical dashed stripe (alpha alternates along Y, solid across X) — the notch beam
        // axis. A different technique from the ring's angular dashing (that alternates by angle
        // around a circle; this is a straight bar, so it alternates along its own length instead).
        private static Sprite ResolveDashedLineSprite()
        {
            if (_dashedLineSprite != null) return _dashedLineSprite;

            const int w = 8, h = 256;
            const int dashCount = 10; // visual dash count, not rwr.html's exact "14 12" spacing
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float dashPhase = ((float)y / h * dashCount) % 1f;
                byte alpha = dashPhase < 0.55f ? (byte)255 : (byte)0; // ~55% on, 45% gap
                for (int x = 0; x < w; x++) pixels[y * w + x] = new Color32(255, 255, 255, alpha);
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            _dashedLineSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _dashedLineSprite;
        }

        private static Sprite? _solidRingSprite;
        private static Sprite? _dashedRingSprite;

        // Procedural antialiased ring (annulus): alpha=1 only in a thin band near the edge. The
        // dashed variant additionally zeroes alpha in angular gaps (rwr.html's two inner rings use
        // stroke-dasharray; the outer ring is solid) — a runtime-generated texture, so range rings
        // need no shipped art either.
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
        // edges) — the heading reference mark. 80x64 (wider than tall), matching rwr.html's own
        // heading-triangle proportions (40 wide x 32 tall).
        private static Sprite ResolveTriangleSprite()
        {
            if (_triangleSprite != null) return _triangleSprite;
            _triangleSprite = BuildFilledTriangleSprite(80, 64);
            return _triangleSprite;
        }

        private static Sprite? _dartSprite;

        // The missile dart — same apex-at-local-(-Y) shape as the heading triangle, but its own
        // sprite rather than a reuse: the dart is narrow and TALL (~20:44, matching rwr.js's own
        // HW*2 width / HL+HB length), the heading marker is wide and short. Stretching the wrong-
        // aspect sprite into this box distorted the shape instead of just scaling it.
        private static Sprite ResolveDartSprite()
        {
            if (_dartSprite != null) return _dartSprite;
            _dartSprite = BuildFilledTriangleSprite(40, 88);
            return _dartSprite;
        }

        // Filled isoceles triangle, apex at local -Y (i.e. the LOW end of the y-axis once rendered —
        // see the row-order note below), base at local +Y — shared rasterizer for both the heading
        // marker and the missile dart, which differ only in aspect ratio.
        private static Sprite BuildFilledTriangleSprite(int w, int h)
        {
            // Texture2D.SetPixels32 stores row 0 as the BOTTOM of the resulting texture (Unity's
            // standard bottom-up convention) — so the base (meant to render at local +Y) needs the
            // HIGH y fraction, and the apex (local -Y) the LOW one. Getting this backwards is
            // exactly what shipped first: both this and the ownship caret rendered upside down for
            // the same reason before it was caught.
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
                    // p0/p1/p2's winding makes points truly inside the triangle NEGATIVE on all
                    // three edges, not positive (checked by hand: a pixel confirmed geometrically
                    // inside via linear interpolation came out negative on e0/e1/e2 alike) - Max
                    // + negate flips that consistently, instead of Min silently clamping the whole
                    // interior to fully transparent while only a thin ~1px band near the boundary
                    // (where the least-negative edge value crosses zero) ever got any alpha at all.
                    float inside = -Mathf.Max(e0, Mathf.Max(e1, e2));
                    byte alpha = (byte)(Mathf.Clamp01(inside + 0.5f) * 255f);
                    pixels[y * w + x] = new Color32(255, 255, 255, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        // Signed distance (pixels) of point p from directed edge a->b — consistently the same sign
        // for every point on one side of the line, opposite sign on the other. Which sign means
        // "inside" depends on the winding of whatever triangle calls this three times (BuildFilled
        // TriangleSprite negates the result — see its own comment for why).
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
