using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOXMFD
{
    internal static class SseHub
    {
        // The loop ticks at CursorTickMs (the MAP cursor's rate — a continuous analog signal, so
        // latency here is felt directly as a heavy, lagging crosshair) and emits the telemetry frame
        // every FrameEveryMs. 10 Hz for telemetry both during a mission and at the main menu, where
        // SOI focus changes must still feel immediate.
        private const int CursorTickMs = 16;    // ~60 Hz, but only ~60 bytes and only when it changes
        private const int FrameEveryMs = 100;   // 10 Hz

        // One /stream connection IS one MFD instance: HandleAsync runs for exactly as long as a
        // browser sits on the display, so registering on entry and dropping in finally is the whole
        // of the registry. Nothing else needs to track anything.
        //
        // Keyed by a server-side connection number, not by the client's cid. A duplicated browser tab
        // copies its sessionStorage and so claims a cid that is already in use — keying on that would
        // let the copy evict a live connection from the list, and let either one's disconnect remove
        // the other. The connection number is unique by construction; the cid rides along as data.
        internal sealed class MfdInstance
        {
            public long     Conn;
            public string   Cid    = string.Empty;
            public string   Remote = string.Empty;
            public DateTime ConnectedUtc;
            // How many independently-focusable SURFACES this instance shows right now — 1 in full
            // view, 2 in a classic split, up to 4 F-35 portals. The client reports it (soi.panes) and
            // re-reports on every layout change; SOI cycles surfaces, not whole documents. Defaults to
            // 1 so a client that never reports behaves exactly as before (whole-instance focus).
            public int      PaneCount = 1;
        }

        private static readonly ConcurrentDictionary<long, MfdInstance> _instances =
            new ConcurrentDictionary<long, MfdInstance>();
        private static long _nextConn;

        // Snapshot of the live instances, oldest connection first — a stable order to cycle SOI
        // through, unlike the dictionary's own.
        internal static List<MfdInstance> Instances()
        {
            var all = new List<MfdInstance>(_instances.Values);
            all.Sort((a, b) => a.Conn.CompareTo(b.Conn));
            return all;
        }

        internal static async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            ctx.Response.StatusCode   = 200;
            ctx.Response.ContentType  = "text/event-stream; charset=utf-8";
            ctx.Response.SendChunked  = true;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.Headers.Add("X-Accel-Buffering", "no");

            // Register this instance for its whole lifetime. The cid is the client's own durable id
            // (telemetry-source.js), empty when its storage is unavailable.
            long conn = Interlocked.Increment(ref _nextConn);
            // A client with no usable storage sends nothing; give it a connection-scoped id anyway so
            // that every instance is addressable. It is told which id it got (the hello event below),
            // because focus is broadcast BY cid and a client that doesn't know its own can never
            // recognise itself. Such an id lasts only as long as the connection — which is exactly
            // what "no durable identity" means.
            string cid = SanitizeCid(ctx.Request.QueryString["cid"]);
            if (cid.Length == 0) cid = "conn-" + conn.ToString(CultureInfo.InvariantCulture);

            _instances[conn] = new MfdInstance
            {
                Conn         = conn,
                Cid          = cid,
                Remote       = ctx.Request.RemoteEndPoint?.ToString() ?? string.Empty,
                ConnectedUtc = DateTime.UtcNow,
            };
            // No auto-claim: a fresh display does NOT become the SOI on its own. Focus stays empty
            // until the pilot presses a SOI key, so mouse/touch users never get the ring.

            Plugin.Log?.LogInfo($"[NOXMFD] Client connected from {ctx.Request.RemoteEndPoint} (instance {conn})");

            try
            {
                // Tell this client which id it is known by, once, before the stream proper. A named
                // SSE event so it can't be mistaken for a telemetry frame, and written to this one
                // connection only — the shared frame stays shared.
                byte[] hello = Encoding.UTF8.GetBytes(
                    "event: hello\ndata: {\"cid\":\"" + TelemetryServer.EscapeJson(cid) + "\"}\n\n");
                await ctx.Response.OutputStream.WriteAsync(hello, 0, hello.Length, ct).ConfigureAwait(false);

                // The loop ticks at the CURSOR's rate and sends the telemetry frame every Nth tick, so
                // the two cadences are independent: a slewed axis gets ~60 Hz of tiny updates while
                // the expensive snapshot keeps its 10 Hz. lastCursor suppresses repeats, so a centred
                // stick costs one comparison per tick and no traffic at all.
                string lastCursor = string.Empty;
                // Per-extension high-rate events (Api.PublishEvent) — one "last sent" entry per
                // event name this connection has seen, same change-gating as cursor above but for
                // a runtime-registered set of names instead of one fixed one.
                var lastExtEvents = new Dictionary<string, string>(StringComparer.Ordinal);
                // Squad state (docs/squadron-transport.md): same latest-value-wins comparison as the
                // cursor above — role/leader/members/pending/notice is a snapshot, not a queue, so a
                // display only needs the newest one, not every intermediate value.
                string lastSquad = string.Empty;
                // Leader-shared data payloads (wpt.route, ...) ARE a queue, not a snapshot: per-
                // connection cursor into Squad's data inbox, starting at whatever has already
                // arrived so a display that opens mid-session doesn't replay the backlog. Each
                // payload then reaches each display exactly once.
                long lastSquadDataSeq = Squad.LatestDataSeq();
                int sinceFrame = FrameEveryMs;   // send a frame immediately on connect
                while (!ct.IsCancellationRequested)
                {
                    if (sinceFrame >= FrameEveryMs)
                    {
                        // Shared frame: serialized at most once per snapshot version, regardless of
                        // how many clients are connected. Always send something — real data during a
                        // mission, a ping otherwise.
                        byte[] bytes = TelemetryServer.GetFrameBytes(out _);
                        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                        sinceFrame = 0;
                    }

                    string cursor = TelemetryServer.CursorJson();
                    if (!string.Equals(cursor, lastCursor, StringComparison.Ordinal))
                    {
                        lastCursor = cursor;
                        byte[] cbytes = Encoding.UTF8.GetBytes("event: cursor\ndata: " + cursor + "\n\n");
                        await ctx.Response.OutputStream.WriteAsync(cbytes, 0, cbytes.Length, ct).ConfigureAwait(false);
                    }

                    // Squad state, sent on its own event rather than inside the telemetry frame so a
                    // role/roster/invite change arrives as soon as it lands instead of waiting for
                    // the next 10 Hz tick — the same reasoning the cursor event uses.
                    string squadState = Squad.StateJson;
                    if (!string.Equals(squadState, lastSquad, StringComparison.Ordinal))
                    {
                        lastSquad = squadState;
                        byte[] qbytes = Encoding.UTF8.GetBytes("event: sqd\ndata: " + squadState + "\n\n");
                        await ctx.Response.OutputStream.WriteAsync(qbytes, 0, qbytes.Length, ct).ConfigureAwait(false);
                    }

                    // Leader-shared data payloads, one named event each.
                    var dataInbound = Squad.DataSince(lastSquadDataSeq);
                    foreach (var msg in dataInbound)
                    {
                        lastSquadDataSeq = msg.Seq;
                        string json = string.Format(CultureInfo.InvariantCulture,
                            "{{\"seq\":{0},\"type\":\"{1}\",\"payload\":\"{2}\"}}",
                            msg.Seq, TelemetryServer.EscapeJson(msg.Type), TelemetryServer.EscapeJson(msg.Payload));
                        byte[] dbytes = Encoding.UTF8.GetBytes("event: squadron\ndata: " + json + "\n\n");
                        await ctx.Response.OutputStream.WriteAsync(dbytes, 0, dbytes.Length, ct).ConfigureAwait(false);
                    }

                    foreach (var kv in ExtensionRegistry.EventsSnapshot())
                    {
                        if (lastExtEvents.TryGetValue(kv.Key, out string prev) && prev == kv.Value) continue;
                        lastExtEvents[kv.Key] = kv.Value;
                        byte[] ebytes = Encoding.UTF8.GetBytes("event: ext-" + kv.Key + "\ndata: " + kv.Value + "\n\n");
                        await ctx.Response.OutputStream.WriteAsync(ebytes, 0, ebytes.Length, ct).ConfigureAwait(false);
                    }
                    ctx.Response.OutputStream.Flush();

                    await Task.Delay(CursorTickMs, ct).ConfigureAwait(false);
                    sinceFrame += CursorTickMs;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] Client error: {ex.Message}"); }
            finally
            {
                _instances.TryRemove(conn, out _);
                TelemetryServer.SoiReleaseOnDisconnect(cid);
                try { ctx.Response.Close(); } catch { }
                Plugin.Log?.LogInfo($"[NOXMFD] Client disconnected from {ctx.Request.RemoteEndPoint} (instance {conn})");
            }
        }

        // The live MFD instances as JSON — the SOI instance registry made visible. Diagnostic for now:
        // it is what proves the registry tracks connects, disconnects and reloads correctly before any
        // of SOI is wired to it, and it stays useful afterwards for "which displays does the server
        // think are open?". Safe off the main thread — the dictionary is concurrent and touches no
        // Unity state.
        internal static void ServeInstances(HttpListenerContext ctx)
        {
            try
            {
                var sb = new StringBuilder("{\"instances\":[");
                var all = Instances();
                for (int i = 0; i < all.Count; i++)
                {
                    var it = all[i];
                    if (i > 0) sb.Append(',');
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "{{\"conn\":{0},\"cid\":\"{1}\",\"remote\":\"{2}\",\"upSec\":{3:0.0}}}",
                        it.Conn, TelemetryServer.EscapeJson(it.Cid), TelemetryServer.EscapeJson(it.Remote),
                        (DateTime.UtcNow - it.ConnectedUtc).TotalSeconds);
                }
                sb.Append("]}");
                TelemetryServer.WriteJson(ctx, sb.ToString());
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // The cid arrives over the network, so it is untrusted: it lands in JSON and, later, in an
        // SOI target comparison. Keep it to what the client is supposed to send — a UUID or the
        // fallback id — and drop anything else rather than escaping it downstream. An empty cid is
        // legal and means "this instance has no durable identity" (private mode, storage blocked).
        private const int MaxCidLength = 64;
        private static string SanitizeCid(string? raw)
        {
            if (string.IsNullOrEmpty(raw) || raw!.Length > MaxCidLength) return string.Empty;
            foreach (char c in raw)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                      (c >= '0' && c <= '9') || c == '-'))
                    return string.Empty;
            return raw;
        }
    }
}
