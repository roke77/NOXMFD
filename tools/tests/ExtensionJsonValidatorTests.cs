namespace NOXMFD.Tests;

public sealed class ExtensionJsonValidatorTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("-12.5e+2")]
    [InlineData("\"escaped \\\"text\\\"\"")]
    [InlineData("[1, {\"nested\": [false, null]}]")]
    [InlineData("{\"unicode\": \"\\u00e9\"}")]
    public void AcceptsCompleteJsonValues(string json) =>
        Assert.True(ExtensionJsonValidator.IsCompleteValue(json));

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"x\":}")]
    [InlineData("[1,")]
    [InlineData("\"unterminated")]
    [InlineData("01")]
    [InlineData("true trailing")]
    [InlineData("{\"x\": \"\\q\"}")]
    public void RejectsMalformedOrTrailingJson(string json) =>
        Assert.False(ExtensionJsonValidator.IsCompleteValue(json));
}
