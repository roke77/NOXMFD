namespace NOXMFD
{
    // Resolves a locked-target persistentID (almost always TargetFocus.Id) to a live, still-relevant
    // Unit — shared by every consumer that reads the focused target's live state
    // (TargetTtiEstimator.ComputeTti, WeaponSelectors.FireSingleAtFocused, HudFocusMark), each of
    // which independently grew the same "id 0 means nothing, and a resolved Unit might already be
    // gone/disabled" check before this was pulled out. Same shape as CmReflection.cs: the one place
    // this answer gets computed, not a new abstraction over what any single caller does with it.
    internal static class TargetUnitLookup
    {
        internal static bool TryResolve(uint id, out Unit unit)
        {
            if (id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out unit) &&
                unit != null && !unit.disabled)
                return true;
            unit = null!;
            return false;
        }
    }
}
