using System;
using System.Collections.Generic;
using System.Threading;

namespace NOXMFD
{
    internal static class RemoteInputState
    {
        // Remote browser cursor input is a second held source, merged by Keybinds.Poll() with the
        // local keyboard/axis state. It expires quickly so a lost keyup, closed tab, or network drop
        // cannot strand the MAP/TGT cursor in a held direction.
        private const long RemoteCursorTtlTicks = TimeSpan.TicksPerMillisecond * 250;
        private static readonly object _remoteCursorLock = new object();
        private static float _remoteCursorX, _remoteCursorY;
        private static bool _remoteCursorSelectHeld;
        private static long _remoteCursorUntilUtcTicks;

        // Returns true when selectHeld just changed — the caller (TelemetryServer, which has the
        // logger) logs on that edge only. Kept as a plain return value rather than calling out to a
        // logger directly: this file has no BepInEx/Unity reference and is linked straight into
        // tools/tests for that reason (docs/remote-keybinds.md).
        internal static bool SetCursor(float x, float y, bool selectHeld)
        {
            lock (_remoteCursorLock)
            {
                bool changed = selectHeld != _remoteCursorSelectHeld;
                _remoteCursorX = x;
                _remoteCursorY = y;
                _remoteCursorSelectHeld = selectHeld;
                // Volatile, not a plain assignment: GetCursor reads this field unlocked as a fast-path
                // check below, so it needs the release-fence guarantee Volatile.Write gives, not just
                // whatever a lock's own exit fence happens to provide to an unsynchronized reader.
                Volatile.Write(ref _remoteCursorUntilUtcTicks, DateTime.UtcNow.Ticks + RemoteCursorTtlTicks);
                return changed;
            }
        }

        internal static void GetCursor(out float x, out float y, out bool selectHeld)
        {
            // Unsynchronized fast path: _remoteCursorUntilUtcTicks only ever moves forward (SetCursor
            // raises it under the lock; nothing here ever lowers it), so a stale unlocked read can
            // only be wrong toward "still active", never falsely "expired" -- skipping the lock
            // whenever it reads as expired is always correct. This is the steady state for any
            // keyboard/HOTAS-only player, and for anyone else between uses of the remote cursor
            // (docs/plugin-efficiency-audit.md finding 05).
            if (DateTime.UtcNow.Ticks > Volatile.Read(ref _remoteCursorUntilUtcTicks))
            {
                x = 0f; y = 0f; selectHeld = false;
                return;
            }
            lock (_remoteCursorLock)
            {
                if (DateTime.UtcNow.Ticks > _remoteCursorUntilUtcTicks)
                {
                    _remoteCursorX = 0f;
                    _remoteCursorY = 0f;
                    _remoteCursorSelectHeld = false;
                }
                x = _remoteCursorX;
                y = _remoteCursorY;
                selectHeld = _remoteCursorSelectHeld;
            }
        }

        // Named held-state flags (gun/release/release-single/jammer-pod fire, zoom-in/zoom-out) —
        // one arbitrary-string-keyed table rather than a field quad per group, so Keybinds.cs adding
        // a new remoteable held bind never needs a matching edit here. A fast browser tap can send
        // down/up between Unity frames, so a min-press window keeps the press visible long enough
        // for Poll() to observe at least one held frame; the TTL is the safety valve if a browser tab
        // closes or drops a keyup.
        private const long RemoteFireTtlTicks = TimeSpan.TicksPerMillisecond * 250;
        private const long RemoteFireMinPressTicks = TimeSpan.TicksPerMillisecond * 90;
        private static readonly object _remoteFireLock = new object();
        private static readonly Dictionary<string, (bool active, long untilUtcTicks, long minUntilUtcTicks)> _remoteFire =
            new Dictionary<string, (bool, long, long)>();
        // Max of every group's own (untilUtcTicks, minUntilUtcTicks) ever written by SetFire -- an
        // aggregate "is anything live in any group" fast-path check for GetFire, below. Only ever
        // raised, never lowered, so an unsynchronized read is safe the same way
        // _remoteCursorUntilUtcTicks's is (see GetCursor's comment).
        private static long _remoteFireActiveUntilUtcTicks;

        // Returns true only on the rising edge (this call transitioned the group from not-held to
        // held) — the caller logs on that, plus unconditionally on every `held:false` (already
        // low-frequency: the browser sends that once on keyup, not as a keepalive). Kept as a plain
        // return value rather than calling out to a logger directly — see SetCursor's own comment.
        internal static bool SetFire(string group, bool held)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (_remoteFireLock)
            {
                _remoteFire.TryGetValue(group, out var f);
                bool risingEdge = held && !f.active;
                if (held)
                {
                    f.active = true;
                    f.untilUtcTicks = now + RemoteFireTtlTicks;
                    f.minUntilUtcTicks = now + RemoteFireMinPressTicks;
                }
                else
                {
                    // Only the TTL clears on release — minUntilUtcTicks (set by the last press) is
                    // left alone so GetFire still honors the remaining min-press window instead of
                    // dropping a lightning-fast tap that released before the next frame polled it.
                    f.untilUtcTicks = 0L;
                }
                _remoteFire[group] = f;
                long groupUntil = Math.Max(f.untilUtcTicks, f.minUntilUtcTicks);
                if (groupUntil > _remoteFireActiveUntilUtcTicks) Volatile.Write(ref _remoteFireActiveUntilUtcTicks, groupUntil);
                return risingEdge;
            }
        }

        internal static bool GetFire(string group)
        {
            // Unsynchronized fast path, same reasoning as GetCursor's: _remoteFireActiveUntilUtcTicks
            // only grows, so reading it here without the lock can only be stale toward "something's
            // still active" -- never falsely "everything's idle" -- so returning false straight away
            // whenever it reads as expired is always correct, for every group at once
            // (docs/plugin-efficiency-audit.md finding 05). A live group falls through to the locked
            // per-group lookup below, same as before.
            long now = DateTime.UtcNow.Ticks;
            if (now > Volatile.Read(ref _remoteFireActiveUntilUtcTicks)) return false;
            lock (_remoteFireLock)
            {
                if (!_remoteFire.TryGetValue(group, out var f)) return false;
                if (f.active && now > f.untilUtcTicks && now > f.minUntilUtcTicks)
                {
                    f.active = false;
                    _remoteFire[group] = f;
                }
                return f.active;
            }
        }
    }
}
