using System;
using System.Collections.Generic;
using System.IO;

namespace NOXMFD
{
    // Keeps one rolling copy of a config/data file next to itself. Callers are expected to back up
    // BEFORE overwriting — Keybinds.cs's mutators call this right before each ConfigEntry.Value
    // write, and RouteStore/LayoutStore/HudPresetStore.Save|Persist call it right before their
    // File.WriteAllText — so .bak always holds the prior state, not a copy of what just replaced
    // it. Recovers a blank/corrupted file (a bad BepInEx reinstall, antivirus quarantine, a future
    // regression like the KeyboardShortcut lazy-cctor bug) by copying the .bak back over the live
    // file, instead of hunting for a stray backup folder.
    // ponytail: single generation, no rotation — two corrupting saves in a row before anyone
    // notices still loses the last good state. Upgrade path: timestamped rotation, keep last N.
    internal static class ConfigBackup
    {
        // Storage seam: Plugin wires this to BepInEx logging while the standalone test project
        // leaves it null and keeps this backup primitive BCL-only.
        internal static Action<string>? LogWarning;
        private static readonly HashSet<string> _warnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void BackupIfExists(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.Copy(path, path + ".bak", overwrite: true);
            }
            catch (IOException ex)
            {
                WarnOnce(path, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                WarnOnce(path, ex);
            }
        }

        private static void WarnOnce(string path, Exception ex)
        {
            lock (_warnedPaths)
            {
                if (!_warnedPaths.Add(path)) return;
            }
            LogWarning?.Invoke($"[NOXMFD] could not back up '{path}': {ex.Message}; saving will continue.");
        }
    }
}
