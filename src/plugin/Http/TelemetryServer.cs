using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

        // Sent immediately to a fresh MJPEG connection when no real frame exists yet — a 4x4
        // dark-gray JPEG, precomputed offline (not generated at runtime: Texture2D.EncodeToJPG
        // needs the Unity main thread, and HandleMjpegAsync runs on the HTTP listener's own
        // thread). Without this, a client that connects before TgpFeed's pipeline has produced
        // its first frame (target lock + first capture + first async readback, confirmed to take
        // several seconds — docs/performance.md, 2026-08-23) sits on zero bytes, which some
        // browsers can mark the stream failed for and never recover from without a page reload
        // (docs/tgp-high-quality-mode.md).
        private static readonly byte[] TgpPlaceholderJpg =
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
                // MissionRunning is the same idea: a mission loading/ending with no aircraft chosen
                // yet changes nothing else about a ping frame, so it needs the same invalidation.
                long sv = Interlocked.Read(ref _soiVersion);
                bool mr = _missionRunning;
                if (_frameVersion == v && _frameSoiVersion == sv && _frameMissionRunning == mr && _frameBytes != null) return _frameBytes;
                _frameSoiVersion = sv;
                _frameMissionRunning = mr;

                string payload = snap.Valid
                    ? TelemetryJson.Serialize(snap, SoiJson(), ImmersionState.MasterArmsOn,
                        ImmersionState.CombatMode switch { CombatMode.AirToAir => "aa", CombatMode.AirToGround => "ag", _ => "all" },
                        ExtensionRegistry.SlicesJson())
                    : "{\"ping\":true,\"missionRunning\":" + (mr ? "true" : "false") + "," + SoiJson() + "}";
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

                    TelemetryHttpRouter.Route(ctx, path, _cts.Token);
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

        // docs/server-hardening.md — a command envelope is a few hundred bytes at most; 16 KB
        // leaves headroom without letting a single request allocate an arbitrarily large string.
        // Checks the declared Content-Length first (the common case, rejected before any body read
        // at all), but also caps the actual bytes read when the length is unknown (-1, e.g. chunked
        // transfer) rather than trusting the header alone. Shared by both command endpoints below —
        // the only two places in this file that read a request body from an untrusted caller.
        private const int MaxCommandBodyBytes = 16 * 1024;

        private static bool TryReadBoundedBody(HttpListenerContext ctx, out string body)
        {
            body = string.Empty;
            if (ctx.Request.ContentLength64 > MaxCommandBodyBytes)
            {
                ctx.Response.StatusCode = 413;
                ctx.Response.Close();
                return false;
            }

            using var ms = new MemoryStream();
            var buffer = new byte[4096];
            Stream input = ctx.Request.InputStream;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (ms.Length + read > MaxCommandBodyBytes)
                {
                    ctx.Response.StatusCode = 413;
                    ctx.Response.Close();
                    return false;
                }
                ms.Write(buffer, 0, read);
            }
            body = Encoding.UTF8.GetString(ms.ToArray());
            return true;
        }

        internal static void HandleCommand(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod != "POST")
                {
                    ctx.Response.StatusCode = 405;   // /ext/<id>/command already gates on POST at the routing site
                    ctx.Response.Close();
                    return;
                }
                if (!TryReadBoundedBody(ctx, out string body)) return;

                CommandEnvelope? env = null;
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
        internal static bool TryDequeueCommand(out CommandEnvelope? env)
        {
            lock (_cmdLock)
            {
                if (_cmdQueue.Count > 0) { env = _cmdQueue.Dequeue(); return true; }
            }
            env = null;
            return false;
        }

        // ── Response helpers ────────────────────────────────────────────────────
        // The HTTP response mechanics every Serve* handler repeats regardless of what it's
        // serving: status/content-type/length/write/close, plus Cache-Control for the small
        // on-demand JSON snapshots. Orthogonal to *what* gets serialized (that's the JSON-writer
        // layer docs/server-hardening.md already scopes) — this is just the plumbing.
        private static void WriteJson(HttpListenerContext ctx, string json)
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

        private static void WriteBinary(HttpListenerContext ctx, byte[] body, string contentType)
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

        // ── Extension API (docs/extensions-api.md) ────────────────────────────────

        internal static void ServeExtManifest(HttpListenerContext ctx)
        {
            try
            {
                List<ExtensionRegistry.Entry> list = ExtensionRegistry.Manifest();
                var sb = new StringBuilder("[");
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"id\":\"").Append(EscapeJson(list[i].Id))
                      .Append("\",\"label\":\"").Append(EscapeJson(list[i].Label)).Append("\"}");
                }
                WriteJson(ctx, sb.Append(']').ToString());
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // Routes "/ext/<id>" (the page itself), "/ext/<id>/<relPath>" (its assets), and
        // POST "/ext/<id>/command" (its command endpoint) — one generic handler for every
        // registered extension rather than per-extension routing, the whole point of this
        // surface (see docs/extensions-api.md).
        internal static void HandleExtRequest(HttpListenerContext ctx, string path)
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

            if (relPath == "command" && ctx.Request.HttpMethod == "POST")
            {
                HandleExtCommand(ctx, id);
                return;
            }

            if (relPath == "feed.mjpg")
            {
                _ = Task.Run(() => HandleExtMjpegAsync(ctx, id, _cts.Token));
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

        // Same accepted-fire-and-forget shape as HandleCommand above — the raw body is queued
        // as-is (not parsed here, see Api.CommandHandler) and drained on the main thread by
        // ExtensionRegistry.Drain.
        private static void HandleExtCommand(HttpListenerContext ctx, string id)
        {
            try
            {
                if (!TryReadBoundedBody(ctx, out string body)) return;

                if (!ExtensionRegistry.TryEnqueueCommand(id, body))
                    Plugin.Log?.LogDebug($"[NOXMFD] extension '{id}' command queue full — dropped.");
                ctx.Response.StatusCode = 204;   // accepted (fire-and-forget); main thread acts next frame
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[NOXMFD] /ext/{id}/command error: {ex.Message}");
                try { ctx.Response.Abort(); } catch { }
            }
        }

        // Same shape as HandleMjpegAsync, generalized to a runtime-registered
        // extension id instead of a hardcoded page — see Api.PushMjpegFrame.
        private static async Task HandleExtMjpegAsync(HttpListenerContext ctx, string id, CancellationToken ct)
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

        internal static void ServeConfig(HttpListenerContext ctx)
        {
            try
            {
                string json = string.Format(CultureInfo.InvariantCulture,
                    "{{\"localhost\":\"http://localhost:{0}\",\"lanUrl\":\"{1}\",\"port\":{0}}}",
                    Port, EscapeJson(LanUrl ?? string.Empty));
                WriteJson(ctx, json);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // The live MFD instances as JSON — the SOI instance registry made visible. Diagnostic for now:
        // it is what proves the registry tracks connects, disconnects and reloads correctly before any
        // of SOI is wired to it, and it stays useful afterwards for "which displays does the server
        // think are open?". Safe off the main thread — the dictionary is concurrent and touches no
        // Unity state.
        internal static void ServeSoiInstances(HttpListenerContext ctx)
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
                WriteJson(ctx, sb.ToString());
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // The keybind registry as JSON for the /keybinds page: every bind's identity + current values,
        // plus which bind (if any) is armed for joystick capture — the page polls this while open, and
        // it's also how a capture result comes back. Safe off the main thread: the registry list is
        // built once at Awake and never mutated, and ConfigEntry/CapturingId reads are plain field reads
        // (worst case one poll stale).
        internal static void ServeKeybindsConfig(HttpListenerContext ctx)
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
                    // renders no key/joy cell for a row that has no key/joyButton field. The two are
                    // independently optional: a key-only bind (issue #51's SAVE/LOAD LAYOUT — browser-
                    // side only, no joystick/HOTAS) has KeyEntry but no JoyEntry, so joyButton/joyNum
                    // are omitted too — the page renders one wide key cell for that row instead of an
                    // always-empty joystick cell next to it.
                    if (b.KeyEntry != null)
                    {
                        var key = b.KeyEntry.Value.MainKey;
                        sb.Append(",\"key\":\"").Append(key == UnityEngine.KeyCode.None ? string.Empty : EscapeJson(key.ToString())).Append('"');
                        if (b.JoyEntry != null)
                            sb.Append(",\"joyButton\":").Append(b.JoyEntry.Value.ToString(CultureInfo.InvariantCulture))
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
                    string? note = Keybinds.SectionNote(b.Section);
                    if (note == null) continue;
                    if (!firstNote) sb.Append(',');
                    firstNote = false;
                    sb.Append('"').Append(EscapeJson(Keybinds.SectionTitle(b.Section)))
                      .Append("\":\"").Append(EscapeJson(note)).Append('"');
                }
                string? cap = Keybinds.CapturingId;
                string? capKind = Keybinds.CapturingKind;
                sb.Append("},\"capturing\":").Append(cap == null ? "null" : "\"" + EscapeJson(cap) + "\"")
                  .Append(",\"capturingKind\":").Append(capKind == null ? "null" : "\"" + EscapeJson(capKind) + "\"")
                  .Append(",\"bgInput\":").Append(Keybinds.BackgroundInput ? "true" : "false")
                  .Append(",\"radarOnOnStart\":").Append(ImmersionConfig.RadarOnOnStart ? "true" : "false")
                  .Append(",\"engineOnOnStart\":").Append(ImmersionConfig.EngineOnOnStart ? "true" : "false")
                  .Append(",\"masterArmsOnOnStart\":").Append(ImmersionConfig.MasterArmsOnOnStart ? "true" : "false")
                  .Append(",\"hudFiltersOnCombatMode\":").Append(ImmersionConfig.HudFiltersOnCombatMode ? "true" : "false")
                  .Append('}');

                WriteJson(ctx, sb.ToString());
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // The in-game HUD OPTIONS state, as JSON, for the HUD page to render. Built on the main
        // thread by RefreshHudOptions (below) and cached here — HUD options change only on a toggle,
        // so this is fetched on demand rather than streamed, like /config. "{}" until a mission with
        // a live HUDOptions is up; the page treats that as "unavailable".
        internal static volatile string HudOptionsJson = "{}";

        internal static void ServeHudOptions(HttpListenerContext ctx)
        {
            try
            {
                WriteJson(ctx, HudOptionsJson ?? "{}");
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // The waypoint/route library (docs/hud-waypoint-indicator.md, Option 2) — RouteStore is the
        // single source of truth now, not any browser's localStorage. Mission-independent, like
        // /hud-options, so the WPT page works at the main menu too. Cached the same way
        // (RouteStore.RoutesJson is volatile, rebuilt on the main thread after every mutation).
        internal static void ServeWptOptions(HttpListenerContext ctx)
        {
            try
            {
                WriteJson(ctx, RouteStore.RoutesJson ?? "{\"activeRouteId\":null,\"routes\":[]}");
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // Saved layouts (issue #51) — LayoutStore is the single source of truth, same pattern as
        // /wpt-options. Mission-independent, so SAVE/LOAD LAYOUT work at the main menu too.
        internal static void ServeLayoutOptions(HttpListenerContext ctx)
        {
            try
            {
                WriteJson(ctx, LayoutStore.LayoutsJson ?? "{\"layouts\":[]}");
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // HUD filter presets (issue #50 follow-up) — HudPresetStore is the single source of truth,
        // same pattern as /layout-options. The LOAD picker's on-demand fetch; the bottom label's
        // current-slot summary rides /hud-options instead (RefreshHudOptions), not this endpoint.
        internal static void ServeHudPresets(HttpListenerContext ctx)
        {
            try
            {
                WriteJson(ctx, HudPresetStore.PresetsJson ?? "{\"current\":1,\"presets\":[]}");
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // cfg-rates experiment (issue #39): the RTS page's two sliders read their starting position
        // from here on load, same shape as /hud-options — a small on-demand JSON snapshot rather
        // than something streamed. Built fresh per request (RatesConfig's getters are plain floats,
        // no game-object reads), so no caching/refresh-on-tick needed like HudOptionsJson.
        internal static void ServeRatesConfig(HttpListenerContext ctx)
        {
            try
            {
                string json = string.Format(CultureInfo.InvariantCulture,
                    "{{\"fastHz\":{0},\"tgpHz\":{1}}}", RatesConfig.FastHz, RatesConfig.TgpHz);
                WriteJson(ctx, json);
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

        // ── Map image handler ──────────────────────────────────────────────────

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

        internal static void ServeMap(HttpListenerContext ctx)
        {
            // Prefer the map image we extracted straight from the game — its bounds match the
            // world coordinates exactly, so the plane lines up with no calibration.
            byte[]? captured;
            lock (_mapLock) captured = _mapPng;
            if (captured != null)
            {
                // The captured map is JPEG (downscaled in TelemetryReader.MapSpriteToJpg).
                WriteBinary(ctx, captured, "image/jpeg");
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
                WriteBinary(ctx, File.ReadAllBytes(filePath), contentType);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        // ── Icon / weapon-image handler ──────────────────────────────────────────

        internal static void ServeIcon(HttpListenerContext ctx) => ServePng(ctx, _icons, _iconLock, "type");
        internal static void ServeWeaponIcon(HttpListenerContext ctx) => ServePng(ctx, _weaponIcons, _weaponLock, "name");
        internal static void ServeCmIcon(HttpListenerContext ctx) => ServePng(ctx, _cmIcons, _cmLock, "type");
        internal static void ServeTgtIcon(HttpListenerContext ctx) => ServePng(ctx, _tgtIcons, _tgtLock, "type");
        internal static void ServeBdfIcon(HttpListenerContext ctx) => ServePng(ctx, _bdfIcons, _bdfLock, "type");
        internal static void ServeBuildingIcon(HttpListenerContext ctx) => ServePng(ctx, _buildingIcons, _buildingLock, "type");
        internal static void ServeHudCategoryIcon(HttpListenerContext ctx) => ServePng(ctx, _hudCatIcons, _hudCatLock, "cat");

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

            WriteBinary(ctx, png, "image/png");
        }

        // ── Airframe handlers ───────────────────────────────────────────────────

        internal static void ServeAirframeImage(HttpListenerContext ctx)
        {
            string type = ctx.Request.QueryString["type"] ?? string.Empty;
            string part = ctx.Request.QueryString["part"] ?? string.Empty;
            byte[]? png = null;
            if (type.Length > 0 && part.Length > 0)
                lock (_airframeLock) _airframeImages.TryGetValue(type + "|" + part, out png);

            if (png == null) { ctx.Response.StatusCode = 404; try { ctx.Response.Close(); } catch { } return; }
            WriteBinary(ctx, png, "image/png");
        }

        internal static void ServeAirframeLayout(HttpListenerContext ctx)
        {
            string type = ctx.Request.QueryString["type"] ?? string.Empty;
            string? json = null;
            if (type.Length > 0)
                lock (_airframeLock) _airframeLayouts.TryGetValue(type, out json);

            if (json == null) { ctx.Response.StatusCode = 404; try { ctx.Response.Close(); } catch { } return; }
            WriteBinary(ctx, Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8");
        }

        // ── MJPEG handler ──────────────────────────────────────────────────────

        // Long-lived multipart/x-mixed-replace response. Browsers render this directly in
        // an <img> tag — when a new JPEG is written, the image swaps in place.
        internal static async Task HandleMjpegAsync(HttpListenerContext ctx, CancellationToken ct)
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
            Interlocked.Increment(ref _tgpSubscribers);
            // Diagnostic: logs how long a client sat with zero bytes written before the first real
            // frame existed. Confirmed live 2026-08-23 (3.25s and 4.3s cold starts) — kept as an
            // ongoing signal that the fix below is actually working, not removed alongside PerfLog.
            var coldStartWatch = Stopwatch.StartNew();
            bool coldStartLogged = false;
            try
            {
                byte[]? initialJpg;
                lock (_tgpLock) { initialJpg = _tgpJpg; }
                if (initialJpg == null) await WritePart(TgpPlaceholderJpg).ConfigureAwait(false);

                while (!ct.IsCancellationRequested)
                {
                    byte[]? jpg; long id;
                    lock (_tgpLock) { jpg = _tgpJpg; id = _tgpFrameId; }

                    if (!coldStartLogged && jpg != null)
                    {
                        coldStartLogged = true;
                        if (coldStartWatch.ElapsedMilliseconds > 500)
                            Plugin.Log?.LogWarning($"[NOXMFD] TGP MJPEG cold start: client waited {coldStartWatch.ElapsedMilliseconds}ms with zero bytes before the first frame.");
                    }

                    if (jpg != null && id != lastSeen)
                    {
                        lastSeen = id;
                        await WritePart(jpg).ConfigureAwait(false);
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

        internal static async Task HandleSseAsync(HttpListenerContext ctx, CancellationToken ct)
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
                // Per-extension high-rate events (Api.PublishEvent) — one "last sent" entry per
                // event name this connection has seen, same change-gating as cursor above but for
                // a runtime-registered set of names instead of one fixed one.
                var lastExtEvents = new Dictionary<string, string>(StringComparer.Ordinal);
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
                SoiReleaseOnDisconnect(cid);
                try { ctx.Response.Close(); } catch { }
                Plugin.Log?.LogInfo($"[NOXMFD] Client disconnected from {ctx.Request.RemoteEndPoint} (instance {conn})");
            }
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

        // Kept in JsonLite.cs so pure callers like RouteStore.cs can compile standalone in a test
        // project without pulling this file's game touchpoints in. This wrapper preserves this
        // file's local call sites.
        internal static string EscapeJson(string s) => JsonLite.EscapeJson(s);
    }
}
