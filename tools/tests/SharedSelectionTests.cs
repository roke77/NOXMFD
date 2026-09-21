namespace NOXMFD.Tests
{
    // SharedSelection (docs/atc-extension-support.md item 3) is process-wide mutable state, same
    // shape as TargetFocus — each test drives it to a known value rather than assuming a starting
    // state, so tests stay independent of xunit's run order.
    public class SharedSelectionTests
    {
        [Fact]
        public void Defaults_to_none()
        {
            SharedSelection.Set(0);
            Assert.Equal(0u, SharedSelection.Id);
        }

        [Fact]
        public void Set_round_trips_through_id()
        {
            SharedSelection.Set(123);
            Assert.Equal(123u, SharedSelection.Id);
            SharedSelection.Set(0);
        }

        [Fact]
        public void Set_zero_clears_a_previous_selection()
        {
            SharedSelection.Set(456);
            Assert.Equal(456u, SharedSelection.Id);
            SharedSelection.Set(0);
            Assert.Equal(0u, SharedSelection.Id);
        }

        [Fact]
        public void Track_defaults_to_off()
        {
            SharedSelection.SetTrack(false);
            Assert.False(SharedSelection.Track);
        }

        [Fact]
        public void SetTrack_round_trips()
        {
            SharedSelection.SetTrack(true);
            Assert.True(SharedSelection.Track);
            SharedSelection.SetTrack(false);
            Assert.False(SharedSelection.Track);
        }
    }
}
