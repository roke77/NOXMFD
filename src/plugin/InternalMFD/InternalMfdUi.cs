using System;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Small UI-construction primitives shared by every internal-MFD page — kept here once instead
    // of duplicated per page (this is exactly the small overlap InternalMfdRwrPage's own private
    // copy of NewUi already showed, before InternalMfdTgpPage would have needed a third copy).
    internal static class InternalMfdUi
    {
        internal static GameObject NewUi(string name, Transform parent, int layer, Type extraComponent)
        {
            var go = new GameObject(name, typeof(RectTransform), extraComponent);
            go.layer = layer;
            go.GetComponent<RectTransform>().SetParent(parent, false);
            return go;
        }

        // Borrow the font off any Text the game already has on screen, same trick HudWaypointCue
        // uses — avoids shipping a font asset for this feature.
        internal static Font? ResolveFont()
        {
            foreach (Text t in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t != null && t.font != null) return t.font;
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // Anchor-fills rt to its parent's full rect — the "just cover the pane" layout every page
        // root and every full-bleed child (InternalMfdTgpPage's feed/status, the HSD/TGP wrapper
        // roots InternalMfdController toggles between) already wrote out by hand.
        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static Sprite? _solidRingSprite;
        private static Sprite? _dashedRingSprite;

        // Procedural antialiased ring (annulus): alpha=1 only in a thin band near the edge, with an
        // optional angular dash pattern. Shared by every page that draws a range-ring-style circle
        // (InternalMfdRwrPage's RWR rings, InternalMfdHsdPage's HSD grid) so the same runtime-
        // generated texture — and its cache — isn't built twice.
        internal static Sprite ResolveRingSprite(bool dashed)
        {
            if (dashed && _dashedRingSprite != null) return _dashedRingSprite;
            if (!dashed && _solidRingSprite != null) return _solidRingSprite;

            const int size = 128;
            const float r = size / 2f;
            const float band = 3f; // stroke thickness in texture pixels
            const int dashCount = 14; // visual dash count, not any one source page's exact spacing
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

        // Signed distance (texture px) of point p from directed edge a->b — shared by any page that
        // rasterizes a filled polygon via an edge-function inside/outside test (InternalMfdRwrPage's
        // heading triangle/missile dart, InternalMfdHsdPage's notched contact/ownship icon). Which
        // sign means "inside" depends on the winding of whatever polygon calls this — see each
        // caller's own comment.
        internal static float EdgeSigned(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float cross = ab.x * (p.y - a.y) - ab.y * (p.x - a.x);
            return cross / ab.magnitude;
        }

        // Shortest distance (texture px) from p to the segment a-b — shared by any page that
        // rasterizes a stroke-only polygon outline via point-to-segment distance
        // (InternalMfdRwrPage's ownship caret).
        internal static float DistancePointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            Vector2 closest = a + ab * t;
            return Vector2.Distance(p, closest);
        }
    }
}
