using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // One-shot game-asset extraction: turns a live game Sprite / cockpit widget into PNG/JPEG bytes
    // (or a JSON layout) ONCE per type/session and hands it to TelemetryServer for serving at /map,
    // /icon, /weapon, /cm, /airframe. Each method dedupes via its own captured-set, so the reader
    // can call them every scan cheaply; the actual encode is async (SpriteCapture.Request — GPU
    // readback + background encode) so none of this runs on the frame's critical path.
    //
    // The lone piece of live state produced here is FailureIndicators: the cockpit StatusDisplay's
    // failure-message GameObjects, cached during airframe capture so the reader can poll their
    // activeSelf each tick (see TelemetryReader.BuildFailures). A plain object (not a MonoBehaviour) —
    // the reader owns one instance and drives it from ScanWorld / PushSnapshot.
    internal class AssetCapture
    {
        // The in-game map sprite can be a multi-K texture whose full-res ReadPixels + EncodeToPNG
        // would freeze the main thread (~670 ms, a 16 MB PNG). GPU-downscaling to a cap and encoding
        // JPEG instead cuts that by ~10-50x and is much lighter for a tablet to fetch.
        private const int MapMaxDim      = 4096; // cap the longer side; preserves aspect
        private const int MapJpegQuality = 85;   // 85 keeps grid/coast detail readable

        // Cap on new icon extractions per world scan, so a busy match's first sight of many new unit
        // types doesn't hitch. The reader budgets its per-unit icon sweep against this.
        internal const int IconsPerScan = 16;

        // Reserved /icon key for the game's missile-warning sprite. The MAP page draws incoming
        // missiles with this real in-game shape (tinted + flashed client-side) instead of a
        // hand-drawn triangle.
        internal const string MissileIconKey = "__missilewarn";


        // Aircraft-type map icons we've already extracted (keyed by unitName).
        private readonly HashSet<string> _capturedIcons = new HashSet<string>();
        // Aircraft definition names whose part layout has already been dumped to the log (one-shot).
        private readonly HashSet<string> _loggedPartLayouts = new HashSet<string>();
        // Aircraft definition names whose airframe silhouette assets have been captured.
        private readonly HashSet<string> _capturedAirframes = new HashSet<string>();
        // Weapon-type icons we've already extracted (keyed by weapon display name).
        private readonly HashSet<string> _capturedWeaponIcons = new HashSet<string>();
        private bool _capturedFlareIcon  = false;
        private bool _capturedJammerIcon = false;
        private bool _missileIconCaptured;
        private bool _mapCaptured;

        // Cached reflection handles into StatusDisplay's private serialized fields.
        private static FieldInfo? _sdStatusDisplaysField;
        private static FieldInfo? _sdBackgroundField;
        private static FieldInfo? _sdFailureIndicatorsField;

        // Cockpit StatusDisplay failure-indicator GameObjects, cached during airframe capture. Any GO
        // with activeSelf=true means the game has fired its OnReportDamage event for the matching
        // message (e.g. "L ENG FIRE" when the left Turbofan dies) — the GO name IS the message. This
        // is the one capture output the reader reads back (each tick, in BuildFailures) rather than
        // the only consumer being TelemetryServer.
        private GameObject[] _failureIndicators = Array.Empty<GameObject>();
        public GameObject[] FailureIndicators => _failureIndicators;

        // StatusDisplay's `statusDisplays` and `aircraftBackground` are private serialized fields,
        // so reflection is needed to reach them (cached once below). Part layouts are emitted
        // normalized 0..1 in the background's local UI rect, so the web side just multiplies by
        // its rendered silhouette size.
        public void TryCaptureAirframe(Aircraft ac)
        {
            string key = ac.definition != null ? ac.definition.unitName : null;
            if (string.IsNullOrEmpty(key) || _capturedAirframes.Contains(key)) return;

            StatusDisplay sd = UnityEngine.Object.FindObjectOfType<StatusDisplay>(includeInactive: true);
            if (sd == null) return;   // not built yet — try again next slow scan

            if (_sdStatusDisplaysField == null)
                _sdStatusDisplaysField = typeof(StatusDisplay).GetField("statusDisplays", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_sdBackgroundField == null)
                _sdBackgroundField = typeof(StatusDisplay).GetField("aircraftBackground", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_sdFailureIndicatorsField == null)
                _sdFailureIndicatorsField = typeof(StatusDisplay).GetField("failureIndicators", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_sdStatusDisplaysField == null || _sdBackgroundField == null)
            {
                Plugin.Log?.LogWarning("[NOXMFD] AVN: StatusDisplay reflection fields not found — airframe capture disabled.");
                _capturedAirframes.Add(key);
                return;
            }

            Image bgImage     = _sdBackgroundField.GetValue(sd)     as Image;
            System.Collections.IList partsList = _sdStatusDisplaysField.GetValue(sd) as System.Collections.IList;
            if (bgImage == null || partsList == null)
                return;   // StatusDisplay found but not populated yet — retry next slow scan (don't cache a miss)

            RectTransform bgRT = bgImage.rectTransform;

            // Right after a respawn / plane change the StatusDisplay can exist for a beat with a
            // zero-size rect; capturing then makes GetPartPlacement reject every part (its zero-rect
            // guard) and would cache an EMPTY layout for this type forever (key already added to
            // _capturedAirframes, never retried). Returning here re-tries on the next slow scan
            // until the cockpit panel is measured.
            if (bgImage.sprite == null || bgRT.rect.width <= 0.0001f || bgRT.rect.height <= 0.0001f)
                return;

            _capturedAirframes.Add(key);

            // Logs the bg's world-space orientation: the .right/.up axes give the world direction
            // of the bg's local +X/+Y, which GetPartPlacement below uses to mirror cx/cy — scale
            // alone misses a 180° rotation flip (rotation negates an axis without changing
            // lossyScale).
            Vector3 bgLs = bgRT.lossyScale;
            Vector3 bgR  = bgRT.right;
            Vector3 bgU  = bgRT.up;
            Vector3 bgEu = bgRT.eulerAngles;
            Plugin.Log?.LogInfo(
                $"[NOXMFD] AVN bg lossyScale=({bgLs.x:0.000},{bgLs.y:0.000},{bgLs.z:0.000})  " +
                $"rectSize=({bgRT.rect.width:0.0},{bgRT.rect.height:0.0})  " +
                $"right=({bgR.x:0.00},{bgR.y:0.00},{bgR.z:0.00})  up=({bgU.x:0.00},{bgU.y:0.00},{bgU.z:0.00})  " +
                $"euler=({bgEu.x:0.0},{bgEu.y:0.0},{bgEu.z:0.0})");

            // Background silhouette — one PNG, served at /airframe?type=<key>&part=__bg
            if (bgImage.sprite != null)
            {
                string bgKey = key;
                SpriteCapture.Request(bgImage.sprite, SpriteCapture.Encoding.Png, synthAlpha: false, quality: 0, maxDim: 0,
                    bgPng => { if (bgPng != null) TelemetryServer.SetAirframeImage(bgKey, "__bg", bgPng); });
            }

            // Per-part PNGs + layout entries.
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"").Append(JsonLite.EscapeJson(key)).Append("\",\"parts\":[");
            int partCount = 0;
            int flippedCount = 0;
            for (int i = 0; i < partsList.Count; i++)
            {
                PartStatusDisplay psd = partsList[i] as PartStatusDisplay;
                if (psd == null || psd.partImage == null) continue;

                Image img = psd.partImage;
                string name = img.gameObject != null ? img.gameObject.name : null;
                if (string.IsNullOrEmpty(name)) continue;

                if (!GetPartPlacement(img.rectTransform, bgRT, out float cx, out float cy, out float w, out float h, out float rotZ, out int sx, out int sy))
                    continue;

                // Skip degenerate "full-frame" parts: rect == the whole bg, centred (w/h ≈ 1, cx/cy ≈ 0.5).
                // Some mods author a part as a full-canvas overlay sprite instead of a small positioned
                // one; stacking those into the per-part mask produces a frame-filling blob or mirror-
                // reversed labels over the silhouette, so they're dropped, leaving the clean bg outline
                // plus any normally-placed parts. No stock aircraft has a part remotely this large (max
                // ~0.26), and mods that place large overlay parts correctly sit off-centre, so the
                // centred-AND-full-frame test doesn't catch a part that would render right. This is a
                // heuristic on placement, not intent — a genuinely frame-sized, centred, meaningful part
                // would be dropped too; revisit if one appears.
                if (w >= 0.98f && h >= 0.98f && Mathf.Abs(cx - 0.5f) < 0.02f && Mathf.Abs(cy - 0.5f) < 0.02f)
                    continue;

                if (img.sprite != null)
                {
                    string partKey = key, partName = name;
                    SpriteCapture.Request(img.sprite, SpriteCapture.Encoding.Png, synthAlpha: false, quality: 0, maxDim: 0,
                        png => { if (png != null) TelemetryServer.SetAirframeImage(partKey, partName, png); });
                }

                if (partCount > 0) sb.Append(',');
                partCount++;
                if (sx < 0 || sy < 0) flippedCount++;
                sb.Append('{')
                  .Append("\"n\":\"").Append(JsonLite.EscapeJson(name)).Append("\",")
                  .Append("\"cx\":").Append(cx.ToString("0.00000", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"cy\":").Append(cy.ToString("0.00000", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"w\":").Append(w.ToString("0.00000", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"h\":").Append(h.ToString("0.00000", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"r\":").Append(rotZ.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"sx\":").Append(sx).Append(',')
                  .Append("\"sy\":").Append(sy).Append(',')
                  .Append("\"rt\":").Append(psd.redStatusThreshold.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append('}');
            }
            sb.Append("]}");
            TelemetryServer.SetAirframeLayout(key, sb.ToString());

            // Only the GameObject references are cached, not position or rendered text — the AVN
            // page owns visual styling. The reader polls activeSelf on these each tick to know
            // which messages are currently firing.
            var failureGOs = new List<GameObject>();
            System.Collections.IList failureList = _sdFailureIndicatorsField?.GetValue(sd) as System.Collections.IList;
            if (failureList != null)
            {
                for (int i = 0; i < failureList.Count; i++)
                {
                    GameObject go = failureList[i] as GameObject;
                    if (go != null) failureGOs.Add(go);
                }
            }
            _failureIndicators = failureGOs.ToArray();

            Plugin.Log?.LogInfo($"[NOXMFD] Captured airframe silhouette '{key}' (bg + {partCount} parts, {flippedCount} flipped, {_failureIndicators.Length} failure messages: {string.Join(", ", System.Linq.Enumerable.Select(_failureIndicators, g => g.name))}).");
        }

        // Computes a part's placement relative to the background's local rect, in normalized 0..1
        // coords (origin top-left to match web layout). All math is done in the bg's LOCAL space
        // (via InverseTransformPoint) so parent transforms — including mirroring flips applied by
        // the cockpit canvas — are handled cleanly and cx/cy match what the player sees.
        // sx/sy report per-part flips relative to the bg's coordinate frame: the cockpit prefab
        // often reuses one sprite for symmetric parts and flips one via RectTransform scale (e.g.
        // wing1_R = wing1_L with scale.x = -1); the /airframe endpoint returns the raw sprite so the
        // renderer applies the same flip via CSS transform: scale(sx, sy).
        // Returns false if the bg rect has zero size (silhouette not laid out yet).
        private static bool GetPartPlacement(RectTransform partRT, RectTransform bgRT,
            out float cx, out float cy, out float w, out float h, out float rotZ,
            out int sx, out int sy)
        {
            cx = cy = w = h = rotZ = 0f;
            sx = sy = 1;
            if (partRT == null || bgRT == null) return false;

            Rect bgRect = bgRT.rect;
            if (bgRect.width <= 0.0001f || bgRect.height <= 0.0001f) return false;

            // Part's centre in BG-local coords. The world <-> local round-trip absorbs any
            // intermediate transforms (offsets, rotations, scales).
            Vector3 partWorldCenter = partRT.TransformPoint(partRT.rect.center);
            Vector3 partBgLocal     = bgRT.InverseTransformPoint(partWorldCenter);

            cx = (partBgLocal.x - bgRect.xMin) / bgRect.width;
            cy = (partBgLocal.y - bgRect.yMin) / bgRect.height;
            cy = 1f - cy;                                       // origin top-left for web

            // Mirror to match what the player sees. Checking the bg's world-space right/up axis
            // directions (rather than lossyScale directly) covers both mirror causes — a negative
            // scale on the axis, or a 180° rotation around the orthogonal axis (which negates the
            // axis direction without changing lossyScale).
            if (bgRT.right.x < 0f) cx = 1f - cx;
            if (bgRT.up.y    < 0f) cy = 1f - cy;

            // Size in fractions of bg width/height, scaled by the part/bg lossy-scale ratio to
            // account for any chain of scales between them.
            float bgSx   = bgRT.lossyScale.x   == 0f ? 1f : Mathf.Abs(bgRT.lossyScale.x);
            float bgSy   = bgRT.lossyScale.y   == 0f ? 1f : Mathf.Abs(bgRT.lossyScale.y);
            float partSx = Mathf.Abs(partRT.lossyScale.x);
            float partSy = Mathf.Abs(partRT.lossyScale.y);
            w = partRT.rect.width  * (partSx / bgSx) / bgRect.width;
            h = partRT.rect.height * (partSy / bgSy) / bgRect.height;

            // Reports local rotation (not world) so it survives the bg-local re-frame. CCW positive.
            rotZ = partRT.localEulerAngles.z;
            if (rotZ > 180f) rotZ -= 360f;

            // Opposite-sign lossy-scale between part and bg on an axis means the part is mirrored
            // on that axis; the renderer applies CSS transform: scale(sx, sy) to match.
            float pSx = partRT.lossyScale.x, pSy = partRT.lossyScale.y;
            float bSx = bgRT.lossyScale.x,   bSy = bgRT.lossyScale.y;
            sx = (pSx == 0f || bSx == 0f) ? 1 : ((pSx * bSx) < 0f ? -1 : 1);
            sy = (pSy == 0f || bSy == 0f) ? 1 : ((pSy * bSy) < 0f ? -1 : 1);
            return true;
        }

        // Captures the cockpit's weapon-station-armed panel's frontal silhouette
        // (WeaponPanel/frontProfile, sprite named "<unitName>_front"). Distinct from
        // TryCaptureAirframe's top-down damage silhouette — this is the OTHER cockpit panel, the one
        // with the GUN/TIP/PYLON/BAY "WEAPON ARMED" boxes. Served at the same /airframe endpoint,
        // under part key "__front", so no new server plumbing is needed.
        //
        // Also captures the panel's per-station hardpoint markers. Their LAYOUT is static (captured
        // once, via the same GetPartPlacement math as the damage-silhouette parts). Their COLOR is
        // live game state (green = ammo remaining, red = exhausted) computed by the game's own
        // script every frame; rather than re-deriving armed/exhausted — which would need a
        // name<->WeaponStation mapping that doesn't exist anywhere reliable — this mirrors the same
        // Image.color the player already sees (ReadFrontalMarkerStates, polled per snapshot tick).
        private readonly HashSet<string> _capturedFrontal = new HashSet<string>();
        private readonly Dictionary<string, List<(string name, Image img)>> _frontalMarkers =
            new Dictionary<string, List<(string, Image)>>();

        public void TryCaptureFrontalSilhouette(Aircraft ac)
        {
            string key = ac.definition != null ? ac.definition.unitName : null;
            if (string.IsNullOrEmpty(key) || _capturedFrontal.Contains(key)) return;

            // frontProfile only exists inside the LIVE (instantiated) WeaponPanel; FindObjectsOfTypeAll
            // also returns inactive template prefabs for other aircraft types, which must be skipped.
            Image frontImg = null;
            foreach (Image img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img == null || img.gameObject.name != "frontProfile") continue;
                if (img.gameObject.scene.name == null) continue;   // prefab asset, not a live instance
                frontImg = img;
                break;
            }
            if (frontImg == null || frontImg.sprite == null) return;   // not built yet — retry next slow scan

            _capturedFrontal.Add(key);
            string bgKey = key;
            SpriteCapture.Request(frontImg.sprite, SpriteCapture.Encoding.Png, synthAlpha: false, quality: 0, maxDim: 0,
                png => { if (png != null) TelemetryServer.SetAirframeImage(bgKey, "__front", png); });

            RectTransform frontRT = frontImg.rectTransform;
            var markers = new List<(string, Image)>();
            var sb = new StringBuilder();
            sb.Append("{\"parts\":[");
            int n = 0;
            for (int i = 0; i < frontRT.childCount; i++)
            {
                Transform child = frontRT.GetChild(i);
                // Naming varies by airframe ("hardpoint_Gun" vs "hardpoint0"), so match the common
                // prefix rather than either exact shape.
                if (!child.name.StartsWith("hardpoint", StringComparison.OrdinalIgnoreCase)) continue;
                Image mImg = child.GetComponent<Image>();
                if (mImg == null) continue;
                if (!GetPartPlacement(mImg.rectTransform, frontRT, out float cx, out float cy, out float w, out float h, out float rotZ, out int sx, out int sy))
                    continue;

                markers.Add((child.name, mImg));
                if (n > 0) sb.Append(',');
                n++;
                sb.Append('{')
                  .Append("\"n\":\"").Append(JsonLite.EscapeJson(child.name)).Append("\",")
                  .Append("\"cx\":").Append(cx.ToString("0.00000", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"cy\":").Append(cy.ToString("0.00000", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"w\":").Append(w.ToString("0.00000", CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"h\":").Append(h.ToString("0.00000", CultureInfo.InvariantCulture))
                  .Append('}');
            }
            sb.Append("]}");
            _frontalMarkers[key] = markers;
            TelemetryServer.SetAirframeLayout(key + "__front", sb.ToString());
        }

        // Reports armed/exhausted, not the raw color: the AFM page renders these in the theme's own
        // solid green/red, not a mirrored, semi-transparent in-game hue. The game sets the marker to
        // exactly Color.green when armed or Color.red when exhausted (briefly lerped toward a flash
        // color on a hit), so "which channel dominates" cleanly classifies both states. A station
        // with nothing mounted is flat gray (equal channels), which a plain g>=r check would
        // misread as armed — gray is checked first to catch that case.
        public List<(string name, string state)> ReadFrontalMarkerStates(string type)
        {
            var result = new List<(string, string)>();
            if (string.IsNullOrEmpty(type) || !_frontalMarkers.TryGetValue(type, out var markers)) return result;
            foreach (var (name, img) in markers)
            {
                if (img == null) continue;   // stale reference from before a scene reload
                Color c = img.color;
                string state = (c.r == c.g && c.g == c.b) ? "empty" : (c.g >= c.r ? "armed" : "exhausted");
                result.Add((name, state));
            }
            return result;
        }

        // Debug aid for the AVN-page silhouette design: walks Aircraft.partLookup and logs every
        // UnitPart's name, HP, and detached state, once per aircraft definition per session. Mirrors
        // the data the game's own StatusDisplay uses to colour its silhouette segments (it matches
        // Image.gameObject.name against UnitPart.gameObject.name).
        public void TryLogPartLayout(Aircraft ac)
        {
            string key = ac.definition != null ? ac.definition.unitName : null;
            if (string.IsNullOrEmpty(key) || _loggedPartLayouts.Contains(key)) return;

            var parts = ac.partLookup;
            if (parts == null) return;

            _loggedPartLayouts.Add(key);
            Plugin.Log?.LogInfo($"[NOXMFD] AVN parts for '{key}' (count={parts.Count}):");
            for (int i = 0; i < parts.Count; i++)
            {
                UnitPart p = parts[i];
                if (p == null) { Plugin.Log?.LogInfo($"  [{i}] <null>"); continue; }
                string n = p.gameObject != null ? p.gameObject.name : "<no-go>";
                Plugin.Log?.LogInfo($"  [{i}] {n}  hp={p.hitPoints:0.#}  detached={p.IsDetached()}");
            }
        }

        // WeaponInfo assets already dumped to the log this session, keyed by the asset's own Unity
        // object name (stable identity — unlike weaponName/shortName, which can be blank or shared
        // across variants). One line per distinct weapon across all aircraft types seen this
        // session, not per-aircraft.
        private readonly HashSet<string> _loggedWeapons = new HashSet<string>();

        // Diagnostic dump of every weapon's full WeaponInfo data to LogOutput.log, for design
        // questions that don't need a dedicated extraction tool. Walks EVERY station, including
        // ones BuildLoadout hides (hideInDisplay, cargo/troops/sling), since those are exactly the
        // kind of thing worth seeing here.
        public void TryLogWeaponInfo(Aircraft ac)
        {
            var stations = ac.weaponStations;
            if (stations == null) return;

            foreach (WeaponStation st in stations)
            {
                WeaponInfo info = st != null ? st.WeaponInfo : null;
                if (info == null) continue;
                string key = !string.IsNullOrEmpty(info.name) ? info.name
                           : !string.IsNullOrEmpty(info.weaponName) ? info.weaponName
                           : info.shortName ?? "?";
                if (_loggedWeapons.Contains(key)) continue;
                _loggedWeapons.Add(key);

                Plugin.Log?.LogInfo(
                    $"[NOXMFD] Weapon '{key}' name=\"{info.weaponName}\" short=\"{info.shortName}\" " +
                    $"flags[gun={info.gun} missile={info.missile} bomb={info.bomb} glideBomb={info.glideBomb} " +
                    $"jammer={info.jammer} energy={info.energy} nuclear={info.nuclear} strategic={info.strategic} " +
                    $"overHorizon={info.overHorizon} laserGuided={info.laserGuided} boresight={info.boresight} " +
                    $"cargo={info.cargo} troops={info.troops} sling={info.sling} " +
                    $"rearmGround={info.rearmGround} rearmShip={info.rearmShip} hideInDisplay={info.hideInDisplay}] " +
                    $"role[antiSurface={info.effectiveness.antiSurface:0.##} antiAir={info.effectiveness.antiAir:0.##} " +
                    $"antiMissile={info.effectiveness.antiMissile:0.##} antiRadar={info.effectiveness.antiRadar:0.##}] " +
                    $"pK={info.pK:0.###} fireInterval={info.fireInterval:0.###} muzzleVelocity={info.muzzleVelocity:0.#} " +
                    $"maxSpeed={info.maxSpeed:0.#} dragCoef={info.dragCoef:0.###} gravMult={info.gravMult:0.##} " +
                    $"pierceDamage={info.pierceDamage:0.#} blastDamage={info.blastDamage:0.#} " +
                    $"armorTierEffectiveness={info.armorTierEffectiveness:0.##} airburstHeight={info.airburstHeight:0.#} " +
                    $"visibilityWhenFired={info.visibilityWhenFired:0.##} costPerRound={info.costPerRound:0.#} " +
                    $"massPerRound={info.massPerRound:0.##} useWeaponDoors={info.useWeaponDoors} " +
                    $"hasIcon={info.weaponIcon != null}");
            }
        }

        // Extracts the flares + radar jammer Sprites from any matching component on the
        // aircraft, once each. Saves them as PNGs so /cm?type=flares|jammer can serve them.
        public void TryCaptureCmIcons(Aircraft ac)
        {
            if (!_capturedFlareIcon)
            {
                FlareEjector? fe = ac.GetComponentInChildren<FlareEjector>();
                if (fe != null && fe.displayImage != null)
                {
                    _capturedFlareIcon = true;   // got the sprite; capture once (async)
                    SpriteCapture.Request(fe.displayImage, SpriteCapture.Encoding.Png, synthAlpha: true,
                        quality: 0, maxDim: 0, png => { if (png != null) TelemetryServer.SetCmIcon("flares", png); });
                }
            }
            if (!_capturedJammerIcon)
            {
                RadarJammer? rj = ac.GetComponentInChildren<RadarJammer>();
                if (rj != null && rj.displayImage != null)
                {
                    _capturedJammerIcon = true;  // got the sprite; capture once (async)
                    SpriteCapture.Request(rj.displayImage, SpriteCapture.Encoding.Png, synthAlpha: true,
                        quality: 0, maxDim: 0, png => { if (png != null) TelemetryServer.SetCmIcon("jammer", png); });
                }
            }
        }

        // TGT filter vehicle-type icons captured this session (keyed by typeName).
        private readonly HashSet<string> _capturedTgtIcons = new HashSet<string>();

        // Extracts the TGT filter panel's vehicle-type icons (TRUCK … RDR) to PNG once each, keyed
        // by typeName so the web TGT page's /tgt-icon?type= requests match the "tgt" telemetry
        // block's vehicle names. Source is Encyclopedia.i.vehicleTypes — the same list the game's
        // TargetListSelector builds its toggle row from. Cheap to call every slow scan: it no-ops
        // once all types are captured. synthAlpha because these icons ship opaque (light-on-dark).
        public void TryCaptureVehicleTypeIcons()
        {
            Encyclopedia enc = Encyclopedia.i;
            if (enc == null) return;   // not ready — retry next scan
            CaptureTypeIcons(enc.vehicleTypes, _capturedTgtIcons,
                vt => vt.typeName, vt => vt.typeSprite, TelemetryServer.SetTgtIcon);
        }

        private readonly HashSet<string> _capturedBdfShipIcons = new HashSet<string>();

        // Extracts the BDF page's ship-type icons (CV … LC) to PNG once each, keyed by typeName so
        // the web BDF page's /bdf-icon?type= requests match the "bdf" telemetry block's ship-row
        // names. Source is Encyclopedia.i.shipTypes — the same list the game's InfoPanel_Faction
        // builds its Ships row from. Straight mirror of TryCaptureVehicleTypeIcons above; a
        // dedicated /bdf-icon keeps the key space separate from the generic /icon (aircraft
        // unitNames) rather than risking a collision.
        public void TryCaptureShipTypeIcons()
        {
            Encyclopedia enc = Encyclopedia.i;
            if (enc == null) return;   // not ready — retry next scan
            CaptureTypeIcons(enc.shipTypes, _capturedBdfShipIcons,
                st => st.typeName, st => st.typeSprite, TelemetryServer.SetBdfIcon);
        }

        private readonly HashSet<string> _capturedBdfLogos = new HashSet<string>();

        // Extracts a faction's header logo (Faction.factionColorLogo) to PNG once per faction name,
        // sharing the /bdf-icon store with the ship-type icons — faction names ("BOSCALI") never
        // collide with the short ship-type codes ("CV" … "LC").
        public void TryCaptureFactionLogo(FactionHQ hq)
        {
            Faction faction = hq != null ? hq.faction : null;
            if (faction == null || string.IsNullOrEmpty(faction.factionName)) return;
            string name = faction.factionName;
            if (_capturedBdfLogos.Contains(name)) return;
            _capturedBdfLogos.Add(name);
            if (faction.factionColorLogo == null) return;

            SpriteCapture.Request(faction.factionColorLogo, SpriteCapture.Encoding.Png, synthAlpha: true, quality: 0, maxDim: 0,
                png => { if (png != null) TelemetryServer.SetBdfIcon(name, png); });
        }

        private readonly HashSet<string> _capturedBuildingIcons = new HashSet<string>();

        // The HUD page's building-type icons — the same typeSprite capture as the vehicles above, on
        // Encyclopedia.buildingTypes ("CIV" … "AMMO"). Served at /building-icon, keyed apart from the
        // vehicle icons because "RDR" is both a vehicle and a building type. One-time per type.
        public void TryCaptureBuildingTypeIcons()
        {
            Encyclopedia enc = Encyclopedia.i;
            if (enc == null) return;   // not ready — retry next scan
            CaptureTypeIcons(enc.buildingTypes, _capturedBuildingIcons,
                bt => bt.typeName, bt => bt.typeSprite, TelemetryServer.SetBuildingIcon);
        }

        // Shared shape behind TryCaptureVehicleTypeIcons/TryCaptureShipTypeIcons/
        // TryCaptureBuildingTypeIcons above: marks a name captured regardless of whether it has a
        // sprite yet, so a type with no icon is never retried. TryCaptureHudCategoryIcons below
        // walks a fixed 5-slot array via transform.Find instead of an Encyclopedia list — a
        // genuinely different shape, not folded in here.
        private static void CaptureTypeIcons<T>(
            IEnumerable<T> list, HashSet<string> captured,
            Func<T, string> nameOf, Func<T, Sprite> spriteOf, Action<string, byte[]> setIcon) where T : class
        {
            if (list == null) return;   // not ready — retry next scan
            foreach (T item in list)
            {
                if (item == null) continue;
                string name = nameOf(item);
                if (string.IsNullOrEmpty(name) || captured.Contains(name)) continue;
                captured.Add(name);   // mark regardless so we never retry this type
                Sprite sprite = spriteOf(item);
                if (sprite == null) continue;

                SpriteCapture.Request(sprite, SpriteCapture.Encoding.Png, synthAlpha: true, quality: 0, maxDim: 0,
                    png => { if (png != null) setIcon(name, png); });
            }
        }

        private readonly HashSet<string> _capturedHudCatIcons = new HashSet<string>();

        // Category index -> the fixed HUD-page label used both as the display name and this icon's
        // server key — the game exposes no per-category name to key by instead. FRIENDLY (0) and
        // ENEMY (1) are omitted: those rows have no "TopContainer/Icon" child at all.
        private static readonly (int index, string key)[] HudCategoryIconSlots =
        {
            (2, "AIRCRAFT"), (3, "MISSILES"), (4, "VEHICLES"), (5, "BUILDINGS"), (6, "SHIPS"),
        };

        // The HUD page's category-row type glyph, centred in each row as in game. HUDOptions_Category
        // exposes no Sprite field for it — unlike the vehicle/building buttons above, whose icon is
        // HUDOptions_ToggleButton.image — so it's located by child path instead: a plain
        // "TopContainer/Icon" placed by hand in the prefab. Served at /hud-cat-icon?cat=.
        public void TryCaptureHudCategoryIcons()
        {
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null || opt.listCategories == null) return;   // not built yet — retry next scan

            foreach (var slot in HudCategoryIconSlots)
            {
                if (_capturedHudCatIcons.Contains(slot.key) || slot.index >= opt.listCategories.Count)
                    continue;
                HUDOptions_Category cat = opt.listCategories[slot.index];
                if (cat == null) continue;   // retry — list still filling in

                Transform iconT = cat.transform.Find("TopContainer/Icon");
                Image img = iconT != null ? iconT.GetComponent<Image>() : null;
                if (img == null || img.sprite == null) continue;   // not ready — retry next scan

                _capturedHudCatIcons.Add(slot.key);   // mark regardless so we never retry this one
                string key = slot.key;
                SpriteCapture.Request(img.sprite, SpriteCapture.Encoding.Png, synthAlpha: true, quality: 0, maxDim: 0,
                    png => { if (png != null) TelemetryServer.SetHudCategoryIcon(key, png); });
            }
        }

        // Called from the reader's BuildLoadout as it iterates the live weapon stations.
        public void TryCaptureWeaponIcon(string name, Sprite icon)
        {
            if (string.IsNullOrEmpty(name) || _capturedWeaponIcons.Contains(name)) return;
            _capturedWeaponIcons.Add(name);
            if (icon == null) return;

            string weaponName = name;
            SpriteCapture.Request(icon, SpriteCapture.Encoding.Png, synthAlpha: true, quality: 0, maxDim: 0,
                png => { if (png != null) TelemetryServer.SetWeaponIcon(weaponName, png); });
        }

        public void TryCaptureMap(MapSettings ms)
        {
            if (_mapCaptured) return;
            Sprite mapSprite = ms.MapImage;
            if (mapSprite == null) return;   // not ready yet — retry next scan

            _mapCaptured = true;             // got a sprite; capture once (async), don't retry

            Texture src = mapSprite.texture;
            int sw = src != null ? src.width : 0;
            int sh = src != null ? src.height : 0;

            // Async + downscaled + JPEG-encoded (maps are opaque) — avoids the main-thread freeze a
            // synchronous full-res PNG capture would cause, and keeps the tablet's fetch small.
            bool started = SpriteCapture.Request(mapSprite, SpriteCapture.Encoding.Jpg, synthAlpha: false,
                quality: MapJpegQuality, maxDim: MapMaxDim, jpg =>
                {
                    if (jpg != null)
                    {
                        TelemetryServer.SetMapImage(jpg);
                        Plugin.Log?.LogInfo($"[NOXMFD] Captured in-game map ({jpg.Length} bytes, JPEG; source {sw}x{sh}).");
                    }
                    else
                        Plugin.Log?.LogWarning("[NOXMFD] Map capture failed; falling back to map file.");
                });
            if (!started) _mapCaptured = false;   // sprite unusable — allow a later retry
        }

        // Captures the game's missile-warning sprite (GameAssets.missileWarningSprite) once, under
        // the reserved MissileIconKey, so the MAP page can draw incoming missiles with the real shape.
        public void CaptureMissileWarningIcon()
        {
            if (_missileIconCaptured) return;
            try
            {
                GameAssets ga = GameAssets.i;
                if (ga == null || ga.missileWarningSprite == null) return;   // not ready — retry next scan
                _missileIconCaptured = true;                                  // got the sprite; capture once
                SpriteCapture.Request(ga.missileWarningSprite, SpriteCapture.Encoding.Png, synthAlpha: true,
                    quality: 0, maxDim: 0, png => { if (png != null) TelemetryServer.SetIcon(MissileIconKey, png); });
            }
            catch { /* retry on a later scan */ }
        }

        // Extracts a unit type's top-down map icon to PNG, once per type, and registers it.
        // Returns true if it kicked off a (costly) extraction this call, so callers can budget.
        public bool TryCaptureIcon(UnitDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.unitName) || _capturedIcons.Contains(def.unitName))
                return false;

            _capturedIcons.Add(def.unitName); // mark regardless so we never retry this type
            if (def.mapIcon == null)
            {
                // No icon for this type (buildings, etc.) — register the transparent sentinel so
                // /icon answers 200 and the client stops re-requesting (it draws its square instead).
                TelemetryServer.SetIcon(def.unitName, TelemetryServer.NoIconPng);
                return false;
            }

            // Fall back to the sentinel if extraction fails, so this type never 404s either.
            string name = def.unitName;
            bool started = SpriteCapture.Request(def.mapIcon, SpriteCapture.Encoding.Png, synthAlpha: true,
                quality: 0, maxDim: 0, png => TelemetryServer.SetIcon(name, png ?? TelemetryServer.NoIconPng));
            if (!started) TelemetryServer.SetIcon(name, TelemetryServer.NoIconPng);
            return started;
        }
    }
}
