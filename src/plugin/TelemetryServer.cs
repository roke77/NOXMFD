using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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

        // SSE cadences. The loop ticks at CursorTickMs (the MAP cursor's rate — a continuous analog
        // signal, so latency here is felt directly as a heavy, lagging crosshair) and emits the
        // telemetry frame every FrameEveryMs. 10 Hz for telemetry both during a mission and at the
        // main menu, where SOI focus changes must still feel immediate.
        private const int CursorTickMs = 16;    // ~60 Hz, but only ~60 bytes and only when it changes
        private const int FrameEveryMs = 100;   // 10 Hz

        private static HttpListener?           _listener;
        private static Thread?                 _acceptThread;
        private static CancellationTokenSource _cts = new CancellationTokenSource();

        private static TelemetrySnapshot _latest;
        private static long              _snapVersion;   // bumped on every Push/Reset
        private static readonly object   _lock = new object();

        // Shared serialized SSE frame, built at most once per snapshot version and reused by
        // every connected client (#2 in docs/performance.md). Without this, each of N clients
        // re-serialized the full snapshot every tick — wasteful with 3+ screens open.
        private static long             _frameVersion = -1;
        private static long             _frameSoiVersion = -1;   // see SetSoiTarget — the target moves independently of the snapshot
        private static byte[]?          _frameBytes;
        private static readonly object  _frameLock = new object();

        // Captured in-game map image (PNG), set from the Unity main thread.
        private static byte[]?          _mapPng;
        private static readonly object  _mapLock = new object();

        // Per-aircraft-type map icons (PNG), keyed by unitName.
        private static readonly Dictionary<string, byte[]> _icons    = new Dictionary<string, byte[]>();
        private static readonly object                     _iconLock = new object();

        // A 1×1 fully-transparent PNG registered for types that have no map icon (buildings, etc.).
        // Serving this with HTTP 200 — instead of 404 — stops the client re-requesting icon-less
        // types and keeps the browser console clean; the client spots the 1×1 size and falls back
        // to its generic square marker.
        internal static readonly byte[] NoIconPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M8AAAMBAQDJ/pLvAAAAAElFTkSuQmCC");

        // Per-weapon-type icons (PNG), keyed by weapon display name.
        private static readonly Dictionary<string, byte[]> _weaponIcons = new Dictionary<string, byte[]>();
        private static readonly object                     _weaponLock  = new object();

        // Per-countermeasure icons (PNG), keyed by short name ("flares", "jammer").
        private static readonly Dictionary<string, byte[]> _cmIcons = new Dictionary<string, byte[]>();
        private static readonly object                     _cmLock  = new object();

        // TGT filter vehicle-type icons (PNG), keyed by vehicle typeName ("TRUCK" … "RDR") — the
        // same names the "tgt" telemetry block's vehicle row carries. Served at /tgt-icon?type=.
        private static readonly Dictionary<string, byte[]> _tgtIcons = new Dictionary<string, byte[]>();
        private static readonly object                     _tgtLock  = new object();

        // BDF ship-type icons (PNG), keyed by ship typeName ("CV" … "LC") — the same names the
        // "bdf" telemetry block's ship row carries (docs/bdf-page.md). Served at /bdf-icon?type=.
        private static readonly Dictionary<string, byte[]> _bdfIcons = new Dictionary<string, byte[]>();
        private static readonly object                     _bdfLock  = new object();

        // HUD-page building-type icons (PNG), keyed by building typeName ("CIV" … "AMMO"). A separate
        // map from _tgtIcons on purpose: a name like "RDR" is BOTH a vehicle and a building type, so
        // sharing one keyspace would collide. Served at /building-icon?type=.
        private static readonly Dictionary<string, byte[]> _buildingIcons = new Dictionary<string, byte[]>();
        private static readonly object                     _buildingLock  = new object();

        // HUD OPTIONS category-row icons (PNG) — AIRCRAFT/MISSILES/VEHICLES/BUILDINGS/SHIPS, keyed by
        // the same fixed label the HUD page's CATEGORY_LABELS carries (the game exposes no per-category
        // name to key by instead). FRIENDLY/ENEMY have no entry — the game draws no glyph on those rows
        // either. Served at /hud-cat-icon?cat=.
        private static readonly Dictionary<string, byte[]> _hudCatIcons = new Dictionary<string, byte[]>();
        private static readonly object                     _hudCatLock  = new object();

        // Airframe silhouette assets. Images keyed by "unitName|partName" — partName is the
        // GameObject name from Aircraft.partLookup (e.g. "wing1_L") or "__bg" for the background
        // silhouette. Layouts keyed by unitName, value is a JSON descriptor of part placements.
        private static readonly Dictionary<string, byte[]> _airframeImages = new Dictionary<string, byte[]>();
        private static readonly Dictionary<string, string> _airframeLayouts = new Dictionary<string, string>();
        private static readonly object                     _airframeLock    = new object();

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

        // ── Connected MFD instances (SOI — docs/keybinds-page.md) ──────────────
        // One /stream connection IS one MFD instance: HandleSseAsync runs for exactly as long as a
        // browser sits on the display, so registering on entry and dropping in its existing finally
        // is the whole of the registry. Nothing else needs to track anything.
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
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, MfdInstance>
            _instances = new System.Collections.Concurrent.ConcurrentDictionary<long, MfdInstance>();
        private static long _nextConn;

        // Snapshot of the live instances, oldest connection first — a stable order to cycle SOI
        // through, unlike the dictionary's own.
        internal static List<MfdInstance> Instances()
        {
            var all = new List<MfdInstance>(_instances.Values);
            all.Sort((a, b) => a.Conn.CompareTo(b.Conn));
            return all;
        }

        // ── SOI focus ──────────────────────────────────────────────────────────
        // Which instance the SOI keys drive, as its cid. Broadcast in every frame so each client can
        // compare it against its own — see the shared-frame note on GetFrameBytes. Empty = nothing
        // focused, which is the state at startup and after the focused display disconnects.
        //
        // _soiVersion is what keeps the frame cache honest: the cache is keyed on the snapshot
        // version, and the target can change without a new snapshot — at the main menu, where frames
        // are 1 Hz pings, a stale cached frame would otherwise hide the change indefinitely.
        // Focus is a SURFACE, not a whole document: a cid PLUS which of that instance's surfaces
        // (panes/portals) is focused. An instance shows 1 surface in full view, 2 in a classic split,
        // up to 4 F-35 portals — the client reports the count (soi.panes). _soiTargetPane is -1 when
        // nothing is focused.
        private static string _soiTargetCid  = string.Empty;
        private static int    _soiTargetPane = -1;
        private static long   _soiVersion;
        // Guards every change of focus. Connects and disconnects arrive on their own threadpool
        // threads and both can move the target, so "is anything focused?" and the write that follows
        // it have to be one step — otherwise two displays connecting together both see no focus and
        // the second silently steals it.
        private static readonly object _soiLock = new object();
        internal static string SoiTarget     => Volatile.Read(ref _soiTargetCid);
        internal static int    SoiTargetPane => Volatile.Read(ref _soiTargetPane);

        private static void SetSoiTarget(string cid, int pane) { lock (_soiLock) SetSoiTargetLocked(cid, pane); }

        private static void SetSoiTargetLocked(string cid, int pane)
        {
            if (cid.Length == 0) pane = -1;   // "nothing focused" has no surface
            if (string.Equals(_soiTargetCid, cid, StringComparison.Ordinal) && _soiTargetPane == pane) return;
            Volatile.Write(ref _soiTargetCid, cid);
            Volatile.Write(ref _soiTargetPane, pane);
            Interlocked.Increment(ref _soiVersion);
        }

        // The last SOI key pressed, and a counter that makes it idempotent to broadcast. A client acts
        // when the counter CHANGES and ignores the field otherwise, so a duplicated frame can't
        // double-press and a dropped one costs at most a repeat of the same value. The plugin has no
        // idea what "up" means on a page — it only says a key was pressed.
        private static long   _soiSeq;
        private static string _soiAct = string.Empty;
        internal static long   SoiSeq => Interlocked.Read(ref _soiSeq);
        internal static string SoiAct => Volatile.Read(ref _soiAct);

        internal static void SoiAction(string act)
        {
            lock (_soiLock)
            {
                Volatile.Write(ref _soiAct, act);
                Interlocked.Increment(ref _soiSeq);
                Interlocked.Increment(ref _soiVersion);   // rebuild the cached frame so the press ships
            }
        }

        // ── MAP cursor (docs/map-cursor.md) ──────────────────────────────────────
        // A velocity, not a position: the plugin only says which way the cursor should move this
        // frame ([-1,1] per axis — held direction keys give ±1, an axis gives its analog deflection),
        // and the focused MAP integrates it locally against real elapsed time. That's what lets a
        // digital key and an analog axis drive the exact same field, and what avoids trying to
        // animate smooth motion over a 10 Hz transport. Only meaningful while the SOI focus is a MAP;
        // otherwise the map that would read it isn't there to read it.
        private static float _cursorX, _cursorY;
        internal static float CursorX => Volatile.Read(ref _cursorX);
        internal static float CursorY => Volatile.Read(ref _cursorY);

        // Quantized to 1% before comparing: an analog axis jitters in the last decimals even when the
        // pilot is holding it still, and every distinct value would otherwise bump _soiVersion and
        // force a full frame re-serialize on the next tick. 1% is far below what the eye can see in
        // cursor speed, so this costs nothing visible and keeps a steady hold genuinely steady.
        // None of the cursor writers touch _soiVersion: they ride their own SSE event (CursorJson),
        // so invalidating the shared telemetry frame for them would re-serialize the whole snapshot
        // for a value that frame no longer carries.
        internal static void SetCursorVector(float x, float y)
        {
            x = (float)Math.Round(x, 2);
            y = (float)Math.Round(y, 2);
            lock (_soiLock)
            {
                if (_cursorX == x && _cursorY == y) return;   // steady hold — nothing to ship
                Volatile.Write(ref _cursorX, x);
                Volatile.Write(ref _cursorY, y);
            }
        }

        // Cursor Select: a discrete press, same idempotent-counter shape as SoiAction/SoiSeq — the
        // map acts when this changes, not on any particular value.
        private static long _cursorSelSeq;
        internal static long CursorSelSeq => Interlocked.Read(ref _cursorSelSeq);

        internal static void CursorSelect()
        {
            Interlocked.Increment(ref _cursorSelSeq);
        }

        // Cursor Select's LIVE held state (docs/page-cursor.md) — separate from the edge counter
        // above: MAP only ever wants the instant-select edge, but a page with its own tap/long-press
        // controls (TGT) needs to see the press through to release to tell the two apart, the same
        // way a real pointerdown/pointerup pair would. Rides the same 'cursor' SSE event as x/y, so
        // it costs nothing extra to transport — a change here just makes that event fire sooner.
        private static bool _cursorSelHeld;
        internal static void SetCursorSelectHeld(bool held) => Volatile.Write(ref _cursorSelHeld, held);

        // MAP view actions (Follow / Zoom In / Zoom Out) — binds for what the bezel's FLW/Z+/Z- keys
        // already do. Same idempotent-counter shape again: the focused map's mfd.js/f35.js forwarding
        // reads mapAct only when mapActSeq changes, then maps the string straight onto the existing
        // toggle-follow/zoom-in/zoom-out postMessage it already sends for those bezel keys.
        private static long   _mapActSeq;
        private static string _mapAct = string.Empty;
        internal static long   MapActSeq => Interlocked.Read(ref _mapActSeq);
        internal static string MapAct    => Volatile.Read(ref _mapAct);

        internal static void MapAction(string act)
        {
            lock (_soiLock)
            {
                Volatile.Write(ref _mapAct, act);
                Interlocked.Increment(ref _mapActSeq);
            }
        }

        // A display drops. If it was the focused one, focus clears — it does NOT move to another
        // display on its own. SOI is opt-in: the ring only ever appears once the pilot presses a SOI
        // key (SoiCycle from empty), so it must never re-appear on a display they didn't pick. A
        // mouse/touch user who never touches the keys therefore never sees it. Nothing to do unless
        // the dropped display held focus.
        private static void SoiReleaseOnDisconnect(string cid)
        {
            lock (_soiLock)
            {
                if (!string.Equals(_soiTargetCid, cid, StringComparison.Ordinal)) return;
                var all = Instances();   // the disconnecting one is already out of the registry
                // A duplicated tab copies its cid, so a twin may still be holding that display open —
                // keep focus if so, otherwise clear it (the next SOI keypress re-picks a display).
                if (all.Exists(x => string.Equals(x.Cid, cid, StringComparison.Ordinal))) return;
                SetSoiTargetLocked(string.Empty, -1);
            }
        }

        // The flat ring SOI cycles through: every instance's every surface, instance-major and
        // surface-minor, oldest connection first. Deduped by cid so a twin (same cid, second
        // connection) doesn't put the same document in the ring twice. Built under _soiLock by the
        // callers that need it.
        private static List<(string cid, int pane)> SoiRingLocked()
        {
            var ring = new List<(string, int)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var inst in Instances())
            {
                if (!seen.Add(inst.Cid)) continue;
                for (int p = 0; p < inst.PaneCount; p++) ring.Add((inst.Cid, p));
            }
            return ring;
        }

        // Move focus one step along that ring. From no focus, NEXT takes the first surface and PREV
        // the last, so either key lights something up on the first press.
        internal static void SoiCycle(int dir)
        {
            lock (_soiLock)
            {
                var ring = SoiRingLocked();
                if (ring.Count == 0) { SetSoiTargetLocked(string.Empty, -1); return; }

                int i = ring.FindIndex(s => string.Equals(s.cid, _soiTargetCid, StringComparison.Ordinal)
                                            && s.pane == _soiTargetPane);
                int next = i < 0
                    ? (dir >= 0 ? 0 : ring.Count - 1)
                    : ((i + dir) % ring.Count + ring.Count) % ring.Count;
                SetSoiTargetLocked(ring[next].cid, ring[next].pane);
            }
        }

        // A client reports how many surfaces it now shows (soi.panes). Update every instance on that
        // cid (twins share one), and if that display is the focused one and a merge has shrunk it
        // below the focused surface, clamp — the pilot stays on the glass they were driving rather
        // than being dropped. This is the one focus move not caused by a keypress, and it never
        // leaves the instance.
        internal static void SetPaneCount(string cid, int n)
        {
            if (cid.Length == 0) return;
            if (n < 1) n = 1;
            lock (_soiLock)
            {
                foreach (var inst in Instances())
                    if (string.Equals(inst.Cid, cid, StringComparison.Ordinal)) inst.PaneCount = n;

                if (string.Equals(_soiTargetCid, cid, StringComparison.Ordinal) && _soiTargetPane >= n)
                    SetSoiTargetLocked(cid, n - 1);
            }
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

        // ── Lifecycle ──────────────────────────────────────────────────────────

        // Local-network URL (e.g. http://192.168.1.42:5005) — empty if the listener fell back
        // to localhost-only. Exposed through /config so the shell and MAIN pane can render it.
        internal static string LanUrl { get; private set; } = "";

        public static void Start()
        {
            _cts = new CancellationTokenSource();

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
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                try { _listener.Start(); }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"[NOXMFD] Failed to start on port {Port}: {ex.Message}");
                    return;
                }
            }

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "NOXMFD-Accept" };
            _acceptThread.Start();

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

        // Try to bind the wildcard prefix (all interfaces). Returns false on the access-denied
        // HttpListenerException that a missing URL reservation raises; rethrows nothing else so
        // the caller can decide whether to attempt setup or fall back.
        private static bool TryBindWildcard()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{Port}/");
            try { listener.Start(); _listener = listener; return true; }
            catch (HttpListenerException) { return false; }
            catch (Exception ex)
            {
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

        public static void Stop()
        {
            _cts.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener = null;
            Plugin.Log?.LogInfo("[NOXMFD] Server stopped.");
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
        private static byte[] GetFrameBytes(out bool valid)
        {
            lock (_frameLock)
            {
                long v;
                TelemetrySnapshot snap;
                lock (_lock) { v = _snapVersion; snap = _latest; }
                valid = snap.Valid;

                // SOI focus can move without a new snapshot, so it versions the cache too — a ping
                // frame at the main menu is otherwise identical forever and the change never ships.
                long sv = Interlocked.Read(ref _soiVersion);
                if (_frameVersion == v && _frameSoiVersion == sv && _frameBytes != null) return _frameBytes;
                _frameSoiVersion = sv;

                string payload = snap.Valid
                    ? Serialize(snap)
                    : "{\"ping\":true," + SoiJson() + "}";
                _frameBytes   = Encoding.UTF8.GetBytes("data: " + payload + "\n\n");
                _frameVersion = v;
                return _frameBytes;
            }
        }

        // Called from Unity main thread once the map image has been extracted.
        public static void SetMapImage(byte[] png)
        {
            lock (_mapLock) _mapPng = png;
            Plugin.Log?.LogInfo($"[NOXMFD] In-game map image ready ({png.Length} bytes) — serving at /map.");
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

        // Called from Unity main thread once a TGT vehicle-type sprite has been extracted.
        public static void SetTgtIcon(string name, byte[] png)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_tgtLock) _tgtIcons[name] = png;
        }

        // Called from Unity main thread once a BDF ship-type sprite has been extracted.
        public static void SetBdfIcon(string name, byte[] png)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_bdfLock) _bdfIcons[name] = png;
        }

        // Called from Unity main thread once a HUD building-type sprite has been extracted.
        public static void SetBuildingIcon(string name, byte[] png)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_buildingLock) _buildingIcons[name] = png;
        }

        // Called from Unity main thread once a HUD OPTIONS category-row icon sprite has been extracted.
        public static void SetHudCategoryIcon(string name, byte[] png)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_hudCatLock) _hudCatIcons[name] = png;
        }

        // Called from Unity main thread once a countermeasure's display sprite has been extracted.
        public static void SetCmIcon(string key, byte[] png)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_cmLock) _cmIcons[key] = png;
        }

        // Called from Unity main thread once a part of an airframe silhouette has been extracted.
        // partName == "__bg" for the aircraftBackground image.
        public static void SetAirframeImage(string unitName, string partName, byte[] png)
        {
            if (string.IsNullOrEmpty(unitName) || string.IsNullOrEmpty(partName) || png == null) return;
            lock (_airframeLock) _airframeImages[unitName + "|" + partName] = png;
        }

        // Called from Unity main thread once an airframe's part-layout descriptor is built.
        public static void SetAirframeLayout(string unitName, string json)
        {
            if (string.IsNullOrEmpty(unitName) || json == null) return;
            lock (_airframeLock) _airframeLayouts[unitName] = json;
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
            lock (_lock)    { _latest = default; _snapVersion++; }
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
                    else if (path == "/tgt-icon")
                        ServePng(ctx, _tgtIcons, _tgtLock, "type");
                    else if (path == "/bdf-icon")
                        ServePng(ctx, _bdfIcons, _bdfLock, "type");
                    else if (path == "/building-icon")
                        ServePng(ctx, _buildingIcons, _buildingLock, "type");
                    else if (path == "/hud-cat-icon")
                        ServePng(ctx, _hudCatIcons, _hudCatLock, "cat");
                    else if (path == "/airframe")
                        ServeAirframeImage(ctx);
                    else if (path == "/airframe-layout")
                        ServeAirframeLayout(ctx);
                    else if (path == "/config")
                        ServeConfig(ctx);
                    else if (path == "/hud-options")
                        ServeHudOptions(ctx);
                    else if (path == "/rates-config")
                        ServeRatesConfig(ctx);
                    else if (path == "/keybinds-config")
                        ServeKeybindsConfig(ctx);
                    else if (path == "/soi-instances")
                        ServeSoiInstances(ctx);
                    else if (path.StartsWith("/assets/", StringComparison.Ordinal))
                        ServeAsset(ctx, path);
                    else if (path == "/map-view")
                        ServeAssetRel(ctx, "pages/map/map.html");
                    else if (path == "/main")
                        ServeAssetRel(ctx, "pages/main/main.html");
                    else if (path == "/avn")
                        ServeAssetRel(ctx, "pages/avn/avn.html");
                    else if (path == "/afm")
                        ServeAssetRel(ctx, "pages/afm/afm.html");
                    else if (path == "/tgp")
                        ServeAssetRel(ctx, "pages/tgp/tgp.html");
                    else if (path == "/wpn")
                        ServeAssetRel(ctx, "pages/wpn/wpn.html");
                    else if (path == "/rwr")
                        ServeAssetRel(ctx, "pages/rwr/rwr.html");
                    else if (path == "/rdr")
                        ServeAssetRel(ctx, "pages/rdr/rdr.html");
                    else if (path == "/tgt")
                        ServeAssetRel(ctx, "pages/tgt/tgt.html");
                    else if (path == "/akf")
                        ServeAssetRel(ctx, "pages/akf/akf.html");
                    else if (path == "/bdf")
                        ServeAssetRel(ctx, "pages/bdf/bdf.html");
                    else if (path == "/mis")
                        ServeAssetRel(ctx, "pages/mis/mis.html");
                    else if (path == "/obj")
                        ServeAssetRel(ctx, "pages/obj/obj.html");
                    else if (path == "/wpt")
                        ServeAssetRel(ctx, "pages/wpt/wpt.html");
                    else if (path == "/hud")
                        ServeAssetRel(ctx, "pages/hud/hud.html");
                    else if (path == "/keybinds")
                        ServeAssetRel(ctx, "pages/keybinds/keybinds.html");
                    else if (path == "/rates")
                        ServeAssetRel(ctx, "pages/rates/rates.html");
                    else if (path == "/command")
                        HandleCommand(ctx);
                    else if (path == "/mfd")
                        Redirect(ctx, "/");
                    else if (path == "/f35")
                        ServeAssetRel(ctx, "shell/f35/f35.html");
                    else if (path == "/" || path == "/index.html")
                        ServeAssetRel(ctx, "shell/classic/mfd.html");
                    else
                        Redirect(ctx, "/");
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!_cts.IsCancellationRequested)
                        Plugin.Log?.LogError($"[NOXMFD] Accept error: {ex.Message}");
                }
            }
        }

        // ── Inbound command channel ──────────────────────────────────────────────
        // The web client POSTs JSON commands to /command (e.g. tap-to-target, TGT deselect).
        // HttpListener dispatches this on a threadpool thread, where touching Unity/game state is
        // illegal — so we only parse + validate + ENQUEUE here, and the Unity main thread
        // (CommandDispatcher, drained from TelemetryReader.Update) executes each command. This is a
        // built-in feature, always live; commands only invoke the player's own legitimate cockpit
        // actions on their own aircraft.
        private const int MaxQueuedCommands = 64;   // bound the queue so a misbehaving client can't grow it unbounded
        private static readonly Queue<CommandEnvelope> _cmdQueue = new Queue<CommandEnvelope>();
        private static readonly object                 _cmdLock  = new object();

        private static void HandleCommand(HttpListenerContext ctx)
        {
            try
            {
                string body;
                using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = r.ReadToEnd();

                CommandEnvelope env = null;
                try { env = UnityEngine.JsonUtility.FromJson<CommandEnvelope>(body); }
                catch { /* malformed JSON → handled below as 400 */ }

                if (env == null || string.IsNullOrEmpty(env.cmd))
                {
                    ctx.Response.StatusCode = 400;   // malformed / no cmd
                }
                else if (!CommandDispatcher.IsKnown(env.cmd))
                {
                    ctx.Response.StatusCode = 422;   // well-formed but no handler
                }
                else
                {
                    bool queued = false;
                    lock (_cmdLock)
                    {
                        if (_cmdQueue.Count < MaxQueuedCommands) { _cmdQueue.Enqueue(env); queued = true; }
                    }
                    if (!queued) Plugin.Log?.LogDebug("[NOXMFD] command queue full — dropped.");
                    ctx.Response.StatusCode = 204;   // accepted (fire-and-forget); main thread acts next frame
                }
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[NOXMFD] /command error: {ex.Message}");
                try { ctx.Response.Abort(); } catch { /* client gone */ }
            }
        }

        // Drained by the Unity main thread (CommandDispatcher) once per frame. False when empty.
        internal static bool TryDequeueCommand(out CommandEnvelope env)
        {
            lock (_cmdLock)
            {
                if (_cmdQueue.Count > 0) { env = _cmdQueue.Dequeue(); return true; }
            }
            env = null;
            return false;
        }

        private static void ServeConfig(HttpListenerContext ctx)
        {
            try
            {
                string json = string.Format(CultureInfo.InvariantCulture,
                    "{{\"localhost\":\"http://localhost:{0}\",\"lanUrl\":\"{1}\",\"port\":{0}}}",
                    Port, EscapeJson(LanUrl ?? string.Empty));
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

        // The live MFD instances as JSON — the SOI instance registry made visible. Diagnostic for now:
        // it is what proves the registry tracks connects, disconnects and reloads correctly before any
        // of SOI is wired to it, and it stays useful afterwards for "which displays does the server
        // think are open?". Safe off the main thread — the dictionary is concurrent and touches no
        // Unity state.
        private static void ServeSoiInstances(HttpListenerContext ctx)
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
                        it.Conn, EscapeJson(it.Cid), EscapeJson(it.Remote),
                        (DateTime.UtcNow - it.ConnectedUtc).TotalSeconds);
                }
                sb.Append("]}");
                byte[] body = Encoding.UTF8.GetBytes(sb.ToString());
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // The keybind registry as JSON for the /keybinds page: every bind's identity + current values,
        // plus which bind (if any) is armed for joystick capture — the page polls this while open, and
        // it's also how a capture result comes back. Safe off the main thread: the registry list is
        // built once at Awake and never mutated, and ConfigEntry/CapturingId reads are plain field reads
        // (worst case one poll stale).
        private static void ServeKeybindsConfig(HttpListenerContext ctx)
        {
            try
            {
                var sb = new StringBuilder(512);
                sb.Append("{\"binds\":[");
                bool first = true;
                foreach (var b in Keybinds.Binds)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":\"").Append(EscapeJson(b.Id))
                      .Append("\",\"section\":\"").Append(EscapeJson(Keybinds.SectionTitle(b.Section)))
                      .Append("\",\"label\":\"").Append(EscapeJson(b.Label))
                      .Append("\",\"description\":\"").Append(EscapeJson(b.Description)).Append('"');
                    // Digital source — absent for an axis-only bind (docs/map-cursor.md); the page
                    // renders no key/joy cell for a row that has no key/joyButton field.
                    if (b.KeyEntry != null)
                    {
                        var key = b.KeyEntry.Value.MainKey;
                        sb.Append(",\"key\":\"").Append(key == UnityEngine.KeyCode.None ? string.Empty : EscapeJson(key.ToString()))
                          .Append("\",\"joyButton\":").Append(b.JoyEntry!.Value.ToString(CultureInfo.InvariantCulture))
                          .Append(",\"joyNum\":").Append(b.JoyNumEntry!.Value.ToString(CultureInfo.InvariantCulture));
                    }
                    // Analog source — present only for the MAP cursor's axis-capable rows.
                    if (b.AxisEntry != null)
                    {
                        sb.Append(",\"axis\":").Append(b.AxisEntry.Value.ToString(CultureInfo.InvariantCulture))
                          .Append(",\"axisNum\":").Append(b.AxisJoyNumEntry!.Value.ToString(CultureInfo.InvariantCulture))
                          .Append(",\"axisInvert\":").Append(b.AxisInvertEntry!.Value ? "true" : "false");
                    }
                    sb.Append('}');
                }
                // Per-section notes (shared behaviour text under a section header), keyed by the
                // display title the binds carry in "section".
                sb.Append("],\"notes\":{");
                bool firstNote = true;
                var seen = new List<string>(4);
                foreach (var b in Keybinds.Binds)
                {
                    if (seen.Contains(b.Section)) continue;
                    seen.Add(b.Section);
                    string note = Keybinds.SectionNote(b.Section);
                    if (note == null) continue;
                    if (!firstNote) sb.Append(',');
                    firstNote = false;
                    sb.Append('"').Append(EscapeJson(Keybinds.SectionTitle(b.Section)))
                      .Append("\":\"").Append(EscapeJson(note)).Append('"');
                }
                string cap = Keybinds.CapturingId;
                string capKind = Keybinds.CapturingKind;
                sb.Append("},\"capturing\":").Append(cap == null ? "null" : "\"" + EscapeJson(cap) + "\"")
                  .Append(",\"capturingKind\":").Append(capKind == null ? "null" : "\"" + EscapeJson(capKind) + "\"")
                  .Append(",\"bgInput\":").Append(Keybinds.BackgroundInput ? "true" : "false")
                  .Append(",\"radarOnOnStart\":").Append(ImmersionConfig.RadarOnOnStart ? "true" : "false")
                  .Append(",\"engineOnOnStart\":").Append(ImmersionConfig.EngineOnOnStart ? "true" : "false")
                  .Append(",\"masterArmsOnOnStart\":").Append(ImmersionConfig.MasterArmsOnOnStart ? "true" : "false")
                  .Append('}');

                byte[] body = Encoding.UTF8.GetBytes(sb.ToString());
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // The in-game HUD OPTIONS state, as JSON, for the HUD page to render. Built on the main
        // thread by RefreshHudOptions (below) and cached here — HUD options change only on a toggle,
        // so this is fetched on demand rather than streamed, like /config. "{}" until a mission with
        // a live HUDOptions is up; the page treats that as "unavailable".
        internal static volatile string HudOptionsJson = "{}";

        private static void ServeHudOptions(HttpListenerContext ctx)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(HudOptionsJson ?? "{}");
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // cfg-rates experiment (issue #39): the RTS page's two sliders read their starting position
        // from here on load, same shape as /hud-options — a small on-demand JSON snapshot rather
        // than something streamed. Built fresh per request (RatesConfig's getters are plain floats,
        // no game-object reads), so no caching/refresh-on-tick needed like HudOptionsJson.
        private static void ServeRatesConfig(HttpListenerContext ctx)
        {
            try
            {
                string json = string.Format(CultureInfo.InvariantCulture,
                    "{{\"fastHz\":{0},\"tgpHz\":{1}}}", RatesConfig.FastHz, RatesConfig.TgpHz);
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
              .Append(",\"wpt\":").Append(HudDeclutterConfig.HideWaypointCue ? "true" : "false")
              .Append('}');

            sb.Append('}');
            HudOptionsJson = sb.ToString();
        }

        private static void AppendTypeList(StringBuilder sb, string key,
            System.Collections.Generic.List<HUDOptions_ToggleButton> toggles,
            System.Collections.Generic.List<Encyclopedia.UnitType> types)
        {
            sb.Append(key).Append('[');
            int n = toggles == null ? 0 : toggles.Count;
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

        // ── Embedded web-asset serving ─────────────────────────────────────────
        // Real files under src/web/ are baked into the DLL as embedded resources and served
        // here under /assets/. MSBuild names a resource like
        // "<RootNamespace>.src.web.<dotted path>" (and may mangle odd characters), so we
        // match by the stable ".web.<dotted path>" suffix against the actual manifest rather
        // than reconstruct the whole name. Path traversal is moot — the manifest is a flat,
        // fixed set baked at build time, not a filesystem.
        private static readonly Assembly _asm = typeof(TelemetryServer).Assembly;
        private static string[]? _resourceNames;
        private static string[] ResourceNames => _resourceNames ??= _asm.GetManifestResourceNames();

        // ETag for all embedded web assets. They're baked into the DLL, so they're immutable for a
        // given build and ALL change together on rebuild — so one build-stamped tag (the module's
        // MVID, which changes every compile) validates every asset. Served with Cache-Control:
        // no-cache, so the browser caches but revalidates each load via If-None-Match; an unchanged
        // asset gets a tiny 304 (no body), and a new build's MVID busts the whole set automatically.
        private static readonly string AssetETag =
            "\"" + _asm.ManifestModule.ModuleVersionId.ToString("N") + "\"";

        private static void ServeAsset(HttpListenerContext ctx, string path)
            => ServeAssetRel(ctx, path.Substring("/assets/".Length).Trim('/'));

        // Serve an embedded web asset by its source-relative path under src/web/ (e.g.
        // "pages/wpn.html"). Used both by the /assets/ route and by the page routes
        // (e.g. /wpn) that serve a file directly.
        private static void ServeAssetRel(HttpListenerContext ctx, string rel)
        {
            try
            {
                // "shared/theme.css" -> suffix ".web.shared.theme.css"
                string suffix = "." + ("web/" + rel).Replace('/', '.');

                string? resourceName = null;
                foreach (string n in ResourceNames)
                {
                    if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { resourceName = n; break; }
                }

                if (resourceName == null)
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }

                // Conditional GET: assets cache but revalidate. If the client's cached validator
                // still matches this build's tag, the asset is unchanged → 304 with no body.
                ctx.Response.Headers["ETag"]          = AssetETag;
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                if (ctx.Request.Headers["If-None-Match"] == AssetETag)
                {
                    ctx.Response.StatusCode      = 304;
                    ctx.Response.ContentLength64 = 0;
                    return;
                }

                using Stream? s = _asm.GetManifestResourceStream(resourceName);
                if (s == null)
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }

                byte[] body;
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    body = ms.ToArray();
                }

                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = ContentTypeFor(rel);
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        private static string ContentTypeFor(string path)
        {
            int dot = path.LastIndexOf('.');
            string ext = dot >= 0 ? path.Substring(dot).ToLowerInvariant() : "";
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".css":  return "text/css; charset=utf-8";
                case ".js":   return "text/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg":  return "image/svg+xml";
                case ".woff2": return "font/woff2";
                case ".woff": return "font/woff";
                case ".png":  return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".txt":  return "text/plain; charset=utf-8";
                default:      return "application/octet-stream";
            }
        }

        // ── Map image handler ──────────────────────────────────────────────────

        private static void Redirect(HttpListenerContext ctx, string location)
        {
            try
            {
                ctx.Response.StatusCode = 302;
                ctx.Response.RedirectLocation = location;
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

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
                    // The captured map is JPEG (downscaled in TelemetryReader.MapSpriteToJpg).
                    ctx.Response.StatusCode      = 200;
                    ctx.Response.ContentType     = "image/jpeg";
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
                Plugin.Log?.LogWarning($"[NOXMFD] Map not found in: {dir}");
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

        // ── Airframe handlers ───────────────────────────────────────────────────

        private static void ServeAirframeImage(HttpListenerContext ctx)
        {
            string type = ctx.Request.QueryString["type"] ?? string.Empty;
            string part = ctx.Request.QueryString["part"] ?? string.Empty;
            byte[]? png = null;
            if (type.Length > 0 && part.Length > 0)
                lock (_airframeLock) _airframeImages.TryGetValue(type + "|" + part, out png);

            if (png == null) { ctx.Response.StatusCode = 404; try { ctx.Response.Close(); } catch { } return; }
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

        private static void ServeAirframeLayout(HttpListenerContext ctx)
        {
            string type = ctx.Request.QueryString["type"] ?? string.Empty;
            string? json = null;
            if (type.Length > 0)
                lock (_airframeLock) _airframeLayouts.TryGetValue(type, out json);

            if (json == null) { ctx.Response.StatusCode = 404; try { ctx.Response.Close(); } catch { } return; }
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                ctx.Response.StatusCode      = 200;
                ctx.Response.ContentType     = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body, 0, body.Length);
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

            // Register this instance for its whole lifetime — see MfdInstance. The cid is the
            // client's own durable id (telemetry-source.js), empty when its storage is unavailable.
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
                    "event: hello\ndata: {\"cid\":\"" + EscapeJson(cid) + "\"}\n\n");
                await ctx.Response.OutputStream.WriteAsync(hello, 0, hello.Length, ct).ConfigureAwait(false);

                // The loop ticks at the CURSOR's rate and sends the telemetry frame every Nth tick, so
                // the two cadences are independent: a slewed axis gets ~60 Hz of tiny updates while
                // the expensive snapshot keeps its 10 Hz. lastCursor suppresses repeats, so a centred
                // stick costs one comparison per tick and no traffic at all.
                string lastCursor = string.Empty;
                int sinceFrame = FrameEveryMs;   // send a frame immediately on connect
                while (!ct.IsCancellationRequested)
                {
                    if (sinceFrame >= FrameEveryMs)
                    {
                        // Shared frame: serialized at most once per snapshot version, regardless of
                        // how many clients are connected. Always send something — real data during a
                        // mission, a ping otherwise.
                        byte[] bytes = GetFrameBytes(out _);
                        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                        sinceFrame = 0;
                    }

                    string cursor = CursorJson();
                    if (!string.Equals(cursor, lastCursor, StringComparison.Ordinal))
                    {
                        lastCursor = cursor;
                        byte[] cbytes = Encoding.UTF8.GetBytes("event: cursor\ndata: " + cursor + "\n\n");
                        await ctx.Response.OutputStream.WriteAsync(cbytes, 0, cbytes.Length, ct).ConfigureAwait(false);
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
                SoiReleaseOnDisconnect(cid);
                try { ctx.Response.Close(); } catch { }
                Plugin.Log?.LogInfo($"[NOXMFD] Client disconnected from {ctx.Request.RemoteEndPoint} (instance {conn})");
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
                "\"selWeapon\":\"{24}\",\"cmCat\":{25},\"tgpActive\":{26}," +
                "\"fuel\":{27:0.000},\"thr\":{28:0.000},\"hasAb\":{29},\"abStart\":{30:0.000}," +
                "\"softGun\":\"{31}\",\"softRel\":\"{32}\",\"masterArmsOn\":{34},\"combatMode\":\"{35}\",{33},",
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
                s.TgpActive ? "true" : "false",
                s.Fuel, s.Throttle,
                s.HasAfterburner ? "true" : "false", s.AbStart,
                EscapeJson(s.SoftGun ?? string.Empty), EscapeJson(s.SoftRel ?? string.Empty),
                SoiJson(),   // server state, not the snapshot's — see SetSoiTarget
                ImmersionState.MasterArmsOn ? "true" : "false",   // mod state, not the snapshot's — docs/radar-master-arms.md
                ImmersionState.CombatMode switch { CombatMode.AirToAir => "aa", CombatMode.AirToGround => "ag", _ => "all" });

            return head + "\"loadout\":" + LoadoutArray(s.Loadout)
                        + ",\"colors\":{"
                        +   "\"f\":\"" + EscapeJson(s.ColFriendly ?? "#39ff14") + "\","
                        +   "\"e\":\"" + EscapeJson(s.ColHostile  ?? "#ff4040") + "\","
                        +   "\"n\":\"" + EscapeJson(s.ColNeutral  ?? "#9aa0a6") + "\"}"
                        + ",\"contacts\":" + UnitsArray(s.Units)
                        + ",\"playerId\":" + s.PlayerId
                        + ",\"pjm\":" + (s.PlayerJammed ? "true" : "false")
                        + ",\"pjb\":" + s.PlayerJammedBy
                        + ",\"parts\":" + PartsArray(s.Parts)
                        + ",\"pylons\":" + PylonsArray(s.Pylons)
                        + ",\"rwr\":" + RwrArray(s.Rwr)
                        + ",\"mw\":" + MwArray(s.Mw)
                        + ",\"rdr\":" + RdrBlock(s)
                        + ",\"radar\":" + (s.RadarOn ? "true" : "false")
                        + ",\"guns\":" + (s.GunsLinked ? "true" : "false")
                        + ",\"ign\":" + (s.Ignition ? "true" : "false")
                        + ",\"assist\":" + (s.FlightAssist ? "true" : "false")
                        + ",\"turret\":" + (s.TurretAuto ? "true" : "false")
                        + ",\"nvg\":" + (s.NightVision ? "true" : "false")
                        + ",\"navlt\":" + (s.NavLightsOn ? "true" : "false")
                        + ",\"heat\":" + s.Heat.ToString("0.000", CultureInfo.InvariantCulture)
                        + ",\"heatColor\":\"" + EscapeJson(s.HeatColor ?? "#39ff14") + "\""
                        + ",\"rpm\":" + s.Rpm.ToString("0.000", CultureInfo.InvariantCulture)
                        + ",\"failures\":" + StringArray(s.Failures)
                        + ",\"tgt\":" + TgtBlock(s)
                        + ",\"bdf\":" + BdfBlock(s)
                        + ",\"pal\":" + PalBlock(s)
                        + ",\"mis\":" + MisBlock(s)
                        + ",\"obj\":" + ObjBlock(s)
                        + ",\"akf\":" + AkfBlock(s) + "}";
        }

        // AKF advanced kill feed (docs/akf-page.md). Always present while a mission runs (no "faction
        // has no HQ yet" gate like MIS/OBJ — an empty session just reads as all-zero). Kills are
        // scoped to the local player's own kills; all is everyone's, matching the game's own feed.
        // rank is the player's persistent Player.PlayerRank, not session-scoped.
        private static string AkfBlock(TelemetrySnapshot s)
        {
            return "{\"all\":" + AkfArray(s.AkfAll) + ",\"player\":" + AkfArray(s.AkfPlayer)
                + string.Format(CultureInfo.InvariantCulture,
                    ",\"kills\":{{\"aircraft\":{0},\"ship\":{1},\"vehicle\":{2},\"building\":{3}}}" +
                    ",\"rank\":{4},\"fundsGained\":{5:0.0},\"fundsSpent\":{6:0.0}}}",
                    s.AkfKillsAircraft, s.AkfKillsShip, s.AkfKillsVehicle, s.AkfKillsBuilding,
                    s.AkfRank, s.AkfFundsGained, s.AkfFundsSpent);
        }

        private static string AkfArray(AkfKillEntry[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AkfKillEntry e = items[i];
                sb.Append('{');
                if (e.Attacker != null)
                    sb.Append("\"a\":\"").Append(EscapeJson(e.Attacker)).Append("\",\"h\":").Append(e.AttackerHostile ? "true" : "false").Append(',');
                sb.Append("\"v\":\"").Append(EscapeJson(e.Victim)).Append("\",\"vh\":").Append(e.VictimHostile ? "true" : "false")
                  .Append(",\"verb\":\"").Append(EscapeJson(e.Verb)).Append('"');
                if (e.Weapon != null)
                    sb.Append(",\"w\":\"").Append(EscapeJson(e.Weapon)).Append('"');
                if (e.PlayerIsVictim)
                    sb.Append(",\"pv\":true");
                sb.Append('}');
            }
            return sb.Append(']').ToString();
        }

        // MIS mission-info panel (docs/mdt-pages.md). {present:false} in multiplayer or between
        // missions. level: 0 Conventional, 1 Tactical, 2 Strategic (TelemetryReader.BuildMis).
        private static string MisBlock(TelemetrySnapshot s)
        {
            if (!s.MisPresent) return "{\"present\":false}";
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"present\":true,\"name\":\"{0}\",\"description\":\"{1}\",\"tod\":{2:0.000},\"duration\":{3:0.0},\"score\":{4:0.0},\"level\":{5}}}",
                EscapeJson(s.MissionName ?? string.Empty), EscapeJson(s.MisDescription ?? string.Empty),
                s.MisTimeOfDay, s.MisDuration, s.MisScore, s.MisLevel);
        }

        // OBJ active-objectives list (docs/mdt-pages.md). {present:false} when the player faction's
        // HQ isn't resolved yet.
        private static string ObjBlock(TelemetrySnapshot s)
        {
            if (!s.ObjPresent) return "{\"present\":false}";
            return "{\"present\":true,\"items\":" + ObjArray(s.Obj) + "}";
        }

        private static string ObjArray(ObjEntry[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "{{\"n\":\"{0}\",\"s\":{1},\"p\":{2:0.000},\"pos\":{3}}}",
                    EscapeJson(items[i].Name ?? string.Empty), items[i].Status, items[i].Percent,
                    ObjPositionArray(items[i].Positions)));
            }
            return sb.Append(']').ToString();
        }

        // Position sub-rows under one objective (ObjectiveInfoList_Item — "DestroyUnits / Lb105 /
        // 18km"). x/z are true world coords; the page derives the grid label and live distance itself.
        private static string ObjPositionArray(ObjPosition[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "{{\"n\":\"{0}\",\"x\":{1:0.0},\"z\":{2:0.0}}}",
                    EscapeJson(items[i].Name ?? string.Empty), items[i].X, items[i].Z));
            }
            return sb.Append(']').ToString();
        }

        // TGT filter panel state (docs/tgt-page.md). {present:false} when the game's TargetListSelector
        // isn't up; otherwise the three toggle groups (ordered as the tgt.* commands index them) plus
        // the two standalone toggles.
        private static string TgtBlock(TelemetrySnapshot s)
        {
            if (!s.TgtPresent) return "{\"present\":false}";
            return "{\"present\":true"
                 + ",\"laser\":" + (s.TgtLaser ? "true" : "false")
                 + ",\"hud\":"   + (s.TgtHud   ? "true" : "false")
                 + ",\"faction\":"  + TgtToggleArray(s.TgtFaction)
                 + ",\"category\":" + TgtToggleArray(s.TgtCategory)
                 + ",\"vehicle\":"  + TgtToggleArray(s.TgtVehicle)
                 + "}";
        }

        private static string TgtToggleArray(TgtToggleInfo[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":\"").Append(EscapeJson(items[i].Name ?? string.Empty))
                  .Append("\",\"on\":").Append(items[i].On ? "true" : "false").Append('}');
            }
            return sb.Append(']').ToString();
        }

        // BDF faction-forces panel (docs/bdf-page.md) — always BOSCALI, a fixed identity.
        // {present:false} when Boscali has no FactionHQ yet; otherwise the header scalars plus the
        // four breakdown rows.
        private static string BdfBlock(TelemetrySnapshot s)
        {
            if (!s.BdfPresent) return "{\"present\":false}";
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"present\":true,\"faction\":\"{0}\",\"funds\":{1:0.000},\"score\":{2:0.0},\"warheads\":{3},",
                EscapeJson(s.BdfFaction ?? string.Empty), s.BdfFunds, s.BdfScore, s.BdfWarheads)
                + "\"ships\":"     + BdfCountArray(s.BdfShips)
                + ",\"vehicles\":"  + BdfCountArray(s.BdfVehicles)
                + ",\"buildings\":" + BdfCountArray(s.BdfBuildings)
                + ",\"aircraft\":"  + BdfCountArray(s.BdfAircraft)
                + "}";
        }

        // PAL — the same faction-forces panel as BDF, always PRIMEVA (a fixed identity, like BDF is
        // always BOSCALI — docs/bdf-page.md). {present:false} when Primeva has no FactionHQ yet.
        private static string PalBlock(TelemetrySnapshot s)
        {
            if (!s.PalPresent) return "{\"present\":false}";
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"present\":true,\"faction\":\"{0}\",\"funds\":{1:0.000},\"score\":{2:0.0},\"warheads\":{3},",
                EscapeJson(s.PalFaction ?? string.Empty), s.PalFunds, s.PalScore, s.PalWarheads)
                + "\"ships\":"     + BdfCountArray(s.PalShips)
                + ",\"vehicles\":"  + BdfCountArray(s.PalVehicles)
                + ",\"buildings\":" + BdfCountArray(s.PalBuildings)
                + ",\"aircraft\":"  + BdfCountArray(s.PalAircraft)
                + "}";
        }

        private static string BdfCountArray(BdfCountInfo[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":\"").Append(EscapeJson(items[i].Name ?? string.Empty))
                  .Append("\",\"c\":").Append(items[i].Count).Append('}');
            }
            return sb.Append(']').ToString();
        }

        private static string MwArray(MwContact[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"x\":{0:0.0},\"z\":{1:0.0},\"st\":\"{2}\",\"nb\":{3:0.0},\"h\":{4:0.0}}}",
                    items[i].X, items[i].Z, EscapeJson(items[i].Seeker ?? string.Empty), items[i].Notch, items[i].Heading);
            }
            return sb.Append(']').ToString();
        }

        // RDR page (docs/rdr-page.md). {present:false} when the aircraft has no radar; otherwise the
        // scope's range scale + cone half-angle and the air contacts the own radar detects. Contacts
        // carry world x/z (client derives bearing/range from the player's own position), altitude,
        // travel heading (velocity stub), lock state (tg) and label.
        private static string RdrBlock(TelemetrySnapshot s)
        {
            string pb = PitbullArray(s.Pitbull);
            if (!s.RadarPresent) return "{\"present\":false,\"pb\":" + pb + "}";
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"present\":true,\"range\":{0:0.0},\"cone\":{1:0.0},\"metric\":{2},\"lvlt\":{3:0.000},\"items\":{4},\"pb\":{5}}}",
                s.RadarRange, s.RadarConeDeg, s.RdrMetric ? "true" : "false", s.RdrLevelTime, RdrArray(s.Rdr), pb);
        }

        // Pitbull missiles (issue #40): the player's own AA missiles with an active-radar seeker
        // currently locked. tid is the designated target's persistentID.Id, 0 if none/unresolved —
        // the client only draws the dashed target line when it can resolve tid against a live
        // RDR/MAP contact.
        private static string PitbullArray(PitbullContact[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"id\":{0},\"x\":{1:0.0},\"z\":{2:0.0},\"alt\":{3:0.0},\"hdg\":{4:0.0},\"tid\":{5}}}",
                    items[i].Id, items[i].X, items[i].Z, items[i].Alt, items[i].Heading, items[i].TargetId);
            }
            return sb.Append(']').ToString();
        }

        private static string RdrArray(RdrContact[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"id\":{0},\"x\":{1:0.0},\"z\":{2:0.0},\"alt\":{3:0.0},\"hdg\":{4:0.0},\"tg\":{5},\"rd\":{6},\"dl\":{7},\"n\":\"{8}\"}}",
                    items[i].Id, items[i].X, items[i].Z, items[i].Alt, items[i].Heading,
                    items[i].Targeted ? 1 : 0, items[i].Radar ? 1 : 0, items[i].Datalink ? 1 : 0,
                    EscapeJson(items[i].Name ?? string.Empty));
            }
            return sb.Append(']').ToString();
        }

        private static string RwrArray(RwrContact[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"x\":{0:0.0},\"z\":{1:0.0},\"tr\":{2},\"pw\":{3:0.000},\"fr\":{4:0.000},\"n\":\"{5}\",\"k\":{6}}}",
                    items[i].X, items[i].Z, items[i].Tier, items[i].Power, items[i].Fresh,
                    EscapeJson(items[i].Name ?? string.Empty), items[i].Kind);
            }
            return sb.Append(']').ToString();
        }

        private static string StringArray(string[]? items)
        {
            if (items == null || items.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(EscapeJson(items[i] ?? string.Empty)).Append('"');
            }
            return sb.Append(']').ToString();
        }

        private static string PartsArray(PartHp[]? parts)
        {
            if (parts == null || parts.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{{\"n\":\"{0}\",\"hp\":{1:0.#},\"d\":{2}}}",
                    EscapeJson(parts[i].Name ?? string.Empty),
                    parts[i].Hp,
                    parts[i].Detached ? 1 : 0);
            }
            return sb.Append(']').ToString();
        }

        private static string PylonsArray(PylonMarker[]? pylons)
        {
            if (pylons == null || pylons.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < pylons.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":\"").Append(EscapeJson(pylons[i].Name ?? string.Empty)).Append("\",")
                  .Append("\"s\":\"").Append(EscapeJson(pylons[i].State ?? "empty")).Append("\"}");
            }
            return sb.Append(']').ToString();
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
                    "{{\"id\":{8},\"t\":\"{0}\",\"x\":{1:0.0},\"z\":{2:0.0},\"h\":{3:0.0},\"f\":{4},\"o\":{5},\"s\":{6:0.000},\"tg\":{7},\"jm\":{9},\"jb\":{10},\"dl\":{11},\"st\":{12}}}",
                    EscapeJson(u.Type ?? string.Empty),
                    u.X, u.Z, u.Heading, u.Faction,
                    u.Orient ? "true" : "false", u.Scale,
                    u.Targeted ? 1 : 0,
                    u.Id,
                    u.Jammed ? 1 : 0,
                    u.JammedBy,
                    u.Datalink ? 1 : 0,
                    u.Stale ? 1 : 0);
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

        // SOI's slice of a frame. Shared by the real payload and the no-mission ping, because a display
        // is focusable and drivable at the main menu, where the ping is the only frame there is.
        private static string SoiJson() => string.Format(CultureInfo.InvariantCulture,
            "\"soiTarget\":\"{0}\",\"soiPane\":{1},\"soiSeq\":{2},\"soiAct\":\"{3}\"",
            EscapeJson(SoiTarget), SoiTargetPane, SoiSeq, EscapeJson(SoiAct));

        // The MAP cursor's own payload (docs/map-cursor.md), sent as its OWN SSE event rather than in
        // the telemetry frame above. A slewed axis is continuous and wants the lowest latency we can
        // give it, but the telemetry frame is the most expensive thing we build — so pushing the
        // frame faster to chase the cursor would re-serialize every contact, every RWR emitter and
        // every loadout row dozens of times a second, and on a tablet over wifi the extra bulk costs
        // more latency than the faster tick buys back. This is ~60 bytes: it can go out many times
        // per telemetry frame and still be free. It is the ONLY place these fields travel — carrying
        // them in both would let a cached (older) frame overwrite a fresher event.
        private static string CursorJson() => string.Format(CultureInfo.InvariantCulture,
            "{{\"x\":{0:0.00},\"y\":{1:0.00},\"selSeq\":{2},\"act\":\"{3}\",\"actSeq\":{4},\"held\":{5}}}",
            CursorX, CursorY, CursorSelSeq, EscapeJson(MapAct), MapActSeq,
            Volatile.Read(ref _cursorSelHeld) ? "true" : "false");

        // Escapes every character the JSON spec forbids raw inside a string literal — not just the
        // ones a prior caller happened to hit. Earlier versions only handled \, ", \n, \r, \t (added
        // for MIS's mission description); that missed the rest of the C0 control range (0x00-0x1F,
        // e.g. \b, \f, a stray control char in a unit/weapon name from the game's own data), which
        // JSON.parse rejects as "Bad control character in string literal" — the same failure mode
        // as the untranslated-decimal-point bug, just a different source field each time. Escaping
        // the whole class here means no future caller needs to remember this. Lazily allocates only
        // when a string actually needs escaping (every prior caller was escape-free, hot path stays
        // allocation-free).
        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            StringBuilder? sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                string? esc = c switch
                {
                    '\\' => "\\\\",
                    '"'  => "\\\"",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ => c < 0x20 ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture) : null
                };
                if (esc == null) { sb?.Append(c); continue; }
                if (sb == null) { sb = new StringBuilder(s.Length + 8); sb.Append(s, 0, i); }
                sb.Append(esc);
            }
            return sb?.ToString() ?? s;
        }
    }
}
