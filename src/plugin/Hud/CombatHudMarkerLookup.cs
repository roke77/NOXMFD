using System.Collections.Generic;
using System.Reflection;

namespace NOXMFD
{
    // CombatHUD.markerLookup is private, so any HUD cue that needs to find a unit's own on-screen
    // marker (HudFocusMark.cs's amber "+", HudSquadTargetMark.cs's teal "*"/"⌃") has to reflect into
    // it. Both cues target this exact same field — shared here rather than each keeping its own
    // cache, so a game update that renames the field needs fixing in one place, not two.
    internal static class CombatHudMarkerLookup
    {
        private static bool _reflectionTried;
        private static FieldInfo? _field;
        private static bool _loggedNoField, _loggedBadType;

        // The failure log is generic (not per-caller) rather than naming whichever cue happens to
        // call this first — EnsureReflection/the bad-type check each only ever run/log once, shared
        // across every cue using this lookup, so a per-caller label would just blame load order.
        internal static bool TryGet(CombatHUD hud, out Dictionary<Unit, HUDUnitMarker> lookup)
        {
            lookup = null!;
            if (!EnsureReflection()) return false;
            if (_field!.GetValue(hud) is not Dictionary<Unit, HUDUnitMarker> map)
            {
                if (!_loggedBadType)
                {
                    _loggedBadType = true;
                    Plugin.Log?.LogWarning("[NOXMFD] HUD marker cue: CombatHUD.markerLookup read null/wrong type — disabled.");
                }
                return false;
            }
            lookup = map;
            return true;
        }

        private static bool EnsureReflection()
        {
            if (_reflectionTried) return _field != null;
            _reflectionTried = true;
            _field = typeof(CombatHUD).GetField("markerLookup", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_field == null && !_loggedNoField)
            {
                _loggedNoField = true;
                Plugin.Log?.LogWarning("[NOXMFD] HUD marker cue: could not locate CombatHUD.markerLookup — disabled.");
            }
            return _field != null;
        }
    }
}
