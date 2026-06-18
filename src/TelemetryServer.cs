using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOTelemetryReader
{
    internal static class TelemetryServer
    {
        private const int Port = 5005;

        private static HttpListener?           _listener;
        private static Thread?                 _acceptThread;
        private static CancellationTokenSource _cts = new CancellationTokenSource();

        private static TelemetrySnapshot _latest;
        private static readonly object   _lock = new object();

        // Captured in-game map image (PNG), set from the Unity main thread.
        private static byte[]?          _mapPng;
        private static readonly object  _mapLock = new object();

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public static void Start()
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            try { _listener.Start(); }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[NOTelemetry] Failed to start on port {Port}: {ex.Message}");
                return;
            }

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "NOTelemetry-Accept" };
            _acceptThread.Start();
            Plugin.Log?.LogInfo($"[NOTelemetry] Server listening on http://localhost:{Port}/");
        }

        public static void Stop()
        {
            _cts.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener = null;
            Plugin.Log?.LogInfo("[NOTelemetry] Server stopped.");
        }

        // Called from Unity main thread — just stores the latest snapshot.
        public static void Push(in TelemetrySnapshot snap)
        {
            lock (_lock) _latest = snap;
        }

        // Called from Unity main thread once the map image has been extracted.
        public static void SetMapImage(byte[] png)
        {
            lock (_mapLock) _mapPng = png;
            Plugin.Log?.LogInfo($"[NOTelemetry] In-game map image ready ({png.Length} bytes) — serving at /map.");
        }

        // ── Accept loop ────────────────────────────────────────────────────────

        private static void AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var ctx  = _listener!.GetContext();
                    var path = ctx.Request.Url?.AbsolutePath ?? "/";

                    if (path == "/stream")
                        _ = Task.Run(() => HandleSseAsync(ctx, _cts.Token));
                    else if (path == "/map" || path == "/map.png" || path == "/map.jpg")
                        ServeMap(ctx);
                    else
                        ServeHtml(ctx);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!_cts.IsCancellationRequested)
                        Plugin.Log?.LogError($"[NOTelemetry] Accept error: {ex.Message}");
                }
            }
        }

        // ── HTML handler ───────────────────────────────────────────────────────

        private static void ServeHtml(HttpListenerContext ctx)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(ClientPage.Html);
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // ── Map image handler ──────────────────────────────────────────────────

        private static void ServeMap(HttpListenerContext ctx)
        {
            // Prefer the map image we extracted straight from the game — its bounds match the
            // world coordinates exactly, so the plane lines up with no calibration.
            byte[]? captured;
            lock (_mapLock) captured = _mapPng;
            if (captured != null)
            {
                try
                {
                    ctx.Response.StatusCode      = 200;
                    ctx.Response.ContentType     = "image/png";
                    ctx.Response.ContentLength64 = captured.Length;
                    ctx.Response.OutputStream.Write(captured, 0, captured.Length);
                }
                catch { }
                finally { try { ctx.Response.Close(); } catch { } }
                return;
            }

            // Fallback: a map file dropped into the plugins folder (used until a mission loads).
            string dir       = BepInEx.Paths.PluginPath;
            string pngPath   = Path.Combine(dir, "map.png");
            string jpgPath   = Path.Combine(dir, "map.jpg");
            string jpegPath  = Path.Combine(dir, "map.jpeg");
            string noExtPath = Path.Combine(dir, "map");          // Windows sometimes hides extensions

            string filePath = File.Exists(pngPath)   ? pngPath
                            : File.Exists(jpgPath)   ? jpgPath
                            : File.Exists(jpegPath)  ? jpegPath
                            : File.Exists(noExtPath) ? noExtPath
                            : string.Empty;

            string contentType = filePath.EndsWith(".png") ? "image/png" : "image/jpeg";

            if (filePath == string.Empty)
            {
                ctx.Response.StatusCode = 404;
                try { ctx.Response.Close(); } catch { }
                Plugin.Log?.LogWarning($"[NOTelemetry] Map not found in: {dir}");
                return;
            }

            try
            {
                byte[] body = File.ReadAllBytes(filePath);
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = contentType;
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // ── SSE handler ────────────────────────────────────────────────────────

        private static async Task HandleSseAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            ctx.Response.StatusCode   = 200;
            ctx.Response.ContentType  = "text/event-stream; charset=utf-8";
            ctx.Response.SendChunked  = true;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.Headers.Add("X-Accel-Buffering", "no");

            Plugin.Log?.LogInfo($"[NOTelemetry] Client connected from {ctx.Request.RemoteEndPoint}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TelemetrySnapshot snap;
                    lock (_lock) snap = _latest;

                    // Always send something — real data during a mission, a ping otherwise.
                    string payload = snap.Valid ? Serialize(snap) : "{\"ping\":true}";
                    byte[] bytes   = Encoding.UTF8.GetBytes("data: " + payload + "\n\n");

                    await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                    ctx.Response.OutputStream.Flush();

                    // 10 Hz during a mission, 1 Hz ping otherwise.
                    await Task.Delay(snap.Valid ? 100 : 1000, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[NOTelemetry] Client error: {ex.Message}"); }
            finally
            {
                try { ctx.Response.Close(); } catch { }
                Plugin.Log?.LogInfo($"[NOTelemetry] Client disconnected from {ctx.Request.RemoteEndPoint}");
            }
        }

        // ── Serialization ──────────────────────────────────────────────────────

        private static string Serialize(TelemetrySnapshot s) =>
            string.Format(CultureInfo.InvariantCulture,
                "{{\"ping\":false,\"t\":{0:0.000},\"name\":\"{1}\"," +
                "\"world\":{{\"x\":{2:0.0},\"y\":{3:0.0},\"z\":{4:0.0}}}," +
                "\"hdg\":{5:0.0},\"tas\":{6:0.0},\"agl\":{7:0.0},\"gear\":\"{8}\"," +
                "\"units\":{9},\"aircraft\":{10}," +
                "\"map\":{{\"valid\":{11},\"w\":{12:0.0},\"h\":{13:0.0},\"ox\":{14},\"oy\":{15}}}}}",
                s.Time,
                EscapeJson(s.PlaneName ?? string.Empty),
                s.WorldX, s.WorldY, s.WorldZ,
                s.Heading, s.TAS, s.AGL,
                s.GearDown ? "down" : "up",
                s.TotalUnits, s.TotalAircraft,
                s.MapValid ? "true" : "false",
                s.MapW, s.MapH,
                s.GridOffsetX, s.GridOffsetY);

        private static string EscapeJson(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
