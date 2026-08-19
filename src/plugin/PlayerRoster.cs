using System.Collections.Generic;
using System.Text;
using NuclearOption.Networking;

namespace NOXMFD
{
    // Live roster of players in the LOCAL PLAYER'S OWN FACTION for the current match — the source
    // for SQD's "pick a squadmate" list (docs/squadron-transport.md). Scoped to one faction because
    // a squad only makes sense among teammates; the enemy faction's roster is never useful for this.
    // Any client, host or not, can read this: Player/BasePlayer are ordinary Mirage
    // NetworkBehaviours whose SteamID SyncVar replicates server -> every observing client, and
    // FactionHQ.GetPlayers already exposes them per faction — the same source the game's own
    // scoreboard uses — so no host privilege and no new game-side plumbing is needed. Display names
    // come from Player.GetDisplayName(PlayerNameContext.Other), not a plain field — the game
    // resolves/caches the Steam persona name behind that call.
    //
    // Further filtered to faction-mates actually running NOXMFD right now (Presence.cs) — someone
    // without the mod can't receive or answer an invite, so offering them just produces a silent
    // 15s timeout (Squad.cs's InviteTimeoutSeconds) that reads as a bug rather than "they don't
    // have it."
    internal static class PlayerRoster
    {
        // Server-thread-readable cache, same threading contract as RouteStore.RoutesJson: refreshed
        // synchronously on the Unity main thread (TelemetryReader's 1 Hz slow scan), read as a plain
        // reference by the HTTP server thread — no lock needed.
        internal static volatile string Json = "[]";

        // FactionHQ.GetPlayers(false) returns ITS OWN shared static buffer, cleared again on the next
        // call — copy out immediately, never hold onto the result.
        private static readonly List<Player> _scratch = new List<Player>();

        // Current aircraft type (unitName) for anyone found in the local faction on the last
        // Refresh(), keyed by SteamID — SQD's roster table (Squad.cs's BuildStateJson) reads this
        // to show each squadmate's plane. Distinct from the Json roster above (which is Presence-
        // filtered and excludes self): this dictionary is unfiltered and DOES include self, since a
        // squad member row can be self (SQD viewing a squad you're a member, not leader, of) and the
        // aircraft itself is a plain Player.Aircraft SyncVar read that has nothing to do with whether
        // its owner happens to be running NOXMFD. Player.Aircraft is null whenever there's nothing to
        // report (dead, ejected, not spawned yet) — that naturally becomes "" here, same as a player
        // not found in the faction at all (out of the match, or this poll raced their departure).
        private static readonly Dictionary<ulong, string> _aircraftBySteamId = new Dictionary<ulong, string>();

        internal static string AircraftFor(ulong steamId) =>
            _aircraftBySteamId.TryGetValue(steamId, out string name) ? name : string.Empty;

        // Called once per slow-scan tick from TelemetryReader.Update. Cheap: one FactionHQ lookup,
        // not FindObjectsByType. Empty (not stale) whenever there's no local aircraft/HQ yet — the
        // main menu, or between missions.
        internal static void Refresh()
        {
            if (!GameManager.GetLocalHQ(out FactionHQ hq) || hq == null)
            {
                Json = "[]";
                _aircraftBySteamId.Clear();
                return;
            }

            ulong self = Squadron.SelfId();
            _scratch.Clear();
            _scratch.AddRange(hq.GetPlayers(sortByScore: false));
            _aircraftBySteamId.Clear();

            // Ping the WHOLE faction, including anyone filtered out below — someone who just
            // (re)launched NOXMFD needs to start receiving beats before Presence.HasNoxmfd can ever
            // return true for them, and this is the one place that already has the faction's peer ids.
            var peerIds = new List<ulong>(_scratch.Count);
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (Player p in _scratch)
            {
                if (p == null) continue;
                ulong id = p.SteamID;
                if (id == 0) continue;   // no Steam id — nothing to key either dictionary on

                _aircraftBySteamId[id] = p.Aircraft != null && p.Aircraft.definition != null
                    ? (p.Aircraft.definition.unitName ?? string.Empty) : string.Empty;

                if (id == self) continue;   // exclude self from the invite candidate list below
                peerIds.Add(id);
                if (!Presence.HasNoxmfd(id)) continue;   // only offer players actually running NOXMFD
                if (!first) sb.Append(',');
                first = false;
                string name = p.GetDisplayName(PlayerNameContext.Other) ?? string.Empty;
                sb.Append("{\"id\":\"").Append(id).Append("\",\"name\":\"")
                  .Append(TelemetryServer.EscapeJson(name)).Append("\"}");
            }
            sb.Append(']');
            Json = sb.ToString();

            Presence.Tick(peerIds);
        }
    }
}
