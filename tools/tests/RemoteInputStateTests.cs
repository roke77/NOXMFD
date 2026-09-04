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
    }
}
