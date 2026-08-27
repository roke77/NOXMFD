using System;
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

        // The manual TGP camera's synthetic SOI ring entry (docs/tgp-manual-control.md's PAD Cursor
        // consolidation plan) — a native, non-browser target folded into the same ring real (cid,
        // pane) clients use, so SOI Next/Prev tabs onto it like any other display. The leading space
        // can never arrive from a real client — SseHub's cid sanitizer only lets [a-zA-Z0-9-] through —
        // so a client can't pick this exact cid for itself even by coincidence.
        internal const string NativeTgpCid = " tgp-camera";
        internal static bool IsNativeTgpSoi =>
            string.Equals(Volatile.Read(ref _soiTargetCid), NativeTgpCid, StringComparison.Ordinal);

        // Called by TgpManualControl.Engage() — turning manual mode on steals SOI onto the camera
        // immediately, so the pilot doesn't have to Tab to the newly-added ring entry by hand.
        internal static void ClaimNativeTgpSoi() { lock (_soiLock) SetSoiTargetLocked(NativeTgpCid, 0); }

        // Called by TgpManualControl.ExitManual() — mirrors SoiReleaseOnDisconnect: only acts
        // if the camera actually held focus. Unlike a disconnect there's always something else left
        // to focus (every real pane is still in the ring), so this moves to the ring's first member
        // instead of clearing focus outright. Must run after ManualMode has already flipped false,
        // so SoiRingLocked() below no longer offers the camera as a candidate.
        internal static void ReleaseNativeTgpSoi()
        {
            lock (_soiLock)
            {
                if (!string.Equals(_soiTargetCid, NativeTgpCid, StringComparison.Ordinal)) return;
                var ring = SoiRingLocked();
                if (ring.Count == 0) { SetSoiTargetLocked(string.Empty, -1); return; }
                SetSoiTargetLocked(ring[0].cid, ring[0].pane);
            }
        }

        private static void SetSoiTarget(string cid, int pane) { lock (_soiLock) SetSoiTargetLocked(cid, pane); }

        private static void SetSoiTargetLocked(string cid, int pane)
        {
            if (cid.Length == 0) pane = -1;   // "nothing focused" has no surface
            if (string.Equals(_soiTargetCid, cid, StringComparison.Ordinal) && _soiTargetPane == pane) return;
            Volatile.Write(ref _soiTargetCid, cid);
            Volatile.Write(ref _soiTargetPane, pane);
            Volatile.Write(ref _soiFocusedPage, string.Empty);   // stale until the new target reports in
            Interlocked.Increment(ref _soiVersion);
        }

        // Which page the SOI-focused surface is currently showing, as reported by the shell that
        // owns it (soi.page — the plugin has no way to know a pane's content on its own, unlike
        // (cid, pane) identity). Lets the manual TGP camera (docs/tgp-manual-control.md's PAD
        // Cursor consolidation plan) also receive PAD Cursor input when the pilot is looking at the
        // external TGP page directly, instead of only when they've Tab'd onto the camera's own
        // synthetic ring entry — the TGP page IS this camera's display, so a pilot who never opens
        // the in-cockpit view (or hides it) still expects pointing control to work through it.
        private static string _soiFocusedPage = string.Empty;

        internal static void ReportSoiPage(string cid, int pane, string page)
        {
            lock (_soiLock)
            {
                // Ignore a report that doesn't match the CURRENT target — it's either stale (focus
                // already moved on before this arrived) or from a surface that was never focused.
                if (!string.Equals(_soiTargetCid, cid, StringComparison.Ordinal) || _soiTargetPane != pane) return;
                Volatile.Write(ref _soiFocusedPage, page ?? string.Empty);
            }
        }

        // True while PAD Cursor input should reach the manual TGP camera: either its own synthetic
        // SOI entry is focused (Tab'd onto directly), or an ordinary pane/portal that happens to be
        // showing the TGP page is focused. Distinct from IsNativeTgpSoi (ring identity — exactly the
        // synthetic entry, no more) — this one is about function, not the visual ring, so the two
        // must stay separate: a real TGP pane is its own independently focusable ring member, so the
        // ring must never light it up just because the CAMERA — a different ring member — is
        // focused; conflating the two would mis-highlight a pane that isn't actually SOI.
        internal static bool IsTgpSoi =>
            IsNativeTgpSoi || string.Equals(Volatile.Read(ref _soiFocusedPage), "tgp", StringComparison.Ordinal);

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

        internal static void SetRemoteCursorState(float x, float y, bool selectHeld) =>
            RemoteInputState.SetCursor(x, y, selectHeld);

        internal static void GetRemoteCursorState(out float x, out float y, out bool selectHeld) =>
            RemoteInputState.GetCursor(out x, out y, out selectHeld);

        internal static void SetRemoteFireState(string group, bool held) =>
            RemoteInputState.SetFire(group, held);

        internal static void GetRemoteFireState(out bool gun, out bool release, out bool jammerPod) =>
            RemoteInputState.GetFire(out gun, out release, out jammerPod);

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
        internal static void SoiReleaseOnDisconnect(string cid)
        {
            lock (_soiLock)
            {
                if (!string.Equals(_soiTargetCid, cid, StringComparison.Ordinal)) return;
                var all = SseHub.Instances();   // the disconnecting one is already out of the registry
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
            foreach (var inst in SseHub.Instances())
            {
                if (!seen.Add(inst.Cid)) continue;
                for (int p = 0; p < inst.PaneCount; p++) ring.Add((inst.Cid, p));
            }
            // The manual TGP camera joins the same ring, but only while it's actually engaged
            // (docs/tgp-manual-control.md's PAD Cursor consolidation plan) — appended last so an
            // existing pane layout's cycle order doesn't shift under a pilot who never touches it.
            if (TgpManualControl.ManualMode) ring.Add((NativeTgpCid, 0));
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
                foreach (var inst in SseHub.Instances())
                    if (string.Equals(inst.Cid, cid, StringComparison.Ordinal)) inst.PaneCount = n;

                if (string.Equals(_soiTargetCid, cid, StringComparison.Ordinal) && _soiTargetPane >= n)
                    SetSoiTargetLocked(cid, n - 1);
            }
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
        internal static string CursorJson() => string.Format(CultureInfo.InvariantCulture,
            "{{\"x\":{0:0.00},\"y\":{1:0.00},\"selSeq\":{2},\"act\":\"{3}\",\"actSeq\":{4},\"held\":{5}}}",
            CursorX, CursorY, CursorSelSeq, EscapeJson(MapAct), MapActSeq,
            Volatile.Read(ref _cursorSelHeld) ? "true" : "false");

        // Kept in JsonLite.cs so pure callers like RouteStore.cs can compile standalone in a test
        // project without pulling this file's game touchpoints in. This wrapper preserves this
        // file's local call sites.
        internal static string EscapeJson(string s) => JsonLite.EscapeJson(s);
    }
}
