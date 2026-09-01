using System.Net;

namespace NOXMFD
{
    // Squad/roster HTTP endpoints (docs/squadron-transport.md) — split out of the old monolithic
    // TelemetryServer.cs the same way CapturedAssetEndpoint/ConfigEndpoint/ExtensionEndpoint were.
    internal static class SquadEndpoint
    {
        // Squad state as JSON — role, leader, members, any pending invite/sent-invites, and the
        // latest notice. `ready:false` is the honest answer on a non-Steam launch, and the SQD page
        // shows the feature as unavailable rather than silently failing to send. Squad.StateJson is
        // prebuilt on the main thread (Squad.Drain), so this stays a plain string read.
        internal static void ServeSquad(HttpListenerContext ctx) =>
            TelemetryServer.WriteJson(ctx, "{\"ready\":" + (Squadron.Ready ? "true" : "false") + ",\"state\":" + Squad.StateJson + "}");

        // Every other player in the LOCAL PLAYER'S OWN FACTION for the current match, for SQD's
        // "pick a squadmate" list (docs/squadron-transport.md) — PlayerRoster.Json is prebuilt on
        // the main thread (PlayerRoster.Refresh, TelemetryReader's 1 Hz slow scan), so this is a
        // plain string read. Empty (not missing) outside a mission, at the main menu, or before any
        // other player in the faction has been observed.
        internal static void ServeServerPlayers(HttpListenerContext ctx) =>
            TelemetryServer.WriteJson(ctx, PlayerRoster.Json);

        // Target Designator state (issue #47, docs/target-designator.md) — same shape/threading
        // contract as ServeSquad above, one HTTP handler next to another for the same feature
        // family rather than a near-empty file of its own.
        internal static void ServeTd(HttpListenerContext ctx) =>
            TelemetryServer.WriteJson(ctx, "{\"ready\":" + (Squadron.Ready ? "true" : "false") + ",\"state\":" + TdStore.StateJson + "}");
    }
}
