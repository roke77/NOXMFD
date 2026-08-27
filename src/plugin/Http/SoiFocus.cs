using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace NOXMFD
{
    // SOI (Sensor Of Interest) focus — which display/surface the SOI keys and PAD cursor drive,
    // plus the MAP cursor/action state that only makes sense relative to whichever surface holds
    // it. Split out of TelemetryServer.cs (docs/server-hardening.md) — this is the one remaining
    // stateful subsystem there, distinct from the HTTP transport/frame-cache concerns that stayed.
    internal static class SoiFocus
    {
        // ── SOI focus ──────────────────────────────────────────────────────────
        // Which instance the SOI keys drive, as its cid. Broadcast in every frame so each client can
        // compare it against its own — see the shared-frame note on GetFrameBytes. Empty = nothing
        // focused, which is the state at startup and after the focused display disconnects.
        //
        // _version is what keeps the frame cache honest: the cache is keyed on the snapshot
        // version, and the target can change without a new snapshot — at the main menu, where frames
        // are 1 Hz pings, a stale cached frame would otherwise hide the change indefinitely.
        // Focus is a SURFACE, not a whole document: a cid PLUS which of that instance's surfaces
        // (panes/portals) is focused. An instance shows 1 surface in full view, 2 in a classic split,
        // up to 4 F-35 portals — the client reports the count (soi.panes). _targetPane is -1 when
        // nothing is focused.
        private static string _targetCid  = string.Empty;
        private static int    _targetPane = -1;
        private static long   _version;
        // Guards every change of focus. Connects and disconnects arrive on their own threadpool
        // threads and both can move the target, so "is anything focused?" and the write that follows
        // it have to be one step — otherwise two displays connecting together both see no focus and
        // the second silently steals it.
        private static readonly object _lock = new object();
        internal static string Target     => Volatile.Read(ref _targetCid);
        internal static int    TargetPane => Volatile.Read(ref _targetPane);

        // Read by TelemetryServer.GetFrameBytes to decide whether the cached SSE frame needs
        // rebuilding — focus can move without a new snapshot (see _version above).
        internal static long Version => Interlocked.Read(ref _version);

        // The manual TGP camera's synthetic SOI ring entry (docs/tgp-manual-control.md's PAD Cursor
        // consolidation plan) — a native, non-browser target folded into the same ring real (cid,
        // pane) clients use, so SOI Next/Prev tabs onto it like any other display. The leading space
        // can never arrive from a real client — SseHub's cid sanitizer only lets [a-zA-Z0-9-] through —
        // so a client can't pick this exact cid for itself even by coincidence.
        internal const string NativeTgpCid = " tgp-camera";
        internal static bool IsNativeTgpSoi =>
            string.Equals(Volatile.Read(ref _targetCid), NativeTgpCid, StringComparison.Ordinal);

        // Called by TgpManualControl.Engage() — turning manual mode on steals SOI onto the camera
        // immediately, so the pilot doesn't have to Tab to the newly-added ring entry by hand.
        internal static void ClaimNativeTgpSoi() { lock (_lock) SetTargetLocked(NativeTgpCid, 0); }

        // Called by TgpManualControl.ExitManual() — mirrors ReleaseOnDisconnect: only acts
        // if the camera actually held focus. Unlike a disconnect there's always something else left
        // to focus (every real pane is still in the ring), so this moves to the ring's first member
        // instead of clearing focus outright. Must run after ManualMode has already flipped false,
        // so RingLocked() below no longer offers the camera as a candidate.
        internal static void ReleaseNativeTgpSoi()
        {
            lock (_lock)
            {
                if (!string.Equals(_targetCid, NativeTgpCid, StringComparison.Ordinal)) return;
                var ring = RingLocked();
                if (ring.Count == 0) { SetTargetLocked(string.Empty, -1); return; }
                SetTargetLocked(ring[0].cid, ring[0].pane);
            }
        }

        private static void SetTarget(string cid, int pane) { lock (_lock) SetTargetLocked(cid, pane); }

        private static void SetTargetLocked(string cid, int pane)
        {
            if (cid.Length == 0) pane = -1;   // "nothing focused" has no surface
            if (string.Equals(_targetCid, cid, StringComparison.Ordinal) && _targetPane == pane) return;
            Volatile.Write(ref _targetCid, cid);
            Volatile.Write(ref _targetPane, pane);
            Volatile.Write(ref _focusedPage, string.Empty);   // stale until the new target reports in
            Interlocked.Increment(ref _version);
        }

        // Which page the SOI-focused surface is currently showing, as reported by the shell that
        // owns it (soi.page — the plugin has no way to know a pane's content on its own, unlike
        // (cid, pane) identity). Lets the manual TGP camera (docs/tgp-manual-control.md's PAD
        // Cursor consolidation plan) also receive PAD Cursor input when the pilot is looking at the
        // external TGP page directly, instead of only when they've Tab'd onto the camera's own
        // synthetic ring entry — the TGP page IS this camera's display, so a pilot who never opens
        // the in-cockpit view (or hides it) still expects pointing control to work through it.
        private static string _focusedPage = string.Empty;

        internal static void ReportPage(string cid, int pane, string page)
        {
            lock (_lock)
            {
                // Ignore a report that doesn't match the CURRENT target — it's either stale (focus
                // already moved on before this arrived) or from a surface that was never focused.
                if (!string.Equals(_targetCid, cid, StringComparison.Ordinal) || _targetPane != pane) return;
                Volatile.Write(ref _focusedPage, page ?? string.Empty);
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
            IsNativeTgpSoi || string.Equals(Volatile.Read(ref _focusedPage), "tgp", StringComparison.Ordinal);

        // The last SOI key pressed, and a counter that makes it idempotent to broadcast. A client acts
        // when the counter CHANGES and ignores the field otherwise, so a duplicated frame can't
        // double-press and a dropped one costs at most a repeat of the same value. The plugin has no
        // idea what "up" means on a page — it only says a key was pressed.
        private static long   _seq;
        private static string _act = string.Empty;
        internal static long   Seq => Interlocked.Read(ref _seq);
        internal static string Act => Volatile.Read(ref _act);

        internal static void Action(string act)
        {
            lock (_lock)
            {
                Volatile.Write(ref _act, act);
                Interlocked.Increment(ref _seq);
                Interlocked.Increment(ref _version);   // rebuild the cached frame so the press ships
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
        // pilot is holding it still, and every distinct value would otherwise bump _version and
        // force a full frame re-serialize on the next tick. 1% is far below what the eye can see in
        // cursor speed, so this costs nothing visible and keeps a steady hold genuinely steady.
        // None of the cursor writers touch _version: they ride their own SSE event (CursorJson),
        // so invalidating the shared telemetry frame for them would re-serialize the whole snapshot
        // for a value that frame no longer carries.
        internal static void SetCursorVector(float x, float y)
        {
            x = (float)Math.Round(x, 2);
            y = (float)Math.Round(y, 2);
            lock (_lock)
            {
                if (_cursorX == x && _cursorY == y) return;   // steady hold — nothing to ship
                Volatile.Write(ref _cursorX, x);
                Volatile.Write(ref _cursorY, y);
            }
        }

        // Cursor Select: a discrete press, same idempotent-counter shape as Action/Seq — the
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
            lock (_lock)
            {
                Volatile.Write(ref _mapAct, act);
                Interlocked.Increment(ref _mapActSeq);
            }
        }

        // A display drops. If it was the focused one, focus clears — it does NOT move to another
        // display on its own. SOI is opt-in: the ring only ever appears once the pilot presses a SOI
        // key (Cycle from empty), so it must never re-appear on a display they didn't pick. A
        // mouse/touch user who never touches the keys therefore never sees it. Nothing to do unless
        // the dropped display held focus.
        internal static void ReleaseOnDisconnect(string cid)
        {
            lock (_lock)
            {
                if (!string.Equals(_targetCid, cid, StringComparison.Ordinal)) return;
                var all = SseHub.Instances();   // the disconnecting one is already out of the registry
                // A duplicated tab copies its cid, so a twin may still be holding that display open —
                // keep focus if so, otherwise clear it (the next SOI keypress re-picks a display).
                if (all.Exists(x => string.Equals(x.Cid, cid, StringComparison.Ordinal))) return;
                SetTargetLocked(string.Empty, -1);
            }
        }

        // The flat ring SOI cycles through: every instance's every surface, instance-major and
        // surface-minor, oldest connection first. Deduped by cid so a twin (same cid, second
        // connection) doesn't put the same document in the ring twice. Built under _lock by the
        // callers that need it.
        private static List<(string cid, int pane)> RingLocked()
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
        internal static void Cycle(int dir)
        {
            lock (_lock)
            {
                var ring = RingLocked();
                if (ring.Count == 0) { SetTargetLocked(string.Empty, -1); return; }

                int i = ring.FindIndex(s => string.Equals(s.cid, _targetCid, StringComparison.Ordinal)
                                            && s.pane == _targetPane);
                int next = i < 0
                    ? (dir >= 0 ? 0 : ring.Count - 1)
                    : ((i + dir) % ring.Count + ring.Count) % ring.Count;
                SetTargetLocked(ring[next].cid, ring[next].pane);
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
            lock (_lock)
            {
                foreach (var inst in SseHub.Instances())
                    if (string.Equals(inst.Cid, cid, StringComparison.Ordinal)) inst.PaneCount = n;

                if (string.Equals(_targetCid, cid, StringComparison.Ordinal) && _targetPane >= n)
                    SetTargetLocked(cid, n - 1);
            }
        }

        // SOI's slice of a frame. Shared by the real payload and the no-mission ping, because a display
        // is focusable and drivable at the main menu, where the ping is the only frame there is.
        internal static string SoiJson() => string.Format(CultureInfo.InvariantCulture,
            "\"soiTarget\":\"{0}\",\"soiPane\":{1},\"soiSeq\":{2},\"soiAct\":\"{3}\"",
            TelemetryServer.EscapeJson(Target), TargetPane, Seq, TelemetryServer.EscapeJson(Act));

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
            CursorX, CursorY, CursorSelSeq, TelemetryServer.EscapeJson(MapAct), MapActSeq,
            Volatile.Read(ref _cursorSelHeld) ? "true" : "false");
    }
}
