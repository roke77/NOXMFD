using System;

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

        private const long RemoteFireTtlTicks = TimeSpan.TicksPerMillisecond * 250;
        // A fast browser tap can send down/up between Unity frames; keep the press visible long
        // enough for Keybinds.Poll() to observe at least one held frame.
        private const long RemoteFireMinPressTicks = TimeSpan.TicksPerMillisecond * 90;
        private static readonly object _remoteFireLock = new object();
        private static bool _remoteFireGun, _remoteFireRelease, _remoteFireJammerPod;
        private static long _remoteFireGunUntilUtcTicks, _remoteFireReleaseUntilUtcTicks, _remoteFireJammerPodUntilUtcTicks;
        private static long _remoteFireGunMinUntilUtcTicks, _remoteFireReleaseMinUntilUtcTicks, _remoteFireJammerPodMinUntilUtcTicks;

        internal static void SetFire(string group, bool held)
        {
            long now = DateTime.UtcNow.Ticks;
            long until = held ? now + RemoteFireTtlTicks : 0L;
            long minUntil = held ? now + RemoteFireMinPressTicks : 0L;
            lock (_remoteFireLock)
            {
                switch (group)
                {
                    case "gun":
                        if (held)
                        {
                            _remoteFireGun = true;
                            _remoteFireGunUntilUtcTicks = until;
                            _remoteFireGunMinUntilUtcTicks = minUntil;
                        }
                        else
                        {
                            _remoteFireGunUntilUtcTicks = 0L;
                        }
                        break;
                    case "release":
                        if (held)
                        {
                            _remoteFireRelease = true;
                            _remoteFireReleaseUntilUtcTicks = until;
                            _remoteFireReleaseMinUntilUtcTicks = minUntil;
                        }
                        else
                        {
                            _remoteFireReleaseUntilUtcTicks = 0L;
                        }
                        break;
                    case "jammer-pod":
                        if (held)
                        {
                            _remoteFireJammerPod = true;
                            _remoteFireJammerPodUntilUtcTicks = until;
                            _remoteFireJammerPodMinUntilUtcTicks = minUntil;
                        }
                        else
                        {
                            _remoteFireJammerPodUntilUtcTicks = 0L;
                        }
                        break;
                    default:
                        Plugin.Log?.LogInfo($"[NOXMFD] fire.set: unknown group '{group}' — ignored.");
                        break;
                }
            }
        }

        internal static void GetFire(out bool gun, out bool release, out bool jammerPod)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (_remoteFireLock)
            {
                if (_remoteFireGun && now > _remoteFireGunUntilUtcTicks && now > _remoteFireGunMinUntilUtcTicks)
                    _remoteFireGun = false;
                if (_remoteFireRelease && now > _remoteFireReleaseUntilUtcTicks && now > _remoteFireReleaseMinUntilUtcTicks)
                    _remoteFireRelease = false;
                if (_remoteFireJammerPod && now > _remoteFireJammerPodUntilUtcTicks && now > _remoteFireJammerPodMinUntilUtcTicks)
                    _remoteFireJammerPod = false;
                gun = _remoteFireGun;
                release = _remoteFireRelease;
                jammerPod = _remoteFireJammerPod;
            }
        }
    }
}
