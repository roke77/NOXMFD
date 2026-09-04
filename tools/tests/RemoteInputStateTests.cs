using System.Threading;
using Xunit;

namespace NOXMFD.Tests
{
    public class RemoteInputStateTests
    {
        [Fact]
        public void Unknown_group_reads_as_not_held()
        {
            Assert.False(RemoteInputState.GetFire("nonexistent-group-" + System.Guid.NewGuid()));
        }

        [Fact]
        public void Held_group_reads_true_until_release_and_the_min_press_window_elapse()
        {
            string group = "test-" + System.Guid.NewGuid();
            RemoteInputState.SetFire(group, true);
            Assert.True(RemoteInputState.GetFire(group));

            // Release before the ~90ms min-press window elapses — a fast browser tap sending
            // down/up between Unity frames must still read as held for at least one poll.
            RemoteInputState.SetFire(group, false);
            Assert.True(RemoteInputState.GetFire(group));

            Thread.Sleep(150);
            Assert.False(RemoteInputState.GetFire(group));
        }

        [Fact]
        public void Independent_groups_do_not_interfere()
        {
            string a = "test-a-" + System.Guid.NewGuid();
            string b = "test-b-" + System.Guid.NewGuid();
            RemoteInputState.SetFire(a, true);
            Assert.True(RemoteInputState.GetFire(a));
            Assert.False(RemoteInputState.GetFire(b));
        }

        [Fact]
        public void SetFire_reports_the_rising_edge_only_not_the_50ms_keepalive_resends()
        {
            // TelemetryServer logs "fire ON" off this return value on the assumption that a browser
            // holding a key resends held:true every ~50ms — if this ever returned true on every call
            // instead of just the first, that log would spam once per keepalive instead of once per
            // press (docs/remote-keybinds.md).
            string group = "test-" + System.Guid.NewGuid();
            Assert.True(RemoteInputState.SetFire(group, true));   // press: rising edge
            Assert.False(RemoteInputState.SetFire(group, true));  // keepalive while still held: no edge
            Assert.False(RemoteInputState.SetFire(group, true));  // another keepalive: still no edge
            Assert.False(RemoteInputState.SetFire(group, false)); // release is reported unconditionally by the caller, not via this return

            // `active` only actually clears once GetFire() observes the TTL/min-press window has
            // elapsed (same "Poll() drives it" contract Held_group_reads_true_until_release... above
            // exercises) — a real browser press is always separated by many Poll() ticks, so simulate
            // that here rather than calling SetFire twice back-to-back with nothing in between.
            Thread.Sleep(150);
            Assert.False(RemoteInputState.GetFire(group));        // this is what actually clears `active`

            Assert.True(RemoteInputState.SetFire(group, true));   // pressed again: rising edge once more
        }

        [Fact]
        public void SetCursor_reports_a_change_only_when_selectHeld_flips()
        {
            RemoteInputState.SetCursor(0f, 0f, false);   // baseline: not held
            Assert.True(RemoteInputState.SetCursor(0.5f, 0f, true));    // select pressed: changed
            Assert.False(RemoteInputState.SetCursor(0.6f, 0.1f, true)); // keepalive, still held: unchanged
            Assert.True(RemoteInputState.SetCursor(0f, 0f, false));     // select released: changed
        }
    }
}
