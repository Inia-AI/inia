using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// A third-party key travels in a Web API call's address or header, and its template keeps it there by decision. Whatever
/// the call's outcome, the key reaches no log line at any level, no failure text a reader of the run sees, and no
/// response stored from a server's error answer.
/// </summary>
public sealed class WebApiCallKeyNeverLeavesTests
    : IClassFixture<WorkflowsFixture<WebApiCallKeyNeverLeavesTests>>, IClassFixture<LocalTestHttpServer>
{
    private const string KEY = "QUOKKA-KEY-5E19";
    private const string QUERY = "query";
    private const string HEADER = "header";
    private const string REFUSED_BASE_URL = "http://127.0.0.1:1";

    private readonly IServiceProvider _services;
    private readonly LocalTestHttpServer _server;
    private readonly CapturingLoggerProvider _logs = new();

    public WebApiCallKeyNeverLeavesTests(WorkflowsFixture<WebApiCallKeyNeverLeavesTests> fixture, LocalTestHttpServer server, ITestOutputHelper output)
    {
        fixture.PlatformSettings.OutboundAccess.AllowedInternalHosts = ["127.0.0.1"];
        _services = fixture.BuildTestServiceProvider(output, services =>
        {
            _ = services.AddSingleton<ILoggerProvider>(_logs);
            _ = services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Trace);
        });
        _server = server;
    }

    public static TheoryData<string, string> Outcomes => new()
    {
        { "success", QUERY }, { "success", HEADER },
        { "401", QUERY }, { "401", HEADER },
        { "500 echoing the address", QUERY }, { "500 echoing the address", HEADER },
        { "timeout", QUERY }, { "timeout", HEADER },
        { "refused connection", QUERY }, { "refused connection", HEADER },
    };

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task TheKey_ReachesNoLogNoFailureTextAndNoStoredErrorAnswer(string outcome, string position)
    {
        string url = Address(outcome, position);
        var block = new Block(BlockTypes.WebApiCall, new Section("test", new Workflow("test")))
        {
            Name = "Quote",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = url,
                HttpMethod = "GET",
                AdditionalHeaders = position == HEADER ? new Dictionary<string, string> { ["Authorization"] = $"Bearer {KEY}" } : [],
            },
        };
        BlockExecutionContext context = _services.GetRequiredService<BlockExecutionContext>();
        block.SetExecutionState(false, []);

        Exception? failure = await Record.ExceptionAsync(() => new FastStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        List<string> leaks = [.. _logs.Entries
            .Where(entry => entry.Text.Contains(KEY, StringComparison.Ordinal) || entry.Values.Any(value => value.Value?.ToString()?.Contains(KEY, StringComparison.Ordinal) == true))
            .Select(entry => $"log {entry.Level}: {entry.Message}")];
        if (failure is not null && BlockFailureException.ReasonOf(failure).Contains(KEY, StringComparison.Ordinal))
        {
            leaks.Add($"failure text: {BlockFailureException.ReasonOf(failure)}");
        }

        if (failure is not null && block.BlockResponse?.RawResponse?.Contains(KEY, StringComparison.Ordinal) == true)
        {
            leaks.Add("the stored response of an error answer");
        }

        Assert.True(leaks.Count == 0, $"{outcome}, key in the {position}: {string.Join(" | ", leaks)}");
    }

    private string Address(string outcome, string position)
    {
        string path = outcome switch
        {
            "success" => $"{_server.BaseUrl}/status/200",
            "401" => $"{_server.BaseUrl}/status/401",
            "500 echoing the address" => $"{_server.BaseUrl}/status/500",
            "timeout" => $"{_server.BaseUrl}/delay",
            _ => $"{REFUSED_BASE_URL}/v3/quote",
        };
        string query = position == QUERY ? $"apikey={KEY}&symbol=X" : "symbol=X";
        string address = $"{path}?{query}";
        return outcome switch
        {
            "500 echoing the address" => $"{address}&body={Uri.EscapeDataString(address)}",
            "timeout" => $"{address}&ms=2000",
            _ => address,
        };
    }

    /// <summary>The strategy with its waits taken out: a retry does not sleep, and a request times out in a moment.</summary>
    private sealed class FastStrategy : WebApiCallStrategy
    {
        protected override TimeSpan RequestTimeout => TimeSpan.FromMilliseconds(200);

        protected override Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected override Task DelayBetweenPollsAsync(int milliseconds, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
