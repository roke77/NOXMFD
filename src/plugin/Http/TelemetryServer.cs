using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOXMFD
{
    internal static class TelemetryServer
    {
        // TCP port the HTTP/SSE server listens on, and whether to auto-add the Windows LAN
        // gates (URL reservation + firewall rule) when the wildcard bind is denied. Both are
        // set from config by Configure() before Start(); the defaults match a fresh .cfg.
        internal static int Port { get; private set; } = 5005;
        private static bool _autoSetupLan = true;

        // Called from Plugin.Awake before Start(), with the values from the plugin .cfg.
        internal static void Configure(int port, bool autoSetupLan)
        {
            if (port > 0 && port <= 65535) Port = port;
            _autoSetupLan = autoSetupLan;
        }

        private static readonly object          _lifecycleLock = new object();
        private static HttpListener?            _listener;
        private static Thread?                  _acceptThread;
        private static CancellationTokenSource? _cts;

        private sealed class ActiveRequest
        {
            internal readonly long Id;
            internal readonly HttpListenerContext Context;
            internal readonly string Path;
            internal readonly string Remote;
            internal readonly DateTime StartedUtc = DateTime.UtcNow;
            internal readonly TaskCompletionSource<bool> Completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal int AbortLogged;

            internal ActiveRequest(long id, HttpListenerContext context, string path)
            {
                Id = id;
                Context = context;
                Path = path;
                Remote = context.Request.RemoteEndPoint?.ToString() ?? "unknown";
            }
        }

        private static readonly ConcurrentDictionary<long, ActiveRequest> _activeRequests =
            new ConcurrentDictionary<long, ActiveRequest>();
        private static long _nextRequestId;
        private const int AcceptJoinTimeoutMs = 500;
        private const int RequestStopTimeoutMs = 2500;
        private const int PortReleaseTimeoutMs = 500;

        private static TelemetrySnapshot _latest;
        private static long              _snapVersion;   // bumped on every Push/Reset
        private static readonly object   _lock = new object();

        // Shared serialized SSE frame, built at most once per snapshot version and reused by
        // every connected client (#2 in docs/performance.md). Without this, each of N clients
        // re-serialized the full snapshot every tick — wasteful with 3+ screens open.
        private static long             _frameVersion = -1;
        private static long             _frameSoiVersion = -1;   // see SetSoiTarget — the target moves independently of the snapshot
        private static bool             _frameMissionRunning;    // see SetMissionRunning — same reasoning, for a mission with no aircraft yet
        private static byte[]?          _frameBytes;
        private static readonly object  _frameLock = new object();

        // True between a mission loading and it ending, independent of whether a local aircraft
        // exists yet (MissionLifecycle.Update polls MissionManager.IsRunning and calls this every
        // frame). PushSnapshot only pushes once an aircraft is chosen, so without this a pilot who
        // loaded into a mission but hasn't picked an aircraft yet reads identically to the main
        // menu — a plain ping, "no mission" — even though a mission genuinely is running.
        private static volatile bool _missionRunning;
        public static void SetMissionRunning(bool running) { _missionRunning = running; }

        internal static byte[] NoIconPng => CapturedAssetEndpoint.NoIconPng;

        // Sent immediately to a fresh MJPEG connection when no real frame exists yet — a 4x4
        // dark-gray JPEG, precomputed offline (not generated at runtime: Texture2D.EncodeToJPG
        // needs the Unity main thread, and TgpMjpegHandler runs on the HTTP listener's own
        // thread). Without this, a client that connects before TgpFeed's pipeline has produced
        // its first frame (target lock + first capture + first async readback, confirmed to take
        // several seconds — docs/performance.md, 2026-08-23) sits on zero bytes, which some
        // browsers can mark the stream failed for and never recover from without a page reload
        // (docs/tgp-high-quality-mode.md).
        internal static readonly byte[] TgpPlaceholderJpg =
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
            0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x14, 0x0E, 0x0F, 0x12, 0x0F, 0x0D, 0x14,
            0x12, 0x10, 0x12, 0x17, 0x15, 0x14, 0x18, 0x1E, 0x32, 0x21, 0x1E, 0x1C, 0x1C, 0x1E, 0x3D, 0x2C,
            0x2E, 0x24, 0x32, 0x49, 0x40, 0x4C, 0x4B, 0x47, 0x40, 0x46, 0x45, 0x50, 0x5A, 0x73, 0x62, 0x50,
            0x55, 0x6D, 0x56, 0x45, 0x46, 0x64, 0x88, 0x65, 0x6D, 0x77, 0x7B, 0x81, 0x82, 0x81, 0x4E, 0x60,
            0x8D, 0x97, 0x8C, 0x7D, 0x96, 0x73, 0x7E, 0x81, 0x7C, 0xFF, 0xDB, 0x00, 0x43, 0x01, 0x15, 0x17,
            0x17, 0x1E, 0x1A, 0x1E, 0x3B, 0x21, 0x21, 0x3B, 0x7C, 0x53, 0x46, 0x53, 0x7C, 0x7C, 0x7C, 0x7C,
            0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C,
            0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C,
            0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0x7C, 0xFF, 0xC0,
            0x00, 0x11, 0x08, 0x00, 0x04, 0x00, 0x04, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11,
            0x01, 0xFF, 0xC4, 0x00, 0x1F, 0x00, 0x00, 0x01, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09,
            0x0A, 0x0B, 0xFF, 0xC4, 0x00, 0xB5, 0x10, 0x00, 0x02, 0x01, 0x03, 0x03, 0x02, 0x04, 0x03, 0x05,
            0x05, 0x04, 0x04, 0x00, 0x00, 0x01, 0x7D, 0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21,
            0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07, 0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23,
            0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0, 0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17,
            0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A,
            0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A,
            0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A,
            0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99,
            0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7,
            0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5,
            0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1,
            0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA, 0xFF, 0xC4, 0x00, 0x1F, 0x01, 0x00, 0x03,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
            0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0xFF, 0xC4, 0x00, 0xB5, 0x11, 0x00,
            0x02, 0x01, 0x02, 0x04, 0x04, 0x03, 0x04, 0x07, 0x05, 0x04, 0x04, 0x00, 0x01, 0x02, 0x77, 0x00,
            0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71, 0x13,
            0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0, 0x15,
            0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34, 0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26, 0x27,
            0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
            0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88,
            0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6,
            0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4,
            0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE2,
            0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9,
            0xFA, 0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00, 0xE5,
            0x68, 0xA2, 0x8A, 0x00, 0xFF, 0xD9,
        };

        public static bool WantsTgpFrames => TgpMjpegHandler.WantsFrames;

        // SOI focus + MAP cursor/action state — see src/plugin/Http/SoiFocus.cs. Kept as thin
        // facades so existing call sites (CommandDispatcher.cs, Keybinds.cs, HarmonyPatches.cs,
        // TgpManualControl.cs, SseHub.cs) don't need to change.
        internal static string SoiTarget           => SoiFocus.Target;
        internal static int    SoiTargetPane       => SoiFocus.TargetPane;
        internal const  string NativeTgpCid        = SoiFocus.NativeTgpCid;
        internal static bool   IsNativeTgpSoi      => SoiFocus.IsNativeTgpSoi;
        internal static bool   IsTgpSoi            => SoiFocus.IsTgpSoi;
        internal static long   SoiSeq              => SoiFocus.Seq;
        internal static string SoiAct              => SoiFocus.Act;
        internal static float  CursorX             => SoiFocus.CursorX;
        internal static float  CursorY             => SoiFocus.CursorY;
        internal static long   CursorSelSeq        => SoiFocus.CursorSelSeq;
        internal static long   MapActSeq           => SoiFocus.MapActSeq;
        internal static string MapAct              => SoiFocus.MapAct;

        internal static void ClaimNativeTgpSoi() => SoiFocus.ClaimNativeTgpSoi();
        internal static void ReleaseNativeTgpSoi() => SoiFocus.ReleaseNativeTgpSoi();
        internal static void ReportSoiPage(string cid, int pane, string page) => SoiFocus.ReportPage(cid, pane, page);
        internal static void SoiAction(string act) => SoiFocus.Action(act);
        internal static void SetCursorVector(float x, float y) => SoiFocus.SetCursorVector(x, y);
        internal static void CursorSelect() => SoiFocus.CursorSelect();
        internal static void SetCursorSelectHeld(bool held) => SoiFocus.SetCursorSelectHeld(held);
        internal static void MapAction(string act) => SoiFocus.MapAction(act);
        internal static void SoiReleaseOnDisconnect(string cid) => SoiFocus.ReleaseOnDisconnect(cid);
        internal static void SoiCycle(int dir) => SoiFocus.Cycle(dir);
        internal static void SetPaneCount(string cid, int n) => SoiFocus.SetPaneCount(cid, n);

        // Which locked target Next/Previous currently focuses (issue #62) — see TargetFocus.cs.
        // Read-only facade: the cycling itself needs the live player aircraft's target list, so it
        // runs from Keybinds.cs (which already touches Aircraft for its other binds) rather than here.
        internal static uint FocusedTargetId => TargetFocus.Id;

        internal static void SetRemoteCursorState(float x, float y, bool selectHeld)
        {
            if (RemoteInputState.SetCursor(x, y, selectHeld))
                Plugin.Log?.LogInfo($"[NOXMFD] remote cursor select {(selectHeld ? "ON" : "OFF")}.");
        }

        internal static void GetRemoteCursorState(out float x, out float y, out bool selectHeld) =>
            RemoteInputState.GetCursor(out x, out y, out selectHeld);

        internal static void SetRemoteFireState(string group, bool held)
        {
            // The rising edge is reported by SetFire itself (it alone knows the prior state); every
            // release is logged here regardless, since the browser only ever sends held:false once
            // per keyup — not as a 50ms keepalive like held:true — so it's already low-frequency.
            bool risingEdge = RemoteInputState.SetFire(group, held);
            if (risingEdge) Plugin.Log?.LogInfo($"[NOXMFD] remote fire '{group}' ON.");
            else if (!held) Plugin.Log?.LogInfo($"[NOXMFD] remote fire '{group}' OFF.");
        }

        internal static bool GetRemoteFireState(string group) => RemoteInputState.GetFire(group);

        // ── Lifecycle ──────────────────────────────────────────────────────────

        // Local-network URL (e.g. http://192.168.1.42:5005) — empty if the listener fell back
        // to localhost-only. Exposed through /config so the shell and MAIN pane can render it.
        internal static string LanUrl { get; private set; } = "";

        public static void Start()
        {
            lock (_lifecycleLock)
            {
                if (_listener?.IsListening == true)
                {
                    Plugin.Log?.LogDebug($"[NOXMFD] Server is already listening on port {Port}.");
                    return;
                }

                LanUrl = "";

                // Prefer binding all interfaces so a tablet on the LAN can reach us. Windows guards
                // that with two gates (see docs/networking.md): HTTP.sys needs a URL reservation for
                // the wildcard prefix, and the firewall needs an inbound allow for the port. If the
                // bind is denied we try to add both ourselves (works only when the game is elevated;
                // they persist, so it's one-time); otherwise we fall back to localhost-only and log
                // the manual fix.
                bool boundAll = TryBindWildcard();
                if (!boundAll && _autoSetupLan && TryAutoSetupLanAccess())
                    boundAll = TryBindWildcard();

                if (!boundAll)
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://localhost:{Port}/");
                    try
                    {
                        listener.Start();
                        _listener = listener;
                    }
                    catch (Exception ex)
                    {
                        try { listener.Close(); } catch { }
                        Plugin.Log?.LogError($"[NOXMFD] Failed to start on port {Port}: {ex.Message}");
                        return;
                    }
                }

                var activeListener = _listener!;
                var cts = new CancellationTokenSource();
                var acceptThread = new Thread(() => AcceptLoop(activeListener, cts.Token))
                {
                    IsBackground = true,
                    Name = "NOXMFD-Accept",
                };
                _cts = cts;
                _acceptThread = acceptThread;
                try { acceptThread.Start(); }
                catch (Exception ex)
                {
                    _cts = null;
                    _acceptThread = null;
                    _listener = null;
                    cts.Cancel();
                    try { activeListener.Close(); } catch { }
                    Plugin.Log?.LogError($"[NOXMFD] Failed to start the server thread on port {Port}: {ex.Message}");
                    return;
                }

                Plugin.Log?.LogInfo($"[NOXMFD] Server listening on http://localhost:{Port}/");
                if (boundAll)
                {
                    string lanIp = DetectLanIp();
                    if (!string.IsNullOrEmpty(lanIp))
                    {
                        LanUrl = $"http://{lanIp}:{Port}";
                        Plugin.Log?.LogInfo($"[NOXMFD] LAN access:  {LanUrl}/");
                    }
                }
                else
                {
                    Plugin.Log?.LogWarning($"[NOXMFD] LAN access disabled (localhost only). To enable it, run the game as Administrator once (auto-setup), or run these once in an elevated shell — see docs/networking.md:");
                    Plugin.Log?.LogWarning($"[NOXMFD]   netsh http add urlacl url=http://+:{Port}/ user=Everyone");
                    Plugin.Log?.LogWarning($"[NOXMFD]   netsh advfirewall firewall add rule name=\"NOXMFD ({Port})\" dir=in action=allow protocol=TCP localport={Port}");
                }
            }
        }

        // Try to bind the wildcard prefix (all interfaces). Returns false on the access-denied
        // HttpListenerException that a missing URL reservation raises; rethrows nothing else so
        // the caller can decide whether to attempt setup or fall back.
        private static bool TryBindWildcard()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{Port}/");
            try { listener.Start(); _listener = listener; return true; }
            catch (HttpListenerException)
            {
                try { listener.Close(); } catch { }
                return false;
            }
            catch (Exception ex)
            {
                try { listener.Close(); } catch { }
                Plugin.Log?.LogWarning($"[NOXMFD] Wildcard bind on port {Port} failed: {ex.Message}");
                return false;
            }
        }

        // Add the two Windows LAN gates via netsh: a URL reservation for the wildcard prefix and
        // an inbound firewall allow for the port. Both persist, so this runs only on the first
        // launch where the bind is denied. Both need admin; when the game isn't elevated they
        // fail cleanly (non-zero exit, and no UAC prompt under UseShellExecute=false) and we fall
        // back to localhost. Returns true if the URL reservation succeeded — that's what unblocks
        // the bind; a failed firewall rule only means a tablet may not reach us yet.
        private static bool TryAutoSetupLanAccess()
        {
            // sddl D:(A;;GX;;;WD): grant the generic-execute right a URL reservation needs to the
            // Everyone/World SID (WD). SDDL aliases are locale-independent, unlike `user=Everyone`.
            bool acl = RunNetsh($"http add urlacl url=http://+:{Port}/ sddl=D:(A;;GX;;;WD)");
            bool fw  = RunNetsh($"advfirewall firewall add rule name=\"NOXMFD ({Port})\" dir=in action=allow protocol=TCP localport={Port}");
            if (acl) Plugin.Log?.LogInfo($"[NOXMFD] LAN auto-setup: urlacl=ok, firewall={(fw ? "ok" : "failed")}.");
            else     Plugin.Log?.LogInfo("[NOXMFD] LAN auto-setup couldn't add the URL reservation (needs the game run as Administrator once).");
            return acl;
        }

        private static bool RunNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args)
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.WaitForExit(5000);
                    return p.HasExited && p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] netsh '{args}' failed to run: {ex.Message}");
                return false;
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

        // Public so a process-replacement plugin can request cooperative shutdown through
        // reflection without taking a compile-time dependency on NOXMFD's internal server type.
        public static void Stop()
        {
            HttpListener? listener;
            Thread? acceptThread;
            CancellationTokenSource? cts;
            var shutdownWatch = Stopwatch.StartNew();

            lock (_lifecycleLock)
            {
                listener = _listener;
                acceptThread = _acceptThread;
                cts = _cts;
                _listener = null;
                _acceptThread = null;
                _cts = null;
                LanUrl = "";
            }

            // Application.quitting and a replacement launcher may race to stop the same server.
            // Only the first caller owns the listener resources; another caller can still help
            // abort a handler that has not finished unwinding.
            if (listener == null && acceptThread == null && cts == null && _activeRequests.IsEmpty) return;

            Plugin.Log?.LogInfo($"[NOXMFD] Stopping server on port {Port}: activeRequests={_activeRequests.Count}.");
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }

            // Cancellation lets cooperative loops leave normally. Aborting each response also
            // breaks a WriteAsync that is blocked on a slow or disconnected remote client.
            AbortActiveRequests();

            try { listener?.Abort(); }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] Listener abort on port {Port} failed: {ex.Message}");
            }
            try { listener?.Close(); }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] Listener close on port {Port} failed: {ex.Message}");
            }

            bool acceptStopped = true;
            if (acceptThread != null && acceptThread != Thread.CurrentThread)
            {
                try { acceptStopped = acceptThread.Join(AcceptJoinTimeoutMs); }
                catch (Exception ex)
                {
                    acceptStopped = false;
                    Plugin.Log?.LogWarning($"[NOXMFD] Accept-thread shutdown check failed: {ex.Message}");
                }
            }

            // The accept loop can receive one context immediately before cancellation. Joining it
            // seals registration; this second pass catches that final request without duplicate logs.
            AbortActiveRequests();
            bool requestsStopped = WaitForActiveRequests(RequestStopTimeoutMs);
            if (requestsStopped)
                Plugin.Log?.LogInfo("[NOXMFD] All HTTP request handlers stopped.");
            else
            {
                Plugin.Log?.LogWarning($"[NOXMFD] HTTP request cleanup timed out after {RequestStopTimeoutMs} ms: remaining={_activeRequests.Count}.");
                LogRemainingRequests();
            }

            // A synchronous short-response handler runs on the accept thread. It may finish only
            // after its response is aborted above, so re-check after the handler wait before warning.
            if (!acceptStopped && acceptThread != null && acceptThread != Thread.CurrentThread)
            {
                try { acceptStopped = acceptThread.Join(0); } catch { }
            }
            if (!acceptStopped)
                Plugin.Log?.LogWarning("[NOXMFD] Accept thread is still running after HTTP request cleanup.");

            bool portReleased = WaitForPortRelease(Port, PortReleaseTimeoutMs);
            if (requestsStopped && acceptStopped && portReleased)
                Plugin.Log?.LogInfo($"[NOXMFD] Port {Port} released after {shutdownWatch.ElapsedMilliseconds} ms.");
            else if (!portReleased)
                Plugin.Log?.LogWarning($"[NOXMFD] Port {Port} is still listening after {shutdownWatch.ElapsedMilliseconds} ms.");
        }

        // Called from Unity main thread — just stores the latest snapshot.
        public static void Push(in TelemetrySnapshot snap)
        {
            lock (_lock) { _latest = snap; _snapVersion++; }
        }

        // Returns the SSE frame bytes for the current snapshot, serializing at most once per
        // version. The first client to ask after a new Push builds it (under _frameLock, so a
        // second concurrent client waits rather than duplicating the work); everyone else reuses
        // the cached bytes. `valid` mirrors the snapshot's Valid flag (drives the 10 Hz vs 1 Hz
        // ping cadence). Runs on background SSE threads — never the Unity main thread.
        internal static byte[] GetFrameBytes(out bool valid)
        {
            lock (_frameLock)
            {
                long v;
                TelemetrySnapshot snap;
                lock (_lock) { v = _snapVersion; snap = _latest; }
                valid = snap.Valid;

                // SOI focus can move without a new snapshot, so it versions the cache too — a ping
                // frame at the main menu is otherwise identical forever and the change never ships.
                // MissionRunning is the same idea: a mission loading/ending with no aircraft chosen
                // yet changes nothing else about a ping frame, so it needs the same invalidation.
                long sv = SoiFocus.Version;
                bool mr = _missionRunning;
                if (_frameVersion == v && _frameSoiVersion == sv && _frameMissionRunning == mr && _frameBytes != null) return _frameBytes;
                _frameSoiVersion = sv;
                _frameMissionRunning = mr;

                string payload = snap.Valid
                    ? TelemetryJson.Serialize(snap, SoiFocus.SoiJson(), ImmersionState.MasterArmsOn,
                        ImmersionState.CombatMode switch { CombatMode.AirToAir => "aa", CombatMode.AirToGround => "ag", _ => "all" },
                        ExtensionRegistry.SlicesJson())
                    : "{\"ping\":true,\"missionRunning\":" + (mr ? "true" : "false") + "," + SoiFocus.SoiJson() + "}";
                _frameBytes   = Encoding.UTF8.GetBytes("data: " + payload + "\n\n");
                _frameVersion = v;

                return _frameBytes;
            }
        }

        public static void SetMapImage(byte[] png) => CapturedAssetEndpoint.SetMapImage(png);
        public static void SetIcon(string unitName, byte[] png) => CapturedAssetEndpoint.SetIcon(unitName, png);
        public static void SetWeaponIcon(string name, byte[] png) => CapturedAssetEndpoint.SetWeaponIcon(name, png);
        public static void SetTgtIcon(string name, byte[] png) => CapturedAssetEndpoint.SetTgtIcon(name, png);
        public static void SetBdfIcon(string name, byte[] png) => CapturedAssetEndpoint.SetBdfIcon(name, png);
        public static void SetBuildingIcon(string name, byte[] png) => CapturedAssetEndpoint.SetBuildingIcon(name, png);
        public static void SetHudCategoryIcon(string name, byte[] png) => CapturedAssetEndpoint.SetHudCategoryIcon(name, png);
        public static void SetCmIcon(string key, byte[] png) => CapturedAssetEndpoint.SetCmIcon(key, png);
        public static void SetAirframeImage(string unitName, string partName, byte[] png) => CapturedAssetEndpoint.SetAirframeImage(unitName, partName, png);
        public static void SetAirframeLayout(string unitName, string json) => CapturedAssetEndpoint.SetAirframeLayout(unitName, json);

        public static void PushTgpFrame(byte[] jpg) => TgpMjpegHandler.PushFrame(jpg);
        public static void ClearTgpFrame() => TgpMjpegHandler.ClearFrame();

        // Called from Unity main thread when a mission ends — clears all per-mission state so
        // the client drops back to "no mission" and wipes its display. Icons are static
        // per-type assets and stay cached across missions.
        public static void Reset()
        {
            lock (_lock)    { _latest = default; _snapVersion++; }
            CapturedAssetEndpoint.ClearMissionState();
        }

        // ── Accept loop ────────────────────────────────────────────────────────

        private static void AcceptLoop(HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx  = listener.GetContext();
                    var path = ctx.Request.Url?.AbsolutePath ?? "/";

                    long requestId = Interlocked.Increment(ref _nextRequestId);
                    // TrackRequestAsync observes every handler exception and exposes a separate
                    // completion task through ActiveRequest, so shutdown can wait without relying
                    // on this fire-and-continue accept loop retaining the returned Task.
                    _ = TrackRequestAsync(requestId, ctx, path, ct);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Plugin.Log?.LogError($"[NOXMFD] Accept error: {ex.Message}");
                }
            }
        }

        private static async Task TrackRequestAsync(
            long id, HttpListenerContext context, string path, CancellationToken ct)
        {
            var request = new ActiveRequest(id, context, path);
            _activeRequests[id] = request;

            try
            {
                ct.ThrowIfCancellationRequested();
                await TelemetryHttpRouter.RouteAsync(context, path, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    Plugin.Log?.LogWarning($"[NOXMFD] HTTP request {path} from {request.Remote} failed: {ex.Message}");
            }
            finally
            {
                try { context.Response.Close(); } catch { }
                _activeRequests.TryRemove(id, out _);
                request.Completion.TrySetResult(true);
            }
        }

        private static void AbortActiveRequests()
        {
            foreach (ActiveRequest request in _activeRequests.Values)
            {
                if (Interlocked.Exchange(ref request.AbortLogged, 1) == 0)
                {
                    double age = (DateTime.UtcNow - request.StartedUtc).TotalSeconds;
                    Plugin.Log?.LogInfo(
                        $"[NOXMFD] Aborting request #{request.Id}: path={request.Path} " +
                        $"remote={request.Remote} age={age.ToString("F1", CultureInfo.InvariantCulture)}s.");
                }

                try { request.Context.Response.Abort(); } catch { }
            }
        }

        private static bool WaitForActiveRequests(int timeoutMs)
        {
            var requests = new List<ActiveRequest>(_activeRequests.Values);
            if (requests.Count == 0) return true;

            var completions = new Task[requests.Count];
            for (int i = 0; i < requests.Count; i++)
                completions[i] = requests[i].Completion.Task;

            try { return Task.WaitAll(completions, timeoutMs) && _activeRequests.IsEmpty; }
            catch (AggregateException) { return _activeRequests.IsEmpty; }
        }

        private static void LogRemainingRequests()
        {
            foreach (ActiveRequest request in _activeRequests.Values)
            {
                double age = (DateTime.UtcNow - request.StartedUtc).TotalSeconds;
                Plugin.Log?.LogWarning(
                    $"[NOXMFD] Request still active #{request.Id}: path={request.Path} " +
                    $"remote={request.Remote} age={age.ToString("F1", CultureInfo.InvariantCulture)}s.");
            }
        }

        private static bool WaitForPortRelease(int port, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            do
            {
                if (!IsLoopbackPortListening(port)) return true;
                Thread.Sleep(25);
            }
            while (watch.ElapsedMilliseconds < timeoutMs);

            return !IsLoopbackPortListening(port);
        }

        private static bool IsLoopbackPortListening(int port)
        {
            try
            {
                using (var client = new TcpClient(AddressFamily.InterNetwork))
                {
                    IAsyncResult connect = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    using (WaitHandle waitHandle = connect.AsyncWaitHandle)
                    {
                        try
                        {
                            if (!waitHandle.WaitOne(100)) return true;
                            client.EndConnect(connect);
                            return client.Connected;
                        }
                        catch (SocketException) { return false; }
                    }
                }
            }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
            catch { return true; } // An inconclusive probe must not be reported as a released port.
        }

        // ── Response helpers ────────────────────────────────────────────────────
        // The HTTP response mechanics every Serve* handler repeats regardless of what it's
        // serving: status/content-type/length/write/close, plus Cache-Control for the small
        // on-demand JSON snapshots. Orthogonal to *what* gets serialized (that's the JSON-writer
        // layer docs/server-hardening.md already scopes) — this is just the plumbing.
        internal static void WriteJson(HttpListenerContext ctx, string json)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        internal static void WriteBinary(HttpListenerContext ctx, byte[] body, string contentType)
        {
            try
            {
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = contentType;
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        internal static void ServeSoiInstances(HttpListenerContext ctx) => SseHub.ServeInstances(ctx);

        // The in-game HUD OPTIONS state, as JSON, for the HUD page to render. Built on the main
        // thread by RefreshHudOptions (below) and cached here — HUD options change only on a toggle,
        // so this is fetched on demand rather than streamed, like /config. "{}" until a mission with
        // a live HUDOptions is up; the page treats that as "unavailable".
        internal static volatile string HudOptionsJson = "{}";

        // Snapshot HUDOptions into HudOptionsJson. MAIN THREAD ONLY — reads live game objects; the
        // reader calls it on the slow (1 Hz) tick, since options change only when the pilot toggles.
        //   modes      : the HUDMode tab names, plus the current index.
        //   categories : one bool per listCategories entry (FRIENDLY/ENEMY/AIRCRAFT/…). The names are
        //                the page's, by index — the game assigns the category order in its inspector
        //                and exposes no display name here, so the page carries the fixed labels and
        //                this emits only their count + state. (Vehicles/buildings DO have names,
        //                below, from the Encyclopedia the game built the toggles from.)
        //   vehicles   : {n:name, on} per listVehicleTypes entry, name from Encyclopedia (parallel).
        //   buildings  : {n:name, on} per listBuildingTypes entry.
        public static void RefreshHudOptions()
        {
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) { HudOptionsJson = "{}"; return; }

            var sb = new StringBuilder(512);
            sb.Append('{');

            // modes — currentMode only tracks AutomaticToggle's weapon-driven switches, not a manual
            // tab click (ToggleButtons never touches it — confirmed in HUDOptions.decompiled.cs), so a
            // player-selected mode would otherwise never show here. The lit tab (listModes[i].status)
            // is the one state a manual click always updates; fall back to currentMode only if for some
            // reason no tab is lit.
            var modes = opt.listModes;
            int modeIndex = (int)opt.currentMode;
            for (int i = 0; modes != null && i < modes.Count; i++)
            {
                if (modes[i] != null && modes[i].status) { modeIndex = i; break; }
            }
            sb.Append("\"mode\":").Append(modeIndex).Append(",\"modes\":[");
            string[] modeNames = Enum.GetNames(typeof(HUDOptions.HUDMode));
            for (int i = 0; i < modeNames.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(EscapeJson(modeNames[i])).Append('"');
            }
            sb.Append(']');

            // categories — booleans only (names live in the page)
            sb.Append(",\"categories\":[");
            var cats = opt.listCategories;
            for (int i = 0; cats != null && i < cats.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(cats[i] != null && cats[i].maximized ? "true" : "false");
            }
            sb.Append(']');

            // vehicles / buildings — {n,on}, names from the Encyclopedia the toggles were built from
            AppendTypeList(sb, ",\"vehicles\":", opt.listVehicleTypes, Encyclopedia.i?.vehicleTypes);
            AppendTypeList(sb, ",\"buildings\":", opt.listBuildingTypes, Encyclopedia.i?.buildingTypes);

            // declutter — the mod's OWN native-HUD hide flags (HudDeclutterConfig), a separate axis from
            // the HUDOptions unit-icon toggles above. true = that native widget is currently hidden.
            sb.Append(",\"declutter\":{\"weapon\":").Append(HudDeclutterConfig.HideWeaponAmmo ? "true" : "false")
              .Append(",\"minimap\":").Append(HudDeclutterConfig.HideMinimap ? "true" : "false")
              .Append(",\"boxes\":").Append(HudDeclutterConfig.HideTopBoxes ? "true" : "false")
              .Append(",\"feed\":").Append(HudDeclutterConfig.HideKillFeed ? "true" : "false")
              .Append('}');

            // Current HUD preset (issue #50 follow-up) — just the slot the bottom label names;
            // the full 5-slot list for the LOAD picker is a separate on-demand fetch (/hud-presets),
            // not part of this 1.2s poll payload.
            sb.Append(",\"preset\":{\"index\":").Append(HudPresetStore.CurrentIndex)
              .Append(",\"name\":\"").Append(EscapeJson(HudPresetStore.CurrentName)).Append("\"}");

            sb.Append('}');
            HudOptionsJson = sb.ToString();
        }

        private static void AppendTypeList(StringBuilder sb, string key,
            System.Collections.Generic.List<HUDOptions_ToggleButton>? toggles,
            System.Collections.Generic.List<Encyclopedia.UnitType>? types)
        {
            sb.Append(key).Append('[');
            if (toggles == null)
            {
                sb.Append(']');
                return;
            }
            int n = toggles.Count;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                // Name from the parallel Encyclopedia list SetupList built the toggles from; fall
                // back to the index if the lists ever disagree, so a mismatch degrades rather than throws.
                string name = (types != null && i < types.Count && types[i] != null) ? types[i].typeName : ("#" + i);
                bool on = toggles[i] != null && toggles[i].status;
                sb.Append("{\"n\":\"").Append(EscapeJson(name)).Append("\",\"on\":").Append(on ? "true" : "false").Append('}');
            }
            sb.Append(']');
        }

        internal static void Redirect(HttpListenerContext ctx, string location)
        {
            try
            {
                ctx.Response.StatusCode = 302;
                ctx.Response.RedirectLocation = location;
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // The MAP cursor's own SSE payload (docs/map-cursor.md) — see SoiFocus.CursorJson. Kept as a
        // facade since SseHub.cs calls it as TelemetryServer.CursorJson().
        internal static string CursorJson() => SoiFocus.CursorJson();

        // Kept in JsonLite.cs so pure callers like RouteStore.cs can compile standalone in a test
        // project without pulling this file's game touchpoints in. This wrapper preserves this
        // file's local call sites.
        internal static string EscapeJson(string s) => JsonLite.EscapeJson(s);
    }
}
