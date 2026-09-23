using System;

namespace NOXMFD
{
    // Duplicate-assignment rule for the Layout Preset slots (issue #90): a key or joystick button
    // already used by another bind can't be given to a slot, and a slot's key/button can't be given
    // to another bind. Pure (ids + a "does bind i use this value" callback), so it's unit-checkable
    // without Unity/BepInEx config entries — Keybinds.cs supplies both from its registry.
    //
    // ponytail: only enforced when a Layout Preset slot is on either side. Every other bind keeps
    // allowing a shared key as it always has; widening this to the whole registry is dropping the
    // IsLayoutSlot condition in Find.
    internal static class KeybindConflict
    {
        internal const string LayoutSlotPrefix = "layout-preset-";
        internal const int LayoutSlotCount = 5;

        internal static bool IsLayoutSlot(string id) => id.StartsWith(LayoutSlotPrefix, StringComparison.Ordinal);

        // Index of the first OTHER bind that already uses the value being assigned to bind `target`,
        // or -1 when the assignment is allowed.
        internal static int Find(int count, Func<int, string> id, int target, Func<int, bool> uses)
        {
            bool targetIsSlot = IsLayoutSlot(id(target));
            for (int i = 0; i < count; i++)
                if (i != target && (targetIsSlot || IsLayoutSlot(id(i))) && uses(i)) return i;
            return -1;
        }

        // Same Rewired button on an overlapping device. Joystick number 0 = "any device", so it
        // overlaps every pinned device too.
        internal static bool JoyMatches(int button, int joyNum, int otherButton, int otherJoyNum) =>
            button >= 0 && button == otherButton && (joyNum == 0 || otherJoyNum == 0 || joyNum == otherJoyNum);
    }
}
