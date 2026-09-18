namespace NOXMFD.Tests
{
    public class DocNameMatchTests
    {
        private static readonly string[] Files = { "heartland.png", "kadena.jpg", "checklist.jpeg" };

        [Fact]
        public void Find_returns_an_exact_match()
        {
            Assert.Equal("kadena.jpg", DocNameMatch.Find(Files, "kadena.jpg"));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("nonexistent.png")]
        // Path-traversal attempts never match a plain file name in the live listing — the endpoint
        // never Path.Combine's the raw query value, so these just 404 instead of resolving anywhere.
        [InlineData("../../secrets.png")]
        [InlineData("..\\..\\secrets.png")]
        [InlineData("/etc/passwd")]
        public void Find_returns_null_for_anything_not_in_the_live_listing(string? requested)
        {
            Assert.Null(DocNameMatch.Find(Files, requested!));
        }

        [Fact]
        public void Find_is_case_sensitive()
        {
            Assert.Null(DocNameMatch.Find(Files, "Kadena.jpg"));
        }

        [Fact]
        public void Find_against_an_empty_listing_never_matches()
        {
            Assert.Null(DocNameMatch.Find(System.Array.Empty<string>(), "kadena.jpg"));
        }
    }
}
