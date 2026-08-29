namespace NOXMFD
{
    internal struct TelemetrySnapshot
    {
        public bool   Valid;
        public float  Time;
        public string PlaneName;
        public string MissionName;
        public string MapName;
        public LoadoutEntry[] Loadout;   // weapon loadout, aggregated by type
        public PylonMarker[]  Pylons;    // AFM frontal-silhouette hardpoint marker colors (live)

        // True world position (floating-origin corrected): pos - Datum.originPosition.
        public float  WorldX, WorldY, WorldZ;

        // Compass heading in degrees (0 = north / +Z, 90 = east / +X).
        public float  Heading;

        public float  TAS;
        public float  AGL;
        public bool   GearDown;
        public bool   RadarOn;      // radar actively emitting (Unit.HasRadarEmission)
        public bool   GunsLinked;   // multiple guns linked to fire together (WeaponManager.gunsLinked)
        public bool   Ignition;     // engine ignition on (Aircraft.Ignition)
        public bool   FlightAssist; // flight assist engaged (false if the airframe has none)
        public bool   TurretAuto;   // turrets "engage at will" (false if the airframe has no turrets)
        public bool   NightVision;  // NVG active (HUD-wide, NightVision.i)
        public bool   NavLightsOn;  // nav lights on (Aircraft.navLights.isOn)

        // Countermeasures (-1 = the aircraft has no such system).
        public int    Flares;     // IR flare rounds remaining
        public int    FlaresMax;  // IR flare capacity
        public float  EwKJ;       // EW capacitor charge, kilojoules
        public float  EwKJMax;    // EW capacitor capacity, kilojoules

        // Avionics gauges (-1 = unavailable / no aircraft yet). Both are normalized 0..1.
        // Fuel comes from Aircraft.GetFuelLevel() (aggregated across all tanks). Throttle
        // is the pilot's commanded throttle from Aircraft.GetInputs().throttle — all
        // engines consume the same commanded value, so no per-engine averaging needed.
        public float  Fuel;       // 0..1 fuel fraction across all tanks
        public float  Throttle;   // 0..1 commanded throttle

        // Airframe IR/heat signature, approximated as the strongest live non-flare IRSource on the
        // aircraft (TelemetryReader.GetHeatLevel), normalized against a ceiling of 12 — matched to the
        // game's own cockpit IR gauge (StatusGauges.Gauge), which clamps intensity to the same [0,12]
        // before dividing by its maxValue. -1 = no aircraft.
        public float  Heat;       // ~0..1 relative heat signature
        // Hex color the game's own IR gauge would show at this Heat (TelemetryReader.GetHeatColor,
        // sampled straight from GameAssets.i.redGreenGradient) — the page paints the fill arc with
        // this directly instead of re-deriving a gradient client-side.
        public string? HeatColor;

        // Engine RPM, averaged across every IEngine on the aircraft (TelemetryReader.GetRpmLevel).
        // Unlike Heat, this needs no reflection or fixed-ceiling guess: IEngine.GetRPMRatio() is a
        // public, already-normalized (0..1-ish) interface member every propulsion type implements
        // (TurbineEngine, Turbojet/Turbofan, DuctedFan, ConstantSpeedProp, PropFan, RotorShaft) —
        // the same value the game's own RPMGauge/PropGauge/EngineTelemetry cockpit widgets read.
        // -1 = no aircraft or no engines.
        public float  Rpm;        // ~0..1 average RPM ratio across engineStates

        // Afterburner gauge shape (static per airframe; read once from the game's own ThrottleGauge).
        // HasAfterburner planes split the 0..1 throttle axis at AbStart: below = MIL, above = reheat.
        // Compass / helicopters report false → the AVN page keeps the plain 0-100% bar.
        public bool   HasAfterburner;  // airframe has a reheat zone
        public float  AbStart;         // throttle fraction where MIL ends / afterburner begins (1 = none)

        // Currently selected systems (for highlighting).
        public string SelWeapon;   // weaponName of the selected weapon
        public string SoftGun;     // gun soft-selection (WeaponSelectors) — WPN page outline
        public string SoftRel;     // missile/bomb soft-selection — WPN page outline
        public byte   CmCategory;  // selected countermeasure: 0 none, 1 flares, 2 EW, 3 chaff

        // Aircraft map-icon hints (the icon PNG itself is served separately at /icon).
        public bool   IconOrient;   // whether the icon rotates with heading
        public float  IconScale;    // relative size multiplier (default 1)
        public int    TotalUnits;
        public int    TotalAircraft;

        // Map metadata — constant for a given map, lets the client place the plane
        // directly without calibration and reproduce the in-game grid label (e.g. "Hc87").
        public bool   MapValid;
        public float  MapW, MapH;               // world units spanned by the MAP IMAGE (centered on origin)
        public int    GridOffsetX, GridOffsetY; // grid label offsets from MapSettings
        // The mission's real reachable extent (MapSettings.GridSizeX/Y * 10000, its 10km grid-letter
        // bands) — issue #65: terrain/spawns can sit past MapW/H, the smaller square the minimap
        // IMAGE itself was captured at. Falls back to MapW/H when GridSizeX/Y isn't set, so callers
        // needn't special-case a mission without it. Used for the pan/cursor edge margin and the
        // MAP page's own coordinate-grid overlay — never for placing the map image itself, which is
        // still sized to MapW/H, its true pixel coverage.
        public float  MapReachW, MapReachH;

        // Other units the player's faction can see (fog-of-war respected).
        public UnitInfo[] Units;

        // Player's own radar-jammed state (replicates the game's MAP jam marker for the player's
        // own icon, which isn't part of Units — see docs comment on UnitInfo.Jammed).
        public uint PlayerId;         // Unit.persistentID.Id — lets a UnitInfo.JammedBy reference resolve to "the player"
        public bool PlayerJammed;
        public uint PlayerJammedBy;   // persistentID.Id of the jamming unit; 0 = jammed but source unknown/not tracked

        // The single locked target Next/Previous currently focuses, shared across TGT/FCR/HSD
        // (issue #62, docs/tgt-cycle-focus.md — see TargetFocus.cs). persistentID.Id, 0 = none.
        // Also TGT's only row-highlight source; keeping one tracker prevents page-local selection
        // state from drifting when locks change outside a Next/Previous press.
        public uint FocusedTargetId;

        // weaponManager.GetTargetList()'s own order (persistentID.Id) — the exact order
        // TargetFocus's Cycle/Reconcile step through (TargetFocus.cs). TGT sorts its own
        // selected-target list to match, so Next/Previous visibly walks the table in the same
        // order it steps focus in, rather than the list's own (unrelated) contact-scan order.
        public uint[] LockedTargetIds;

        // Time-to-impact per entry in LockedTargetIds (same index, same length) — the smallest TTI
        // among the player's own in-flight guided weapons tracking that lock, or -1 when nothing of
        // the player's is tracking it. Lets TGT show a TTI next to any locked row, not just the
        // focused one the native HUD cue already does.
        public float[] LockedTargetTti;

        // The game's own HUD faction colors (hex), so the web map matches the game.
        public string ColFriendly;
        public string ColHostile;
        public string ColNeutral;

        // True while the targeting-pod feed is producing frames (a target is locked, or the
        // game's 3-second post-loss hold is still running). Drives the MFD's NO TARGET fallback.
        public bool   TgpActive;

        // Resolution is the current native|mid|high contract. Quality is the temporary native|hq
        // compatibility alias older clients use to distinguish baked and client-side overlays.
        public string TgpResolution;
        public string TgpQuality;

        // The stat overlay the game bakes into the cockpit TGP screen via a second stacked camera
        // (TargetScreenUI) — Native resolution gets this for free in the video pixels; the MID/HIGH
        // mirror camera has no such second camera, so the page draws it itself from these fields instead
        // (docs/tgp-high-quality-mode.md's open question, resolved this way). Populated by
        // TgpFeed each capture tick, mirroring TargetScreenUI.UpdateTargetInfo's own field set.
        //
        public float  TgpMag;          // targetCam.GetMag()
        public float  TgpRangeM;       // targetCam.GetDist(), meters — client formats to the player's units
        public string TgpGrid;         // targetCam.GetGrid()
        public bool   TgpIR;           // targetCam.UsingIR()
        public float  TgpBearingDeg;   // active cam mount's local Y euler
        public int    TgpTargetCount;  // 0 when TgpActive is true but the list emptied mid-frame

        // True while TgpManualControl.ManualMode is on (docs/tgp-manual-control.md) — drives the
        // TGP page's MANUAL/AUTO status indicator, and (with no real lock) tells the client the
        // rest of this block's fields carry manual-mode data (TgpOverlay.PopulateManual) rather
        // than being empty just because TgpTargetCount is 0.
        public bool   TgpManualActive;
        // True while Point Track is locked (docs/tgp-manual-control.md) — manual mode only;
        // distinguishes the "POINT TRACK" vs "MANUAL" label client-side.
        public bool   TgpManualPointTrack;
        // Aim elevation, degrees, aircraft-relative (0 = nose) — manual mode only; a locked target
        // never needed this (bearing alone was enough to point back at it).
        public float  TgpElevationDeg;
        // Closure rate toward the manual look point, pre-formatted via UnitConverter.SpeedReading
        // (km/h or kt, matching the player's unit setting) — sent as a ready string, not a raw m/s
        // number, so the client doesn't need to duplicate that unit-conversion logic itself.
        public string TgpClosureReading;
        public string TgpType;         // unitName, or "N targets" when TgpTargetCount > 1
        public string TgpPilot;        // empty when not a player-flown aircraft
        public string TgpStatus;       // "friendly" | "jammed" | "lased" | "outdated" | "normal"
        public bool   TgpHasDetail;    // false ⇒ client shows "-" for hdg/alt/relAlt/spd/relSpd,
                                        // matching TargetScreenUI's own >1-target / stale-position fallback
        public float  TgpHeadingDeg;
        public float  TgpAltitudeM;
        public float  TgpRelAltitudeM;
        public float  TgpSpeedMps;
        public float  TgpRelSpeedMps;

        // Screen-projected lock box, one per locked target (TargetScreenUI's own targetBoxes
        // list). X/Y are the feed camera's WorldToViewportPoint output — see TgpBoxInfo's own doc
        // comment below for why that and not WorldToScreenPoint.
        public TgpBoxInfo[] TgpBoxes;

        // Per-part HP for the AVN page. Built from Aircraft.partLookup, one entry per
        // damageable UnitPart. Names match the silhouette layout served at /airframe-layout.
        public PartHp[] Parts;

        // Names of currently-active failure indicators (e.g. "L ENG FIRE", "FUEL LOW").
        // Polled from the cockpit StatusDisplay's failureIndicators list each tick; the
        // game sets the matching GameObject active when an IReportDamage event fires.
        public string[] Failures;

        // Radar emitters currently painting the player (drives the RWR page). Aggregated from
        // Aircraft.onRadarWarning pings with per-tier decay; positions are in the same world
        // space as Units. Empty when nothing is painting the player.
        public RwrContact[] Rwr;

        // Incoming missiles currently warning the player (drives the RWR's missile-launch
        // indicator). Polled from the aircraft's MissileWarning.knownMissiles each tick;
        // positions are in the same world space as Units. Empty when nothing is inbound.
        public MwContact[] Mw;

        // RDR page (docs/rdr-page.md): the air contacts the player's OWN radar currently detects
        // (Radar.detectedTargets, air-only). RadarPresent=false when the aircraft has no radar —
        // the page then shows a — not available — placeholder. RadarRange is the scope's fixed
        // range scale (RadarParameters.maxRange); RadarConeDeg is the antenna cone half-angle in
        // degrees the B-scope spreads contacts across (0 = no cone limit). Rdr is empty when the
        // radar sees no aircraft.
        public bool        RadarPresent;
        public float       RadarRange;
        public float       RadarConeDeg;
        public RdrContact[] Rdr;

        // HSD page (docs/rdr-fcr-hsd.md): 360-degree aerial datalink picture around the player.
        // Contacts come from the player's faction-known positions, not from the own-radar cone.
        public HsdContact[] Hsd;

        // RDR page pitbull missiles (issue #40): the player's own AA missiles whose active-radar
        // seeker has gone lock — independent of RadarPresent, since it's the missile's own radar,
        // not the aircraft's. Empty when the player has none in flight.
        public PitbullContact[] Pitbull;

        // The player's Imperial/Metric display preference (PlayerSettings.unitSystem), mirroring
        // the game's own UnitConverter — RDR's range/altitude readouts follow it (nm/ft vs km/m)
        // the same way the native HUD does.
        public bool RdrMetric;

        // Time.timeSinceLevelLoad — the exact clock the game's own internal MFD radar sweep
        // (TacScreen.ScanRadar: scanLine rotation = sin(t * 0.5 * PI) * 26deg, a 4s period) is
        // driven by. RDR's sweep caret phase-locks to it on its first "radar just turned on" tick,
        // so both sweeps read the same moment in their cycle even though ours is a linear B-scope
        // caret and the native one is a rotating PPI needle. NOT Time.time (s.Time) — that runs
        // continuously across scene loads, while this resets with the mission, matching the
        // native sweep's own reference frame.
        public float RdrLevelTime;

        // TGT filter panel (docs/tgt-page.md), mirrored from the game's TargetListSelector so the
        // web TGT page renders the real toggle states. TgtPresent=false when the singleton isn't up
        // (the page then shows an unavailable state). The three arrays are ordered as the game holds
        // them — the same order the tgt.set/tgt.only commands index by.
        public bool            TgtPresent;
        public bool            TgtLaser;
        public bool            TgtHud;
        public TgtToggleInfo[] TgtFaction;   // FRIENDLY, ENEMY
        public TgtToggleInfo[] TgtCategory;  // AIR, MSL, GND, BLD, SHP
        public TgtToggleInfo[] TgtVehicle;   // TRUCK … RDR (dynamic; names double as /icon keys)

        // BDF faction-forces panel (docs/bdf-page.md), mirroring the game's InfoPanel_Faction
        // (Forces display only) — always BOSCALI, a fixed identity, not "whichever faction the
        // player is on". BdfPresent=false when Boscali has no FactionHQ yet (e.g. between
        // missions) — the page then shows an unavailable state. Section totals aren't sent
        // separately; they're just the sum of each array (every type is enumerated, so no
        // duplicate source of truth). Arrays are in the game's own enum order.
        public bool           BdfPresent;
        public string         BdfFaction;   // faction display name, e.g. "BOSCALI"
        public float          BdfFunds;     // millions (UnitConverter.ValueReading scale)
        public float          BdfScore;
        public int            BdfWarheads;
        public BdfCountInfo[] BdfShips;     // CV, LHA, LFD, DDG, FFG, FFL, LC
        public BdfCountInfo[] BdfVehicles;  // TRUCK, UGV, LCV, AFV, MBT, ART, AAA, IR_SAM, R_SAM, RDR
        public BdfCountInfo[] BdfBuildings; // CIV, FAC, RDR, DEP, HGR, DEF, AMMO
        public BdfCountInfo[] BdfAircraft;  // one per AircraftDefinition; Name doubles as the /icon key

        // PAL — the same faction-forces panel, always PRIMEVA instead of BOSCALI (docs/bdf-page.md).
        // Shares BdfCountInfo's shape; PalPresent=false when Primeva has no FactionHQ yet (mirrors
        // BdfPresent's guard).
        public bool           PalPresent;
        public string         PalFaction;
        public float          PalFunds;
        public float          PalScore;
        public int            PalWarheads;
        public BdfCountInfo[] PalShips;
        public BdfCountInfo[] PalVehicles;
        public BdfCountInfo[] PalBuildings;
        public BdfCountInfo[] PalAircraft;

        // MIS — mission info panel (docs/md-pages.md), mirroring the game's ObjectiveInfoList
        // "MIS" tab (ShowMissionInfo). MisPresent=false when no mission is running (or the mod
        // can't read one — e.g. multiplayer, where the game shows the Steam lobby name instead of
        // MissionManager.CurrentMission and NOXMFD doesn't plumb that). Name reuses MissionName.
        public bool   MisPresent;
        public string MisDescription;   // MissionSettings.description — full multi-paragraph text
        public float  MisTimeOfDay;     // LevelInfo.timeOfDay, 0..24 — the in-mission clock ("Time")
        public float  MisDuration;      // MissionManager.MissionTime, seconds ("Duration")
        public float  MisScore;         // MissionManager.currentEscalation ("Score")
        public byte   MisLevel;         // 0 Conventional, 1 Tactical, 2 Strategic (vs. the two thresholds)

        // OBJ — active-objectives list (docs/md-pages.md), mirroring the same component's
        // ShowObjectiveList tab. ObjPresent=false when the player's faction has no HQ yet (the page
        // then shows an unavailable state); Obj is empty when the HQ has no active objectives.
        public bool       ObjPresent;
        public ObjEntry[] Obj;

        // AKF advanced kill feed (docs/akf-page.md). Always present while a mission runs — unlike
        // MIS/OBJ there's no "faction has no HQ yet" gate, an empty session just reads as all-zero.
        // Kills are scoped to the LOCAL PLAYER's own kills only; All is everyone's, matching the
        // game's own kill-feed ticker. Rank is Player.PlayerRank, the same integer the game's own
        // KillDisplay shows ("RANK n") — not session-scoped, it's the player's persistent rank.
        public AkfKillEntry[] AkfAll;
        public AkfKillEntry[] AkfPlayer;
        public int   AkfKillsAircraft;
        public int   AkfKillsShip;
        public int   AkfKillsVehicle;
        public int   AkfKillsBuilding;
        public int   AkfRank;
        public float AkfFundsGained;
        public float AkfFundsSpent;
    }

    // One active mission objective. Serialized terse as {n,s,p,pos}. Status mirrors
    // NuclearOption.SavedMission.ObjectiveStatus (0 NotStarted, 1 Running, 2 Complete).
    internal struct ObjEntry
    {
        public string Name;     // SavedObjective.DisplayName
        public byte   Status;
        public float  Percent;      // CompletePercent, 0..1
        public ObjPosition[] Positions;   // sub-rows (ObjectiveInfoList_Item) — empty if none/hidden
    }

    // One position sub-row under an objective (ObjectiveInfoList_Item — the "DestroyUnits / Lb105 /
    // 18km" rows). Name is the objective TYPE, not the objective's own display name (matches the
    // game — SavedObjective.ObjectiveTypeEnum.ToString(), e.g. "DestroyUnits"). X/Z are true world coords (same space as
    // WorldX/WorldZ/UnitInfo); the client derives the grid label and live distance itself, the same
    // way it already does for the player's own position on MAP — the game recomputes distance from
    // the player's aircraft at render time too, not from MissionPosition's own (irrelevant) Distance
    // field. Serialized terse as {n,x,z}.
    internal struct ObjPosition
    {
        public string Name;
        public float  X, Z;
    }

    // One kill-feed line, shared by AKF's ALL and PLAYER arrays (docs/akf-page.md). Attacker is null
    // when the game reports no killer (e.g. a crash — KillType.GetVerb(hasKiller:false)); Weapon is
    // null when the best-effort attribution (DamageEffects.BlastFrag hook) has nothing recent for
    // that attacker. Hostile flags are relative to the LOCAL player's faction, not a fixed identity.
    // PlayerIsVictim is only ever set on PLAYER-feed entries: true for an "incoming" line (the player
    // was killed, or the player's own fired munition was intercepted) where Attacker is NOT the
    // player, as opposed to the normal PLAYER-feed line where the player IS the (omitted) attacker.
    internal struct AkfKillEntry
    {
        public string? Attacker;
        public bool    AttackerHostile;
        public string  Victim;
        public bool    VictimHostile;
        public string  Verb;
        public string? Weapon;
        public bool    PlayerIsVictim;
    }

    // One TGT filter toggle: its label (the canonical typeName for the vehicle row — doubles as the
    // icon key) and current on/off state. Serialized terse as {n,on}.
    internal struct TgtToggleInfo
    {
        public string Name;
        public bool   On;
    }

    // One BDF forces-breakdown row: a type label (or unitName, for aircraft) and its current count.
    // Serialized terse as {n,c}.
    internal struct BdfCountInfo
    {
        public string Name;
        public int    Count;
    }

    // One radar emitter on the RWR scope. Serialized terse as {x,z,tr,pw,n,k}.
    internal struct RwrContact
    {
        public float  X, Z;    // emitter world position (GlobalPosition, same space as UnitInfo)
        public byte   Tier;    // 0 search, 1 track (detected), 2 lock (we are its target)
        public float  Power;   // 0..1 closeness (1 = closest); -> radius from scope centre
        public float  Fresh;   // 0..1 ping freshness (1 = just pinged, fades to 0 over the tier TTL)
        public string Name;    // display label
        public byte   Kind;    // 0 unknown, 1 ground-SAM, 2 air (from typeIdentity)
    }

    // One air contact on the RDR B-scope — either detected by the player's own onboard radar, known
    // via the faction's shared datalink picture, or both at once (docs/rdr-page.md). Position is in
    // the same world space as UnitInfo so the client can derive bearing/range from the player's own
    // position. Serialized terse as {id,x,z,alt,hdg,tg,rd,dl,n}.
    internal struct RdrContact
    {
        public uint   Id;       // Unit.persistentID.Id — correlates with UnitInfo / the target set
        public float  X, Z;     // world position (GlobalPosition, same space as UnitInfo)
        public float  Alt;      // altitude (GlobalPosition.y) — the readout's ALT
        public float  Heading;  // travel heading, degrees — drives the velocity-vector stub
        public bool   Targeted; // in the player's weapon target list — drives the amber lock symbol
        public bool   Radar;    // detected by the player's OWN radar (Radar.detectedTargets)
        public bool   Datalink; // known via the faction's shared tracking (playerHQ.TryGetKnownPosition)
        public string Name;     // display label (unitName, bogey fallback)
    }

    // One aerial contact on the HSD plan view. Serialized terse as {id,x,z,alt,hdg,tg,rd,dl,n}.
    internal struct HsdContact
    {
        public uint   Id;       // Unit.persistentID.Id — correlates with UnitInfo / the target set
        public float  X, Z;     // known world position (GlobalPosition, same space as UnitInfo)
        public float  Alt;      // altitude (GlobalPosition.y)
        public float  Heading;  // travel heading, degrees
        public bool   Targeted; // in the player's weapon target list — amber lock ring, plus a
                                 // fully amber symbol for whichever locked contact the page is
                                 // currently reading out (man/rdr.md's "focused lock")
        public bool   Radar;    // detected by the player's OWN radar (red HSD symbol)
        public bool   Datalink; // known via the faction's shared tracking (purple HSD symbol)
        public bool   Stale;    // datalink-known position has drifted past the trust radius (white
                                 // HSD symbol) — same IsTargetPositionAccurate check as UnitInfo.Stale
                                 // (docs/tgt-stale-lock.md), independent of Targeted/focused-lock color
        public string Name;     // display label
    }

    // One incoming missile on the RWR. Serialized terse as {x,z,st,nb,h}.
    internal struct MwContact
    {
        public float  X, Z;    // missile world position (GlobalPosition, same space as UnitInfo)
        public string Seeker;  // seeker type code (e.g. "ARH", "IR") — short, used as the label
        public float  Notch;   // beam-notch heading (world deg) for radar seekers; -1 = none
        public float  Heading; // missile travel heading (world deg) — orients the map icon
    }

    // One of the player's own AA missiles that has gone pitbull — active-radar seeker locked, no
    // longer riding the launching aircraft's radar (RDR page, issue #40).
    internal struct PitbullContact
    {
        public uint  Id;       // missile's own persistentID.Id
        public float X, Z;     // world position (GlobalPosition, same space as UnitInfo)
        public float Alt;      // altitude (GlobalPosition.y)
        public float Heading;  // travel heading, degrees — orients the RDR triangle icon
        public uint  TargetId; // designated target's persistentID.Id, 0 if none/unresolved
    }

    // One UnitPart's live damage state.
    internal struct PartHp
    {
        public string Name;     // UnitPart.gameObject.name (matches the airframe-layout key)
        public float  Hp;       // 0..100
        public bool   Detached; // true once the part has been blown off the aircraft
    }

    // One weapon type in the loadout. The icon PNG is served separately at /weapon?name=.
    internal struct LoadoutEntry
    {
        public string Name;
        public int    Ammo;       // rounds/missiles remaining (summed across stations)
        public int    FullAmmo;   // capacity (summed across stations)
    }

    // One hardpoint marker on the AFM frontal silhouette (WeaponPanel/frontProfile/hardpoint_*).
    // State mirrors the LIVE state the game's own cockpit panel already shows for that station —
    // "armed" (has ammo), "exhausted" (mounted, no ammo left), or "empty" (nothing mounted) — see
    // AssetCapture.ReadFrontalMarkerStates. The AFM page renders this in its own theme colors, not
    // the game's raw (semi-transparent) hue.
    internal struct PylonMarker
    {
        public string Name;
        public string State;
    }

    // A locked target's screen-space lock box for the TGP overlay. X/Y are the feed camera's own
    // WorldToViewportPoint output (0-1, origin bottom-left, y-up) — NOT WorldToScreenPoint, which
    // returns render-texture pixels instead of a resolution-independent fraction the client can
    // position a CSS box with directly. The client also flips Y (CSS is top-down) and hides the
    // box outside [0,1] or when Visible is false (behind the camera).
    internal struct TgpBoxInfo
    {
        public float  X, Y;
        public bool   Visible;
        public string Status;   // "target" | "friendly" | "jammed" | "lased" | "outdated"
    }

    // One tracked unit, in the same global coordinate space as WorldX/WorldZ.
    internal struct UnitInfo
    {
        public uint   Id;       // Unit.persistentID.Id — stable network identity; lets the client
                                //   POST a click back to /select so the game can target this unit.
        public string Type;     // unitName — keys the /icon endpoint
        public float  X, Z;     // known world position (true for friendlies, last-seen for enemies)
        public float  Heading;  // degrees
        public byte   Faction;  // 0 = neutral/unknown, 1 = friendly, 2 = enemy
        public bool   Orient;   // icon rotates with heading
        public float  Scale;    // icon size multiplier
        public bool   Targeted; // true when this unit is one of the player's current targets

        // Radar-jammed state, replicating the game's MAP JammedMarker (yellow line + icon to the
        // jamming unit). JammedBy is 0 when jammed but the jamming unit isn't known/tracked (the
        // client then shows the jam icon with no line, matching the game's own fallback).
        public bool   Jammed;
        public uint   JammedBy;  // persistentID.Id of the jamming unit

        // True for an enemy contact known only via the faction's shared tracking database (stale —
        // no friendly sensor has painted it in the last ~4s; docs/tgt-datalink-cancel.md), as opposed
        // to one your own faction is actively sensing right now. Always false for friendly/neutral.
        public bool   Datalink;

        // True once a Datalink-only contact's relayed position can no longer be trusted — the same
        // check the game's own TGP uses to swap a locked target's box for the "?" (outdated) sprite
        // (FactionHQ.IsTargetPositionAccurate; docs/tgt-stale-lock.md). Implies Datalink; always false
        // for friendly/neutral or anything still fresh.
        public bool   Stale;
    }
}
