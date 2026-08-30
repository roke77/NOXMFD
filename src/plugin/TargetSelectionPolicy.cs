namespace NOXMFD
{
    // Guards target.select against id enumeration (docs/jamming-contact-telemetry-hardening.md,
    // F3): persistentIDs are a plain sequential counter (UnitRegistry.cs), so without this check a
    // caller can select any live unit regardless of fog of war. A unit is selectable only through a
    // channel the player already has — faction-known (friendly, or a faction-tracked enemy) or
    // currently painted by the player's own radar, the same two gates BuildUnits/BuildRdr already
    // use for what the MAP/FCR pages disclose.
    internal static class TargetSelectionPolicy
    {
        internal static bool IsSelectable(bool factionKnown, bool ownRadarDetected)
            => factionKnown || ownRadarDetected;
    }
}
