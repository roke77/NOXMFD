using System.Reflection;

namespace NOXMFD
{
    // Cached private access to MissileSeeker's own persistent target, for TargetTtiEstimator.
    //
    // Missile.targetID (the public field every seeker calls SetTarget on) reflects "does this
    // weapon's seeker have a live, currently-confirmed track right now" -- every seeker clears it on
    // any routine dropout (a radar notch, a bomb's own 0.25s visual recheck, chaff) and, for
    // radar-guided missiles, it stays unset for the entire midcourse/datalink phase before the
    // seeker's own terminal search ever goes active. That makes it a poor "who is this weapon
    // ultimately going for" signal -- see docs/hud-tti-estimate.md's in-game finding that almost no
    // shot ever shows a TTI.
    //
    // MissileSeeker (the common base class every seeker type -- ARH/SARH/IR/Laser/Optical --
    // inherits from, never redeclares its own copy) carries a `protected Unit targetUnit` set once
    // in Initialize() and left alone through the whole flight; it's only cleared when the seeker
    // genuinely gives up on the target entirely (e.g. its position estimate diverges past the
    // seeker's own search radius), not on a routine signal dropout. One cached FieldInfo on the base
    // class covers every seeker subclass, same shape as TgpManualTargetCamAccess.cs.
    internal static class MissileSeekerAccess
    {
        private static bool _reflectionTried;
        private static FieldInfo? _targetUnitField;

        private static bool Ensure()
        {
            if (_reflectionTried) return _targetUnitField != null;
            _reflectionTried = true;
            _targetUnitField = typeof(MissileSeeker).GetField("targetUnit", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_targetUnitField == null)
                Plugin.Log?.LogWarning("[NOXMFD] TTI: could not locate MissileSeeker.targetUnit — falling back to Missile.targetID only.");
            return _targetUnitField != null;
        }

        internal static Unit? GetTargetUnit(MissileSeeker seeker) =>
            Ensure() ? _targetUnitField!.GetValue(seeker) as Unit : null;
    }
}
