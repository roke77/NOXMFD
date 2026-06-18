namespace NOTelemetryReader
{
    internal struct TelemetrySnapshot
    {
        public bool   Valid;
        public float  Time;
        public string PlaneName;

        // True world position (floating-origin corrected): pos - Datum.originPosition.
        public float  WorldX, WorldY, WorldZ;

        // Compass heading in degrees (0 = north / +Z, 90 = east / +X).
        public float  Heading;

        public float  TAS;
        public float  AGL;
        public bool   GearDown;
        public int    TotalUnits;
        public int    TotalAircraft;

        // Map metadata — constant for a given map, lets the client place the plane
        // directly without calibration and reproduce the in-game grid label (e.g. "Hc87").
        public bool   MapValid;
        public float  MapW, MapH;               // world units spanned by the map (centered on origin)
        public int    GridOffsetX, GridOffsetY; // grid label offsets from MapSettings
    }
}
