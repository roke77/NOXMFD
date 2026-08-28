using System;
using System.Collections.Generic;
using System.Reflection;
using NuclearOption.SavedMission;
using UnityEngine;

namespace NOXMFD
{
    internal class TelemetryReader : MonoBehaviour
    {
        // Not a const: RatesConfig.SetFastHz (rates.set command) writes this live from the MAP CFG
        // page's TLM slider, so PushSnapshot's fast group — own-ship, weapons, RWR/MW, TGT,
        // BDF/PAL — moves together.
        internal static float FastInterval = 0.1f; // 10 Hz — position / speed
        private const  float SlowInterval  = 1.0f; // 1 Hz  — world scan + map metadata (FindObjectsByType is expensive)
        // Not a const either: RatesConfig.SetContactHz (rates.set group "contact", MAP CFG page's
        // own CONTACTS slider) writes this live, independently of FastHz above.
        internal static float ContactInterval = 0.25f; // 4 Hz default — MAP/RDR/HSD contacts are expensive and don't need 10 Hz

        // One-shot game-asset extraction (map / unit icons / weapon + CM icons / airframe silhouette).
        // Owned here; driven from ScanWorld / PushSnapshot. See AssetCapture.cs.
        private readonly AssetCapture _assets = new AssetCapture();

        private float _fastTimer;
        private float _slowTimer;
        private float _contactTimer = ContactInterval;
        private int   _totalUnits;
        private int   _totalAircraft;

        // Map metadata, resolved once LevelInfo is available.
        private LevelInfo? _level;
        private bool  _mapValid;
        private float _mapW, _mapH;
        private float _mapReachW, _mapReachH;   // see the reach comment on TelemetrySnapshot.MapReachW
        private int   _gridOffsetX, _gridOffsetY;

        // Scratch for BuildFailures (the failure-indicator GameObjects themselves are captured by
        // AssetCapture and read back via _assets.FailureIndicators).
        private readonly List<string> _failureScratch = new List<string>();

        // Cached unit list from the 1 Hz scan; positions are sampled into contact snapshots at 4 Hz.
        private Unit[] _units = Array.Empty<Unit>();

        // Contacts are rebuilt at 4 Hz, then reused by the fast snapshots. Own-ship, RWR, and MW stay
        // on FastInterval; full unit, radar, and HSD datalink contact lists are visually fine at map
        // scale at 4 Hz and include most of PushSnapshot's per-unit work.
        private Aircraft? _contactAircraft;
        private UnitInfo[] _cachedUnits = Array.Empty<UnitInfo>();
        private RdrContact[] _cachedRdr = Array.Empty<RdrContact>();
        private HsdContact[] _cachedHsd = Array.Empty<HsdContact>();
        private PitbullContact[] _cachedPitbull = Array.Empty<PitbullContact>();
        private bool _cachedRadarPresent;
        private float _cachedRadarRange;
        private float _cachedRadarConeDeg;

        // MAP jam markers (docs comment on UnitInfo.Jammed): Unit.onJam only fires the jamming
        // source, Radar.IsJammed() doesn't remember it — so we hook every radar-equipped unit once
        // and remember its last jammer. Radar.IsJammed() (polled fresh each scan) gates whether a
        // unit is CURRENTLY jammed; _jammedBy just answers "by whom" once it is.
        // Hooked units are never unhooked (a despawned unit's entry just sits inert) — bounded by
        // total units spawned in one mission, not worth pruning for a HOTAS MFD mod.
        private readonly HashSet<Unit> _jamHooked = new HashSet<Unit>();
        private readonly Dictionary<Unit, Unit> _jammedBy = new Dictionary<Unit, Unit>();

        // Slowly-changing context, refreshed in the 1 Hz scan.
        private string         _missionName = string.Empty;
        private string         _mapName     = string.Empty;
        private LoadoutEntry[]  _loadout     = Array.Empty<LoadoutEntry>();
        private PylonMarker[]   _pylons      = Array.Empty<PylonMarker>();

        private int _flares    = -1;   // IR flares remaining (refreshed in the 1 Hz scan)
        private int _flaresMax = -1;   // IR flares capacity   (refreshed in the 1 Hz scan)

        // BDF/PAL faction-forces panels (docs/bdf-page.md), refreshed in the 1 Hz scan alongside
        // the loadout — forces counts change on unit spawn/loss, not frame to frame. Same block
        // shape for both, always the fixed BOSCALI/PRIMEVA identities.
        private FactionForcesBlock _bdf = FactionForcesBlock.Empty;
        private FactionForcesBlock _pal = FactionForcesBlock.Empty;

        // MIS mission-info panel (docs/md-pages.md), refreshed in the 1 Hz scan — same cadence the
        // game's own ObjectiveInfoList.Update() refreshes this panel at (refreshDelay = 1f).
        private bool   _misPresent;
        private string _misDescription = string.Empty;
        private float  _misTimeOfDay;
        private float  _misDuration;
        private float  _misScore;
        private byte   _misLevel;

        // OBJ active-objectives list (docs/md-pages.md), refreshed alongside MIS.
        private bool       _objPresent;
        private ObjEntry[] _obj = Array.Empty<ObjEntry>();
        private readonly List<MissionPosition.PositionResult> _objPosScratch = new List<MissionPosition.PositionResult>();

