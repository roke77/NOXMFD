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

        // Called once per slow-scan tick from TelemetryReader.Update. Cheap: one FactionHQ lookup,
        // not FindObjectsByType. Empty (not stale) whenever there's no local aircraft/HQ yet — the
        // main menu, or between missions.
        internal static void Refresh()
        {
            if (!GameManager.GetLocalHQ(out FactionHQ hq) || hq == null) { Json = "[]"; return; }

            ulong self = Squadron.SelfId();
            _scratch.Clear();
            _scratch.AddRange(hq.GetPlayers(sortByScore: false));

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
                if (id == 0 || id == self) continue;   // exclude self and anyone with no Steam id
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
