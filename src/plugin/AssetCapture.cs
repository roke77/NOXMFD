using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // One-shot game-asset extraction, peeled out of TelemetryReader so the reader stays focused on
    // per-frame telemetry. Everything here turns a live game Sprite / cockpit widget into PNG/JPEG
    // bytes (or a JSON layout) ONCE per type/session and hands it to TelemetryServer for serving at
    // /map, /icon, /weapon, /cm, /airframe. Each method dedupes via its own captured-set, so the
    // reader can call them every scan cheaply; the actual encode is async (SpriteCapture.Request — GPU
    // readback + background encode) so none of this runs on the frame's critical path.
    //
    // The lone piece of live state produced here is FailureIndicators: the cockpit StatusDisplay's
    // failure-message GameObjects, cached during airframe capture so the reader can poll their
    // activeSelf each tick (see TelemetryReader.BuildFailures). A plain object (not a MonoBehaviour) —
    // the reader owns one instance and drives it from ScanWorld / PushSnapshot.
    internal class AssetCapture
    {
        // Map capture: the in-game map sprite can be huge — a multi-K texture whose full-res
        // ReadPixels + EncodeToPNG would freeze the main thread ~670 ms on mission load (a 16 MB
        // PNG). So we GPU-downscale to a sane cap and encode JPEG instead — typically a ~10-50×
        // size cut (also much lighter for a tablet to fetch) for one one-time capture.
        private const int MapMaxDim      = 4096; // cap the longer side; preserves aspect
        private const int MapJpegQuality = 85;   // JPEG quality 0–100; 85 keeps grid/coast detail readable

        // Cap on new icon extractions per world scan, so a busy match's first sight of many new unit
        // types doesn't hitch. The reader budgets its per-unit icon sweep against this.
        internal const int IconsPerScan = 16;

        // Reserved /icon key for the game's missile-warning sprite (GameAssets.missileWarningSprite).
        // The MAP page draws incoming missiles with this real in-game shape (tinted + flashed
        // client-side) instead of a hand-drawn triangle. Captured once, then reused.
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

        // One-shot per aircraft type: walk the cockpit's StatusDisplay, capture the
        // aircraft-background silhouette + every per-part Image sprite to PNG, and emit a JSON
        // layout descriptor so the AVN page can re-compose the same picture on the web.
        //
        // The StatusDisplay's `statusDisplays` and `aircraftBackground` are private serialized
        // fields, so we reflect into them once and cache the FieldInfos. Part layouts are
        // normalized 0..1 in the background's local UI rect, so the web side just multiplies
        // by its rendered silhouette size.
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

            // Wait for the silhouette to have its sprite AND be laid out before we commit the capture.
            // Right after a respawn / plane change the StatusDisplay can exist for a beat with a zero-size
            // rect; capturing then makes GetPartPlacement reject every part (its zero-rect guard) and we'd
            // cache an EMPTY layout for this type forever (key added to _capturedAirframes, never retried,
            // so the AVN page shows a bare or stale silhouette). Returning here re-tries on the next slow
            // scan until the cockpit panel is measured.
            if (bgImage.sprite == null || bgRT.rect.width <= 0.0001f || bgRT.rect.height <= 0.0001f)
                return;

            _capturedAirframes.Add(key);

            // Diagnostic: dump the bg's full orientation in world space so we can see
            // exactly what flip / rotation the cockpit canvas applies. The .right/.up
            // axes give the world direction of the bg's local +X / +Y; if either points
            // the "wrong" way, GetPartPlacement below mirrors cx/cy to match the visible
            // orientation. Scale alone misses 180° rotation flips (rotation negates an
            // axis without changing lossyScale), which is why we check directions too.
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
            sb.Append("{\"type\":\"").Append(EscapeJson(key)).Append("\",\"parts\":[");
            int partCount = 0;
            int flippedCount = 0;
            for (int i = 0; i < partsList.Count; i++)
            {
                PartStatusDisplay psd = partsList[i] as PartStatusDisplay;
                if (psd == null || psd.partImage == null) continue;

                Image img = psd.partImage;
                string name = img.gameObject != null ? img.gameObject.name : null;
                if (string.IsNullOrEmpty(name)) continue;

                if (img.sprite != null)
                {
                    string partKey = key, partName = name;
                    SpriteCapture.Request(img.sprite, SpriteCapture.Encoding.Png, synthAlpha: false, quality: 0, maxDim: 0,
                        png => { if (png != null) TelemetryServer.SetAirframeImage(partKey, partName, png); });
                }

                if (!GetPartPlacement(img.rectTransform, bgRT, out float cx, out float cy, out float w, out float h, out float rotZ, out int sx, out int sy))
                    continue;

                if (partCount > 0) sb.Append(',');
                partCount++;
                if (sx < 0 || sy < 0) flippedCount++;
                sb.Append('{')
                  .Append("\"n\":\"").Append(EscapeJson(name)).Append("\",")
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

            // Cache the cockpit's failure-indicator GameObjects (e.g. "LEFT ENGINE FIRE",
            // "FUEL LOW"). We don't capture their positions or rendered text — visual
            // styling is the AVN page's concern. The cached references just let the reader poll
            // activeSelf each tick to know which messages are currently firing.
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

        // Computes a part's placement relative to the background's local rect, in normalized
        // 0..1 coords (origin top-left to match web layout). All math is done in the bg's
        // LOCAL space (via InverseTransformPoint), so any parent transforms — including
        // mirroring flips applied by the cockpit canvas — are handled cleanly, and the cx/cy
        // values match what the player sees on the cockpit screen.
        // sx/sy report per-part flips relative to the bg's coordinate frame: ±1 each. The
        // cockpit prefab often re-uses a single sprite for symmetric parts and flips one
        // via the RectTransform's scale (e.g. wing1_R = wing1_L sprite with scale.x = -1).
        // Our /airframe endpoint returns the raw sprite so the renderer can apply the same
        // flip via CSS transform: scale(sx, sy) — otherwise the R parts would render mirror-
        // reversed and look out of place.
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

            // Part's centre in BG-local coords. World ↔ local round-trip absorbs any
            // intermediate transforms (offsets, rotations, scales), so what we read is
            // strictly "where the part sits inside the bg's own rect".
            Vector3 partWorldCenter = partRT.TransformPoint(partRT.rect.center);
            Vector3 partBgLocal     = bgRT.InverseTransformPoint(partWorldCenter);

            cx = (partBgLocal.x - bgRect.xMin) / bgRect.width;
            cy = (partBgLocal.y - bgRect.yMin) / bgRect.height;
            cy = 1f - cy;                                       // origin top-left for web

            // Mirror to match what the player actually sees. Two cases produce a mirror:
            //   (a) A negative lossyScale on the axis (the obvious flip), or
            //   (b) A 180° rotation around the orthogonal axis (rotation negates the axis
            //       direction without touching lossyScale).
            // We check the bg's world-space "right" / "up" axis directions instead of
            // scale because that covers both cases — if local +X ends up pointing in
            // world -X, the visual is mirrored regardless of *how* it got there.
            if (bgRT.right.x < 0f) cx = 1f - cx;
            if (bgRT.up.y    < 0f) cy = 1f - cy;

            // Size in fractions of bg width/height. The part's lossy-scale ratio against
            // the bg accounts for any chain of scales between them.
            float bgSx   = bgRT.lossyScale.x   == 0f ? 1f : Mathf.Abs(bgRT.lossyScale.x);
            float bgSy   = bgRT.lossyScale.y   == 0f ? 1f : Mathf.Abs(bgRT.lossyScale.y);
            float partSx = Mathf.Abs(partRT.lossyScale.x);
            float partSy = Mathf.Abs(partRT.lossyScale.y);
            w = partRT.rect.width  * (partSx / bgSx) / bgRect.width;
            h = partRT.rect.height * (partSy / bgSy) / bgRect.height;

            // Z rotation: report local so it survives the bg-local re-frame. CCW positive.
            rotZ = partRT.localEulerAngles.z;
            if (rotZ > 180f) rotZ -= 360f;

            // Per-part flip sign, expressed relative to the bg's frame. If the part and
            // bg have opposite-sign lossy-scale on an axis, the part is mirrored on that
            // axis. The renderer applies CSS transform: scale(sx, sy) to match.
            float pSx = partRT.lossyScale.x, pSy = partRT.lossyScale.y;
            float bSx = bgRT.lossyScale.x,   bSy = bgRT.lossyScale.y;
            sx = (pSx == 0f || bSx == 0f) ? 1 : ((pSx * bSx) < 0f ? -1 : 1);
            sy = (pSy == 0f || bSy == 0f) ? 1 : ((pSy * bSy) < 0f ? -1 : 1);
            return true;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if      (c == '"')  sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20)  sb.Append("\\u").Append(((int)c).ToString("x4"));
                else                sb.Append(c);
            }
            return sb.ToString();
        }

        // One-shot debug aid for the AVN-page silhouette design: walks Aircraft.partLookup and
        // logs every UnitPart's name + HP + critical flag + detached state. Fires once per
        // aircraft definition name per session. Mirrors the data the game's own StatusDisplay
        // uses to colour its silhouette segments (StatusDisplay matches Image.gameObject.name
        // against UnitPart.gameObject.name — see decompiled/StatusDisplay.decompiled.cs).
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
            if (enc == null || enc.vehicleTypes == null) return;   // not ready — retry next scan

            for (int i = 0; i < enc.vehicleTypes.Count; i++)
            {
                var vt = enc.vehicleTypes[i];
                if (vt == null || string.IsNullOrEmpty(vt.typeName) || _capturedTgtIcons.Contains(vt.typeName))
                    continue;
                _capturedTgtIcons.Add(vt.typeName);   // mark regardless so we never retry this type
                if (vt.typeSprite == null) continue;

                string name = vt.typeName;
                SpriteCapture.Request(vt.typeSprite, SpriteCapture.Encoding.Png, synthAlpha: true, quality: 0, maxDim: 0,
                    png => { if (png != null) TelemetryServer.SetTgtIcon(name, png); });
            }
        }

        // Extracts a weapon type's icon to PNG, once per name, and registers it. Called from the
        // reader's BuildLoadout as it iterates the live weapon stations.
        public void TryCaptureWeaponIcon(string name, Sprite icon)
        {
            if (string.IsNullOrEmpty(name) || _capturedWeaponIcons.Contains(name)) return;
            _capturedWeaponIcons.Add(name);
            if (icon == null) return;

            string weaponName = name;
            SpriteCapture.Request(icon, SpriteCapture.Encoding.Png, synthAlpha: true, quality: 0, maxDim: 0,
                png => { if (png != null) TelemetryServer.SetWeaponIcon(weaponName, png); });
        }

        // Pulls MapSettings.MapImage (the actual in-game map sprite) into JPEG bytes and hands
        // them to the server. GPU-downscaled to MapMaxDim and JPEG-encoded so this one-time
        // capture doesn't freeze the main thread (a full-res PNG capture costs ~670 ms) and
        // so a tablet isn't fetching a 16 MB map.
        public void TryCaptureMap(MapSettings ms)
        {
            if (_mapCaptured) return;
            Sprite mapSprite = ms.MapImage;
            if (mapSprite == null) return;   // not ready yet — retry next scan

            _mapCaptured = true;             // got a sprite; capture once (async), don't retry

            Texture src = mapSprite.texture;
            int sw = src != null ? src.width : 0;
            int sh = src != null ? src.height : 0;

            // Async + JPEG: removes the ~670 ms / 222 ms main-thread freeze the synchronous
            // full-res PNG path caused on mission load. Downscaled to MapMaxDim and JPEG-encoded
            // (maps are opaque) so the tablet also fetches a few hundred KB, not 16 MB.
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
