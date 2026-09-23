namespace NOXMFD
{
    // Guards target.select against id enumeration (docs/jamming-contact-telemetry-hardening.md,
    // F3): persistentIDs are a plain sequential counter (UnitRegistry.cs), so without this check a
    // caller can select any live unit regardless of fog of war. A unit is selectable only through a
    // channel the player already has — faction-known (friendly, or a faction-tracked enemy) or
    // currently painted by the player's own radar, the same two gates BuildUnits/BuildRdr already
    // use for what the MAP/FCR pages disclose.
    //
    // Also shared with BuildRdr's datalink-only pass (F1): while the native picture is jammed,
    // Radar.IsJammed() is a separate mechanic, so an own-radar detection stays eligible even though
    // a plain faction-known (datalink-only) track is not — unlike BuildUnits/BuildHsd, which omit
    // every enemy during picture jamming with no own-radar exception at all.
    internal static class TargetSelectionPolicy
    {
        internal static bool IsSelectable(bool factionKnown, bool ownRadarDetected, bool pictureJamActive)
            => ownRadarDetected || (factionKnown && !pictureJamActive);

        // F6 (docs/jamming-contact-telemetry-hardening.md): is this id one of the units already
        // disclosed to the player this frame? Used to avoid leaking a hidden jammer's identity via
        // PlayerJammedBy. UnitInfo is a plain struct (no Unity types), so this stays pure.
        internal static bool IsDisclosed(UnitInfo[] units, uint id)
        {
            for (int i = 0; i < units.Length; i++)
                if (units[i].Id == id) return true;
            return false;
        }
    }
}
