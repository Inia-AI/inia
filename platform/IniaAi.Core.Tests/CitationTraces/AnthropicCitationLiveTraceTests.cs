using IniaAi.Common.Enums;
using IniaAi.Core.Tests.Workflows;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.DependencyInjection;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// Records the Anthropic (Claude) citation pipeline at every transformation stage.
/// Sets up an AiQuery block with one document of grounded financial text and
/// ShouldGetCitationsFromModel=true, then captures the resulting Fact[] with
/// their BaseSource attributions.
///
/// This is the path where Claude returns native citations (Citation.cs in
/// AnthropicChatConnector ApiModel), which get converted to SourceReference[] in
/// ResponseCommonContent and then to BaseSource[] inside Fact via FactExtractor.
/// </summary>
public class AnthropicCitationLiveTraceTests : IClassFixture<WorkflowsFixture<AnthropicCitationLiveTraceTests>>
{
    // Invented text; it describes no real company or person.
    private const string GROUNDED_INPUT_TEXT =
        "Harrowgate Aerostructures plc Ownership and Capital Structure (FY2025 Filings)\n" +
        "\n" +
        "Harrowgate Aerostructures plc has one class of ordinary shares, listed on a regional " +
        "stock exchange. Ownership is split between the founding family, institutions and " +
        "retail holders. Specifically, founding family trusts hold 38.4% of the ordinary " +
        "shares, Northgate Pension Trustees hold 9.2%, and institutional investors hold " +
        "about 46% of the free float. The market value of the equity was reported at " +
        "£612 million at the end of March 2026, with net debt of £418 million. The sale of " +
        "the tooling business in 2025 raised £36 million, a 12.5% premium to its book " +
        "value. The largest single institutional holder, Larchmont Asset Management, holds " +
        "a 6.30% stake (approximately 14.2 million shares).";

    private readonly IServiceProvider _testServiceProvider;
    private readonly WorkflowsFixture<AnthropicCitationLiveTraceTests> _fixture;
    private readonly ITestOutputHelper _outputHelper;

    public AnthropicCitationLiveTraceTests(
        WorkflowsFixture<AnthropicCitationLiveTraceTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _fixture = fixture;
        _outputHelper = outputHelper;
        _testServiceProvider = _fixture.BuildTestServiceProvider(_outputHelper);
    }

    [PaidApiFact("Hits live api.anthropic.com with citations enabled; Anthropic key from PlatformSettings")]
    public async Task Trace_LiveAnthropicWithCitations_AndCapturesEveryStageAsync()
    {
        // Arrange — a single grounded document Claude can cite from
        var workflow = new Section("anthropic-citation-trace", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Harrowgate FY25 Ownership Facts (citations enabled)",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate =
                    "From the provided document, list the four most important quantitative facts about Harrowgate Aerostructures plc. " +
                    "For each fact, quote the exact number with its unit (e.g. '%', '$', 'million', 'billion'). " +
                    "Output as a bulleted list, one fact per line. No commentary.",
                SystemPromptTemplate =
                    "You are a financial analyst. When citing facts from the provided document, " +
                    "always include the precise figure as it appears in the source. Be concise.",
                MaxTokens = 600,
                ShouldUseAdvancedModel = false,
                ShouldGetCitationsFromModel = true,
            },
        };

        var doc = new Document(new FileMetadata(
            "IniaTest",
            "harrowgate-ownership-fy25.txt",
            GROUNDED_INPUT_TEXT.Length,
            StorageProvider.Database,
            Guid.NewGuid().ToString()), MediaType.TextPlain);
        _ = new Page(doc, 1, 1) { RawText = GROUNDED_INPUT_TEXT };

        block.Documents.Add(doc);
        _ = new RagSettings(block) { Type = RagTypes.WholeDocument };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new AiQueryStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.NotEmpty(block.BlockResponse.RawResponse);

        // Build the trace
        int factsTotal = context.BlockAnnotations.Facts.Count;
        int factsNumerical = context.BlockAnnotations.Facts.Count(f => f.IsNumerical);
        int factsBroad = factsTotal - factsNumerical;

        var trace = new
        {
            provider = "anthropic",
            scenario = "harrowgate-fy25-ownership-citations",
            note = "Live api.anthropic.com call via AiQueryStrategy with ShouldGetCitationsFromModel=true. " +
                   "One source document fed in via WholeDocument RAG. Captures the Claude → SourceReference → Fact pipeline.",
            blockConfiguration = new
            {
                type = nameof(BlockTypes.AiQuery),
                userPromptTemplate = block.Properties is AiQueryProperties p ? p.UserPromptTemplate : null,
                systemPromptTemplate = block.Properties is AiQueryProperties p2 ? p2.SystemPromptTemplate : null,
                shouldGetCitationsFromModel = true,
                shouldUseAdvancedModel = false,
                maxTokens = 600,
                ragType = nameof(RagTypes.WholeDocument),
                inputDocumentName = doc.FileMetadata?.Name,
                inputTextLength = GROUNDED_INPUT_TEXT.Length,
                inputTextPreview = GROUNDED_INPUT_TEXT[..Math.Min(300, GROUNDED_INPUT_TEXT.Length)],
            },
            stage1_rawResponse = new
            {
                description = "BlockResponse.RawResponse — final text from Claude after de-anonymization. Citations are NOT inline here; they're in BlockAnnotations.",
                length = block.BlockResponse.RawResponse!.Length,
                preview = block.BlockResponse.RawResponse[..Math.Min(800, block.BlockResponse.RawResponse.Length)],
            },
            stage2_blockAnnotationsFacts = new
            {
                description = "Facts produced by FactExtractor.IdentifyAndCollectFacts. " +
                              "Each Fact has a Value (the cited claim text from Claude) plus Sources mapped from the original SourceReference (DocumentSource for input documents, BlockSource for inter-block citations).",
                factsTotal,
                factsBroad,
                factsNumerical,
                factsWithMultipleSources = context.BlockAnnotations.Facts.Count(f => f.Sources.Count > 1),
                facts = context.BlockAnnotations.Facts.Select(f => new
                {
                    value = f.Value,
                    isNumerical = f.IsNumerical,
                    sourceCount = f.Sources.Count,
                    sources = f.Sources.Select(s => new
                    {
                        type = s.Type.ToString(),
                        sourceKey = s.SourceKey,
                        citedText = s.CitedText,
                        documentName = (s as DocumentSource)?.DocumentName,
                        documentId = (s as DocumentSource)?.DocumentId,
                        pageNumber = (s as DocumentSource)?.PageNumber,
                        url = (s as UrlSource)?.Url,
                        blockId = (s as BlockSource)?.BlockId,
                    }).ToArray(),
                }).ToArray(),
            },
            stage3_executionMetadata = new
            {
                status = block.ExecutionState?.Status.ToString(),
                executionDuration = block.ExecutionState?.ExecutionDuration?.ToString(),
            },
        };

        string outputPath = CitationTraceWriter.Write("anthropic-ownership-trace.json", trace);
        _outputHelper.WriteLine($"Anthropic trace written: {outputPath}");
        _outputHelper.WriteLine($"facts: {factsTotal} (numerical {factsNumerical}, broad {factsBroad})");
    }
}
