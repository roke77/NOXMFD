using System.IO;

namespace NOXMFD
{
    // Keeps one rolling copy of a config file next to itself. Keybinds.cs calls this on every
    // settings change (config.SettingChanged), so a blank/corrupted .cfg — a bad BepInEx reinstall,
    // antivirus quarantine, a future regression like the KeyboardShortcut lazy-cctor bug — is
    // recoverable by copying the .bak back over the live file, instead of hunting for a stray
    // backup folder.
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
