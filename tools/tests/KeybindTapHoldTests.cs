namespace NOXMFD.Tests
{
    public class KeybindTapHoldTests
    {
        [Fact]
        public void Press_fires_tap_immediately()
        {
            float started = -1f;
            bool holdFired = true; // previous press may have held; a fresh press resets it.

            var ev = KeybindTapHold.Poll(activeNow: true, now: 10f, ref started, ref holdFired);

            Assert.Equal(KeybindTapHold.Event.Tap, ev);
            Assert.Equal(10f, started);
            Assert.False(holdFired);
        }

        [Fact]
        public void Hold_fires_once_after_threshold()
        {
            float started = -1f;
            bool holdFired = false;

            Assert.Equal(KeybindTapHold.Event.Tap, KeybindTapHold.Poll(true, 1.00f, ref started, ref holdFired));
            Assert.Equal(KeybindTapHold.Event.None, KeybindTapHold.Poll(true, 1.34f, ref started, ref holdFired));
            Assert.Equal(KeybindTapHold.Event.Hold, KeybindTapHold.Poll(true, 1.35f, ref started, ref holdFired));
            Assert.Equal(KeybindTapHold.Event.None, KeybindTapHold.Poll(true, 2.00f, ref started, ref holdFired));
            Assert.True(holdFired);
        }

        [Fact]
        public void Release_resets_press_start_so_next_press_can_tap_again()
        {
            float started = -1f;
            bool holdFired = false;

            KeybindTapHold.Poll(true, 4.00f, ref started, ref holdFired);
            KeybindTapHold.Poll(true, 4.40f, ref started, ref holdFired);

            var release = KeybindTapHold.Poll(false, 4.50f, ref started, ref holdFired);

            Assert.Equal(KeybindTapHold.Event.None, release);
            Assert.Equal(-1f, started);

            var nextPress = KeybindTapHold.Poll(true, 5.00f, ref started, ref holdFired);

            Assert.Equal(KeybindTapHold.Event.Tap, nextPress);
            Assert.Equal(5.00f, started);
            Assert.False(holdFired);
        }

        [Fact]
        public void Custom_threshold_is_supported_for_boundary_tests()
        {
            float started = -1f;
            bool holdFired = false;

            Assert.Equal(KeybindTapHold.Event.Tap, KeybindTapHold.Poll(true, 0f, 0.5f, ref started, ref holdFired));
            Assert.Equal(KeybindTapHold.Event.None, KeybindTapHold.Poll(true, 0.49f, 0.5f, ref started, ref holdFired));
            Assert.Equal(KeybindTapHold.Event.Hold, KeybindTapHold.Poll(true, 0.50f, 0.5f, ref started, ref holdFired));
        }
    }
}
