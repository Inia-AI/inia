using IniaAi.Common.Text;

namespace IniaAi.Common.Tests.Text;

public class LogSanitizerTests
{
    [Theory]
    [InlineData("https://api.example.com/v3/quote?apikey=SECRET&symbol=X", "https://api.example.com/v3/quote")]
    [InlineData("https://user:SECRET@api.example.com/v3/quote", "https://api.example.com/v3/quote")]
    [InlineData("https://api.example.com/v3/quote#token=SECRET", "https://api.example.com/v3/quote")]
    [InlineData("https://user:SECRET@api.example.com:8443?apikey=SECRET", "https://api.example.com:8443")]
    [InlineData("not a url ?apikey=SECRET", "not a url ")]
    [InlineData("https://api.example.com/a@b/c", "https://api.example.com/a@b/c")]
    public void ForLogUrl_LeavesOutTheQueryTheFragmentAndTheUserInfo(string url, string expected)
    {
        Assert.Equal(expected, LogSanitizer.ForLogUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ForLogUrl_OfNothing_IsEmpty(string? url)
    {
        Assert.Equal(string.Empty, LogSanitizer.ForLogUrl(url));
    }

    [Fact]
    public void ForLogUrl_DropsALineBreak_AsForLogDoes()
    {
        Assert.Equal("https://api.example.com/forged", LogSanitizer.ForLogUrl("https://api.example.com/\nforged"));
    }
}
