using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOXMFD
{
    internal static class ExtensionEndpoint
    {
        internal static void ServeManifest(HttpListenerContext ctx)
        {
            try
            {
                List<ExtensionRegistry.Entry> list = ExtensionRegistry.Manifest();
                var sb = new StringBuilder("[");
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"id\":\"").Append(TelemetryServer.EscapeJson(list[i].Id))
                      .Append("\",\"label\":\"").Append(TelemetryServer.EscapeJson(list[i].Label)).Append("\"}");
                }
                TelemetryServer.WriteJson(ctx, sb.Append(']').ToString());
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // Routes "/ext/<id>" (the page itself), "/ext/<id>/<relPath>" (its assets), and
        // POST "/ext/<id>/command" (its command endpoint) — one generic handler for every
        // registered extension rather than per-extension routing, the whole point of this surface
        // (see docs/extensions-api.md).
        internal static async Task HandleRequestAsync(HttpListenerContext ctx, string path, CancellationToken ct)
        {
            string rest = path.Substring("/ext/".Length);
            int slash = rest.IndexOf('/');
            string id      = slash < 0 ? rest : rest.Substring(0, slash);
            string relPath = slash < 0 ? string.Empty : rest.Substring(slash + 1);

            if (!ExtensionRegistry.TryGet(id, out ExtensionRegistry.Entry entry))
            {
                ctx.Response.StatusCode = 404;
                try { ctx.Response.Close(); } catch { }
                return;
            }

            if (relPath == "command")
            {
                if (ctx.Request.HttpMethod != "POST")
                {
                    ctx.Response.StatusCode = 405;
                    try { ctx.Response.Close(); } catch { }
                    return;
                }
                CommandEndpoint.HandleExtensionCommand(ctx, id);
                return;
            }

            if (relPath == "feed.mjpg")
            {
                await HandleMjpegAsync(ctx, id, ct).ConfigureAwait(false);
                return;
            }

            try
            {
                byte[]? body = entry.Resolve(relPath);
                if (body == null) { ctx.Response.StatusCode = 404; return; }
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = TelemetryAssets.ContentTypeFor(relPath.Length == 0 ? "index.html" : relPath);
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[NOXMFD] /ext/{id}/{relPath} error: {ex.Message}");
                try { ctx.Response.Abort(); } catch { }
                return;
            }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // Same shape as TgpMjpegHandler, generalized to a runtime-registered extension id instead
        // of a hardcoded page — see Api.PushMjpegFrame.
        private static async Task HandleMjpegAsync(HttpListenerContext ctx, string id, CancellationToken ct)
        {
            const string boundary = "extframe";
            ctx.Response.StatusCode  = 200;
            ctx.Response.ContentType = "multipart/x-mixed-replace; boundary=" + boundary;
            ctx.Response.SendChunked = true;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.Headers.Add("X-Accel-Buffering", "no");

            long lastSeen = -1;
            ExtensionRegistry.MjpegSubscribe(id);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (ExtensionRegistry.TryGetMjpegFrame(id, out byte[]? jpg, out long frameId)
                        && jpg != null && frameId != lastSeen)
                    {
                        lastSeen = frameId;
                        string head = "\r\n--" + boundary + "\r\nContent-Type: image/jpeg\r\nContent-Length: " + jpg.Length + "\r\n\r\n";
                        byte[] headBytes = Encoding.ASCII.GetBytes(head);
                        await ctx.Response.OutputStream.WriteAsync(headBytes, 0, headBytes.Length, ct).ConfigureAwait(false);
                        await ctx.Response.OutputStream.WriteAsync(jpg, 0, jpg.Length, ct).ConfigureAwait(false);
                        ctx.Response.OutputStream.Flush();
                    }

                    // No fixed source rate to match (unlike TGP/RC's own hardcoded intervals) —
                    // 30ms polling keeps this responsive to whatever cadence an extension publishes.
                    await Task.Delay(30, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* client disconnected, normal */ }
            finally
            {
                ExtensionRegistry.MjpegUnsubscribe(id);
                try { ctx.Response.Close(); } catch { }
            }
        }
    }
}
