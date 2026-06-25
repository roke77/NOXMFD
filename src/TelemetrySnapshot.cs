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

        // True world position (floating-origin corrected): pos - Datum.originPosition.
        public float  WorldX, WorldY, WorldZ;

        // Compass heading in degrees (0 = north / +Z, 90 = east / +X).
        public float  Heading;

        public float  TAS;
        public float  AGL;
        public bool   GearDown;

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

        // Currently selected systems (for highlighting).
        public string SelWeapon;   // weaponName of the selected weapon
        public byte   CmCategory;  // selected countermeasure: 0 none, 1 flares, 2 EW, 3 chaff

        // Aircraft map-icon hints (the icon PNG itself is served separately at /icon).
        public bool   IconOrient;   // whether the icon rotates with heading
        public float  IconScale;    // relative size multiplier (default 1)
        public int    TotalUnits;
        public int    TotalAircraft;

        // Map metadata — constant for a given map, lets the client place the plane
        // directly without calibration and reproduce the in-game grid label (e.g. "Hc87").
        public bool   MapValid;
        public float  MapW, MapH;               // world units spanned by the map (centered on origin)
        public int    GridOffsetX, GridOffsetY; // grid label offsets from MapSettings

        // Other units the player's faction can see (fog-of-war respected).
        public UnitInfo[] Units;

        // The game's own HUD faction colors (hex), so the web map matches the game.
        public string ColFriendly;
        public string ColHostile;
        public string ColNeutral;

        // True while the targeting-pod feed is producing frames (a target is locked, or the
        // game's 3-second post-loss hold is still running). Drives the MFD's NO TARGET fallback.
        public bool   TgpActive;

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

    // One incoming missile on the RWR. Serialized terse as {x,z,st,nb}.
    internal struct MwContact
    {
        public float  X, Z;    // missile world position (GlobalPosition, same space as UnitInfo)
        public string Seeker;  // seeker type code (e.g. "ARH", "IR") — short, used as the label
        public float  Notch;   // beam-notch heading (world deg) for radar seekers; -1 = none
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

    // One tracked unit, in the same global coordinate space as WorldX/WorldZ.
    internal struct UnitInfo
    {
        public string Type;     // unitName — keys the /icon endpoint
        public float  X, Z;     // known world position (true for friendlies, last-seen for enemies)
        public float  Heading;  // degrees
        public byte   Faction;  // 0 = neutral/unknown, 1 = friendly, 2 = enemy
        public bool   Orient;   // icon rotates with heading
        public float  Scale;    // icon size multiplier
        public bool   Targeted; // true when this unit is one of the player's current targets
    }
}
