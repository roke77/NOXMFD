using System.Collections.Generic;
using System.Linq;

namespace NOXMFD
{
    // The stored text form of a keyboard bind with optional Ctrl/Alt/Shift modifiers:
    // "LeftControl+LeftAlt+LeftShift+<main>" — only the modifiers the bind has, always in that order,
    // always the Left* name (either side satisfies it; see Keybinds.ModifiersHeld). The browser writes
    // this form (keybinds-keymap.js) and /keybinds-config serves it back (Keybinds.KeyName). Pure
    // string work over Unity KeyCode NAMES, so it's unit-checkable without Unity; Keybinds.cs turns
    // the names into KeyCodes.
    internal static class KeyChord
    {
        private static readonly string[] Order = { "LeftControl", "LeftAlt", "LeftShift" };

        // The stored modifier name for either side of Ctrl/Alt/Shift, or null for any other key.
        internal static string? Family(string key) => key switch
        {
            "LeftControl" or "RightControl" => "LeftControl",
            "LeftAlt" or "RightAlt"         => "LeftAlt",
            "LeftShift" or "RightShift"     => "LeftShift",
            _ => null,
        };

        // Splits a stored name into its modifier families and main key name. False for an empty
        // part, a non-modifier in a modifier position, a repeated modifier, or a main key that is
        // itself one of the chord's own modifiers ("LeftAlt+RightAlt"). A lone modifier ("LeftAlt")
        // is a valid main key with no modifiers.
        internal static bool TrySplit(string name, out List<string> modifiers, out string main)
        {
            modifiers = new List<string>();
            string[] parts = name.Split('+');
            main = parts[parts.Length - 1];
            if (parts.Any(p => p.Length == 0)) return false;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string? fam = Family(parts[i]);
                if (fam == null || modifiers.Contains(fam)) return false;
                modifiers.Add(fam);
            }
            string? mainFam = Family(main);
            return mainFam == null || !modifiers.Contains(mainFam);
        }

        // The stored name for a main key plus modifiers given by any side/order: normalized to their
        // families, in Order.
        internal static string Join(IEnumerable<string> modifiers, string main)
        {
            var fams = modifiers.Select(Family).ToList();
            return string.Join("+", Order.Where(fams.Contains).Concat(new[] { main }));
        }
    }
}
