using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOXMFD
{
    // Finds the local player's cockpit TacScreen canvas (docs/internal-mfd.md) — the insertion
    // point every internal-MFD page ultimately renders into. Separated from InternalMfdController's own
    // toggle/lifecycle/layout concerns: this only knows how to locate the canvas for a given
    // aircraft (and log why it couldn't), not what gets built on it or how a pane splits.
    internal static class InternalMfdScreenResolver
    {
        // A live test showed a full-canvas overlay painting on all three cockpit screens (center
        // MFD + two side sub-displays), not just the center one. Confirmed by directly inspecting
        // the game's mesh/material assets (not just decompiled C#): all three screens are ONE mesh
        // ("tacscreen"), ONE Renderer, ONE material — the three screens are different regions of
        // the SAME 1024x512 texture, with the crop baked directly into the mesh's own per-vertex
        // UVs. This is per-aircraft mesh data (docs/internal-mfd.md's per-airframe-geometry
        // question) — callers fall back to the full canvas for any aircraft not in this table.
        //
        // The T/A-30 Compass entry (V 0.29068-1.0, full width) is live-measured; 11 more are
        // converted from MFDCustomizer (https://github.com/9138noms/MFDCustomizer, MIT license)'s
        // own hand-measured "main" slot per aircraft, which already matched this repo's own T/A-30
        // measurement to within rounding. Its rects are local canvas coordinates (centerX, centerY,
        // width, height) on the same 1024x512 canvas; converting to anchor fractions is
        // (center ± size/2 + canvasHalfSize) / canvasSize, independently per axis (1024 wide, 512
        // tall). VT-7 Vagrant isn't in MFDCustomizer's table at all — it shares FS-20 Vortex's own
        // crop instead (see Fs20VortexGeometry below). All 13 entries are live-confirmed; CI-22
        // Cricket's own comment below covers the one conversion that needed adjustment.
        //
        // Split marks a wide screen (~2.8:1 like the T/A-30) that gets HSD/TGP-left + RWR-right
        // (docs/internal-mfd.md "Split-screen layout"); false means a squarish screen (~1.3-1.7:1)
        // that gets one full region showing RWR, with InternalMfdTgpPage overriding it the same way
        // TGP overrides the split layout's HSD pane. SAH-46 Chicane's ~2.0:1 sat between the two
        // clusters seen in the rest of this table (wide: ~2.8:1, squarish: ~1.3-1.7:1) — classified
        // and confirmed squarish.
        internal readonly struct ScreenGeometry
        {
            internal readonly Vector2 AnchorMin;
            internal readonly Vector2 AnchorMax;
            internal readonly bool Split;

            // True when this aircraft's covered screen already natively shows a correct TGP view
            // on lock, with no internal-MFD involvement (confirmed live on CI-22 Cricket: toggling
            // internal MFD off during a lock shows the same clean feed on the same screen) — unlike
            // the T/A-30, where this screen shows something else natively during a lock, which is
            // why InternalMfdTgpPage exists at all. When true, InternalMfdController skips building
            // a TGP-override page for this aircraft entirely and hides the whole overlay (not just
            // swaps to our own TGP page) whenever locked, so the native feed shows through clean
            // instead of a second, slightly misaligned copy stacking on top of it (seen live as
            // doubled/ghosted overlay text). Defaults false — confirmed live as the correct default
            // (build our own override, matching the T/A-30) for every other aircraft in this table;
            // CI-22 Cricket is the only one that needs it set.
            internal readonly bool NativeTgpOnLock;

            internal ScreenGeometry(Vector2 anchorMin, Vector2 anchorMax, bool split, bool nativeTgpOnLock = false)
            {
                AnchorMin = anchorMin;
                AnchorMax = anchorMax;
                Split = split;
                NativeTgpOnLock = nativeTgpOnLock;
            }
        }

        // VT-7 Vagrant's screen (~2.7:1, matching the wide/split cluster rather than the squarish
        // one) shares this exact crop with FS-20 Vortex, confirmed live — a named value instead of
        // a second copy of the same literals, so the two entries can't silently drift apart.
        private static readonly ScreenGeometry Fs20VortexGeometry =
            new ScreenGeometry(new Vector2(0.002f, 0.2939f), new Vector2(0.998f, 0.9951f), split: true);

        internal static readonly Dictionary<string, ScreenGeometry> ScreenGeometryByAircraft = new Dictionary<string, ScreenGeometry>
        {
            ["T/A-30 Compass"]   = new ScreenGeometry(new Vector2(0f, 0.29068f), new Vector2(1f, 1f), split: true),
            ["A-19 Brawler"]     = new ScreenGeometry(new Vector2(0.002f, 0.2969f), new Vector2(0.998f, 1f), split: true),
            ["FS-12 Revoker"]    = new ScreenGeometry(new Vector2(0.002f, 0.2959f), new Vector2(1f, 0.9932f), split: true),
            ["FS-20 Vortex"]     = Fs20VortexGeometry,
            ["VT-7 Vagrant"]     = Fs20VortexGeometry,
            ["SAH-46 Chicane"]   = new ScreenGeometry(new Vector2(0.002f, 0.2568f), new Vector2(0.748f, 1f), split: false),
            ["KR-67 Ifrit"]      = new ScreenGeometry(new Vector2(0.001f, 0.1807f), new Vector2(0.7471f, 1f), split: false),
            // Widened past MFDCustomizer's own (0.0889,0.7627): left is pushed to the canvas edge —
            // nothing in their table claims that space; right is fenced by their own "engine" slot
            // starting at X~0.805 (converted), so only modestly widened there.
            ["CI-22 Cricket"]    = new ScreenGeometry(new Vector2(0f, 0.0039f), new Vector2(0.79f, 0.9961f), split: false, nativeTgpOnLock: true),
            ["SFB-81 Darkreach"] = new ScreenGeometry(new Vector2(0f, 0.2832f), new Vector2(0.5708f, 0.998f), split: false),
            ["EW-25 Medusa"]     = new ScreenGeometry(new Vector2(0.0034f, 0.2568f), new Vector2(0.5591f, 0.9932f), split: false),
            ["VL-49 Tarantula"]  = new ScreenGeometry(new Vector2(0f, 0.2715f), new Vector2(0.5005f, 0.998f), split: false),
            ["UH-90 Ibis"]       = new ScreenGeometry(new Vector2(0f, 0.252f), new Vector2(0.52f, 0.998f), split: false),
            ["Alkyon AB-4"]      = new ScreenGeometry(new Vector2(0f, 0.2842f), new Vector2(0.5703f, 0.958f), split: false),
        };

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
            // Distinct from "no Cockpit component references this aircraft" below, same as every
            // other reflection field in this method — without this check, a renamed/removed
            // Cockpit.aircraft field would silently fall through to that message instead, which
            // reads as "the scan ran and found no match" rather than "the reflection assumption
            // itself broke," sending a future debugger at the wrong half of this method.
            if (_cockpitAircraftField == null)
            { LogFailure("Cockpit.aircraft field not found via reflection"); return false; }

            Cockpit? cockpit = null;
            foreach (var candidate in Object.FindObjectsByType<Cockpit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ReferenceEquals(_cockpitAircraftField.GetValue(candidate), aircraft)) { cockpit = candidate; break; }
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
            string unitName = aircraft.definition?.unitName ?? "?";

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
