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
        // One TacScreen canvas backs multiple physical cockpit screens through regions of the same
        // texture. Each crop therefore belongs to the aircraft mesh rather than to a canvas or
        // material transform; callers use the full canvas only when no calibrated geometry exists.
        //
        // Split marks a wide screen (~2.8:1 like the T/A-30) that gets HSD/TGP-left + RWR-right
        // (docs/internal-mfd.md "Split-screen layout"); false means a squarish screen (~1.3-1.7:1)
        // that gets one full region showing RWR, with InternalMfdTgpPage overriding it the same way
        // TGP overrides the split layout's HSD pane. SAH-46 Chicane's ~2.0:1 sat between the two
        // clusters seen in the rest of this table (wide: ~2.8:1, squarish: ~1.3-1.7:1), so it uses
        // the squarish layout.
        internal readonly struct ScreenGeometry
        {
            internal readonly Vector2 AnchorMin;
            internal readonly Vector2 AnchorMax;
            internal readonly bool Split;

            // Native TGP feeds take precedence on aircraft that already render them on this screen;
            // drawing another feed there would stack two independently aligned overlays.
            internal readonly bool NativeTgpOnLock;

            internal ScreenGeometry(Vector2 anchorMin, Vector2 anchorMax, bool split, bool nativeTgpOnLock = false)
            {
                AnchorMin = anchorMin;
                AnchorMax = anchorMax;
                Split = split;
                NativeTgpOnLock = nativeTgpOnLock;
            }
        }

        // These aircraft share one calibrated wide-screen crop; a shared value prevents drift.
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
            // The left edge reaches the canvas boundary; the right edge stops before the adjacent
            // engine display region.
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

        // Cockpit belongs to a separate scene hierarchy, so its private aircraft reference is the
        // stable way to select the local player's instance before reading its TacScreen canvas.
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
                if (!TryRead(_cockpitAircraftField, candidate, "Cockpit.aircraft", out object? owner)) return false;
                if (ReferenceEquals(owner, aircraft)) { cockpit = candidate; break; }
            }
            if (cockpit == null) { LogFailure("no Cockpit component references this aircraft"); return false; }

            // tacScreen is only ever instantiated for the local player's own aircraft, and only
            // after Aircraft.onInitialize fires — null here just means "not ready yet this frame".
            if (_tacScreenField == null)
                _tacScreenField = typeof(Cockpit).GetField("tacScreen", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_tacScreenField == null)
            { LogFailure("Cockpit.tacScreen field not found via reflection"); return false; }
            if (!TryRead(_tacScreenField, cockpit, "Cockpit.tacScreen", out object? tacScreenValue)) return false;
            if (tacScreenValue is not TacScreen tacScreen || tacScreen == null)
            { LogFailure("Cockpit.tacScreen not yet instantiated"); return false; }

            if (_canvasField == null)
                _canvasField = typeof(TacScreen).GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_canvasField == null)
            { LogFailure("TacScreen.canvas field not found via reflection"); return false; }
            if (!TryRead(_canvasField, tacScreen, "TacScreen.canvas", out object? canvasValue)) return false;
            if (canvasValue is not Canvas c || c == null)
            { LogFailure("TacScreen.canvas field missing/null"); return false; }

            if (_camField == null)
                _camField = typeof(TacScreen).GetField("cam", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_camField == null)
            { LogFailure("TacScreen.cam field not found via reflection"); return false; }
            if (!TryRead(_camField, tacScreen, "TacScreen.cam", out object? cameraValue)) return false;
            cam = cameraValue as Camera;

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

        private static bool TryRead(FieldInfo field, object instance, string member, out object? value)
        {
            try
            {
                value = field.GetValue(instance);
                return true;
            }
            catch (System.Exception ex)
            {
                value = null;
                string reason = member + " reflection read failed: " + ex.GetType().Name;
                if (_lastFailure == reason) return false;
                _lastFailure = reason;
                Plugin.Log?.LogWarning($"[NOXMFD] Internal MFD POC: {reason}: {ex.Message}");
                return false;
            }
        }

            // Logged on attach so a live test can distinguish resolution failure from rendering
            // failure without emitting diagnostics every frame.
        private static void LogAttachDiagnostics(Aircraft aircraft, TacScreen tacScreen, Canvas canvas, Camera? cam)
        {
            try
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
            if (_renderTextureField == null)
            {
                Plugin.Log?.LogInfo("[NOXMFD] Internal MFD POC: TacScreen.renderTexture field is unavailable, can't check for sharing.");
                return;
            }
            if (!TryRead(_renderTextureField, tacScreen, "TacScreen.renderTexture", out object? renderTextureValue)) return;
            if (renderTextureValue is not RenderTexture rt || rt == null)
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
                // RT to an emission texture slot instead, which .mainTexture alone would miss.
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
            catch (System.Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] Internal MFD POC attach diagnostics failed: {ex}");
            }
        }
    }
}
