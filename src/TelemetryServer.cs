using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
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

        // Per-aircraft-type map icons (PNG), keyed by unitName.
        private static readonly Dictionary<string, byte[]> _icons    = new Dictionary<string, byte[]>();
        private static readonly object                     _iconLock = new object();

        // Per-weapon-type icons (PNG), keyed by weapon display name.
        private static readonly Dictionary<string, byte[]> _weaponIcons = new Dictionary<string, byte[]>();
        private static readonly object                     _weaponLock  = new object();

        // Per-countermeasure icons (PNG), keyed by short name ("flares", "jammer").
        private static readonly Dictionary<string, byte[]> _cmIcons = new Dictionary<string, byte[]>();
        private static readonly object                     _cmLock  = new object();

        // Latest TGP camera frame as a JPEG, refreshed ~10 Hz from TelemetryReader.
        // The frame id lets each MJPEG client only send when it changes.
        private static byte[]? _tgpJpg;
        private static long    _tgpFrameId;
        private static readonly object _tgpLock = new object();

        // Number of HTTP clients currently subscribed to /tgp.mjpg. The reader checks this
        // each tick and skips the entire capture pipeline (cam swap, GPU readback, JPEG
        // encode) while nobody is watching — that's where most of the per-target FPS hit
        // comes from. Counter is bumped in HandleMjpegAsync's try and decremented in finally.
        private static int _tgpSubscribers;
        public static bool WantsTgpFrames => Volatile.Read(ref _tgpSubscribers) > 0;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        // Local-network URL (e.g. http://192.168.1.42:5005) — empty if the listener fell back
        // to localhost-only. Embedded into the MFD page's MAIN card so the user can read it
        // from a tablet on the same Wi-Fi.
        internal static string LanUrl { get; private set; } = "";

        public static void Start()
        {
            _cts = new CancellationTokenSource();

            // Prefer binding to all interfaces so a tablet on the LAN can reach us. On
            // Windows that requires either Administrator or a one-time URL ACL:
            //   netsh http add urlacl url=http://+:5005/ user=Everyone
            // If that bind fails (access denied), fall back to localhost-only — same as
            // the original behaviour.
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{Port}/");
            bool boundAll = false;
            try { _listener.Start(); boundAll = true; }
            catch (HttpListenerException)
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                try { _listener.Start(); }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"[NOTelemetry] Failed to start on port {Port}: {ex.Message}");
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[NOTelemetry] Failed to start on port {Port}: {ex.Message}");
                return;
            }

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "NOTelemetry-Accept" };
            _acceptThread.Start();

            Plugin.Log?.LogInfo($"[NOTelemetry] Server listening on http://localhost:{Port}/");
            if (boundAll)
            {
                string lanIp = DetectLanIp();
                if (!string.IsNullOrEmpty(lanIp))
                {
                    LanUrl = $"http://{lanIp}:{Port}";
                    Plugin.Log?.LogInfo($"[NOTelemetry] LAN access:  {LanUrl}/");
                }
            }
            else
            {
                Plugin.Log?.LogInfo("[NOTelemetry] LAN access disabled — to enable, run once in an elevated shell:  netsh http add urlacl url=http://+:" + Port + "/ user=Everyone");
            }
        }

        // Find the local IPv4 that would be used to reach the LAN. The UDP "connect" doesn't
        // actually send any packets — it just resolves the outbound interface via the routing
        // table, which gives us the same address the tablet will see.
        private static string DetectLanIp()
        {
            try
            {
                using (var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    sock.Connect("8.8.8.8", 65530);
                    return ((IPEndPoint?)sock.LocalEndPoint)?.Address.ToString() ?? "";
                }
            }
            catch { return ""; }
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

        // Called from Unity main thread once an aircraft type's map icon has been extracted.
        public static void SetIcon(string unitName, byte[] png)
        {
            if (string.IsNullOrEmpty(unitName)) return;
            lock (_iconLock) _icons[unitName] = png;
        }

        // Called from Unity main thread once a weapon type's icon has been extracted.
        public static void SetWeaponIcon(string name, byte[] png)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_weaponLock) _weaponIcons[name] = png;
        }

        // Called from Unity main thread once a countermeasure's display sprite has been extracted.
        public static void SetCmIcon(string key, byte[] png)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_cmLock) _cmIcons[key] = png;
        }

        // Called from Unity main thread with each captured TGP camera frame.
        public static void PushTgpFrame(byte[] jpg)
        {
            if (jpg == null || jpg.Length == 0) return;
            lock (_tgpLock) { _tgpJpg = jpg; _tgpFrameId++; }
        }

        // Drops the cached TGP frame so MJPEG clients see "no frame" again.
        public static void ClearTgpFrame()
        {
            lock (_tgpLock) { _tgpJpg = null; _tgpFrameId++; }
        }

        // Called from Unity main thread when a mission ends — clears all per-mission state so
        // the client drops back to "no mission" and wipes its display. Icons are static
        // per-type assets and stay cached across missions.
        public static void Reset()
        {
            lock (_lock)    _latest = default;
            lock (_mapLock) _mapPng = null;
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
                    else if (path == "/tgp.mjpg")
                        _ = Task.Run(() => HandleMjpegAsync(ctx, _cts.Token));
                    else if (path == "/map" || path == "/map.png" || path == "/map.jpg")
                        ServeMap(ctx);
                    else if (path == "/icon")
                        ServePng(ctx, _icons, _iconLock, "type");
                    else if (path == "/weapon")
                        ServePng(ctx, _weaponIcons, _weaponLock, "name");
                    else if (path == "/cm")
                        ServePng(ctx, _cmIcons, _cmLock, "type");
                    else if (path == "/mfd")
                    {
                        string lanBlock = string.IsNullOrEmpty(LanUrl)
                            ? ""
                            : $"<div class=\"ib-url\">{LanUrl}</div>";
                        ServePage(ctx, MfdPage.Html.Replace("{{LAN_URL_BLOCK}}", lanBlock));
                    }
                    else
                        ServePage(ctx, ClientPage.Html);
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

        private static void ServePage(HttpListenerContext ctx, string html)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(html);
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

        // ── Icon / weapon-image handler ──────────────────────────────────────────

        private static void ServePng(HttpListenerContext ctx, Dictionary<string, byte[]> dict, object dictLock, string queryKey)
        {
            string key = ctx.Request.QueryString[queryKey] ?? string.Empty;
            byte[]? png = null;
            if (key.Length > 0)
                lock (dictLock) dict.TryGetValue(key, out png);

            if (png == null)
            {
                ctx.Response.StatusCode = 404;
                try { ctx.Response.Close(); } catch { }
                return;
            }

            try
            {
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = "image/png";
                ctx.Response.ContentLength64 = png.Length;
                ctx.Response.OutputStream.Write(png, 0, png.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // ── MJPEG handler ──────────────────────────────────────────────────────

        // Long-lived multipart/x-mixed-replace response. Browsers render this directly in
        // an <img> tag — when a new JPEG is written, the image swaps in place.
        private static async Task HandleMjpegAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            const string boundary = "tgpframe";
            ctx.Response.StatusCode  = 200;
            ctx.Response.ContentType = "multipart/x-mixed-replace; boundary=" + boundary;
            ctx.Response.SendChunked = true;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.Headers.Add("X-Accel-Buffering", "no");

            long lastSeen = -1;
            Interlocked.Increment(ref _tgpSubscribers);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    byte[]? jpg; long id;
                    lock (_tgpLock) { jpg = _tgpJpg; id = _tgpFrameId; }

                    if (jpg != null && id != lastSeen)
                    {
                        lastSeen = id;
                        string head = "\r\n--" + boundary + "\r\nContent-Type: image/jpeg\r\nContent-Length: " + jpg.Length + "\r\n\r\n";
                        byte[] headBytes = Encoding.ASCII.GetBytes(head);
                        await ctx.Response.OutputStream.WriteAsync(headBytes, 0, headBytes.Length, ct).ConfigureAwait(false);
                        await ctx.Response.OutputStream.WriteAsync(jpg, 0, jpg.Length, ct).ConfigureAwait(false);
                        ctx.Response.OutputStream.Flush();
                    }

                    // Source publishes at 15 Hz (~66 ms/frame); 40 ms polls stay ahead so we
                    // don't drop alternate frames waiting for the next wake-up.
                    await Task.Delay(40, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* client disconnected, normal */ }
            finally
            {
                Interlocked.Decrement(ref _tgpSubscribers);
                try { ctx.Response.Close(); } catch { }
            }
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

        private static string Serialize(TelemetrySnapshot s)
        {
            string head = string.Format(CultureInfo.InvariantCulture,
                "{{\"ping\":false,\"t\":{0:0.000},\"name\":\"{1}\"," +
                "\"mission\":\"{2}\",\"mapName\":\"{3}\"," +
                "\"world\":{{\"x\":{4:0.0},\"y\":{5:0.0},\"z\":{6:0.0}}}," +
                "\"hdg\":{7:0.0},\"tas\":{8:0.0},\"agl\":{9:0.0},\"gear\":\"{10}\"," +
                "\"units\":{11},\"aircraft\":{12}," +
                "\"map\":{{\"valid\":{13},\"w\":{14:0.0},\"h\":{15:0.0},\"ox\":{16},\"oy\":{17}}}," +
                "\"iconOrient\":{18},\"iconScale\":{19:0.000}," +
                "\"flares\":{20},\"flaresMax\":{21},\"ewKJ\":{22:0.0},\"ewKJMax\":{23:0.0}," +
                "\"selWeapon\":\"{24}\",\"cmCat\":{25},\"tgpActive\":{26},",
                s.Time,
                EscapeJson(s.PlaneName ?? string.Empty),
                EscapeJson(s.MissionName ?? string.Empty),
                EscapeJson(s.MapName ?? string.Empty),
                s.WorldX, s.WorldY, s.WorldZ,
                s.Heading, s.TAS, s.AGL,
                s.GearDown ? "down" : "up",
                s.TotalUnits, s.TotalAircraft,
                s.MapValid ? "true" : "false",
                s.MapW, s.MapH,
                s.GridOffsetX, s.GridOffsetY,
                s.IconOrient ? "true" : "false",
                s.IconScale,
                s.Flares, s.FlaresMax, s.EwKJ, s.EwKJMax,
                EscapeJson(s.SelWeapon ?? string.Empty), s.CmCategory,
                s.TgpActive ? "true" : "false");

            return head + "\"loadout\":" + LoadoutArray(s.Loadout)
                        + ",\"colors\":{"
                        +   "\"f\":\"" + EscapeJson(s.ColFriendly ?? "#39ff14") + "\","
                        +   "\"e\":\"" + EscapeJson(s.ColHostile  ?? "#ff4040") + "\","
                        +   "\"n\":\"" + EscapeJson(s.ColNeutral  ?? "#9aa0a6") + "\"}"
                        + ",\"contacts\":" + UnitsArray(s.Units) + "}";
        }

        private static string UnitsArray(UnitInfo[]? units)
        {
            if (units == null || units.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < units.Length; i++)
            {
                UnitInfo u = units[i];
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"t\":\"{0}\",\"x\":{1:0.0},\"z\":{2:0.0},\"h\":{3:0.0},\"f\":{4},\"o\":{5},\"s\":{6:0.000},\"tg\":{7}}}",
                    EscapeJson(u.Type ?? string.Empty),
                    u.X, u.Z, u.Heading, u.Faction,
                    u.Orient ? "true" : "false", u.Scale,
                    u.Targeted ? 1 : 0);
            }
            return sb.Append(']').ToString();
        }

        private static string LoadoutArray(LoadoutEntry[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"n\":\"{0}\",\"a\":{1},\"f\":{2}}}",
                    EscapeJson(items[i].Name ?? string.Empty), items[i].Ammo, items[i].FullAmmo);
            }
            return sb.Append(']').ToString();
        }

        private static string EscapeJson(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
