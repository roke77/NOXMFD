using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace NOXMFD
{
    internal class TelemetryReader : MonoBehaviour
    {
        private const float FastInterval = 0.1f; // 10 Hz — position / speed
        private const float SlowInterval = 1.0f; // 1 Hz  — world scan + map metadata (FindObjectsByType is expensive)

        // One-shot game-asset extraction (map / unit icons / weapon + CM icons / airframe silhouette).
        // Owned here; driven from ScanWorld / PushSnapshot. See AssetCapture.cs.
        private readonly AssetCapture _assets = new AssetCapture();

        private float _fastTimer;
        private float _slowTimer;
        private int   _totalUnits;
        private int   _totalAircraft;
        private int   _lastContactCount;   // contacts pushed last tick (for the perf rollup)

        // Map metadata, resolved once LevelInfo is available.
        private LevelInfo? _level;
        private bool  _mapValid;
        private float _mapW, _mapH;
        private int   _gridOffsetX, _gridOffsetY;

        // Scratch for BuildFailures (the failure-indicator GameObjects themselves are captured by
        // AssetCapture and read back via _assets.FailureIndicators).
        private readonly List<string> _failureScratch = new List<string>();

        // Cached unit list from the 1 Hz scan; positions are read from it at 10 Hz.
        private Unit[] _units = Array.Empty<Unit>();

        // Slowly-changing context, refreshed in the 1 Hz scan.
        private string         _missionName = string.Empty;
        private string         _mapName     = string.Empty;
        private LoadoutEntry[]  _loadout     = Array.Empty<LoadoutEntry>();

        private int _flares    = -1;   // IR flares remaining (refreshed in the 1 Hz scan)
        private int _flaresMax = -1;   // IR flares capacity   (refreshed in the 1 Hz scan)

        // BDF faction-forces panel (docs/bdf-page.md), refreshed in the 1 Hz scan alongside the
        // loadout — forces counts change on unit spawn/loss, not frame to frame.
        private bool           _bdfPresent;
        private string         _bdfFaction   = string.Empty;
        private float          _bdfFunds;
        private float          _bdfScore;
        private int            _bdfWarheads;
        private BdfCountInfo[] _bdfShips     = Array.Empty<BdfCountInfo>();
        private BdfCountInfo[] _bdfVehicles  = Array.Empty<BdfCountInfo>();
        private BdfCountInfo[] _bdfBuildings = Array.Empty<BdfCountInfo>();
        private BdfCountInfo[] _bdfAircraft  = Array.Empty<BdfCountInfo>();

        // PAL — same panel, enemy faction (docs/bdf-page.md). Refreshed alongside BDF.
        private bool           _palPresent;
        private string         _palFaction   = string.Empty;
        private float          _palFunds;
        private float          _palScore;
        private int            _palWarheads;
        private BdfCountInfo[] _palShips     = Array.Empty<BdfCountInfo>();
        private BdfCountInfo[] _palVehicles  = Array.Empty<BdfCountInfo>();
        private BdfCountInfo[] _palBuildings = Array.Empty<BdfCountInfo>();
        private BdfCountInfo[] _palAircraft  = Array.Empty<BdfCountInfo>();

        // The game's HUD faction colors, read once from GameAssets.
        private string _colFriendly = "#39ff14";
        private string _colHostile  = "#ff4040";
        private string _colNeutral  = "#9aa0a6";
        private bool   _colorsRead;

        // TGP (targeting-pod) camera feed — the continuous capture of aircraft.targetCam, pushed to
        // the server's MJPEG endpoint. Owned here; driven from Update via Tick(dt), its Active flag
        // mirrored into the snapshot, and torn down from OnDestroy. See TgpFeed.cs.
        private readonly TgpFeed _tgp = new TgpFeed();

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

        // Afterburner gauge shape, resolved once per aircraft from the game's own ThrottleGauge
        // (a HUDApp that owns the MIL/reheat region config). Static per airframe, so we cache it
        // on aircraft change rather than reflecting every frame. See EnsureAfterburnerCache.
        private Aircraft _abAircraft;          // aircraft the cache below was resolved for
        private bool     _hasAfterburner;      // airframe has a reheat zone
        private float    _abStart = 1f;        // throttle fraction where afterburner begins (1 = none)
        private static readonly FieldInfo _tgAfterburnerField =
            typeof(ThrottleGauge).GetField("afterburner", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _tgRegionsField =
            typeof(ThrottleGauge).GetField("throttleRegions", BindingFlags.Instance | BindingFlags.NonPublic);

        // Incoming missiles — polled straight from MissileWarning.knownMissiles (a public list),
        // so no event hook needed. Reused buffer to keep the 10 Hz push allocation-light.
        private readonly List<MwContact> _mwBuf = new List<MwContact>(8);

        private void Update()
        {
            float dt = Time.deltaTime;

            // Baseline mode: when the mod's active features are switched off (Diagnostics >
            // FeaturesActive), do NONE of the telemetry/capture/serve work — but still sample FPS
            // each frame so PerfLogging captures a no-features baseline in the SAME mission. Lets
            // you A/B the mod's marginal cost without pulling the DLL (which takes PerfDiag with it).
            if (!Plugin.FeaturesActive)
            {
                PerfDiag.Tick(dt, 0, 0);
                return;
            }

            // Drain any inbound web-client commands first (main thread — safe to touch game state).
            CommandDispatcher.Drain();

            _fastTimer += dt;
            _slowTimer += dt;

            if (_slowTimer >= SlowInterval)
            {
                _slowTimer = 0f;
                long t0 = PerfDiag.Enabled ? Stopwatch.GetTimestamp() : 0L;
                ScanWorld();
                if (PerfDiag.Enabled) PerfDiag.RecordSince("ScanWorld", t0);
                // HUD OPTIONS snapshot for the /hud-options endpoint. Main thread, and cheap; options
                // change only on a toggle, so 1 Hz is ample. Kept out of PushSnapshot's fast path.
                TelemetryServer.RefreshHudOptions();
            }

            if (_fastTimer >= FastInterval)
            {
                _fastTimer = 0f;
                long t0 = PerfDiag.Enabled ? Stopwatch.GetTimestamp() : 0L;
                PushSnapshot();
                if (PerfDiag.Enabled) PerfDiag.RecordSince("PushSnapshot", t0);
            }

            _tgp.Tick(dt);   // TGP feed cadence is owned by TgpFeed (captures at its own interval)

            // Step 0 instrumentation: roll up the timing samples every few seconds (docs/performance.md).
            PerfDiag.Tick(dt, _totalUnits, _lastContactCount);
        }

        private void ScanWorld()
        {
            Unit[] units = UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            _units = units;

            int aircraft = 0;
            int iconBudget = AssetCapture.IconsPerScan;
            foreach (Unit u in units)
            {
                if (u == null) continue;
                if (u is Aircraft) aircraft++;
                // Pre-extract each unit type's map icon (a few per scan so it doesn't hitch).
                if (iconBudget > 0 && _assets.TryCaptureIcon(u.definition)) iconBudget--;
            }
            _totalUnits    = units.Length;
            _totalAircraft = aircraft;

            _assets.CaptureMissileWarningIcon();   // one-time: the real missile-warning sprite for the MAP page
            _assets.TryCaptureVehicleTypeIcons();  // one-time per type: the TGT page's vehicle-filter icons
            _assets.TryCaptureShipTypeIcons();     // one-time per type: the BDF page's ship-row icons
            _assets.TryCaptureBuildingTypeIcons(); // one-time per type: the HUD page's building-type icons
            _assets.TryCaptureHudCategoryIcons();  // one-time per category: the HUD page's type-glyph icons

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
                _assets.TryCaptureMap(ms);
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
                _assets.TryCaptureCmIcons(ac);
                _assets.TryLogPartLayout(ac);
                _assets.TryCaptureAirframe(ac);
                BuildBdf(ac);
            }
            else
            {
                _bdfPresent = false;
            }
            // Unlike BDF, PAL needs no local aircraft — GameManager.GetLocalFaction resolves straight
            // from the local player, so it's built unconditionally here.
            BuildPal();
        }

        // Faction-forces breakdown for the BDF page (docs/bdf-page.md). BdfPresent=false when the
        // local aircraft has no FactionHQ yet (e.g. between missions) — the page then shows an
        // unavailable state, same as TGT's present:false.
        private void BuildBdf(Aircraft ac)
        {
            FactionHQ hq = ac.NetworkHQ;
            MissionStatsTracker tracker = hq != null ? hq.missionStatsTracker : null;
            if (hq == null || tracker == null)
            {
                _bdfPresent   = false;
                _bdfFaction   = string.Empty;
                _bdfFunds     = 0f;
                _bdfScore     = 0f;
                _bdfWarheads  = 0;
                _bdfShips     = Array.Empty<BdfCountInfo>();
                _bdfVehicles  = Array.Empty<BdfCountInfo>();
                _bdfBuildings = Array.Empty<BdfCountInfo>();
                _bdfAircraft  = Array.Empty<BdfCountInfo>();
                return;
            }

            Encyclopedia enc = Encyclopedia.i;
            _assets.TryCaptureFactionLogo(hq);

            _bdfPresent   = true;
            _bdfFaction   = hq.faction != null ? hq.faction.factionName : string.Empty;
            _bdfFunds     = hq.factionFunds;
            _bdfScore     = hq.factionScore;
            _bdfWarheads  = hq.GetWarheadStockpile();
            _bdfShips     = BdfTypeCounts(enc?.shipTypes,     enc?.ships,     tracker, d => ((ShipDefinition)d).shipType.ToString());
            _bdfVehicles  = BdfTypeCounts(enc?.vehicleTypes,  enc?.vehicles,  tracker, d => ((VehicleDefinition)d).vehicleType.ToString());
            _bdfBuildings = BdfTypeCounts(enc?.buildingTypes, enc?.buildings, tracker, d => ((BuildingDefinition)d).buildingType.ToString());
            _bdfAircraft  = BdfAircraftCounts(enc, tracker);
        }

        // Faction-forces breakdown for the PAL page (docs/bdf-page.md) — the same panel as BDF, but
        // for the ENEMY faction. Needs no local aircraft: GameManager.GetLocalFaction resolves
        // straight from the local player, and FactionRegistry holds every faction's HQ. "The other
        // one" is just registry membership that isn't ours — the game currently never has more than
        // two factions, so no further tie-break is needed. (InfoPanel_Faction.cs makes the same
        // two-faction assumption, but hardcodes the two literal faction names to find "the other";
        // reading the registry instead means this doesn't depend on those names.)
        private void BuildPal()
        {
            if (!GameManager.GetLocalFaction(out Faction localFaction)) { ClearPal(); return; }

            Faction enemyFaction = null;
            foreach (Faction f in FactionRegistry.factions)
                if (f != null && f != localFaction) { enemyFaction = f; break; }

            FactionHQ hq = enemyFaction != null ? FactionRegistry.HQFromFaction(enemyFaction) : null;
            MissionStatsTracker tracker = hq != null ? hq.missionStatsTracker : null;
            if (hq == null || tracker == null) { ClearPal(); return; }

            Encyclopedia enc = Encyclopedia.i;
            _assets.TryCaptureFactionLogo(hq);

            _palPresent   = true;
            _palFaction   = hq.faction != null ? hq.faction.factionName : string.Empty;
            _palFunds     = hq.factionFunds;
            _palScore     = hq.factionScore;
            _palWarheads  = hq.GetWarheadStockpile();
            _palShips     = BdfTypeCounts(enc?.shipTypes,     enc?.ships,     tracker, d => ((ShipDefinition)d).shipType.ToString());
            _palVehicles  = BdfTypeCounts(enc?.vehicleTypes,  enc?.vehicles,  tracker, d => ((VehicleDefinition)d).vehicleType.ToString());
            _palBuildings = BdfTypeCounts(enc?.buildingTypes, enc?.buildings, tracker, d => ((BuildingDefinition)d).buildingType.ToString());
            _palAircraft  = BdfAircraftCounts(enc, tracker);
        }

        private void ClearPal()
        {
            _palPresent   = false;
            _palFaction   = string.Empty;
            _palFunds     = 0f;
            _palScore     = 0f;
            _palWarheads  = 0;
            _palShips     = Array.Empty<BdfCountInfo>();
            _palVehicles  = Array.Empty<BdfCountInfo>();
            _palBuildings = Array.Empty<BdfCountInfo>();
            _palAircraft  = Array.Empty<BdfCountInfo>();
        }

        // Sums current-unit counts per named type (SHIPS: CV/LHA/…, VEHICLES: TRUCK/UGV/…,
        // BUILDINGS: CIV/FAC/…), mirroring the game's own InfoPanel_ItemPrefab.RefreshDefinition:
        // one Encyclopedia.i.*Types entry per row, current count summed over every definition in
        // that list whose type-enum name matches. Enum order comes from the *Types list itself
        // (the same list the game builds its panel rows from), not a hardcoded enum dump.
        private static BdfCountInfo[] BdfTypeCounts(
            List<Encyclopedia.UnitType> types, IEnumerable<UnitDefinition> defs,
            MissionStatsTracker tracker, Func<UnitDefinition, string> typeNameOf)
        {
            if (types == null) return Array.Empty<BdfCountInfo>();
            var arr = new BdfCountInfo[types.Count];
            for (int i = 0; i < types.Count; i++)
            {
                string typeName = types[i].typeName;
                int count = 0;
                if (defs != null)
                    foreach (UnitDefinition d in defs)
                        if (d != null && typeNameOf(d) == typeName)
                            count += tracker.GetCurrentUnits(d);
                arr[i] = new BdfCountInfo { Name = typeName, Count = count };
            }
            return arr;
        }

        // One entry per allowed AircraftDefinition — unlike ships/vehicles/buildings, aircraft
        // aren't grouped by type in-game (each is its own icon). Name is the unitName, doubling as
        // the /icon key. Also proactively captures each definition's icon here (not just ones the
        // world-scan has spotted this mission), so the BDF grid has an icon for every airframe.
        private BdfCountInfo[] BdfAircraftCounts(Encyclopedia enc, MissionStatsTracker tracker)
        {
            if (enc == null || enc.aircraft == null) return Array.Empty<BdfCountInfo>();
            var list = new List<BdfCountInfo>(enc.aircraft.Count);
            foreach (AircraftDefinition def in enc.aircraft)
            {
                if (def == null || !def.IsAllowed(MissionManager.AllowEventContent)) continue;
                _assets.TryCaptureIcon(def);
                list.Add(new BdfCountInfo { Name = def.unitName, Count = tracker.GetCurrentUnits(def) });
            }
            return list.ToArray();
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

        // WeaponManager.gunsLinked is private; cache the FieldInfo and read it via reflection.
        // "Linked" is only meaningful with multiple guns, so a single-gun airframe reports false
        // (which the AVN tile renders as its dim/off state).
        private static FieldInfo? _gunsLinkedField;
        private static bool GetGunsLinked(WeaponManager? wm)
        {
            if (wm == null || !wm.HasMultipleGuns()) return false;
            if (_gunsLinkedField == null)
                _gunsLinkedField = typeof(WeaponManager).GetField("gunsLinked", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_gunsLinkedField == null) return false;
            try { return _gunsLinkedField.GetValue(wm) is bool b && b; }
            catch { return false; }
        }

        // Turret auto-control ("engage at will") is a public CombatHUD property, but only meaningful
        // when the airframe actually has turrets — a turret-less plane reports false (the AVN tile's
        // dim/off state). CombatHUD is a scene singleton, so it can be null between missions.
        private static bool GetTurretAuto(WeaponManager? wm)
        {
            if (wm == null || wm.StationsWithTurrets() == 0) return false;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            return hud != null && hud.turretAutoControl;
        }

        // NightVision.nightVisActive is private on the (HUD-wide) singleton; reflect it (cached).
        private static FieldInfo? _nvgActiveField;
        private static bool GetNightVisionActive()
        {
            NightVision nv = NightVision.i;
            if (nv == null) return false;
            if (_nvgActiveField == null)
                _nvgActiveField = typeof(NightVision).GetField("nightVisActive", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_nvgActiveField == null) return false;
            try { return _nvgActiveField.GetValue(nv) is bool b && b; }
            catch { return false; }
        }

        // Nav-light state is Aircraft.navLights (private) -> NavLights.isOn (private); reflect both
        // (cached). Nav lights auto-follow the gear plus a manual force-on toggle, so isOn is the
        // authoritative "are they lit" flag.
        private static FieldInfo? _navLightsField;
        private static FieldInfo? _navLightsIsOnField;
        private static bool GetNavLightsOn(Aircraft ac)
        {
            if (_navLightsField == null)
                _navLightsField = typeof(Aircraft).GetField("navLights", BindingFlags.NonPublic | BindingFlags.Instance);
            object? nl = _navLightsField?.GetValue(ac);
            if (nl == null) return false;
            if (_navLightsIsOnField == null)
                _navLightsIsOnField = typeof(NavLights).GetField("isOn", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_navLightsIsOnField == null) return false;
            try { return _navLightsIsOnField.GetValue(nl) is bool b && b; }
            catch { return false; }
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
                _assets.TryCaptureWeaponIcon(name, info.weaponIcon);
            }

            var result = new LoadoutEntry[_loNames.Count];
            for (int i = 0; i < _loNames.Count; i++)
                result[i] = new LoadoutEntry { Name = _loNames[i], Ammo = _loCur[i], FullAmmo = _loMax[i] };
            return result;
        }

        private void PushSnapshot()
        {
            GameManager.GetLocalAircraft(out Aircraft aircraft);
            if (aircraft == null)
                return;

            // Keep the radar-warning hook attached to the current local aircraft (re-attaches on
            // aircraft change; clears the emitter table so a fresh airframe starts clean).
            EnsureRwrSubscription(aircraft);

            // Resolve the afterburner gauge shape when the aircraft changes (cached; static per airframe).
            EnsureAfterburnerCache(aircraft);

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

            _assets.TryCaptureIcon(aircraft.definition);

            // Built here (not in the initializer) so we can time it — BuildUnits is the
            // suspected per-unit hot path at 10 Hz (docs/performance.md, item #3).
            long tUnits = PerfDiag.Enabled ? Stopwatch.GetTimestamp() : 0L;
            UnitInfo[] units = BuildUnits(aircraft);
            if (PerfDiag.Enabled) PerfDiag.RecordSince("BuildUnits", tUnits);
            _lastContactCount = units.Length;

            // TGT filter panel — read straight off the game's singleton (present all mission, but
            // guard anyway). Unity's == handles a destroyed instance as null, so we take a plain
            // reference + bool rather than ?. (which would sidestep that fake-null check).
            TargetListSelector tgtSel = SceneSingleton<TargetListSelector>.i;
            bool tgtOk = tgtSel != null;

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
                RadarOn        = aircraft.HasRadarEmission(),
                GunsLinked     = GetGunsLinked(wm),
                Ignition       = aircraft.Ignition,
                FlightAssist   = aircraft.flightAssist && (aircraft.GetControlsFilter()?.HasFlightAssist() ?? false),
                TurretAuto     = GetTurretAuto(wm),
                NightVision    = GetNightVisionActive(),
                NavLightsOn    = GetNavLightsOn(aircraft),
                Flares         = _flares,
                FlaresMax      = _flaresMax,
                EwKJ           = ewKJ,
                EwKJMax        = ewKJMax,
                Fuel           = aircraft.GetFuelLevel(),
                Throttle       = aircraft.GetInputs() != null ? aircraft.GetInputs().throttle : -1f,
                HasAfterburner = _hasAfterburner,
                AbStart        = _abStart,
                SelWeapon      = selWeapon,
                CmCategory     = cmCategory,
                TotalUnits     = _totalUnits,
                TotalAircraft  = _totalAircraft,
                MapValid       = _mapValid,
                MapW           = _mapW,
                MapH           = _mapH,
                GridOffsetX    = _gridOffsetX,
                GridOffsetY    = _gridOffsetY,
                Units          = units,
                ColFriendly    = _colFriendly,
                ColHostile     = _colHostile,
                ColNeutral     = _colNeutral,
                TgpActive      = _tgp.Active,
                Parts          = BuildParts(aircraft),
                Failures       = BuildFailures(),
                Rwr            = BuildRwr(aircraft),
                Mw             = BuildMw(aircraft),
                TgtPresent     = tgtOk,
                TgtLaser       = tgtOk && tgtSel.toggleLaser      != null && tgtSel.toggleLaser.status,
                TgtHud         = tgtOk && tgtSel.toggleFollowHUD  != null && tgtSel.toggleFollowHUD.status,
                TgtFaction     = tgtOk ? ReadToggles(tgtSel.toggleFactionItems)      : Array.Empty<TgtToggleInfo>(),
                TgtCategory    = tgtOk ? ReadToggles(tgtSel.toggleUnitTypesItems)    : Array.Empty<TgtToggleInfo>(),
                TgtVehicle     = tgtOk ? ReadToggles(tgtSel.toggleVehicleTypesItems) : Array.Empty<TgtToggleInfo>(),
                BdfPresent     = _bdfPresent,
                BdfFaction     = _bdfFaction,
                BdfFunds       = _bdfFunds,
                BdfScore       = _bdfScore,
                BdfWarheads    = _bdfWarheads,
                BdfShips       = _bdfShips,
                BdfVehicles    = _bdfVehicles,
                BdfBuildings   = _bdfBuildings,
                BdfAircraft    = _bdfAircraft,
                PalPresent     = _palPresent,
                PalFaction     = _palFaction,
                PalFunds       = _palFunds,
                PalScore       = _palScore,
                PalWarheads    = _palWarheads,
                PalShips       = _palShips,
                PalVehicles    = _palVehicles,
                PalBuildings   = _palBuildings,
                PalAircraft    = _palAircraft
            });
        }

        // Snapshots a TGT toggle group's labels + on/off states, preserving the game's ordering
        // (which the tgt.set/tgt.only commands index by). The vehicle row's labels are the game's
        // "_"→"\n"-wrapped typeNames; we reverse the wrap so the name is the canonical typeName that
        // also keys the captured icon (e.g. "IR_SAM").
        private static TgtToggleInfo[] ReadToggles(List<TargetListSelector_ToggleButton> list)
        {
            if (list == null || list.Count == 0) return Array.Empty<TgtToggleInfo>();
            var arr = new TgtToggleInfo[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                TargetListSelector_ToggleButton b = list[i];
                arr[i] = new TgtToggleInfo
                {
                    Name = (b != null && b.label != null) ? b.label.text.Replace("\n", "_") : string.Empty,
                    On   = b != null && b.status
                };
            }
            return arr;
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
                    Notch = NotchHeading(player, m, seeker),
                    Heading = m.transform.eulerAngles.y
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

        // Resolve the airframe's afterburner gauge shape once per aircraft. The game's own
        // ThrottleGauge (a cockpit HUDApp) owns the MIL/reheat split: `afterburner` flags whether
        // the airframe has reheat, and the last throttleRegion's `start` is the MIL→AB boundary on
        // the 0..1 axis. Both are prefab-serialized privates, so we reflect them (same approach as
        // HudDeclutter's CombatHUD access) and cache — they never change for a given airframe.
        // Any miss (no gauge, no regions, reflection failure) degrades to a plain non-AB bar.
        private void EnsureAfterburnerCache(Aircraft ac)
        {
            if (ReferenceEquals(ac, _abAircraft)) return;
            _abAircraft = ac;
            _hasAfterburner = false;
            _abStart = 1f;
            if (ac == null || _tgAfterburnerField == null) return;

            try
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                ThrottleGauge gauge = hud != null ? hud.GetComponentInChildren<ThrottleGauge>(true) : null;
                if (gauge == null) gauge = UnityEngine.Object.FindObjectOfType<ThrottleGauge>(true);
                if (gauge == null) return;

                _hasAfterburner = (bool)_tgAfterburnerField.GetValue(gauge);
                if (!_hasAfterburner) return;

                // AbStart = the last region's start (the reheat zone). If a plane flags afterburner
                // but ships no regions, the game only shows reheat at throttle == 1, so leave AbStart
                // at 1 (no distinct zone until full).
                var regions = _tgRegionsField?.GetValue(gauge) as Array;
                if (regions != null && regions.Length > 0)
                {
                    object last = regions.GetValue(regions.Length - 1);
                    FieldInfo startField = last?.GetType().GetField("start", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (startField != null)
                        _abStart = Mathf.Clamp01((float)startField.GetValue(last));
                }
            }
            catch (Exception)
            {
                // Game internals shifted or the gauge wasn't ready — fall back to a plain bar.
                _hasAfterburner = false;
                _abStart = 1f;
            }
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
                float ttl = em.Tier == 2 ? 6f : (em.Tier == 1 ? 3f : 1.5f);
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
            GameObject[] indicators = _assets.FailureIndicators;
            if (indicators == null || indicators.Length == 0) return Array.Empty<string>();
            _failureScratch.Clear();
            for (int i = 0; i < indicators.Length; i++)
            {
                GameObject go = indicators[i];
                if (go != null && go.activeSelf) _failureScratch.Add(go.name);
            }
            return _failureScratch.Count == 0 ? Array.Empty<string>() : _failureScratch.ToArray();
        }

        // Snapshots every UnitPart in the player aircraft's partLookup into a FRESH array each
        // tick — not a reused buffer. The snapshot is serialized on a background SSE thread (once
        // per version, see TelemetryServer.GetFrameBytes), so a buffer the main thread overwrites
        // next tick could tear mid-serialize. A per-tick alloc of a small (~36-entry) struct array
        // is negligible next to the units/rwr/mw arrays already built here, and it makes the
        // snapshot's arrays owned/immutable — no data race.
        private PartHp[] BuildParts(Aircraft ac)
        {
            var parts = ac.partLookup;
            if (parts == null || parts.Count == 0) return Array.Empty<PartHp>();

            var buf = new PartHp[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                UnitPart p = parts[i];
                if (p == null) continue;
                buf[i].Name     = p.gameObject != null ? p.gameObject.name : string.Empty;
                buf[i].Hp       = p.hitPoints;
                buf[i].Detached = p.IsDetached();
            }
            return buf;
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
                    Id       = u.persistentID.Id,
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

        private void OnDestroy()
        {
            _tgp.Disengage();
            if (_rwrSubscribed != null) { _rwrSubscribed.onRadarWarning -= OnRadarWarning; _rwrSubscribed = null; }
        }
    }
}
