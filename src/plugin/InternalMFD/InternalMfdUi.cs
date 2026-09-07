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
    }
}
