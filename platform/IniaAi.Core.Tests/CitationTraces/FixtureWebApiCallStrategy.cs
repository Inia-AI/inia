using IniaAi.Core.Workflows.Strategies;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// A <see cref="WebApiCallStrategy"/> whose HTTP round-trip returns a canned body,
/// so a captured provider response replays through the real request build,
/// provider detection and response parsing without a network call.
/// </summary>
internal sealed class FixtureWebApiCallStrategy : WebApiCallStrategy
{
    private readonly string _cannedBody;

    public FixtureWebApiCallStrategy(string cannedBody)
    {
        _cannedBody = cannedBody;
    }

    protected override Task<string> ExecuteRequestAsync(
        HttpMessageHandler handler,
        string url,
        string method,
        string? payload,
        Dictionary<string, string> headers,
        string contentType,
        string acceptHeader,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_cannedBody);
    }
}
