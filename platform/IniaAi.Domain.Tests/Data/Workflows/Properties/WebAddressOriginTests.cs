using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class WebAddressOriginTests
{
    [Theory]
    [InlineData("https://api.perplexity.ai/chat/completions", "https://api.perplexity.ai")]
    [InlineData("https://API.Example.com:8443/v1/{Id}?q={Query}", "https://api.example.com:8443")]
    [InlineData("https://api.example.com/v1/{{Id}}?q={{Query}}", "https://api.example.com")]
    [InlineData("https://api.example.com?q={Query}", "https://api.example.com")]
    [InlineData("https://api.example.com#{Fragment}", "https://api.example.com")]
    [InlineData("https://financialmodelingprep.com", "https://financialmodelingprep.com")]
    [InlineData("http://api.example.com:80/v1", "http://api.example.com")]
    public void Of_AnAddressWhoseHostIsWrittenOut_IsItsSchemeHostAndPort(string template, string origin)
    {
        Assert.Equal(origin, WebAddressOrigin.Of(template));
    }

    [Theory]
    [InlineData("{{TargetUrl}}")]
    [InlineData("{target}")]
    [InlineData("https://{{Host}}/v1/search")]
    [InlineData("https://{host}/latest/meta-data/")]
    [InlineData("https://api.{env}.example.com/v1")]
    [InlineData("https://api.example.com{path}")]
    [InlineData("https://api.example.com:{port}/v1")]
    [InlineData("{part}://api.example.com/v1")]
    [InlineData("https://user{name}@api.example.com/v1")]
    [InlineData("ftp://files.example.com/a.csv")]
    [InlineData("api.example.com/v1")]
    [InlineData("https:///v1")]
    [InlineData("")]
    public void Of_AnAddressWhoseHostAPlaceholderCanReach_IsNull(string template)
    {
        Assert.Null(WebAddressOrigin.Of(template));
    }

    [Fact]
    public void FixedHostProblems_NameTheBlockAndEachAddressAPlaceholderCanMove()
    {
        var properties = new WebApiCallProperties
        {
            UrlTemplate = "https://{host}/v1/tasks",
            PollCompletionUrlTemplate = "{Start.result_url}",
        };

        List<string> problems = [.. properties.FixedHostProblems("Start search")];

        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.Contains("'Start search'", problem, StringComparison.Ordinal));
        Assert.Contains(WebApiCallProperties.URL_TEMPLATE_LABEL, problems[0], StringComparison.Ordinal);
        Assert.Contains(WebApiCallProperties.COMPLETION_URL_TEMPLATE_LABEL, problems[1], StringComparison.Ordinal);
    }

    [Fact]
    public void FixedHostProblems_OfAddressesWrittenOutOrLeftEmpty_AreNone()
    {
        var properties = new WebApiCallProperties
        {
            UrlTemplate = "https://api.parallel.ai/v1/tasks/runs/{Start.run_id}",
            PollCompletionUrlTemplate = null,
        };

        Assert.Empty(properties.FixedHostProblems("Fetch result"));
    }
}