        // AKF advanced kill feed (docs/akf-page.md). Kill events accumulate on AkfTracker in real
        // time via Harmony hooks (HarmonyPatches.cs); this just snapshots its state at the same 1 Hz
        // cadence BDF/MIS/OBJ already refresh at — kill-feed lines and session tallies don't need to
        // be re-serialized every 100ms.
        private readonly AkfTracker _akf = new AkfTracker();
        private AkfKillEntry[] _akfAll    = Array.Empty<AkfKillEntry>();
        private AkfKillEntry[] _akfPlayer = Array.Empty<AkfKillEntry>();
        private int   _akfKillsAircraft, _akfKillsShip, _akfKillsVehicle, _akfKillsBuilding;
        private int   _akfRank;
        private float _akfFundsGained, _akfFundsSpent;

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
            public Unit? Unit;
            public byte  Tier;       // 0 search, 1 track (detected), 2 lock (we are its target)
            public float Range;      // emitting radar's max range, for closeness normalisation
            public float LastSeen;   // Time.time of the most recent ping
        }
        private readonly Dictionary<Unit, RwrEmitter> _rwrEmitters = new Dictionary<Unit, RwrEmitter>();
        private readonly List<Unit> _rwrExpireScratch = new List<Unit>();
        private readonly List<RwrContact> _rwrBuf = new List<RwrContact>(32);
        private Aircraft? _rwrSubscribed;   // the aircraft whose onRadarWarning we're hooked to

        // Afterburner gauge shape, resolved once per aircraft from the game's own ThrottleGauge
        // (a HUDApp that owns the MIL/reheat region config). Static per airframe, so we cache it
        // on aircraft change rather than reflecting every frame. See EnsureAfterburnerCache.
        private Aircraft? _abAircraft;          // aircraft the cache below was resolved for
        private bool     _hasAfterburner;      // airframe has a reheat zone
        private float    _abStart = 1f;        // throttle fraction where afterburner begins (1 = none)
        private static readonly FieldInfo _tgAfterburnerField =
            typeof(ThrottleGauge).GetField("afterburner", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _tgRegionsField =
            typeof(ThrottleGauge).GetField("throttleRegions", BindingFlags.Instance | BindingFlags.NonPublic);

        // Incoming missiles — polled straight from MissileWarning.knownMissiles (a public list),
        // so no event hook needed. Reused buffer to keep the 10 Hz push allocation-light.
        private readonly List<MwContact> _mwBuf = new List<MwContact>(8);

        // Player's own in-flight AA missiles that have gone pitbull (RDR page, issue #40). Filtered
        // out of the same _units scan BuildRdr/ScanWorld already do — Missile IS a Unit subclass, so
        // no extra enumeration source is needed. Reused buffer, same reasoning as _mwBuf.
        private readonly List<PitbullContact> _pitbullBuf = new List<PitbullContact>(4);

        // Publishes _akf so the Harmony kill/weapon-attribution patches (HarmonyPatches.cs) — static,
        // with no other way to reach the live mission's tracker — can record into it.
        private void Awake()
        {
            AkfTracker.Active = _akf;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Inbound web-client commands are drained by MissionLifecycle.Update (the persistent host),
            // so the /keybinds page works at the main menu too — not here.

            _fastTimer += dt;
            _slowTimer += dt;
            _contactTimer += dt;

            if (_slowTimer >= SlowInterval)
            {
                _slowTimer = 0f;
                ScanWorld();
                // HUD OPTIONS snapshot for the /hud-options endpoint. Main thread, and cheap; options
                // change only on a toggle, so 1 Hz is ample. Kept out of PushSnapshot's fast path.
                TelemetryServer.RefreshHudOptions();
                // Lazily captures the player's own HUD filter baseline the first time HUDOptions
                // exists this session (issue #50) — a no-op once one has been captured.
                HudCombatModeFilters.EnsureBootstrap();
                // Waypoint route proximity-advance (docs/hud-waypoint-indicator.md) — the plugin now
                // ticks this itself regardless of which page any browser has open, unlike the old
                // browser-side check that only ran while the WPT page happened to be visible. 1 Hz is
                // ample against a 1000m advance radius at combat-aircraft speeds.
                if (GameManager.GetLocalAircraft(out Aircraft advanceAc) && advanceAc != null)
                {
                    Vector3 advanceWorld = advanceAc.transform.position - Datum.originPosition;
                    RouteStore.AdvanceIfNear(advanceWorld.x, advanceWorld.z);
                }
            }

            if (_fastTimer >= FastInterval)
            {
                _fastTimer = 0f;
                PushSnapshot();
            }

            _tgp.Tick(dt);   // TGP feed cadence is owned by TgpFeed (captures at its own interval)
            TgpManualControl.Tick(dt);   // docs/tgp-manual-control.md — no-op while manual mode is off
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
                // Remember who last jammed this unit's radar (see _jammedBy declaration).
                if (u.radar != null && _jamHooked.Add(u))
                {
                    Unit jammed = u;
                    u.onJam += e => _jammedBy[jammed] = e.jammingUnit;
                }
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

                // issue #65: MapSettings.GridSizeX/Y (10km grid-letter bands) is the mission's real
                // reachable extent — confirmed in-game to run past MapSize, the smaller square the
                // minimap IMAGE was captured at (a carrier deck and its surrounding terrain sat
                // beyond MapSize but within GridSize). Falls back to MapSize when GridSizeX/Y isn't
                // authored (0), so a mission without it keeps today's exact behavior.
                _mapReachW = ms.GridSizeX > 0 ? ms.GridSizeX * 10000f : _mapW;
                _mapReachH = ms.GridSizeY > 0 ? ms.GridSizeY * 10000f : _mapH;
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
                _assets.TryLogWeaponInfo(ac);
                _assets.TryCaptureAirframe(ac);
                _assets.TryCaptureFrontalSilhouette(ac);
                var pylonStates = _assets.ReadFrontalMarkerStates(ac.definition != null ? ac.definition.unitName : null);
                _pylons = new PylonMarker[pylonStates.Count];
                for (int i = 0; i < pylonStates.Count; i++)
                    _pylons[i] = new PylonMarker { Name = pylonStates[i].name, State = pylonStates[i].state };
            }
            // BDF/PAL need no local aircraft — each resolves a fixed faction identity straight from
            // FactionRegistry, so both are built unconditionally.
            _bdf = BuildFactionForces(FactionHelper.Boscali);
            _pal = BuildFactionForces(FactionHelper.Primeva);
            BuildMis();
            BuildObj();
            BuildAkf();
        }

