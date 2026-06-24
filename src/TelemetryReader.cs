using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace NORoksMFD
{
    internal class TelemetryReader : MonoBehaviour
    {
        private const float FastInterval = 0.1f; // 10 Hz — position / speed
        private const float SlowInterval = 1.0f; // 1 Hz  — world scan + map metadata (FindObjectsByType is expensive)

        private float _fastTimer;
        private float _slowTimer;
        private int   _totalUnits;
        private int   _totalAircraft;

        // Map metadata, resolved once LevelInfo is available.
        private LevelInfo? _level;
        private bool  _mapValid;
        private float _mapW, _mapH;
        private int   _gridOffsetX, _gridOffsetY;
        private bool  _mapCaptured;

        // Aircraft-type map icons we've already extracted (keyed by unitName).
        private readonly HashSet<string> _capturedIcons = new HashSet<string>();
        private const int IconsPerScan = 16;  // cap new icon extractions per scan to avoid a frame hitch

        // Aircraft definition names whose part layout has already been dumped to the log.
        // One-shot per session; used to inform the AVN page silhouette design.
        private readonly HashSet<string> _loggedPartLayouts = new HashSet<string>();

        // Aircraft definition names whose airframe silhouette assets have been captured
        // (background + per-part PNGs + JSON layout). One-shot per type per session.
        private readonly HashSet<string> _capturedAirframes = new HashSet<string>();

        // Cached reflection handles into StatusDisplay's private serialized fields.
        private static FieldInfo? _sdStatusDisplaysField;
        private static FieldInfo? _sdBackgroundField;
        private static FieldInfo? _sdFailureIndicatorsField;

        // Reusable buffer for per-part HP snapshots; resized only when the part count changes.
        private PartHp[] _partsBuf = Array.Empty<PartHp>();

        // Cached failure-indicator GameObjects from the cockpit StatusDisplay. Polled per
        // tick: any GO with activeSelf=true means the game has fired its OnReportDamage
        // event for the matching message (e.g. "L ENG FIRE" when the left Turbofan dies).
        // The GO name IS the failure message — that's how the prefab matches them up.
        private GameObject[] _failureGOs = Array.Empty<GameObject>();
        private readonly List<string> _failureScratch = new List<string>();

        // Cached unit list from the 1 Hz scan; positions are read from it at 10 Hz.
        private Unit[] _units = Array.Empty<Unit>();

        // Slowly-changing context, refreshed in the 1 Hz scan.
        private string         _missionName = string.Empty;
        private string         _mapName     = string.Empty;
        private LoadoutEntry[]  _loadout     = Array.Empty<LoadoutEntry>();

        // Weapon-type icons we've already extracted (keyed by weapon display name).
        private readonly HashSet<string> _capturedWeaponIcons = new HashSet<string>();
        private bool _capturedFlareIcon  = false;
        private bool _capturedJammerIcon = false;

        private int _flares    = -1;   // IR flares remaining (refreshed in the 1 Hz scan)
        private int _flaresMax = -1;   // IR flares capacity   (refreshed in the 1 Hz scan)

        // The game's HUD faction colors, read once from GameAssets.
        private string _colFriendly = "#39ff14";
        private string _colHostile  = "#ff4040";
        private string _colNeutral  = "#9aa0a6";
        private bool   _colorsRead;

        // TGP feed — captured from aircraft.targetCam at TgpInterval, encoded JPEG, pushed to
        // the server's MJPEG endpoint. Buffers are allocated lazily and freed on disengage,
        // so the cost is zero until the MFD's TGP page actually opens a subscriber.
        //
        // We let the game render the TargetCam at its prefab-native resolution (~360×240)
        // and just READ that RT. Earlier we tried swapping in a 720×480 RT to get a higher-
        // quality feed, but it (a) quadrupled per-frame render cost for cam + UICam, and
        // (b) repositioned UI canvas-anchored elements (the targeting box/crosshair) on the
        // in-cockpit screen because the canvas snapped to the new RT edges. Reading the
        // native RT side-steps both — the cockpit screen is undisturbed.
        private const float TgpInterval    = 1f / 15f;   // 15 Hz — halved from 30 Hz to cut readback+encode rate
        private const int   TgpMaxDim      = 720;        // cap for the encoded frame (native source is smaller, so this is a no-op today)
        private const int   TgpJpegQuality = 50;         // JPEG quality 0–100; 50 is visually fine for a small MFD pane
        private float        _tgpTimer;
        private RenderTexture? _tgpRT;                   // Blit destination, source for AsyncGPUReadback
        private Texture2D?     _tgpTex;                  // CPU-side buffer the readback writes into via LoadRawTextureData
        private FieldInfo?     _tcCamField;              // TargetCam.cam (Camera) — private, cached
        private FieldInfo?     _tcScreenRendererField;   // TargetCam.targetScreenRenderer — private, cached
        private bool           _tcReflectionTried;
        private bool           _tgpEngaged;              // true while we're actively capturing (for clean disengage logging)
        private bool           _tgpActive;               // last capture pushed a frame — mirrored into the snapshot
        private bool           _tgpSrcLogged;            // logged the source texture dimensions once
        private bool           _tgpReadbackInFlight;     // an AsyncGPUReadback is outstanding — skip new captures until it completes

        // ── RWR (radar warning) ───────────────────────────────────────────────────
        // The game raises Aircraft.onRadarWarning once per radar sweep that paints the player
        // (a Mirage ClientRpc, so on the main thread — same as our Update). It's a transient
        // ping, not a standing list, so we aggregate active emitters here with per-tier decay
        // (mirroring DynamicMap: search 1 s, track 2 s, lock 4 s) and snapshot the survivors.
        private sealed class RwrEmitter
        {
            public Unit  Unit;
            public byte  Tier;       // 0 search, 1 track (detected), 2 lock (we are its target)
            public float Range;      // emitting radar's max range, for closeness normalisation
            public float LastSeen;   // Time.time of the most recent ping
        }
        private readonly Dictionary<Unit, RwrEmitter> _rwrEmitters = new Dictionary<Unit, RwrEmitter>();
        private readonly List<Unit> _rwrExpireScratch = new List<Unit>();
        private readonly List<RwrContact> _rwrBuf = new List<RwrContact>(32);
        private Aircraft _rwrSubscribed;   // the aircraft whose onRadarWarning we're hooked to

        // Incoming missiles — polled straight from MissileWarning.knownMissiles (a public list),
        // so no event hook needed. Reused buffer to keep the 10 Hz push allocation-light.
        private readonly List<MwContact> _mwBuf = new List<MwContact>(8);

        private void Update()
        {
            _fastTimer += Time.deltaTime;
            _slowTimer += Time.deltaTime;
            _tgpTimer  += Time.deltaTime;

            if (_slowTimer >= SlowInterval)
            {
                _slowTimer = 0f;
                ScanWorld();
            }

            if (_fastTimer >= FastInterval)
            {
                _fastTimer = 0f;
                PushSnapshot();
            }

            if (_tgpTimer >= TgpInterval)
            {
                _tgpTimer = 0f;
                CaptureTgpFrame();
            }
        }

        private void ScanWorld()
        {
            Unit[] units = UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            _units = units;

            int aircraft = 0;
            int iconBudget = IconsPerScan;
            foreach (Unit u in units)
            {
                if (u == null) continue;
                if (u is Aircraft) aircraft++;
                // Pre-extract each unit type's map icon (a few per scan so it doesn't hitch).
                if (iconBudget > 0 && TryCaptureIcon(u.definition)) iconBudget--;
            }
            _totalUnits    = units.Length;
            _totalAircraft = aircraft;

            // Resolve the map bounds + grid offsets and capture the real in-game map image.
            if (_level == null)
                _level = UnityEngine.Object.FindObjectOfType<LevelInfo>();

            MapSettings? ms = _level != null ? _level.LoadedMapSettings : null;
            if (ms != null)
            {
                _mapW        = ms.MapSize.x;
                _mapH        = ms.MapSize.y;
                _gridOffsetX = ms.OffsetX;
                _gridOffsetY = ms.OffsetY;
                _mapValid    = _mapW > 0f && _mapH > 0f;
                _mapName     = CleanName(ms.name);
                TryCaptureMap(ms);
            }

            _missionName = MissionManager.CurrentMission?.Name ?? string.Empty;

            ReadFactionColors();

            // Loadout changes rarely (only on rearm) — building it here at 1 Hz keeps the
            // 10 Hz push allocation-free.
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac != null)
            {
                _loadout = BuildLoadout(ac);
                CountFlares(ac, out _flares, out _flaresMax);
                TryCaptureCmIcons(ac);
                TryLogPartLayout(ac);
                TryCaptureAirframe(ac);
            }
        }

        // One-shot per aircraft type: walk the cockpit's StatusDisplay, capture the
        // aircraft-background silhouette + every per-part Image sprite to PNG, and emit a JSON
        // layout descriptor so the AVN page can re-compose the same picture on the web.
        //
        // The StatusDisplay's `statusDisplays` and `aircraftBackground` are private serialized
        // fields, so we reflect into them once and cache the FieldInfos. Part layouts are
        // normalized 0..1 in the background's local UI rect, so the web side just multiplies
        // by its rendered silhouette size.
        private void TryCaptureAirframe(Aircraft ac)
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
                Plugin.Log?.LogWarning("[NORoksMFD] AVN: StatusDisplay reflection fields not found — airframe capture disabled.");
                _capturedAirframes.Add(key);
                return;
            }

            Image bgImage     = _sdBackgroundField.GetValue(sd)     as Image;
            System.Collections.IList partsList = _sdStatusDisplaysField.GetValue(sd) as System.Collections.IList;
            if (bgImage == null || partsList == null)
            {
                _capturedAirframes.Add(key);
                return;
            }

            _capturedAirframes.Add(key);

            RectTransform bgRT = bgImage.rectTransform;

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
                $"[NORoksMFD] AVN bg lossyScale=({bgLs.x:0.000},{bgLs.y:0.000},{bgLs.z:0.000})  " +
                $"rectSize=({bgRT.rect.width:0.0},{bgRT.rect.height:0.0})  " +
                $"right=({bgR.x:0.00},{bgR.y:0.00},{bgR.z:0.00})  up=({bgU.x:0.00},{bgU.y:0.00},{bgU.z:0.00})  " +
                $"euler=({bgEu.x:0.0},{bgEu.y:0.0},{bgEu.z:0.0})");

            // Background silhouette — one PNG, served at /airframe?type=<key>&part=__bg
            if (bgImage.sprite != null)
            {
                byte[]? bgPng = SpriteToPng(bgImage.sprite, isIcon: false);
                if (bgPng != null) TelemetryServer.SetAirframeImage(key, "__bg", bgPng);
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
                    byte[]? png = SpriteToPng(img.sprite, isIcon: false);
                    if (png != null) TelemetryServer.SetAirframeImage(key, name, png);
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
            // styling is the AVN page's concern. The cached references just let us poll
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
            _failureGOs = failureGOs.ToArray();

            Plugin.Log?.LogInfo($"[NORoksMFD] Captured airframe silhouette '{key}' (bg + {partCount} parts, {flippedCount} flipped, {_failureGOs.Length} failure messages: {string.Join(", ", System.Linq.Enumerable.Select(_failureGOs, g => g.name))}).");
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
        private void TryLogPartLayout(Aircraft ac)
        {
            string key = ac.definition != null ? ac.definition.unitName : null;
            if (string.IsNullOrEmpty(key) || _loggedPartLayouts.Contains(key)) return;

            var parts = ac.partLookup;
            if (parts == null) return;

            _loggedPartLayouts.Add(key);
            Plugin.Log?.LogInfo($"[NORoksMFD] AVN parts for '{key}' (count={parts.Count}):");
            for (int i = 0; i < parts.Count; i++)
            {
                UnitPart p = parts[i];
                if (p == null) { Plugin.Log?.LogInfo($"  [{i}] <null>"); continue; }
                string n = p.gameObject != null ? p.gameObject.name : "<no-go>";
                Plugin.Log?.LogInfo($"  [{i}] {n}  hp={p.hitPoints:0.#}  detached={p.IsDetached()}");
            }
        }

        // Sums IR flares (remaining + capacity) across all flare ejectors. Returns (-1, -1)
        // if the aircraft has no flare system.
        private static void CountFlares(Aircraft ac, out int ammo, out int max)
        {
            ammo = -1; max = -1;
            FlareEjector[] ejectors = ac.GetComponentsInChildren<FlareEjector>();
            if (ejectors == null || ejectors.Length == 0) return;
            int total = 0, totalMax = 0;
            foreach (FlareEjector fe in ejectors)
                if (fe != null) { total += fe.GetAmmo(); totalMax += fe.GetMaxAmmo(); }
            ammo = total; max = totalMax;
        }

        // Extracts the flares + radar jammer Sprites from any matching component on the
        // aircraft, once each. Saves them as PNGs so /cm?type=flares|jammer can serve them.
        private void TryCaptureCmIcons(Aircraft ac)
        {
            if (!_capturedFlareIcon)
            {
                FlareEjector? fe = ac.GetComponentInChildren<FlareEjector>();
                if (fe != null && fe.displayImage != null)
                {
                    byte[]? png = SpriteToPng(fe.displayImage, isIcon: true);
                    if (png != null) { TelemetryServer.SetCmIcon("flares", png); _capturedFlareIcon = true; }
                }
            }
            if (!_capturedJammerIcon)
            {
                RadarJammer? rj = ac.GetComponentInChildren<RadarJammer>();
                if (rj != null && rj.displayImage != null)
                {
                    byte[]? png = SpriteToPng(rj.displayImage, isIcon: true);
                    if (png != null) { TelemetryServer.SetCmIcon("jammer", png); _capturedJammerIcon = true; }
                }
            }
        }

        // PowerSupply.maxCharge is private; cache the FieldInfo and read it via reflection.
        private static FieldInfo? _powerMaxField;
        private static float GetEwMaxKJ(PowerSupply ps)
        {
            if (ps == null) return -1f;
            if (_powerMaxField == null)
                _powerMaxField = typeof(PowerSupply).GetField("maxCharge", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_powerMaxField == null) return -1f;
            try { return _powerMaxField.GetValue(ps) is float f ? f : -1f; }
            catch { return -1f; }
        }

        // The active countermeasure index points into CountermeasureManager's private station
        // list, so we reflect into it once (cached) and check the active station's type.
        private static FieldInfo?  _cmStationsField;
        private static MethodInfo? _cmGetFirstMethod;

        private static byte GetSelectedCmCategory(Aircraft ac)
        {
            CountermeasureManager mgr = ac.countermeasureManager;
            if (mgr == null) return 0;

            try
            {
                if (_cmStationsField == null)
                    _cmStationsField = typeof(CountermeasureManager)
                        .GetField("countermeasureStations", BindingFlags.NonPublic | BindingFlags.Instance);

                if (_cmStationsField?.GetValue(mgr) is not System.Collections.IList list || list.Count == 0)
                    return 0;

                int idx = mgr.activeIndex;
                if (idx < 0 || idx >= list.Count) return 0;

                object station = list[idx];
                if (station == null) return 0;

                if (_cmGetFirstMethod == null)
                    _cmGetFirstMethod = station.GetType()
                        .GetMethod("GetFirstCountermeasure", BindingFlags.Public | BindingFlags.Instance);

                if (_cmGetFirstMethod?.Invoke(station, null) is not Countermeasure cm) return 0;
                if (cm is FlareEjector) return 1;
                if (cm is RadarJammer)  return 2;
                if (cm is ChaffEjector) return 3;
                return 0;
            }
            catch { return 0; }
        }

        // Reads the game's HUD faction colors once (constant for the session).
        private void ReadFactionColors()
        {
            if (_colorsRead) return;
            try
            {
                GameAssets ga = GameAssets.i;
                if (ga == null) return;
                _colFriendly = ColorHex(ga.HUDFriendly);
                _colHostile  = ColorHex(ga.HUDHostile);
                _colNeutral  = ColorHex(ga.HUDNeutral);
                _colorsRead  = true;
            }
            catch { /* fall back to defaults */ }
        }

        private static string ColorHex(Color c)
        {
            int r = Mathf.Clamp((int)(c.r * 255f + 0.5f), 0, 255);
            int g = Mathf.Clamp((int)(c.g * 255f + 0.5f), 0, 255);
            int b = Mathf.Clamp((int)(c.b * 255f + 0.5f), 0, 255);
            return $"#{r:x2}{g:x2}{b:x2}";
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0) name = name.Substring(0, clone);
            return name.Trim();
        }

        // Aggregates the aircraft's live weapon stations by type (summing remaining/total ammo),
        // and extracts each weapon's icon. Uses weaponStations rather than the static loadout so
        // ammo counts reflect what's actually left.
        private readonly Dictionary<string, int> _loIndex = new Dictionary<string, int>();
        private readonly List<string> _loNames = new List<string>();
        private readonly List<int>    _loCur   = new List<int>();
        private readonly List<int>    _loMax   = new List<int>();

        private LoadoutEntry[] BuildLoadout(Aircraft aircraft)
        {
            var stations = aircraft.weaponStations;
            if (stations == null) return Array.Empty<LoadoutEntry>();

            _loIndex.Clear(); _loNames.Clear(); _loCur.Clear(); _loMax.Clear();

            foreach (WeaponStation st in stations)
            {
                if (st == null) continue;
                WeaponInfo info = st.WeaponInfo;
                if (info == null || info.hideInDisplay) continue;

                string name = !string.IsNullOrEmpty(info.weaponName) ? info.weaponName : info.shortName;
                if (string.IsNullOrEmpty(name)) continue;

                if (!_loIndex.TryGetValue(name, out int i))
                {
                    i = _loNames.Count;
                    _loIndex[name] = i;
                    _loNames.Add(name); _loCur.Add(0); _loMax.Add(0);
                }
                _loCur[i] += st.Ammo;
                _loMax[i] += st.FullAmmo;
                TryCaptureWeaponIcon(name, info.weaponIcon);
            }

            var result = new LoadoutEntry[_loNames.Count];
            for (int i = 0; i < _loNames.Count; i++)
                result[i] = new LoadoutEntry { Name = _loNames[i], Ammo = _loCur[i], FullAmmo = _loMax[i] };
            return result;
        }

        // Extracts a weapon type's icon to PNG, once per name, and registers it.
        private void TryCaptureWeaponIcon(string name, Sprite icon)
        {
            if (string.IsNullOrEmpty(name) || _capturedWeaponIcons.Contains(name)) return;
            _capturedWeaponIcons.Add(name);
            if (icon == null) return;

            byte[]? png = SpriteToPng(icon, isIcon: true);
            if (png != null) TelemetryServer.SetWeaponIcon(name, png);
        }

        private void PushSnapshot()
        {
            GameManager.GetLocalAircraft(out Aircraft aircraft);
            if (aircraft == null)
                return;

            // Keep the radar-warning hook attached to the current local aircraft (re-attaches on
            // aircraft change; clears the emitter table so a fresh airframe starts clean).
            EnsureRwrSubscription(aircraft);

            // The game uses a floating-origin system: transform.position drifts back toward
            // zero as the world re-centers. The true world coordinate is pos - Datum.originPosition.
            Vector3 world   = aircraft.transform.position - Datum.originPosition;
            float   heading = aircraft.transform.eulerAngles.y;

            PowerSupply ps     = aircraft.GetPowerSupply();
            float       ewKJ   = ps != null ? ps.GetChargeKJ() : -1f;
            float       ewKJMax = ps != null ? GetEwMaxKJ(ps)  : -1f;

            string selWeapon = string.Empty;
            WeaponManager wm = aircraft.weaponManager;
            WeaponInfo selInfo = wm != null && wm.currentWeaponStation != null ? wm.currentWeaponStation.WeaponInfo : null;
            if (selInfo != null)
                selWeapon = !string.IsNullOrEmpty(selInfo.weaponName) ? selInfo.weaponName : selInfo.shortName;

            byte cmCategory = GetSelectedCmCategory(aircraft);

            TryCaptureIcon(aircraft.definition);

            TelemetryServer.Push(new TelemetrySnapshot
            {
                Valid          = true,
                Time           = Time.time,
                PlaneName      = aircraft.definition.unitName,
                IconOrient     = aircraft.definition.mapOrient,
                IconScale      = aircraft.definition.mapIconSize,
                MissionName    = _missionName,
                MapName        = _mapName,
                Loadout        = _loadout,
                WorldX         = world.x,
                WorldY         = world.y,
                WorldZ         = world.z,
                Heading        = heading,
                TAS            = aircraft.speed,
                AGL            = Mathf.Max(0f, aircraft.radarAlt),
                GearDown       = aircraft.gearDeployed,
                Flares         = _flares,
                FlaresMax      = _flaresMax,
                EwKJ           = ewKJ,
                EwKJMax        = ewKJMax,
                Fuel           = aircraft.GetFuelLevel(),
                Throttle       = aircraft.GetInputs() != null ? aircraft.GetInputs().throttle : -1f,
                SelWeapon      = selWeapon,
                CmCategory     = cmCategory,
                TotalUnits     = _totalUnits,
                TotalAircraft  = _totalAircraft,
                MapValid       = _mapValid,
                MapW           = _mapW,
                MapH           = _mapH,
                GridOffsetX    = _gridOffsetX,
                GridOffsetY    = _gridOffsetY,
                Units          = BuildUnits(aircraft),
                ColFriendly    = _colFriendly,
                ColHostile     = _colHostile,
                ColNeutral     = _colNeutral,
                TgpActive      = _tgpActive,
                Parts          = BuildParts(aircraft),
                Failures       = BuildFailures(),
                Rwr            = BuildRwr(aircraft),
                Mw             = BuildMw(aircraft)
            });
        }

        // Snapshots the missiles currently warning the player. MissileWarning.knownMissiles is a
        // public list the game maintains (a missile lands here once it's inbound and tracking us),
        // so we just poll it — no event hook. Position is the missile's GlobalPosition (same world
        // space as Units); the seeker type is the label.
        private MwContact[] BuildMw(Aircraft player)
        {
            MissileWarning mw = player.GetMissileWarningSystem();
            List<Missile> known = mw != null ? mw.knownMissiles : null;
            if (known == null || known.Count == 0) return Array.Empty<MwContact>();

            _mwBuf.Clear();
            for (int i = 0; i < known.Count; i++)
            {
                Missile m = known[i];
                if (m == null || m.disabled) continue;
                GlobalPosition gp = m.GlobalPosition();
                string seeker = m.GetSeekerType() ?? string.Empty;
                _mwBuf.Add(new MwContact
                {
                    X = gp.x,
                    Z = gp.z,
                    Seeker = seeker,
                    Notch = NotchHeading(player, m, seeker)
                });
            }
            return _mwBuf.Count == 0 ? Array.Empty<MwContact>() : _mwBuf.ToArray();
        }

        // Beam-notch heading for a radar-guided seeker (ARH/SARH), replicating the game's map
        // notch line (ThreatItem.AlignNotchLine): the horizontal direction to fly to put the
        // missile on the beam (Doppler-notch it). Returns a world compass heading in degrees, or
        // -1 when the missile isn't radar-guided or the geometry is degenerate.
        private static float NotchHeading(Aircraft player, Missile missile, string seeker)
        {
            if (seeker != "ARH" && seeker != "SARH") return -1f;
            if (player.rb == null) return -1f;
            Vector3 evasionVector = missile.GetEvasionPoint() - player.GlobalPosition();
            Vector3 rhs = Vector3.Cross(evasionVector, player.rb.velocity);
            Vector3 v   = Vector3.Cross(evasionVector, rhs);
            if (Vector3.Dot(player.transform.forward, v) < 0f) v *= -1f;
            v.y = 0f;
            if (v.sqrMagnitude < 1e-4f) return -1f;
            return Quaternion.LookRotation(v, Vector3.up).eulerAngles.y;
        }

        // Attaches OnRadarWarning to the current local aircraft, detaching from the previous one
        // on a swap (eject/respawn) and clearing the emitter table so stale threats don't carry
        // over to a new airframe.
        private void EnsureRwrSubscription(Aircraft ac)
        {
            if (ReferenceEquals(ac, _rwrSubscribed)) return;
            if (_rwrSubscribed != null) _rwrSubscribed.onRadarWarning -= OnRadarWarning;
            _rwrEmitters.Clear();
            _rwrSubscribed = ac;
            if (ac != null) ac.onRadarWarning += OnRadarWarning;
        }

        // One radar sweep painted us: record/refresh the emitter with its current threat tier.
        // A later ping can raise or lower the tier (search → track → lock and back).
        private void OnRadarWarning(Aircraft.OnRadarWarning e)
        {
            Unit emitter = e.emitter;
            if (emitter == null) return;
            byte tier = e.isTarget ? (byte)2 : (e.detected ? (byte)1 : (byte)0);
            float range = e.radar != null ? e.radar.RadarParameters.maxRange : 0f;
            if (_rwrEmitters.TryGetValue(emitter, out RwrEmitter em))
            {
                em.Tier = tier;
                em.Range = range;
                em.LastSeen = Time.time;
            }
            else
            {
                _rwrEmitters[emitter] = new RwrEmitter { Unit = emitter, Tier = tier, Range = range, LastSeen = Time.time };
            }
        }

        // Expires stale emitters (per-tier lifetime, matching the game's own map pings) and
        // snapshots the survivors. Position comes from the emitter's GlobalPosition (same world
        // space as Units); pw is closeness 0..1, normalised against the radar's range so a
        // close lock sits near the scope centre.
        private RwrContact[] BuildRwr(Aircraft player)
        {
            if (_rwrEmitters.Count == 0) return Array.Empty<RwrContact>();
            float now = Time.time;
            _rwrExpireScratch.Clear();
            _rwrBuf.Clear();
            foreach (var kv in _rwrEmitters)
            {
                RwrEmitter em = kv.Value;
                Unit u = em.Unit;
                float ttl = em.Tier == 2 ? 4f : (em.Tier == 1 ? 2f : 1f);
                float age = now - em.LastSeen;
                if (u == null || u.disabled || age > ttl) { _rwrExpireScratch.Add(kv.Key); continue; }

                // Freshness: 1 right after a ping, fading to 0 over the tier lifetime — drives
                // the diamond's "ping" pulse on the scope. A new sweep refreshes LastSeen, so a
                // continuously-painting radar stays bright; a single sweep fades out and expires.
                float fr = Mathf.Clamp01(1f - age / ttl);

                float pw;
                if (em.Range > 0f)
                {
                    float dist = Vector3.Distance(player.transform.position, u.transform.position);
                    pw = Mathf.Clamp01(1f - dist / em.Range);
                }
                else
                {
                    pw = em.Tier == 2 ? 0.7f : (em.Tier == 1 ? 0.45f : 0.2f);
                }

                GlobalPosition gp = u.GlobalPosition();
                _rwrBuf.Add(new RwrContact
                {
                    X     = gp.x,
                    Z     = gp.z,
                    Tier  = em.Tier,
                    Power = pw,
                    Fresh = fr,
                    Name  = RwrLabel(u),
                    Kind  = ClassifyEmitter(u)
                });
            }
            for (int i = 0; i < _rwrExpireScratch.Count; i++) _rwrEmitters.Remove(_rwrExpireScratch[i]);
            return _rwrBuf.Count == 0 ? Array.Empty<RwrContact>() : _rwrBuf.ToArray();
        }

        // RWR label: the unit's display name (bogeyName is the generic fallback).
        private static string RwrLabel(Unit u)
        {
            UnitDefinition def = u.definition;
            if (def == null) return "?";
            if (!string.IsNullOrEmpty(def.unitName))  return def.unitName;
            if (!string.IsNullOrEmpty(def.bogeyName)) return def.bogeyName;
            return "?";
        }

        // Emitter kind from the unit's typeIdentity: 2 = air, 1 = ground/SAM, 0 = unknown.
        private static byte ClassifyEmitter(Unit u)
        {
            UnitDefinition def = u.definition;
            if (def == null) return 0;
            TypeIdentity ti = def.typeIdentity;
            if (ti.air > 0.5f) return 2;
            if (ti.surface > 0.5f || ti.radar > 0.5f) return 1;
            return 0;
        }

        // Returns the names of all currently-active failure indicators (e.g. "L ENG FIRE",
        // "FUEL LOW"). The cached list of GameObjects comes from StatusDisplay's
        // failureIndicators field captured at airframe-capture time; the game flips activeSelf
        // on the matching GO when an IReportDamage event fires.
        private string[] BuildFailures()
        {
            if (_failureGOs == null || _failureGOs.Length == 0) return Array.Empty<string>();
            _failureScratch.Clear();
            for (int i = 0; i < _failureGOs.Length; i++)
            {
                GameObject go = _failureGOs[i];
                if (go != null && go.activeSelf) _failureScratch.Add(go.name);
            }
            return _failureScratch.Count == 0 ? Array.Empty<string>() : _failureScratch.ToArray();
        }

        // Snapshots every UnitPart in the player aircraft's partLookup. Allocates only when
        // the part count changes (so steady-state cost is N copies into a cached array).
        private PartHp[] BuildParts(Aircraft ac)
        {
            var parts = ac.partLookup;
            if (parts == null || parts.Count == 0) return Array.Empty<PartHp>();

            if (_partsBuf.Length != parts.Count) _partsBuf = new PartHp[parts.Count];

            for (int i = 0; i < parts.Count; i++)
            {
                UnitPart p = parts[i];
                if (p == null) { _partsBuf[i] = default; continue; }
                _partsBuf[i].Name     = p.gameObject != null ? p.gameObject.name : string.Empty;
                _partsBuf[i].Hp       = p.hitPoints;
                _partsBuf[i].Detached = p.IsDetached();
            }
            return _partsBuf;
        }

        // Pulls MapSettings.MapImage (the actual in-game map sprite) into PNG bytes and hands
        // them to the server.
        private void TryCaptureMap(MapSettings ms)
        {
            if (_mapCaptured) return;

            byte[]? png = SpriteToPng(ms.MapImage);
            _mapCaptured = true; // whether it worked or not, don't retry every second

            if (png != null)
            {
                TelemetryServer.SetMapImage(png);
                Plugin.Log?.LogInfo($"[NORoksMFD] Captured in-game map ({png.Length} bytes).");
            }
            else
            {
                Plugin.Log?.LogWarning("[NORoksMFD] Map capture unavailable; falling back to map file.");
            }
        }

        // Extracts a unit type's top-down map icon to PNG, once per type, and registers it.
        // Returns true if it attempted a (costly) extraction this call, so callers can budget.
        private bool TryCaptureIcon(UnitDefinition def)
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

            byte[]? png = SpriteToPng(def.mapIcon, isIcon: true);
            // Fall back to the sentinel if extraction failed, so this type never 404s either.
            TelemetryServer.SetIcon(def.unitName, png ?? TelemetryServer.NoIconPng);
            return true;
        }

        // Builds the list of units the player's faction can see. Friendlies appear at their
        // true position; enemies only when tracked, at their last-known position (fog of war).
        private readonly List<UnitInfo> _unitBuf = new List<UnitInfo>(256);

        private UnitInfo[] BuildUnits(Aircraft player)
        {
            var playerHQ = player.NetworkHQ;
            if (playerHQ == null) return Array.Empty<UnitInfo>();

            // The player's current target(s): the live weapon target list (public API, no
            // reflection). Reference-matched against each scanned unit below.
            List<Unit> targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;
            bool hasTargets = targets != null && targets.Count > 0;

            _unitBuf.Clear();
            foreach (Unit u in _units)
            {
                if (u == null || u.disabled || ReferenceEquals(u, player)) continue;

                UnitDefinition def = u.definition;
                if (def == null) continue;

                // One call resolves both visibility and position under fog of war.
                if (!playerHQ.TryGetKnownPosition(u, out GlobalPosition gp)) continue;

                var hq = u.NetworkHQ;
                byte faction = hq == null ? (byte)0 : (hq == playerHQ ? (byte)1 : (byte)2);

                _unitBuf.Add(new UnitInfo
                {
                    Type     = def.unitName,
                    X        = gp.x,
                    Z        = gp.z,
                    Heading  = u.transform.eulerAngles.y,
                    Faction  = faction,
                    Orient   = def.mapOrient,
                    Scale    = def.mapIconSize,
                    Targeted = hasTargets && targets.Contains(u)
                });
            }
            return _unitBuf.ToArray();
        }

        // Renders a sprite (atlas-safe via textureRect) to PNG bytes. Uses a Blit→RenderTexture
        // round-trip so it works even when the source texture isn't CPU-readable. Must run on the
        // Unity main thread (Graphics/ReadPixels/EncodeToPNG).
        // When 'isIcon' is true and the result is fully opaque (e.g. alpha-less DXT1 ship icons),
        // the alpha channel is rebuilt from luminance so the silhouette isn't a solid square.
        private static byte[]? SpriteToPng(Sprite sprite, bool isIcon = false)
        {
            if (sprite == null || sprite.texture == null) return null;

            try
            {
                Texture2D tex = sprite.texture;
                Rect r = sprite.textureRect;
                int w = Mathf.RoundToInt(r.width);
                int h = Mathf.RoundToInt(r.height);
                if (w <= 0 || h <= 0)
                {
                    w = tex.width;
                    h = tex.height;
                    r = new Rect(0f, 0f, w, h);
                }

                RenderTexture rt = RenderTexture.GetTemporary(
                    tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);

                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                Texture2D readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
                readable.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                if (isIcon) SynthesizeAlphaIfOpaque(readable);

                byte[] png = ImageConversion.EncodeToPNG(readable);
                Destroy(readable);
                return png;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NORoksMFD] Sprite capture failed: {ex.Message}");
                return null;
            }
        }

        // Icons stored without an alpha channel (DXT1, RGB24, …) read as fully opaque, so a
        // straight tint produces a solid square. The game draws them as light-on-dark
        // silhouettes, so when an icon comes back with no transparency we derive alpha from
        // luminance — dark background → transparent, bright shape → opaque.
        private static void SynthesizeAlphaIfOpaque(Texture2D readable)
        {
            Color32[] px = readable.GetPixels32();
            byte minA = 255;
            for (int i = 0; i < px.Length; i++)
                if (px[i].a < minA) { minA = px[i].a; if (minA == 0) break; }
            if (minA < 250) return; // already has real transparency — leave it alone

            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                c.a = (byte)((c.r * 77 + c.g * 150 + c.b * 29) >> 8); // ~Rec.601 luminance
                px[i] = c;
            }
            readable.SetPixels32(px);
            readable.Apply();
        }

        // ── TGP camera feed ────────────────────────────────────────────────────
        // The game's own TargetCam tracks the player's current target, including IR mode and
        // zoom-on-target FOV. While a target is locked we nudge it active each tick; when the
        // last target disappears we STOP calling SetTargetCam — the TargetCam's own Update()
        // keeps the cam aimed at the final target position for ~3 s via its camTimeout, then
        // disables itself. We mirror that lifetime by reading frames while cam.enabled is true
        // and clearing as soon as it goes false. Net effect: the feed lingers exactly as long
        // as the in-cockpit screen does.
        private void CaptureTgpFrame()
        {
            // Gate on /tgp.mjpg subscribers. When the MFD's TGP page is not open, no client
            // is subscribed, and there's no point running the capture pipeline (or calling
            // SetTargetCam, which keeps the in-cockpit TargetCam alive). Free our buffers
            // on the transition out so we leave nothing allocated while idle.
            if (!TelemetryServer.WantsTgpFrames)
            {
                if (_tgpEngaged) DisengageTgp();
                return;
            }

            // No mission / no aircraft / no TGP component → drop any cached frame and bail.
            GameManager.GetLocalAircraft(out Aircraft ac);
            TargetCam? tc = ac != null ? ac.targetCam : null;
            if (tc == null) { TelemetryServer.ClearTgpFrame(); _tgpActive = false; return; }

            // Cache the three private fields once. cam = scene camera, UICam = overlay canvas
            // camera, targetScreenRenderer = the in-cockpit display whose material is bound
            // to the camera's render texture.
            if (!_tcReflectionTried)
            {
                _tcReflectionTried = true;
                var t = typeof(TargetCam);
                _tcCamField            = t.GetField("cam",                  BindingFlags.NonPublic | BindingFlags.Instance);
                _tcScreenRendererField = t.GetField("targetScreenRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_tcCamField == null || _tcScreenRendererField == null)
                    Plugin.Log?.LogWarning("[NORoksMFD] TGP: could not locate TargetCam private fields — feed disabled.");
            }
            if (_tcCamField == null || _tcScreenRendererField == null) { TelemetryServer.ClearTgpFrame(); _tgpActive = false; return; }

            // Only refresh the camTimeout while a target is actually locked — SetTargetCam
            // would crash on an empty list, and not calling it is what gives us the 3-second
            // post-loss hold (game's Update keeps aiming at the last targetPosition until
            // camTimeout expires).
            List<Unit> targets = ac!.weaponManager != null ? ac.weaponManager.GetTargetList() : null;
            if (targets != null && targets.Count > 0)
            {
                try { tc.SetTargetCam(); }
                catch (Exception ex)
                {
                    // SetTargetCam touches a lot of game state. If anything throws (e.g. the player
                    // just disabled / detached), skip this tick rather than killing Update.
                    Plugin.Log?.LogDebug($"[NORoksMFD] TGP SetTargetCam threw: {ex.Message}");
                    return;
                }
            }

            // After the game's 3-second timeout expires, cam.enabled flips to false. Stop
            // pushing then so MJPEG clients see "no feed" and fall back to NO TARGET.
            Camera cam = _tcCamField.GetValue(tc) as Camera;
            if (cam == null || !cam.enabled) { TelemetryServer.ClearTgpFrame(); _tgpActive = false; return; }

            // Prefer the camera's own targetTexture; fall back to the cockpit renderer's
            // material (which the game points at the same RT) if the prefab puts the
            // assignment there instead of on the Camera.
            Texture src = cam.targetTexture;
            if (src == null)
            {
                if (_tcScreenRendererField.GetValue(tc) is Renderer rend && rend.material != null)
                    src = rend.material.mainTexture;
            }
            if (src == null) { TelemetryServer.ClearTgpFrame(); _tgpActive = false; return; }

            // Match the captured frame to the source's aspect ratio. Forcing a square output
            // squashed the in-game (wider-than-tall) feed; capturing at the native aspect lets
            // the MFD's object-fit:contain letterbox naturally, so the visible cam rectangle
            // shrinks and pixelation drops without distorting the picture. Cap at source size
            // — upsampling here adds no detail, just bytes.
            int sw = Mathf.Max(1, src.width);
            int sh = Mathf.Max(1, src.height);
            int targetW, targetH;
            int maxSide = Mathf.Max(sw, sh);
            if (maxSide <= TgpMaxDim)
            {
                targetW = sw; targetH = sh;
            }
            else if (sw >= sh)
            {
                targetW = TgpMaxDim;
                targetH = Mathf.Max(1, Mathf.RoundToInt(TgpMaxDim * (float)sh / sw));
            }
            else
            {
                targetH = TgpMaxDim;
                targetW = Mathf.Max(1, Mathf.RoundToInt(TgpMaxDim * (float)sw / sh));
            }

            if (!_tgpSrcLogged)
            {
                _tgpSrcLogged = true;
                Plugin.Log?.LogInfo($"[NORoksMFD] TGP source texture {sw}x{sh} (aspect {(float)sw/sh:0.000}); capturing at {targetW}x{targetH}.");
            }

            // Don't stack readbacks if the GPU is still working on the previous one — drop
            // this tick instead. With AsyncGPUReadback completing in 1–3 frames we'll only
            // skip one or two ticks per second under load, no visible stutter on the feed.
            if (_tgpReadbackInFlight) return;

            // (Re)allocate the downscale RT + readback texture when the source dimensions change.
            // RGBA32 on both sides so the bytes from AsyncGPUReadback can be fed straight into
            // LoadRawTextureData without a format conversion.
            if (_tgpRT == null || _tgpRT.width != targetW || _tgpRT.height != targetH)
            {
                if (_tgpRT != null) { _tgpRT.Release(); UnityEngine.Object.Destroy(_tgpRT); }
                _tgpRT = new RenderTexture(targetW, targetH, 0, RenderTextureFormat.ARGB32);
                _tgpRT.Create();
            }
            if (_tgpTex == null || _tgpTex.width != targetW || _tgpTex.height != targetH)
            {
                if (_tgpTex != null) UnityEngine.Object.Destroy(_tgpTex);
                _tgpTex = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
            }

            // GPU downscale, then ASYNC readback. AsyncGPUReadback dispatches the readback to
            // the GPU and returns immediately — no main-thread stall waiting on a pipeline
            // flush, which was the dominant per-frame cost of the synchronous ReadPixels path.
            // The callback fires on the main thread once the GPU has the bytes ready (typically
            // 1–3 frames later); we then copy into _tgpTex, encode, and push.
            Graphics.Blit(src, _tgpRT);
            _tgpReadbackInFlight = true;
            int captureW = targetW;
            int captureH = targetH;
            AsyncGPUReadback.Request(_tgpRT, 0, request => OnTgpReadbackComplete(request, captureW, captureH));
        }

        // Async readback callback — runs on the Unity main thread. Bail cleanly if the worker
        // was destroyed, the GPU errored, or the user disengaged the TGP page mid-flight.
        private void OnTgpReadbackComplete(AsyncGPUReadbackRequest request, int w, int h)
        {
            _tgpReadbackInFlight = false;
            if (this == null) return;                                // MonoBehaviour destroyed
            if (request.hasError) return;
            if (!TelemetryServer.WantsTgpFrames) return;              // disengaged while in flight
            if (_tgpTex == null || _tgpTex.width != w || _tgpTex.height != h) return;

            var data = request.GetData<byte>();
            _tgpTex.LoadRawTextureData(data);
            _tgpTex.Apply(false, false);

            byte[] jpg = _tgpTex.EncodeToJPG(TgpJpegQuality);
            TelemetryServer.PushTgpFrame(jpg);
            _tgpActive  = true;
            _tgpEngaged = true;
        }

        // Release the buffers we lazily allocate during capture and clear the published
        // frame. Safe to call from the gating fast-path or from OnDestroy. We never swap
        // any game-side RTs, so there's nothing to restore.
        private void DisengageTgp()
        {
            if (_tgpRT  != null) { _tgpRT.Release();  UnityEngine.Object.Destroy(_tgpRT);  _tgpRT  = null; }
            if (_tgpTex != null) {                    UnityEngine.Object.Destroy(_tgpTex); _tgpTex = null; }

            bool wasEngaged       = _tgpEngaged;
            _tgpEngaged           = false;
            _tgpActive            = false;
            _tgpSrcLogged         = false;
            _tgpReadbackInFlight  = false;   // any in-flight callback will see !WantsTgpFrames and bail
            TelemetryServer.ClearTgpFrame();
            if (wasEngaged) Plugin.Log?.LogInfo("[NORoksMFD] TGP: disengaged (no subscribers).");
        }

        private void OnDestroy()
        {
            DisengageTgp();
            if (_rwrSubscribed != null) { _rwrSubscribed.onRadarWarning -= OnRadarWarning; _rwrSubscribed = null; }
        }
    }
}
