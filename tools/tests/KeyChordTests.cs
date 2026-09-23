namespace NOXMFD.Tests
{
    public class KeyChordTests
    {
        [Fact]
        public void Join_normalizes_side_and_order()
        {
            Assert.Equal("LeftControl+LeftAlt+Alpha1", KeyChord.Join(new[] { "RightAlt", "LeftControl" }, "Alpha1"));
            Assert.Equal("F5", KeyChord.Join(new string[0], "F5"));
            Assert.Equal("LeftAlt", KeyChord.Join(new string[0], "LeftAlt"));   // lone modifier keeps its name
        }

        [Fact]
        public void Split_accepts_chords_and_lone_keys()
        {
            Assert.True(KeyChord.TrySplit("LeftControl+LeftShift+S", out var mods, out var main));
            Assert.Equal(new[] { "LeftControl", "LeftShift" }, mods);
            Assert.Equal("S", main);
            Assert.True(KeyChord.TrySplit("RightAlt", out mods, out main));
            Assert.Empty(mods);
            Assert.Equal("RightAlt", main);
            Assert.True(KeyChord.TrySplit("LeftControl+LeftAlt", out _, out _));   // Ctrl held, Alt released last
        }

        [Theory]
        [InlineData("A+B")]                   // non-modifier in a modifier position
        [InlineData("LeftAlt+RightAlt+A")]    // same modifier twice
        [InlineData("LeftAlt+RightAlt")]      // main key is its own modifier
        [InlineData("LeftAlt+")]              // empty main
        [InlineData("+A")]                    // empty modifier
        public void Split_rejects_malformed(string name) =>
            Assert.False(KeyChord.TrySplit(name, out _, out _));
    }
}
