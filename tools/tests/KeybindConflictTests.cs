namespace NOXMFD.Tests
{
    public class KeybindConflictTests
    {
        private static readonly string[] Ids = { "flares", "layout-load", "layout-preset-1", "layout-preset-2", "gear-up" };

        // keys[i] = the key bind i currently uses ("" = unbound); returns the conflicting index for
        // assigning `key` to bind `target`.
        private static int Find(string[] keys, int target, string key) =>
            KeybindConflict.Find(Ids.Length, i => Ids[i], target, i => keys[i] == key);

        [Fact]
        public void Slot_cannot_take_a_key_another_bind_uses()
        {
            var keys = new[] { "F", "L", "", "", "" };
            Assert.Equal(0, Find(keys, 2, "F"));   // flares
            Assert.Equal(1, Find(keys, 3, "L"));   // Load Layout
        }

        [Fact]
        public void Other_bind_cannot_take_a_slot_key()
        {
            var keys = new[] { "", "", "F1", "", "" };
            Assert.Equal(2, Find(keys, 4, "F1"));
        }

        [Fact]
        public void Non_slot_binds_may_still_share_a_key()
        {
            var keys = new[] { "G", "", "", "", "" };
            Assert.Equal(-1, Find(keys, 4, "G"));
        }

        [Fact]
        public void Reassigning_a_slot_its_own_key_is_allowed()
        {
            var keys = new[] { "", "", "F1", "", "" };
            Assert.Equal(-1, Find(keys, 2, "F1"));
        }

        [Fact]
        public void Joystick_any_device_overlaps_pinned_devices()
        {
            Assert.True(KeybindConflict.JoyMatches(5, 0, 5, 2));
            Assert.True(KeybindConflict.JoyMatches(5, 2, 5, 2));
            Assert.False(KeybindConflict.JoyMatches(5, 1, 5, 2));
            Assert.False(KeybindConflict.JoyMatches(5, 1, 6, 1));
            Assert.False(KeybindConflict.JoyMatches(-1, 0, -1, 0));   // two unbound slots never clash
        }
    }
}
