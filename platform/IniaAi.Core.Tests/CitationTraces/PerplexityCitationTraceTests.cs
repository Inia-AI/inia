using IniaAi.Core.Tests.Workflows;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// Records the Perplexity citation pipeline at every transformation stage.
///
/// Two scenarios:
/// 1. Replay a fictional Perplexity response (with [N] markers intact)
///    through WebApiCallStrategy + PerplexityResponseParser. Deterministic.
/// 2. Optional live call gated by [PaidApiFact] and the PERPLEXITY_API_KEY env var (skips locally
///    unless both INIA_TEST_PAID_APIS and the key are set, and always skips in CI).
/// </summary>
public class PerplexityCitationTraceTests : IClassFixture<WorkflowsFixture<PerplexityCitationTraceTests>>
{
    private const string FIXTURE_FILE = "CitationTraces/Fixtures/perplexity-ownership-response.json";
    private const string PERPLEXITY_URL = "https://api.perplexity.ai/chat/completions";

    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _outputHelper;

    public PerplexityCitationTraceTests(
        WorkflowsFixture<PerplexityCitationTraceTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _testServiceProvider = fixture.BuildTestServiceProvider(_outputHelper);
    }

    [Fact]
    public async Task Trace_ReplaysPerplexityOwnershipFixture_AndCapturesEveryStageAsync()
    {
        // Arrange — load the fictional Perplexity response (markers intact)
        string fixturePath = Path.Combine(AppContext.BaseDirectory, FIXTURE_FILE);
        Assert.True(File.Exists(fixturePath), $"Fixture not found at {fixturePath} — check csproj copy directives");
        string responseBody = await File.ReadAllTextAsync(fixturePath, TestContext.Current.CancellationToken);

        // Confirm fixture has unstripped markers — otherwise the parser has nothing to do
        var preParse = JObject.Parse(responseBody);
        string preContent = preParse["choices"]?[0]?["message"]?["content"]?.Value<string>() ?? string.Empty;
        int markerOccurrences = CountMarkers(preContent);
        Assert.True(markerOccurrences > 0,
            $"Fixture content has no [N] markers — parser would no-op. Got: {preContent[..Math.Min(200, preContent.Length)]}");

        var workflow = new Section("perplexity-trace", new Workflow());
        var block = new Block(BlockTypes.WebApiCall, workflow)
        {
            Name = "Ownership Structure - iSearch",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = PERPLEXITY_URL,
                HttpMethod = "POST",
                ContentType = "application/json",
                AcceptHeader = "application/json",
                RequestPayloadTemplate = """
                    {
                      "model": "sonar-pro",
                      "messages": [
                        { "role": "system", "content": "You are a credit research assistant. Be precise, factual, and concise. Always cite your sources." },
                        { "role": "user", "content": "Provide a comprehensive ownership and corporate structure analysis of Harrowgate Aerostructures plc based in the United Kingdom. Cite all sources." }
                      ]
                    }
                    """,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["authorization"] = "Bearer stub-not-actually-used",
                },
            },
        };
        block.SetExecutionState(false, []);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new FixtureWebApiCallStrategy(responseBody);

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotEmpty(context.BlockAnnotations.Facts);
        Assert.Contains(context.BlockAnnotations.Facts, f => f.Sources.OfType<UrlSource>().Any());
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.NotNull(block.BlockResponse.JsonResponse);
        Assert.Equal(responseBody, block.BlockResponse.JsonResponse); // original preserved

        // Verify markers were stripped from the cleaned response
        var postParse = JObject.Parse(block.BlockResponse.RawResponse!);
        string postContent = postParse["choices"]?[0]?["message"]?["content"]?.Value<string>() ?? string.Empty;
        Assert.Equal(0, CountMarkers(postContent));

        // Build the trace
        var searchResults = preParse["search_results"] as JArray;
        int factsTotal = context.BlockAnnotations.Facts.Count;
        int factsNumerical = context.BlockAnnotations.Facts.Count(f => f.IsNumerical);
        int factsBroad = factsTotal - factsNumerical;

        var trace = new
        {
            provider = "perplexity",
            scenario = "ownership-structure-replay",
            note = "Fictional response shaped like a production ownership-structure answer. " +
                   "Replayed through WebApiCallStrategy → PerplexityResponseParser to capture each transformation step.",
            blockConfiguration = new
            {
                type = nameof(BlockTypes.WebApiCall),
                url = PERPLEXITY_URL,
                method = "POST",
                model = preParse["model"]?.Value<string>(),
                authHeader = "authorization (per-Organization key resolved via {Organization.PerplexityApiKey})",
            },
            stage1_rawResponse = new
            {
                description = "Unmodified Perplexity API body. Includes citations URL list, search_results metadata, and [N] markers in choices[0].message.content.",
                bodyLength = responseBody.Length,
                citationsUrlListCount = (preParse["citations"] as JArray)?.Count ?? 0,
                searchResultsCount = searchResults?.Count ?? 0,
                contentLength = preContent.Length,
                markerOccurrences,
                contentPreview = preContent[..Math.Min(400, preContent.Length)],
            },
            stage2_searchResults = searchResults?.Select(r => new
            {
                title = r["title"]?.Value<string>(),
                url = r["url"]?.Value<string>(),
                snippet = r["snippet"]?.Value<string>(),
            }).ToArray(),
            stage3_parserOutput = new
            {
                description = "PerplexityResponseParser.TryParse — extracts [N]-delimited claims, builds Facts with UrlSources from indexed search_results, strips markers and re-serializes the body.",
                cleanedBodyLength = block.BlockResponse.RawResponse!.Length,
                cleanedContentLength = postContent.Length,
                cleanedContentPreview = postContent[..Math.Min(400, postContent.Length)],
                factsTotal,
                factsBroad,
                factsNumerical,
                factsWithMultipleSources = context.BlockAnnotations.Facts.Count(f => f.Sources.Count > 1),
                uniqueSourceUrls = context.BlockAnnotations.Facts
                    .SelectMany(f => f.Sources.OfType<UrlSource>())
                    .Select(s => s.Url)
                    .Distinct()
                    .Count(),
            },
            stage4_blockAnnotationsFacts = context.BlockAnnotations.Facts.Select(f => new
            {
                value = f.Value,
                isNumerical = f.IsNumerical,
                sourceCount = f.Sources.Count,
                sources = f.Sources.Select(s => new
                {
                    type = s.Type.ToString(),
                    sourceKey = s.SourceKey,
                    citedText = s.CitedText,
                    url = (s as UrlSource)?.Url,
                }).ToArray(),
            }).ToArray(),
        };

        string outputPath = CitationTraceWriter.Write("perplexity-ownership-trace.json", trace);
        _outputHelper.WriteLine($"Trace written: {outputPath}");
        _outputHelper.WriteLine($"markers stripped: {markerOccurrences} → 0; facts: {factsTotal} (numerical {factsNumerical}, broad {factsBroad})");
    }

    [PaidApiFact("Hits live api.perplexity.ai; requires PERPLEXITY_API_KEY env var")]
    public async Task Trace_LiveCallToPerplexity_AndCapturesEveryStageAsync()
    {
        string? apiKey = Environment.GetEnvironmentVariable("PERPLEXITY_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Skip("PERPLEXITY_API_KEY env var not set");
            return;
        }

        const string question =
            "What was NVIDIA's reported revenue for fiscal year 2024 and what were the exact " +
            "data center, gaming, and professional visualization segment revenue figures? Cite sources.";

        const string requestPayload = """
            {
              "model": "sonar-pro",
              "messages": [
                { "role": "system", "content": "You are a financial analyst. Be precise and cite specific numbers from your sources." },
                { "role": "user", "content": "What was NVIDIA's reported revenue for fiscal year 2024 and what were the exact data center, gaming, and professional visualization segment revenue figures? Cite sources." }
              ]
            }
            """;

        // Direct HTTP call so we can capture the truly raw response
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120),
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var content = new StringContent(requestPayload, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.PostAsync(PERPLEXITY_URL, content, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        string rawBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Now run it through the real strategy via the fixture stub
        var workflow = new Section("perplexity-live-trace", new Workflow());
        var block = new Block(BlockTypes.WebApiCall, workflow)
        {
            Name = "NVIDIA FY24 Revenue (live)",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = PERPLEXITY_URL,
                HttpMethod = "POST",
                ContentType = "application/json",
                AcceptHeader = "application/json",
                RequestPayloadTemplate = requestPayload,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["authorization"] = "Bearer (redacted in trace)",
                },
            },
        };
        block.SetExecutionState(false, []);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new FixtureWebApiCallStrategy(rawBody);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.JsonResponse);
        Assert.Equal(rawBody, block.BlockResponse.JsonResponse);

        var pre = JObject.Parse(rawBody);
        string preContent = pre["choices"]?[0]?["message"]?["content"]?.Value<string>() ?? string.Empty;
        int markers = CountMarkers(preContent);

        var post = JObject.Parse(block.BlockResponse.RawResponse!);
        string postContent = post["choices"]?[0]?["message"]?["content"]?.Value<string>() ?? string.Empty;

        var trace = new
        {
            provider = "perplexity",
            scenario = "live-nvidia-fy24-revenue",
            note = "Live api.perplexity.ai call captured at trace time. Skipped in CI (PaidApiFact), and locally unless INIA_TEST_PAID_APIS and PERPLEXITY_API_KEY are set.",
            request = new
            {
                url = PERPLEXITY_URL,
                method = "POST",
                question,
                payload = requestPayload,
            },
            stage1_rawResponse = new
            {
                bodyLength = rawBody.Length,
                citationsUrlListCount = (pre["citations"] as JArray)?.Count ?? 0,
                searchResultsCount = (pre["search_results"] as JArray)?.Count ?? 0,
                markerOccurrencesInContent = markers,
                contentPreview = preContent[..Math.Min(400, preContent.Length)],
            },
            stage2_parserOutput = new
            {
                cleanedContentLength = postContent.Length,
                cleanedContentPreview = postContent[..Math.Min(400, postContent.Length)],
                factsTotal = context.BlockAnnotations.Facts.Count,
                factsNumerical = context.BlockAnnotations.Facts.Count(f => f.IsNumerical),
                factsBroad = context.BlockAnnotations.Facts.Count(f => !f.IsNumerical),
            },
            stage3_blockAnnotationsFacts = context.BlockAnnotations.Facts.Select(f => new
            {
                value = f.Value,
                isNumerical = f.IsNumerical,
                sourceCount = f.Sources.Count,
                sources = f.Sources.Select(s => new
                {
                    type = s.Type.ToString(),
                    citedText = s.CitedText,
                    url = (s as UrlSource)?.Url,
                }).ToArray(),
            }).ToArray(),
        };

        string outputPath = CitationTraceWriter.Write("perplexity-live-trace.json", trace);
        _outputHelper.WriteLine($"Live trace written: {outputPath}");
    }

    private static int CountMarkers(string content)
    {
        int count = 0;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] != '[')
            {
                continue;
            }

            int j = i + 1;
            while (j < content.Length && char.IsDigit(content[j]))
            {
                j++;
            }

            if (j > i + 1 && j < content.Length && content[j] == ']')
            {
                count++;
                i = j;
            }
        }

        return count;
    }
}
