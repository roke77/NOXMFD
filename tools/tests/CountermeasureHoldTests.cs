namespace NOXMFD.Tests
{
    public class CountermeasureHoldTests
    {
        [Fact]
        public void Idle_is_not_live()
        {
            var hold = new CountermeasureHold();
            Assert.False(hold.Poll(0f));
            Assert.False(hold.Poll(1f));
        }

        [Fact]
        public void Local_key_stays_live_while_requested_every_frame_and_lets_go_one_frame_after()
        {
            var hold = new CountermeasureHold();
            Assert.False(hold.Poll(1.000f));
            hold.Request(1, 1.000f, 0f);           // key down, after this frame's Poll
            Assert.True(hold.Poll(1.016f));
            hold.Request(1, 1.016f, 0f);
            Assert.True(hold.Poll(1.033f));        // still held: must not flicker off
            Assert.False(hold.Poll(1.050f));       // released (no request at 1.033)
        }

        [Fact]
        public void Remote_request_holds_for_its_window()
        {
            var hold = new CountermeasureHold();
            hold.Poll(5.0f);
            hold.Request(2, 5.0f, 0.25f);
            Assert.True(hold.Poll(5.1f));
            Assert.True(hold.Poll(5.2f));
            Assert.True(hold.Poll(5.3f));          // 5.25 >= previous poll 5.2
            Assert.False(hold.Poll(5.4f));
        }

        [Fact]
        public void Shorter_request_does_not_cut_a_longer_one_and_latest_category_wins()
        {
            var hold = new CountermeasureHold();
            hold.Poll(1f);
            hold.Request(2, 1f, 0.25f);
            hold.Request(1, 1f, 0f);
            Assert.True(hold.Poll(1.2f));
            Assert.Equal((byte)1, hold.Category);
        }
    }
}
