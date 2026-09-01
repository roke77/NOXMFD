using System.IO;

namespace NOXMFD
{
    // Keeps one rolling copy of a config/data file next to itself. Called after every save of
    // com.roque.NOXMFD.cfg (Keybinds.cs, on config.SettingChanged) and the three JSON stores
    // (RouteStore/LayoutStore/HudPresetStore.Save|Persist) — so a blank/corrupted file, from a bad
    // BepInEx reinstall, antivirus quarantine, or a future regression like the KeyboardShortcut
    // lazy-cctor bug, is recoverable by copying the .bak back over the live file, instead of
    // hunting for a stray backup folder.
    // ponytail: single generation, no rotation — two corrupting saves in a row before anyone
    // notices still loses the last good state. Upgrade path: timestamped rotation, keep last N.
    internal static class ConfigBackup
    {
        internal static void BackupIfExists(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.Copy(path, path + ".bak", overwrite: true);
            }
            catch { }   // best-effort — never let a backup failure break config saving
        }
    }
}
