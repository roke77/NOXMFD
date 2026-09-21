using System.Threading;

namespace NOXMFD
{
    // A shared "which unit does an extension want MAP to highlight" concept
    // (docs/atc-extension-support.md item 3) — deliberately NOT TargetFocus (which tracks a real
    // weapon lock, driven by weaponManager.GetTargetList(), and is read-only from JS): MAP's own
    // click-to-select actually issues a target.select command (map.js's selectAt), a real in-game
    // weapons action. Reusing that flow for a UI "look at this" concept would risk an unintended
    // weapons command every time an extension asked to highlight a unit. This is its own, narrower
    // thing: an extension sets it, MAP reads and highlights it, nothing else happens in-game.
    //
    // One direction only for now — an extension writes, MAP reads. MAP's own click is not wired to
    // write this back (see the doc's item 3 for why: there's no existing non-weapons click path on
    // MAP to repurpose, and adding one is its own UI decision, not a quick follow-on to this field).
    //
    // 0 means "nothing selected," the same convention TargetFocus/JammedBy already use.
    internal static class SharedSelection
    {
        private static uint _id;
        private static bool _track;

        internal static uint Id => Volatile.Read(ref _id);

        internal static void Set(uint id) => Volatile.Write(ref _id, id);

        // Whether MAP should continuously re-center on Id instead of the player (the ATC
        // extension's TRACK ON MAP checkbox) — a separate flag from Id itself so a controller can
        // switch which unit LOCATE ON MAP points at without re-sending this. Defaults off; still
        // read even when Id is 0, so map.js just finds no matching contact and skips re-centering.
        internal static bool Track => Volatile.Read(ref _track);

        internal static void SetTrack(bool on) => Volatile.Write(ref _track, on);
    }
}
