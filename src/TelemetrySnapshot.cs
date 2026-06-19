namespace NOTelemetryReader
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
