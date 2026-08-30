using System.Net;
using System.Text;

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
        internal static void ServeSquad(HttpListenerContext ctx)
        {
            try
            {
                string body = "{\"ready\":" + (Squadron.Ready ? "true" : "false") + ",\"state\":" + Squad.StateJson + "}";
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // Every other player in the LOCAL PLAYER'S OWN FACTION for the current match, for SQD's
        // "pick a squadmate" list (docs/squadron-transport.md) — PlayerRoster.Json is prebuilt on
        // the main thread (PlayerRoster.Refresh, TelemetryReader's 1 Hz slow scan), so this is a
        // plain string read. Empty (not missing) outside a mission, at the main menu, or before any
        // other player in the faction has been observed.
        internal static void ServeServerPlayers(HttpListenerContext ctx)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(PlayerRoster.Json);
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }
}
