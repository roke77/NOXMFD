using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NOXMFD
{
    // Opt-in per-mission CSV of every kill this client sees — the same events as AKF's ALL feed —
    // so a squadron can keep its own tallies in a spreadsheet. BepInEx-free so tools/tests can
    // compile it: Plugin injects the folder, the config toggle and the logger.
    //
    // One instance per mission, owned by the mission-scoped AkfTracker. The file is created on the
    // first logged kill, not at mission start, so the toggle being off or a kill-less mission
    // leaves no empty file behind. AutoFlush keeps every row on disk if the game crashes mid-mission.
    internal sealed class KillLog : IDisposable
    {
        internal static string Dir = string.Empty;
        internal static Func<bool> Enabled = () => false;
        internal static Action<string>? LogWarning;

        internal const string Header = "time,killer,killer_faction,victim,victim_faction,type,weapon";

        private StreamWriter? _writer;
        private bool _failed;

        internal void Write(DateTime localTime, string missionName, string? killer, string? killerFaction,
            string victim, string? victimFaction, AkfKillKind kind, string? weapon)
        {
            if (_failed || !Enabled()) return;
            try
            {
                _writer ??= Open(Path.Combine(Dir, FileName(missionName, localTime)));
                _writer.WriteLine(Row(localTime, killer, killerFaction, victim, victimFaction, kind, weapon));
            }
            catch (Exception ex)
            {
                // A full disk or locked file must not throw into the kill-message patch every kill;
                // one warning, then this mission stops logging.
                _failed = true;
                LogWarning?.Invoke($"[NOXMFD] Kill log stopped for this mission: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try { _writer?.Dispose(); }
            catch (Exception ex) { LogWarning?.Invoke($"[NOXMFD] Kill log close failed: {ex.Message}"); }
            _writer = null;
        }

        private static StreamWriter Open(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bool isNew = !File.Exists(path);
            // BOM so Excel reads non-ASCII player names as UTF-8 instead of the system codepage.
            var writer = new StreamWriter(path, append: true, new UTF8Encoding(true)) { AutoFlush = true };
            if (isNew) writer.WriteLine(Header);
            return writer;
        }

        internal static string FileName(string missionName, DateTime localTime)
        {
            var sb = new StringBuilder("kills_").Append(localTime.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(missionName))
            {
                // An allow-list, not Path.GetInvalidFileNameChars: that set is per-OS and the mission
                // name comes from a mission author or lobby host.
                sb.Append('_');
                foreach (char c in missionName.Trim())
                    sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
            }
            return sb.Append(".csv").ToString();
        }

        internal static string Row(DateTime localTime, string? killer, string? killerFaction,
            string victim, string? victimFaction, AkfKillKind kind, string? weapon)
        {
            return string.Join(",",
                localTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Cell(killer), Cell(killerFaction), Cell(victim), Cell(victimFaction),
                kind.ToString().ToLowerInvariant(), Cell(weapon));
        }

        // Unit names carry other players' Steam names, which they control: a leading = + - @ would
        // run as a spreadsheet formula, so those cells get a ' prefix (shown as text, not data).
        internal static string Cell(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string v = value!;
            char first = v[0];
            if (first == '=' || first == '+' || first == '-' || first == '@' || first == '\t' || first == '\r')
                v = "'" + v;
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
                ? "\"" + v.Replace("\"", "\"\"") + "\""
                : v;
        }
    }
}
