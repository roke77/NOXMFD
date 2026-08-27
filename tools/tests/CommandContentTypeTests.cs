namespace NOXMFD.Tests
{
    public class CommandContentTypeTests
    {
        [Theory]
        [InlineData("application/json")]
        [InlineData("Application/Json")]
        [InlineData("application/json; charset=utf-8")]
        public void IsJson_accepts_application_json(string contentType)
        {
            Assert.True(CommandContentType.IsJson(contentType));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("text/plain")]
        [InlineData("application/x-www-form-urlencoded")]
        [InlineData("application/json-patch+json")]
        public void IsJson_rejects_non_application_json(string? contentType)
        {
            Assert.False(CommandContentType.IsJson(contentType));
        }
    }
}
