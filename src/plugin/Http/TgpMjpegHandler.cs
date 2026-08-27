using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOXMFD
{
    internal static class TgpMjpegHandler
    {
        // Number of HTTP clients currently subscribed to /tgp.mjpg. The reader checks this each tick
        // and skips the entire capture pipeline (cam swap, GPU readback, JPEG encode) while nobody is
        // watching — that's where most of the per-target FPS hit comes from.
        private static int _subscribers;
        internal static bool WantsFrames => Volatile.Read(ref _subscribers) > 0;

        // Long-lived multipart/x-mixed-replace response. Browsers render this directly in an <img>
        // tag — when a new JPEG is written, the image swaps in place.
        internal static async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            const string boundary = "tgpframe";
            ctx.Response.StatusCode  = 200;
            ctx.Response.ContentType = "multipart/x-mixed-replace; boundary=" + boundary;
            ctx.Response.SendChunked = true;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.Headers.Add("X-Accel-Buffering", "no");

            async Task WritePart(byte[] jpg)
            {
                string head = "\r\n--" + boundary + "\r\nContent-Type: image/jpeg\r\nContent-Length: " + jpg.Length + "\r\n\r\n";
                byte[] headBytes = Encoding.ASCII.GetBytes(head);
                await ctx.Response.OutputStream.WriteAsync(headBytes, 0, headBytes.Length, ct).ConfigureAwait(false);
                await ctx.Response.OutputStream.WriteAsync(jpg, 0, jpg.Length, ct).ConfigureAwait(false);
                ctx.Response.OutputStream.Flush();
            }

            long lastSeen = -1;
            Interlocked.Increment(ref _subscribers);
            // Diagnostic: logs how long a client waited for the first REAL frame after the
            // placeholder below streamed. Confirmed live 2026-08-23 (3.25s and 4.3s cold starts) —
            // kept as an ongoing signal that TargetCam's own capture lag, not this server, is what
            // gates the real picture.
            var coldStartWatch = Stopwatch.StartNew();
            bool coldStartLogged = false;
            try
            {
                byte[]? initialJpg = TelemetryServer.GetTgpFrame(out _);
                if (initialJpg == null) await WritePart(TelemetryServer.TgpPlaceholderJpg).ConfigureAwait(false);

                while (!ct.IsCancellationRequested)
                {
                    byte[]? jpg = TelemetryServer.GetTgpFrame(out long id);

                    if (!coldStartLogged && jpg != null)
                    {
                        coldStartLogged = true;
                        if (coldStartWatch.ElapsedMilliseconds > 500)
                            Plugin.Log?.LogWarning($"[NOXMFD] TGP MJPEG cold start: client waited {coldStartWatch.ElapsedMilliseconds}ms for the first real frame (placeholder streamed immediately).");
                    }

                    if (jpg != null && id != lastSeen)
                    {
                        lastSeen = id;
                        await WritePart(jpg).ConfigureAwait(false);
                    }

                    // Source publishes at 15 Hz (~66 ms/frame); 40 ms polls stay ahead so we don't
                    // drop alternate frames waiting for the next wake-up.
                    await Task.Delay(40, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* client disconnected, normal */ }
            finally
            {
                Interlocked.Decrement(ref _subscribers);
                try { ctx.Response.Close(); } catch { }
            }
        }
    }
}