        // AKF advanced kill feed (docs/akf-page.md) — snapshots AkfTracker's live, Harmony-fed state.
        private void BuildAkf()
        {
            _akf.TickFunds();
            _akf.TickRank();
            _akfAll            = ToArray(_akf.AllFeed);
            _akfPlayer         = ToArray(_akf.PlayerFeed);
            _akfKillsAircraft  = _akf.KillsAircraft;
            _akfKillsShip      = _akf.KillsShip;
            _akfKillsVehicle   = _akf.KillsVehicle;
            _akfKillsBuilding  = _akf.KillsBuilding;
            _akfRank           = _akf.Rank;
            _akfFundsGained    = _akf.FundsGained;
            _akfFundsSpent     = _akf.FundsSpent;
        }

        private static AkfKillEntry[] ToArray(IReadOnlyList<AkfKillEntry> list)
        {
            var arr = new AkfKillEntry[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i];
            return arr;
        }

        // MIS mission-info panel (docs/md-pages.md) — mirrors ObjectiveInfoList.UpdateMissionInfo /
        // InitializeMission. Present only in singleplayer: the game reads the mission name/description
        // off MissionManager.CurrentMission there, but shows the Steam lobby name instead in
        // multiplayer (no equivalent description exists), which this mod doesn't plumb through.
        private void BuildMis()
        {
            if (GameManager.gameState != GameState.SinglePlayer || MissionManager.CurrentMission == null
                || NetworkSceneSingleton<MissionManager>.i == null)
            {
                _misPresent = false;
                _misDescription = string.Empty;
                return;
            }

            MissionManager mm = NetworkSceneSingleton<MissionManager>.i;
            _misPresent     = true;
            _misDescription = MissionManager.CurrentMission.missionSettings?.description ?? string.Empty;
            _misTimeOfDay   = _level != null ? _level.timeOfDay : 0f;
            _misDuration    = mm.MissionTime;
            _misScore       = mm.currentEscalation;
            _misLevel       = mm.currentEscalation > mm.strategicThreshold ? (byte)2
                             : mm.currentEscalation > mm.tacticalThreshold  ? (byte)1
                             : (byte)0;
        }

