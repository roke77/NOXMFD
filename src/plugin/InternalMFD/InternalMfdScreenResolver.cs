using System.Reflection;
using UnityEngine;

namespace NOXMFD
{
    // Finds the local player's cockpit TacScreen canvas (docs/internal-mfd.md) — the insertion
    // point every internal-MFD page ultimately renders into. Separated from InternalMfdPoc's own
    // toggle/lifecycle/layout concerns: this only knows how to locate the canvas for a given
    // aircraft (and log why it couldn't), not what gets built on it or how a pane splits.
    internal static class InternalMfdScreenResolver
    {
        // A live test showed a full-canvas overlay painting on all three cockpit screens (center
        // MFD + two side sub-displays), not just the center one. Confirmed by directly inspecting
        // the game's mesh/material assets (not just decompiled C#): all three screens are ONE mesh
        // ("tacscreen"), ONE Renderer, ONE material — the three screens are different vertical bands
        // of the SAME 1024x512 texture, with the crop baked directly into the mesh's own per-vertex
        // UVs. For the T/A-30 Compass specifically, the large center screen occupies roughly the top
        // 71% of the texture (V ~0.29068-1.0, full width); the two small screens split the bottom
        // ~29% horizontally. This is per-aircraft mesh data, not guaranteed to hold for any other
        // airframe (docs/internal-mfd.md's open per-airframe-geometry question) — only applied when
        // the T/A-30 is confirmed; callers fall back to the full canvas otherwise.
        internal const string CenterScreenAircraft = "T/A-30 Compass";
        internal static readonly Vector2 CenterScreenUvMin = new Vector2(0f, 0.29068f);

        private static FieldInfo? _cockpitAircraftField;
        private static FieldInfo? _tacScreenField;
        private static FieldInfo? _canvasField;
        private static FieldInfo? _camField;
        private static FieldInfo? _renderTextureField;

        // Cockpit.tacScreen and TacScreen.canvas are both private with no public accessor — the
        // insertion point docs/internal-mfd.md proposes.
        //
        // Two live-tested wrong guesses at where Cockpit actually lives, both silently finding
        // nothing (no exception either time — the only signal was LogFailure never advancing past
        // this step): aircraft.cockpit.GetComponent<Cockpit>() (that's the structural/damage
        // UnitPart EscapeCapsule sits on, a different object) and aircraft.GetComponentInChildren
        // <Cockpit>() (so Cockpit isn't under the Aircraft's own subtree either — likely a sibling
        // branch under a shared prefab root, e.g. an "Interior" branch next to a "Systems" branch
        // Aircraft itself sits on, rather than a parent/child relationship). Matching by the
        // component's OWN inspector-assigned `aircraft` reference sidesteps the hierarchy shape
        // entirely — however it's parented, this is how the game itself associates the two.
        internal static bool ResolveCanvas(Aircraft aircraft, out Canvas? canvas, out Camera? cam)
        {
            canvas = null;
            cam = null;

            if (_cockpitAircraftField == null)
                _cockpitAircraftField = typeof(Cockpit).GetField("aircraft", BindingFlags.Instance | BindingFlags.NonPublic);

            Cockpit? cockpit = null;
            foreach (var candidate in Object.FindObjectsByType<Cockpit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ReferenceEquals(_cockpitAircraftField?.GetValue(candidate), aircraft)) { cockpit = candidate; break; }
            }
            if (cockpit == null) { LogFailure("no Cockpit component references this aircraft"); return false; }

            // tacScreen is only ever instantiated for the local player's own aircraft, and only
            // after Aircraft.onInitialize fires — null here just means "not ready yet this frame".
            if (_tacScreenField == null)
                _tacScreenField = typeof(Cockpit).GetField("tacScreen", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_tacScreenField?.GetValue(cockpit) is not TacScreen tacScreen || tacScreen == null)
            { LogFailure("Cockpit.tacScreen not yet instantiated"); return false; }

            if (_canvasField == null)
                _canvasField = typeof(TacScreen).GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_canvasField?.GetValue(tacScreen) is not Canvas c || c == null)
            { LogFailure("TacScreen.canvas field missing/null"); return false; }

            if (_camField == null)
                _camField = typeof(TacScreen).GetField("cam", BindingFlags.Instance | BindingFlags.NonPublic);
            cam = _camField?.GetValue(tacScreen) as Camera;

            canvas = c;
            _lastFailure = null;
            LogAttachDiagnostics(aircraft, tacScreen, c, cam);
            return true;
        }

        private static string? _lastFailure;

        // Logged once per DISTINCT failure reason, not per frame (this runs every LateUpdate while
        // enabled and unresolved) — enough to diagnose a silent "nothing happened" without spamming
        // Player.log at 60-90Hz.
        private static void LogFailure(string reason)
        {
            if (_lastFailure == reason) return;
            _lastFailure = reason;
            Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC: {reason}.");
        }

        // One-shot, on successful attach only — this is the evidence a manual test needs to tell
        // "nothing rendered because the canvas wasn't found" apart from "found it, but the camera/
        // layer/render setup doesn't show it", without spamming a per-frame log.
        private static void LogAttachDiagnostics(Aircraft aircraft, TacScreen tacScreen, Canvas canvas, Camera? cam)
        {
            string unitName = aircraft.definition != null ? aircraft.definition.unitName : "?";

            Plugin.Log?.LogInfo(
                $"[NOXMFD] Internal MFD POC attached: aircraft={unitName}, canvas.renderMode={canvas.renderMode}, " +
                $"canvas.layer={canvas.gameObject.layer} ({LayerMask.LayerToName(canvas.gameObject.layer)}), " +
                $"cam={(cam != null ? cam.name : "null")}, cam.cullingMask={(cam != null ? cam.cullingMask : 0)}.");

            // Confirms, every attach, that the mesh/material story hasn't changed: exactly one
            // Renderer should reference this RenderTexture (see the class-level comment — all three
            // screens are one mesh, one material). A second match, or zero, would mean an aircraft
            // whose cockpit doesn't follow the T/A-30's layout this was calibrated against.
            if (_renderTextureField == null)
                _renderTextureField = typeof(TacScreen).GetField("renderTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_renderTextureField?.GetValue(tacScreen) is not RenderTexture rt || rt == null)
            {
                Plugin.Log?.LogInfo("[NOXMFD] Internal MFD POC: TacScreen.renderTexture is null, can't check for sharing.");
                return;
            }

            int matches = 0;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Material? mat = renderer.sharedMaterial;
                if (mat == null) continue;

                // Not just .mainTexture (the shader's default/albedo slot) — TacScreen.Update() sets
                // _EmissionColor on this material, so an emissive screen shader most likely binds the
                // RT to an emission texture slot instead, which .mainTexture alone would miss (the
                // first pass of this diagnostic found ZERO matches, including for tacScreenRender
                // itself — the one renderer we already know must be using it).
                string? matchedSlot = null;
                foreach (string slot in mat.GetTexturePropertyNames())
                {
                    if (mat.GetTexture(slot) == rt) { matchedSlot = slot; break; }
                }
                if (matchedSlot == null) continue;

                matches++;
                string path = renderer.gameObject.name;
                for (var t = renderer.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC: renderer '{path}' (layer {renderer.gameObject.layer}) uses TacScreen.renderTexture via '{matchedSlot}'.");
            }
            Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC: {matches} renderer(s) total reference TacScreen.renderTexture.");
        }
    }
}
