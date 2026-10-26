using System.Collections.Generic;

namespace NOXMFD
{
    // Which physical device a bind's joystick number means. Windows (and so Rewired) enumerates
    // controllers in whatever order they come up, so a stick number saved on one boot can name a
    // different device on the next. Capture saves the device's identity alongside the number, and
    // Keybinds re-pins the number to wherever that device landed whenever the joystick list changes.
    // Pure (strings in, number out) so it's unit-checkable without Rewired.
    //
    // Identity is "<instance guid>|<device name>". The instance GUID tells two identical sticks
    // apart, but Rewired only promises it per source and it changes if the device moves to another
    // USB port; the name survives that but not identical models. Guid empty = the source has none.
    internal static class JoystickIdentity
    {
        internal static string Make(string guid, string name) => guid + "|" + name;

        // The 1-based number the device `identity` has now among `joys` (guid, name in Rewired order),
        // or `saved` when the identity is empty/unparseable or the device isn't connected.
        internal static int Resolve(string identity, int saved, IList<(string Guid, string Name)> joys)
        {
            int bar = identity.IndexOf('|');
            if (bar < 0) return saved;
            string guid = identity.Substring(0, bar), name = identity.Substring(bar + 1);
            if (guid.Length > 0)
                for (int i = 0; i < joys.Count; i++)
                    if (joys[i].Guid == guid) return i + 1;
            // By name: identical models are ambiguous, so the saved number stands while it still
            // names a device of that model.
            if (saved >= 1 && saved <= joys.Count && joys[saved - 1].Name == name) return saved;
            for (int i = 0; i < joys.Count; i++)
                if (joys[i].Name == name) return i + 1;
            return saved;
        }
    }
}
