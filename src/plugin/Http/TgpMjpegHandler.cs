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

        // Latest TGP camera frame as a JPEG, refreshed from TgpFeed.
        // The frame id lets each MJPEG client only send when it changes.
        private static byte[]? _latestJpg;
        private static long _frameId;
        private static readonly object _frameLock = new object();

        internal static void PushFrame(byte[] jpg)
        {
            if (jpg == null || jpg.Length == 0) return;
            lock (_frameLock) { _latestJpg = jpg; _frameId++; }
        }

        internal static void ClearFrame()
        {
            lock (_frameLock) { _latestJpg = null; _frameId++; }
        }

        private static byte[]? GetFrame(out long frameId)
        {
            lock (_frameLock)
            {
                frameId = _frameId;
                return _latestJpg;
            }
        }

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
            int subscribersNow = Interlocked.Increment(ref _subscribers);
            // Player report (2026-09): "video feed just stops" with nothing useful in the log —
            // because a disconnect (client navigated away, network blip, browser tab reclaimed) was
            // never logged at all, only silently decremented in the finally block below. Connect/
            // disconnect are the two events that actually tell us whether the CLIENT dropped the
            // connection (this log line) or the SERVER stopped sending frames while the client
            // stayed connected (TgpFeed's own stall warning) — two different bugs that look
            // identical from the player's side, so telling them apart needs both logged.
            Plugin.Log?.LogInfo($"[NOXMFD] TGP MJPEG client connected (subscribers={subscribersNow}).");
            // Diagnostic: logs how long a client waited for the first REAL frame after the
            // placeholder below streamed. Confirmed live 2026-08-23 (3.25s and 4.3s cold starts) —
            // kept as an ongoing signal that TargetCam's own capture lag, not this server, is what
            // gates the real picture.
            var coldStartWatch = Stopwatch.StartNew();
            bool coldStartLogged = false;
            string disconnectReason = "cancelled (client closed the connection or server shut down)";
            try
            {
                byte[]? initialJpg = GetFrame(out _);
                if (initialJpg == null) await WritePart(TelemetryServer.TgpPlaceholderJpg).ConfigureAwait(false);

                while (!ct.IsCancellationRequested)
                {
                    byte[]? jpg = GetFrame(out long id);

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
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                disconnectReason = $"exception: {ex.GetType().Name}: {ex.Message}";
                TelemetryServer.LogHttpFailure(ctx, "/tgp.mjpg", ex);
            }
            finally
            {
                int subscribersAfter = Interlocked.Decrement(ref _subscribers);
                Plugin.Log?.LogInfo($"[NOXMFD] TGP MJPEG client disconnected after {coldStartWatch.Elapsed.TotalSeconds:0.0}s (subscribers={subscribersAfter}) — {disconnectReason}.");
                try { ctx.Response.Close(); } catch { }
            }
        }
    }
}
