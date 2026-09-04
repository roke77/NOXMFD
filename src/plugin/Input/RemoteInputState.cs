using System;
using System.Collections.Generic;

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

        internal static void SetCursor(float x, float y, bool selectHeld)
        {
            lock (_remoteCursorLock)
            {
                _remoteCursorX = x;
                _remoteCursorY = y;
                _remoteCursorSelectHeld = selectHeld;
                _remoteCursorUntilUtcTicks = DateTime.UtcNow.Ticks + RemoteCursorTtlTicks;
            }
        }

        internal static void GetCursor(out float x, out float y, out bool selectHeld)
        {
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

        internal static void SetFire(string group, bool held)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (_remoteFireLock)
            {
                _remoteFire.TryGetValue(group, out var f);
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
            }
        }

        internal static bool GetFire(string group)
        {
            long now = DateTime.UtcNow.Ticks;
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