        // OBJ active-objectives list (docs/md-pages.md) — mirrors ObjectiveInfoList.UpdateObjectiveInfo,
        // the player faction's currently active objectives. ObjPresent=false when the map's HQ isn't
        // resolved yet (e.g. between missions).
        private void BuildObj()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.HQ == null || !MissionPosition.TryGetActiveObjectives(map.HQ, out List<Objective> active))
            {
                _objPresent = false;
                _obj = Array.Empty<ObjEntry>();
                return;
            }

            _objPresent = true;

            // One call gathers every position row for every active objective (ObjectiveInfoList's own
            // pattern) — grouped below by objective rather than re-querying per objective. "from" only
            // affects the Distance/Direction fields, which nothing here reads (the client recomputes
            // distance from the player's own position, same as the game's ObjectiveInfoList_Item does).
            MissionPosition.GetAllPositionsResults(map.HQ, Datum.originPosition.ToGlobalPosition(), false, _objPosScratch);
            var posByObjective = new Dictionary<Objective, List<ObjPosition>>();
            foreach (MissionPosition.PositionResult r in _objPosScratch)
            {
                if (!posByObjective.TryGetValue(r.Objective, out List<ObjPosition> positions))
                    posByObjective[r.Objective] = positions = new List<ObjPosition>();
                positions.Add(new ObjPosition { Name = r.Objective.SavedObjective.ObjectiveTypeEnum.ToString(), X = r.Position.x, Z = r.Position.z });
            }

            var list = new List<ObjEntry>(active.Count);
            foreach (Objective o in active)
            {
                // Matches the game's own list membership, not just a map-pin filter: ObjectiveInfoList.
                // AddObjectiveEntry/InitializeObjectiveList both gate on IObjectiveWithPosition too, so
                // position-less objective types (WaitSeconds, DialogueBox, CompleteOtherObjective,
                // SuccessfulSortie, ...) never show up in the in-game OBJ list at all, not just on the map.
                if (o == null || o.SavedObjective == null || o.SavedObjective.Hidden || !(o is IObjectiveWithPosition)) continue;
                list.Add(new ObjEntry
                {
                    Name      = o.SavedObjective.DisplayName,
                    Status    = (byte)o.Status,
                    Percent   = o.CompletePercent,
                    Positions = posByObjective.TryGetValue(o, out List<ObjPosition> positions) ? positions.ToArray() : Array.Empty<ObjPosition>()
                });
            }
            _obj = list.ToArray();
        }

        // One faction's forces snapshot — the exact same shape BDF and PAL each need, just for a
        // different fixed faction identity.
        private struct FactionForcesBlock
        {
            public bool           Present;
            public string         Faction;
            public float          Funds;
            public float          Score;
            public int            Warheads;
            public BdfCountInfo[] Ships;
            public BdfCountInfo[] Vehicles;
            public BdfCountInfo[] Buildings;
            public BdfCountInfo[] Aircraft;

            public static readonly FactionForcesBlock Empty = new FactionForcesBlock
            {
                Present   = false,
                Faction   = string.Empty,
                Ships     = Array.Empty<BdfCountInfo>(),
                Vehicles  = Array.Empty<BdfCountInfo>(),
                Buildings = Array.Empty<BdfCountInfo>(),
                Aircraft  = Array.Empty<BdfCountInfo>(),
            };
        }

        // Faction-forces breakdown for the BDF/PAL pages (docs/bdf-page.md) — always a fixed faction
        // identity (BOSCALI for BDF, PRIMEVA for PAL), never "whichever faction the player is on":
        // switching sides does not change which faction either key opens.
        // FactionHelper.Boscali/Primeva are the game's own two literal faction-name constants
        // (FactionHelper.cs) — InfoPanel_Faction.cs resolves its BDF/PALA-equivalent keys the same
        // fixed-name way. Present=false when that faction has no FactionHQ yet (e.g. between missions).
        private FactionForcesBlock BuildFactionForces(string factionName)
        {
            FactionHQ? hq = FactionRegistry.HqFromName(factionName);
            MissionStatsTracker? tracker = hq != null ? hq.missionStatsTracker : null;
            if (hq == null || tracker == null) return FactionForcesBlock.Empty;

            Encyclopedia enc = Encyclopedia.i;
            _assets.TryCaptureFactionLogo(hq);

            return new FactionForcesBlock
            {
                Present   = true,
                Faction   = hq.faction != null ? hq.faction.factionName : string.Empty,
                Funds     = hq.factionFunds,
                Score     = hq.factionScore,
                Warheads  = hq.GetWarheadStockpile(),
                Ships     = BdfTypeCounts(enc?.shipTypes,     enc?.ships,     tracker, d => ((ShipDefinition)d).shipType.ToString()),
                Vehicles  = BdfTypeCounts(enc?.vehicleTypes,  enc?.vehicles,  tracker, d => ((VehicleDefinition)d).vehicleType.ToString()),
                Buildings = BdfTypeCounts(enc?.buildingTypes, enc?.buildings, tracker, d => ((BuildingDefinition)d).buildingType.ToString()),
                Aircraft  = BdfAircraftCounts(enc, tracker),
            };
        }

        // Sums current-unit counts per named type (SHIPS: CV/LHA/…, VEHICLES: TRUCK/UGV/…,
        // BUILDINGS: CIV/FAC/…), mirroring the game's own InfoPanel_ItemPrefab.RefreshDefinition:
        // one Encyclopedia.i.*Types entry per row, current count summed over every definition in
        // that list whose type-enum name matches. Enum order comes from the *Types list itself
        // (the same list the game builds its panel rows from), not a hardcoded enum dump.
        private static BdfCountInfo[] BdfTypeCounts(
            List<Encyclopedia.UnitType>? types, IEnumerable<UnitDefinition>? defs,
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
        private BdfCountInfo[] BdfAircraftCounts(Encyclopedia? enc, MissionStatsTracker tracker)
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

        // Unit.IRSources is a private List<IRSource> — every heat-emitting engine registers into it
        // (flares do too, flagged IRSource.flare=true). Reflect the list once (cached, same pattern
        // as GetNavLightsOn above), then read each entry's public intensity/flare fields directly —
        // no per-engine-type reflection needed, IRSource itself already carries them.
        //
        // Ceiling of 12 is not a guess: the game's own cockpit IR gauge (StatusGauges.Gauge.Update,
        // decompiled) does `Mathf.Clamp(irSource.intensity, 0f, 12f)` before dividing by its serialized
        // maxValue — a clamp tighter than maxValue would cut the gauge off before full-scale, so 12 is
        // maxValue. That widget also picks a single (random) live IRSource via Unit.GetIRSource()
        // rather than the max non-flare source; we use max-non-flare instead since it's stable frame to
        // frame (the random pick is fine for the game's own gauge, which only needs to look busy).
        private static FieldInfo? _irSourcesField;
        private static float GetHeatLevel(Aircraft ac)
        {
            try
            {
                if (_irSourcesField == null)
                    _irSourcesField = typeof(Unit).GetField("IRSources", BindingFlags.NonPublic | BindingFlags.Instance);
                if (!(_irSourcesField?.GetValue(ac) is System.Collections.IEnumerable sources)) return 0f;
                float max = 0f;
                foreach (object o in sources)
                {
                    if (o is IRSource src && !src.flare && src.intensity > max) max = src.intensity;
                }
                const float ceiling = 12f;
                return Mathf.Clamp01(max / ceiling);
            }
            catch { return 0f; }
        }

        // Matches the game's own IR gauge color exactly (StatusGauges.Gauge.Update): sample the same
        // GameAssets.i.redGreenGradient asset at the same (1 - value/maxValue) point, rather than
        // guessing our own green/amber/red stops.
        private static string GetHeatColor(float heat01)
        {
            try
            {
                GameAssets ga = GameAssets.i;
                if (ga == null || ga.redGreenGradient == null) return "#39ff14";
                return ColorHex(ga.redGreenGradient.Evaluate(1f - heat01));
            }
            catch { return "#39ff14"; }
        }

        // Aircraft.engineStates is a public List<IEngine> (every engine adds itself on
        // Awake/OnEnable), and IEngine.GetRPMRatio() is a public, already-normalized value — no
        // reflection needed, unlike GetHeatLevel above. Averages across engines rather than picking
        // one, same shape as the game's own RPMGauge cockpit widget. Empty list shouldn't happen in
        // practice (every propulsion type implements IEngine), but guard it as 0 rather than divide
        // by zero.
        private static float GetRpmLevel(Aircraft ac)
        {
            if (ac.engineStates.Count == 0) return 0f;
            float sum = 0f;
            foreach (IEngine engine in ac.engineStates) sum += engine.GetRPMRatio();
            return Mathf.Clamp01(sum / ac.engineStates.Count);
        }

        // The active countermeasure index points into CountermeasureManager's private station
        // list, so we reflect into it (via the shared CmReflection) and check the active station's
        // type.
        private static byte GetSelectedCmCategory(Aircraft ac)
        {
            CountermeasureManager mgr = ac.countermeasureManager;
            if (mgr == null) return 0;

            try
            {
                System.Collections.IList? list = CmReflection.GetStations(mgr);
                if (list == null || list.Count == 0) return 0;

                int idx = mgr.activeIndex;
                if (idx < 0 || idx >= list.Count) return 0;

                object station = list[idx];
                if (station == null) return 0;

                Countermeasure? cm = CmReflection.GetFirstCountermeasure(station);
                if (cm == null) return 0;
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

            // Master Arm / combat mode have no game field to patch at spawn (unlike radar/engine,
            // handled by HarmonyPatches instead) — reset them here on every new aircraft.
            ImmersionState.EnsureSpawnDefaults(aircraft);

            // The game uses a floating-origin system: transform.position drifts back toward
            // zero as the world re-centers. The true world coordinate is pos - Datum.originPosition.
            Vector3 world   = aircraft.transform.position - Datum.originPosition;
            float   heading = aircraft.transform.eulerAngles.y;

            PowerSupply ps     = aircraft.GetPowerSupply();
            float       ewKJ   = ps != null ? ps.GetChargeKJ() : -1f;
            float       ewKJMax = ps != null ? GetEwMaxKJ(ps)  : -1f;

            string selWeapon = string.Empty;
            WeaponManager? wm = aircraft.weaponManager;
            WeaponInfo? selInfo = wm != null && wm.currentWeaponStation != null ? wm.currentWeaponStation.WeaponInfo : null;
            if (selInfo != null)
                selWeapon = !string.IsNullOrEmpty(selInfo.weaponName) ? selInfo.weaponName : selInfo.shortName;

            // Weapon soft-selections (WeaponSelectors) — what the fire keybinds would commit right
            // now; the WPN page outlines them. Reading them also keeps the follow-active tracking
            // fresh at snapshot rate.
            string softGun = WeaponSelectors.EffectiveGun(aircraft) ?? string.Empty;
            string softRel = WeaponSelectors.EffectiveRelease(aircraft) ?? string.Empty;

            byte cmCategory = GetSelectedCmCategory(aircraft);

            float throttleRaw = aircraft.GetInputs() != null ? aircraft.GetInputs().throttle : -1f;
            float heat01 = GetHeatLevel(aircraft);

            _assets.TryCaptureIcon(aircraft.definition);

            RefreshContactSnapshotIfNeeded(aircraft);
            bool playerJammed = GetJamState(aircraft, out uint playerJammedBy);

            bool rdrMetric = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric;
            float rdrLevelTime = Time.timeSinceLevelLoad;

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
                Pylons         = _pylons,
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
                Throttle       = throttleRaw,
                Heat           = heat01,
                HeatColor      = GetHeatColor(heat01),
                Rpm            = GetRpmLevel(aircraft),
                HasAfterburner = _hasAfterburner,
                AbStart        = _abStart,
                SelWeapon      = selWeapon,
                SoftGun        = softGun,
                SoftRel        = softRel,
                CmCategory     = cmCategory,
                TotalUnits     = _totalUnits,
                TotalAircraft  = _totalAircraft,
                MapValid       = _mapValid,
                MapW           = _mapW,
                MapH           = _mapH,
                MapReachW      = _mapReachW,
                MapReachH      = _mapReachH,
                GridOffsetX    = _gridOffsetX,
                GridOffsetY    = _gridOffsetY,
                Units          = _cachedUnits,
                PlayerId       = aircraft.persistentID.Id,
                PlayerJammed   = playerJammed,
                PlayerJammedBy = playerJammedBy,
                FocusedTargetId = TargetFocus.Id,
                ColFriendly    = _colFriendly,
                ColHostile     = _colHostile,
                ColNeutral     = _colNeutral,
                TgpActive      = _tgp.Active,
                TgpResolution   = RatesConfig.TgpResolutionName,
                TgpQuality      = RatesConfig.TgpLegacyQualityName,
                TgpMag          = _tgp.Overlay.Mag,
                TgpRangeM       = _tgp.Overlay.RangeM,
                TgpGrid         = _tgp.Overlay.Grid,
                TgpIR           = _tgp.Overlay.IR,
                TgpBearingDeg   = _tgp.Overlay.BearingDeg,
                TgpTargetCount  = _tgp.Overlay.TargetCount,
                TgpManualActive = TgpManualControl.ManualMode,
                TgpManualPointTrack = _tgp.Overlay.PointTrackActive,
                TgpElevationDeg = _tgp.Overlay.ElevationDeg,
                TgpClosureReading = _tgp.Overlay.ClosureReading,
                TgpType         = _tgp.Overlay.TargetType,
                TgpPilot        = _tgp.Overlay.Pilot,
                TgpStatus       = _tgp.Overlay.Status,
                TgpHasDetail    = _tgp.Overlay.HasDetail,
                TgpHeadingDeg   = _tgp.Overlay.HeadingDeg,
                TgpAltitudeM    = _tgp.Overlay.AltitudeM,
                TgpRelAltitudeM = _tgp.Overlay.RelAltitudeM,
                TgpSpeedMps     = _tgp.Overlay.SpeedMps,
                TgpRelSpeedMps  = _tgp.Overlay.RelSpeedMps,
                TgpBoxes        = _tgp.Overlay.Boxes,
                Parts          = BuildParts(aircraft),
                Failures       = BuildFailures(),
                Rwr            = BuildRwr(aircraft),
                Mw             = BuildMw(aircraft),
                RadarPresent   = _cachedRadarPresent,
                RadarRange     = _cachedRadarRange,
                RadarConeDeg   = _cachedRadarConeDeg,
                Rdr            = _cachedRdr,
                Hsd            = _cachedHsd,
                Pitbull        = _cachedPitbull,
                RdrMetric      = rdrMetric,
                RdrLevelTime   = rdrLevelTime,
                TgtPresent     = tgtOk,
                TgtLaser       = tgtOk && tgtSel != null && tgtSel.toggleLaser      != null && tgtSel.toggleLaser.status,
                TgtHud         = tgtOk && tgtSel != null && tgtSel.toggleFollowHUD  != null && tgtSel.toggleFollowHUD.status,
                TgtFaction     = tgtOk && tgtSel != null ? ReadToggles(tgtSel.toggleFactionItems)      : Array.Empty<TgtToggleInfo>(),
                TgtCategory    = tgtOk && tgtSel != null ? ReadToggles(tgtSel.toggleUnitTypesItems)    : Array.Empty<TgtToggleInfo>(),
                TgtVehicle     = tgtOk && tgtSel != null ? ReadToggles(tgtSel.toggleVehicleTypesItems) : Array.Empty<TgtToggleInfo>(),
                BdfPresent     = _bdf.Present,
                BdfFaction     = _bdf.Faction,
                BdfFunds       = _bdf.Funds,
                BdfScore       = _bdf.Score,
                BdfWarheads    = _bdf.Warheads,
                BdfShips       = _bdf.Ships,
                BdfVehicles    = _bdf.Vehicles,
                BdfBuildings   = _bdf.Buildings,
                BdfAircraft    = _bdf.Aircraft,
                PalPresent     = _pal.Present,
                PalFaction     = _pal.Faction,
                PalFunds       = _pal.Funds,
                PalScore       = _pal.Score,
                PalWarheads    = _pal.Warheads,
                PalShips       = _pal.Ships,
                PalVehicles    = _pal.Vehicles,
                PalBuildings   = _pal.Buildings,
                PalAircraft    = _pal.Aircraft,
                MisPresent     = _misPresent,
                MisDescription = _misDescription,
                MisTimeOfDay   = _misTimeOfDay,
                MisDuration    = _misDuration,
                MisScore       = _misScore,
                MisLevel       = _misLevel,
                ObjPresent     = _objPresent,
                Obj            = _obj,
                AkfAll             = _akfAll,
                AkfPlayer          = _akfPlayer,
                AkfKillsAircraft   = _akfKillsAircraft,
                AkfKillsShip       = _akfKillsShip,
                AkfKillsVehicle    = _akfKillsVehicle,
                AkfKillsBuilding   = _akfKillsBuilding,
                AkfRank            = _akfRank,
                AkfFundsGained     = _akfFundsGained,
                AkfFundsSpent      = _akfFundsSpent
            });
        }

        private void RefreshContactSnapshotIfNeeded(Aircraft aircraft)
        {
            if (_contactTimer < ContactInterval && ReferenceEquals(_contactAircraft, aircraft))
                return;

            _contactTimer = 0f;
            _contactAircraft = aircraft;
            _cachedUnits = BuildUnits(aircraft);
            _cachedRdr = BuildRdr(aircraft, out _cachedRadarPresent, out _cachedRadarRange, out _cachedRadarConeDeg);
            _cachedHsd = BuildHsd(aircraft);
            _cachedPitbull = BuildPitbull(aircraft);

            // Keeps TargetFocus honest against locks changing here rather than via a Next/Prev press
            // (issue #62) — see TargetFocus.Reconcile for the actual rules.
            List<Unit>? targets = aircraft?.weaponManager?.GetTargetList();
            TargetFocus.Reconcile(TargetIds(targets));
        }

        // Shared with Keybinds.cs's CycleTargetFocus (issue #62) — both need the same
        // weaponManager.GetTargetList() -> persistentID.Id conversion, one reconciling focus every
        // scan tick, the other cycling it on a Next/Previous press.
        internal static List<uint> TargetIds(List<Unit>? targets)
        {
            var ids = new List<uint>(targets?.Count ?? 0);
            if (targets == null) return ids;
            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null) ids.Add(targets[i].persistentID.Id);
            return ids;
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
            List<Missile>? known = mw != null ? mw.knownMissiles : null;
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
                CombatHUD? hud = SceneSingleton<CombatHUD>.i;
                ThrottleGauge? gauge = hud != null ? hud.GetComponentInChildren<ThrottleGauge>(true) : null;
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
                    object? last = regions.GetValue(regions.Length - 1);
                    FieldInfo? startField = last?.GetType().GetField("start", BindingFlags.Instance | BindingFlags.NonPublic);
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
                Unit? u = em.Unit;
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
                    float dist = Vector3.Distance(player.transform.position - Datum.originPosition, u.transform.position - Datum.originPosition);
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

        private readonly List<RdrContact> _rdrBuf = new List<RdrContact>(32);
        private readonly HashSet<Unit> _rdrSeenScratch = new HashSet<Unit>();
        private readonly List<HsdContact> _hsdBuf = new List<HsdContact>(64);

        // Reflection handle for Radar's private cone half-angle (degrees). Cached once — it's a
        // SerializeField baked per radar prefab, so it never changes at runtime.
        private static FieldInfo? _radarConeField;

        // The air contacts the player's OWN radar currently detects (docs/rdr-page.md). The game's
        // Radar already maintains this per scan as TargetDetector.detectedTargets (cleared + refilled
        // by its own RepeatSearch, which runs client-side for the local aircraft), applying real
        // range / cone / RCS / jamming — so we don't reimplement detection, just project the air
        // entries. Also surfaces the scope's range scale and cone half-angle. present=false when the
        // aircraft carries no radar, which drives the page's — not available — placeholder.
        private RdrContact[] BuildRdr(Aircraft player, out bool present, out float range, out float coneDeg)
        {
            present = false; range = 0f; coneDeg = 0f;
            Radar? radar = player.radar as Radar;
            if (radar == null) return Array.Empty<RdrContact>();

            present = true;
            range = radar.RadarParameters.maxRange;
            coneDeg = ReadRadarCone(radar);

            // Same target-set reference the TGT page / target.select drive — an RDR "lock" IS
            // membership here (reused, not a new mechanism — see docs/rdr-page.md).
            List<Unit>? targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;
            var playerHQ = player.NetworkHQ;

            _rdrBuf.Clear();
            _rdrSeenScratch.Clear();

            // Pass 1: the player's OWN radar detections (Radar=true). Datalink is checked
            // independently — my own detection typically also reaches the faction's shared tracking
            // almost immediately (Radar.DetectTarget → NetworkHQ.RpcUpdateTrackingInfo), so "both" is
            // the everyday case for anything actively painted, not a rare edge case.
            List<Unit> det = radar.detectedTargets;
            if (det != null)
            {
                for (int i = 0; i < det.Count; i++)
                {
                    Unit u = det[i];
                    if (u == null || u.disabled || !_rdrSeenScratch.Add(u)) continue;
                    UnitDefinition def = u.definition;
                    if (def == null || def.typeIdentity.air <= 0.5f) continue;   // aircraft only

                    bool dl = playerHQ != null && playerHQ.TryGetKnownPosition(u, out _);
                    GlobalPosition gp = u.GlobalPosition();
                    _rdrBuf.Add(new RdrContact
                    {
                        Id       = u.persistentID.Id,
                        X        = gp.x,
                        Z        = gp.z,
                        Alt      = gp.y,
                        Heading  = u.transform.eulerAngles.y,
                        Targeted = targets != null && targets.Contains(u),
                        Radar    = true,
                        Datalink = dl,
                        Name     = RwrLabel(u)
                    });
                }
            }

            // Pass 2: datalink-only air contacts — enemy aircraft the faction's shared tracking
            // knows about (same visibility gate BuildUnits uses for MAP/TGT) that the player's own
            // radar isn't currently painting. The B-scope's own range/cone culling (client-side)
            // handles anything outside the displayed window; no distance pre-filter needed here.
            if (playerHQ != null)
            {
                foreach (Unit u in _units)
                {
                    if (u == null || u.disabled || !_rdrSeenScratch.Add(u)) continue;
                    UnitDefinition def = u.definition;
                    if (def == null || def.typeIdentity.air <= 0.5f) continue;

                    var hq = u.NetworkHQ;
                    if (hq == null || hq == playerHQ) continue;   // enemy-only, like BuildUnits' faction==2
                    if (!playerHQ.TryGetKnownPosition(u, out GlobalPosition gp)) continue;

                    _rdrBuf.Add(new RdrContact
                    {
                        Id       = u.persistentID.Id,
                        X        = gp.x,
                        Z        = gp.z,
                        Alt      = gp.y,
                        Heading  = u.transform.eulerAngles.y,
                        Targeted = targets != null && targets.Contains(u),
                        Radar    = false,
                        Datalink = true,
                        Name     = RwrLabel(u)
                    });
                }
            }

            return _rdrBuf.Count == 0 ? Array.Empty<RdrContact>() : _rdrBuf.ToArray();
        }

        // HSD (docs/rdr-fcr-hsd.md): enemy aerial contacts known to the player's faction, without
        // applying the own-radar cone/range limits that shape the FCR B-scope.
        private HsdContact[] BuildHsd(Aircraft player)
        {
            var playerHQ = player.NetworkHQ;
            if (playerHQ == null) return Array.Empty<HsdContact>();

            List<Unit> targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;
            bool hasTargets = targets != null && targets.Count > 0;
            List<Unit> radarTargets = player.radar != null ? player.radar.detectedTargets : null;
            bool hasRadarTargets = radarTargets != null && radarTargets.Count > 0;

            _hsdBuf.Clear();
            foreach (Unit u in _units)
            {
                if (u == null || u.disabled || ReferenceEquals(u, player)) continue;

                UnitDefinition def = u.definition;
                if (def == null || def.typeIdentity.air <= 0.5f) continue;

                var hq = u.NetworkHQ;
                if (hq == null || hq == playerHQ) continue;
                bool radarDetected = hasRadarTargets && radarTargets.Contains(u);
                bool datalinkKnown = playerHQ.TryGetKnownPosition(u, out GlobalPosition gp);
                if (!datalinkKnown && !radarDetected) continue;
                if (!datalinkKnown) gp = u.GlobalPosition();

                // Same 20m trust-radius check BuildUnits uses for UnitInfo.Stale (docs/tgt-stale-lock.md)
                // — returns true immediately while a datalink track is fresh, so this only fires once
                // the position has actually drifted, regardless of whether own radar also sees it.
                bool stale = datalinkKnown && !playerHQ.IsTargetPositionAccurate(u, 20f);

                _hsdBuf.Add(new HsdContact
                {
                    Id       = u.persistentID.Id,
                    X        = gp.x,
                    Z        = gp.z,
                    Alt      = gp.y,
                    Heading  = u.transform.eulerAngles.y,
                    Targeted = hasTargets && targets.Contains(u),
                    Radar    = radarDetected,
                    Datalink = datalinkKnown,
                    Stale    = stale,
                    Name     = RwrLabel(u)
                });
            }
            return _hsdBuf.Count == 0 ? Array.Empty<HsdContact>() : _hsdBuf.ToArray();
        }

        // Player's own AA missiles that have gone pitbull (RDR page, issue #40): active-radar (ARH)
        // seeker, currently locked (SeekerMode.activeLock), launched by this aircraft. Missile is a
        // Unit subclass, so it's already in the _units scan — no separate enumeration needed.
        // "AA" reuses WeaponSelectors' maintained air-to-air missile list rather than re-deriving it.
        private PitbullContact[] BuildPitbull(Aircraft player)
        {
            _pitbullBuf.Clear();
            uint playerId = player.persistentID.Id;
            foreach (Unit u in _units)
            {
                if (!(u is Missile m) || m.disabled) continue;
                if (m.ownerID.Id != playerId) continue;
                if (m.seekerMode != Missile.SeekerMode.activeLock) continue;
                if (m.GetSeekerType() != "ARH") continue;
                WeaponInfo info = m.GetWeaponInfo();
                if (info == null || !WeaponSelectors.IsAirToAir(info)) continue;

                GlobalPosition gp = m.GlobalPosition();
                _pitbullBuf.Add(new PitbullContact
                {
                    Id       = m.persistentID.Id,
                    X        = gp.x,
                    Z        = gp.z,
                    Alt      = gp.y,
                    Heading  = m.transform.eulerAngles.y,
                    TargetId = m.targetID.Id
                });
            }
            return _pitbullBuf.Count == 0 ? Array.Empty<PitbullContact>() : _pitbullBuf.ToArray();
        }

        // Radar's antenna cone half-angle in degrees (private SerializeField). <= 0 means the radar
        // applies no cone limit; the page then falls back to a sensible fixed azimuth span.
        private static float ReadRadarCone(Radar radar)
        {
            if (_radarConeField == null)
                _radarConeField = typeof(Radar).GetField("radarCone", BindingFlags.NonPublic | BindingFlags.Instance);
            return _radarConeField?.GetValue(radar) is float f ? f : 0f;
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
            List<Unit>? targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;

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

                bool jammed = GetJamState(u, out uint jammedBy);

                // An enemy's position only ever reaches us via playerHQ.trackingDatabase (confirmed
                // above by TryGetKnownPosition) — Observed() is false when that entry is stale (no
                // friendly sensor has painted it in the last ~4s), i.e. a datalink-only relay rather
                // than something actively sensed right now. See docs/tgt-datalink-cancel.md.
                bool datalink = faction == 2 && !(playerHQ.GetTrackingData(u.persistentID)?.Observed() ?? false);

                // Stale: a Datalink-only relay whose position has drifted past the game's own
                // trust radius. IsTargetPositionAccurate returns true immediately while fresh (short-
                // circuits the same Observed() check above), so this can only fire once datalink is
                // already true — same 20m threshold TargetScreenUI uses for the TGP's "?" box.
                // See docs/tgt-stale-lock.md.
                bool stale = datalink && !playerHQ.IsTargetPositionAccurate(u, 20f);

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
                    Targeted = targets != null && targets.Contains(u),
                    Jammed   = jammed,
                    JammedBy = jammedBy,
                    Datalink = datalink,
                    Stale    = stale
                });
            }
            return _unitBuf.ToArray();
        }

        // Is this unit's radar currently jammed and (if known) by whom — see _jammedBy declaration.
        private bool GetJamState(Unit u, out uint jammedBy)
        {
            jammedBy = 0;
            if (!(u.radar is Radar radar) || !radar.IsJammed()) return false;
            if (_jammedBy.TryGetValue(u, out Unit source) && source != null && !source.disabled)
                jammedBy = source.persistentID.Id;
            return true;
        }

        private void OnDestroy()
        {
            _tgp.Shutdown();
            if (_rwrSubscribed != null) { _rwrSubscribed.onRadarWarning -= OnRadarWarning; _rwrSubscribed = null; }
            if (AkfTracker.Active == _akf) AkfTracker.Active = null;
        }
    }
}
