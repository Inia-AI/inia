using System.Text.Json;
using System.Text.RegularExpressions;
using IniaAi.Core.Tests.Workflows;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Annotations.Dates;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// Replays a Hedge Funds shaped chain — Parallel.ai deep-search result → Merge →
/// extraction → consolidation → executive-summary snapshot → output Merge — through
/// the real <see cref="WebApiCallStrategy"/>, the handler's annotation propagation and
/// <see cref="WorkflowOutputBuilder"/>, from recorded block outputs. The fixture is
/// synthetic but keeps the production shape that broke: the provider states dates in
/// prose ("May 2011") while the excerpts it cites keep the source form ("2011-05-01"),
/// one URL carries several excerpts, one claim cites a source that was never excerpted,
/// and the extraction hop restates the date in ISO form before later hops return to
/// prose. The AI blocks' native citations are not replayed: they only add
/// <see cref="BlockSource"/>s, which resolve against the same upstream annotations this
/// replay builds, so whether a web URL can reach the output is decided by the facts
/// path exercised here.
/// </summary>
public partial class ParallelAiDateChainTraceTests : IClassFixture<WorkflowsFixture<ParallelAiDateChainTraceTests>>
{
    private const string FIXTURE_FILE = "CitationTraces/Fixtures/parallelai-date-chain.json";
    private const string TRACE_FILE = "parallel-ai-date-chain-trace.json";
    private const string TRACED_VALUE = "May 2011";
    private const string TRACED_YEAR = "2011";
    private const string TRACED_VALUE_SOURCE_FORM = "2011-05";
    private const string FIRST_EXCERPT_VALUE = "June 2005";
    private const string EXPECTED_URL = "https://example.org/people/alex-rivera";
    private const string WEB_BLOCK_NAME = "Result - Manager Deep Search";
    private const string WEB_BLOCK_TAG = "Manager_DeepSearch_Result";

    private static readonly JsonSerializerOptions _fixtureOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _outputHelper;

    public ParallelAiDateChainTraceTests(
        WorkflowsFixture<ParallelAiDateChainTraceTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _testServiceProvider = fixture.BuildTestServiceProvider(_outputHelper);
    }

#pragma warning disable CA1812 // Instantiated by System.Text.Json when the fixture is read
    private sealed record ChainFixture(
        string ParallelResultUrl,
        string ParallelResponse,
        List<ChainHop> Chain);

    private sealed record ChainHop(
        string Role,
        string Name,
        string ReplacementTag,
        string Type,
        string SectionTitle,
        string RawResponse);
#pragma warning restore CA1812

    private sealed record StageReport(string Block, bool HasUrlBackedTracedFact, object Detail);

    [Fact]
    public async Task Trace_DateStatedInProseByParallel_ReachesOutputCitationWithItsUrlAsync()
    {
        ChainFixture fixture = await LoadFixtureAsync();
        var annotationsContext = new WorkflowAnnotationsContext();
        var section = new Section("hedge-funds-2.2-lakeview", new Workflow());
        List<StageReport> stages = [];

        Block webBlock = CitationTraceBlocks.ParallelAiResultBlock(
            section,
            WEB_BLOCK_NAME,
            fixture.ParallelResultUrl,
            WEB_BLOCK_TAG);
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new FixtureWebApiCallStrategy(fixture.ParallelResponse);

        _ = await strategy.ExecuteBlockAsync(webBlock, context, TestContext.Current.CancellationToken);

        Assert.NotNull(webBlock.BlockResponse);
        Assert.NotEmpty(context.BlockAnnotations.Facts);

        BlockAnnotationProcessor.SplitNumericalFacts(context.BlockAnnotations, []);
        annotationsContext.SetAnnotationsForBlock(webBlock.Id, context.BlockAnnotations);
        stages.Add(DescribeStage(
            webBlock,
            context.BlockAnnotations,
            "Parallel.ai result replayed through WebApiCallStrategy → ParallelAiResponseParser → SplitNumericalFacts"));

        Block upstream = webBlock;
        Block? outputBlock = null;
        BlockAnnotations? outputAnnotations = null;

        foreach (ChainHop hop in fixture.Chain)
        {
            Block block = BuildHopBlock(section, hop);
            BlockAnnotations annotations = PropagateFromUpstream(annotationsContext, upstream, hop.RawResponse);
            annotationsContext.SetAnnotationsForBlock(block.Id, annotations);
            stages.Add(DescribeStage(
                block,
                annotations,
                $"{hop.Role}: facts inherited from '{upstream.Name}' by value match against this block's recorded output"));

            upstream = block;
            outputBlock = block;
            outputAnnotations = annotations;
        }

        Assert.NotNull(outputBlock);
        Assert.NotNull(outputAnnotations);

        ReportParagraph? paragraph = await new WorkflowOutputBuilder().BuildParagraphAsync(
            outputBlock,
            context: null,
            outputAnnotations,
            annotationsContext);

        Assert.NotNull(paragraph);
        Assert.NotNull(paragraph.Content);

        Match marker = FactMarkerRegex().Match(paragraph.Content);
        Citation? tracedCitation = marker.Success
            ? paragraph.Citations.FirstOrDefault(c => c.Id == marker.Groups[1].Value)
            : null;

        JObject parallelResponse = JObject.Parse(fixture.ParallelResponse);

        var trace = new
        {
            provider = "parallel.ai",
            scenario = "hedge-funds-shaped-date-chain-replay",
            tracedValue = TRACED_VALUE,
            expectedUrl = EXPECTED_URL,
            groundingEvidence = CollectGroundingEvidence(parallelResponse),
            dateGroundingAudit = AuditDateGrounding(parallelResponse, annotationsContext.GetAnnotationsForBlock(webBlock.Id)!),
            stages = stages.Select(s => s.Detail).ToArray(),
            output = new
            {
                block = outputBlock.Name,
                tracedValueHasMarker = marker.Success,
                citationId = tracedCitation?.Id,
                citationSources = tracedCitation?.Sources.Select(DescribeSource).ToArray(),
                citations = paragraph.Citations.Count,
                citationsWithUrl = paragraph.Citations.Count(c => c.Sources.OfType<UrlSource>().Any()),
                citationsWithBlockSourceOnly = paragraph.Citations.Count(c => c.Sources.Count > 0 && c.Sources.All(s => s is BlockSource)),
            },
        };

        string tracePath = CitationTraceWriter.Write(TRACE_FILE, trace);
        _outputHelper.WriteLine($"Trace written: {tracePath}");

        foreach (StageReport stage in stages)
        {
            _outputHelper.WriteLine($"{stage.Block}: URL-backed '{TRACED_VALUE}' fact = {stage.HasUrlBackedTracedFact}");
        }

        string firstStageWithoutFact = stages.FirstOrDefault(s => !s.HasUrlBackedTracedFact)?.Block ?? "(none)";

        Assert.True(
            marker.Success,
            $"'{TRACED_VALUE}' carries no citation marker in the output paragraph. " +
            $"First stage without a URL-backed '{TRACED_VALUE}' fact: {firstStageWithoutFact}.");
        Assert.NotNull(tracedCitation);
        Assert.Contains(tracedCitation.Sources, s => s is UrlSource url && url.Url == EXPECTED_URL);
        Assert.All(tracedCitation.Sources, s => Assert.IsNotType<BlockSource>(s));

        Match firstExcerptMarker = Regex.Match(paragraph.Content, $"<fact id=\"(c\\d+)\">{FIRST_EXCERPT_VALUE}</fact>");
        Assert.True(firstExcerptMarker.Success, $"'{FIRST_EXCERPT_VALUE}', stated only by the first excerpt of its URL, carries no citation marker.");
        Citation firstExcerptCitation = Assert.Single(paragraph.Citations, c => c.Id == firstExcerptMarker.Groups[1].Value);
        Assert.Contains(firstExcerptCitation.Sources, s => s is UrlSource url && url.Url == EXPECTED_URL);
    }

    private static async Task<ChainFixture> LoadFixtureAsync()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, FIXTURE_FILE);
        Assert.True(File.Exists(fixturePath), $"Fixture not found at {fixturePath} — check csproj copy directives");

        await using FileStream stream = File.OpenRead(fixturePath);
        ChainFixture? fixture = await JsonSerializer.DeserializeAsync<ChainFixture>(
            stream,
            _fixtureOptions,
            TestContext.Current.CancellationToken);

        Assert.NotNull(fixture);
        Assert.NotEmpty(fixture.Chain);

        return fixture;
    }

    private static Block BuildHopBlock(Section section, ChainHop hop)
    {
        var block = new Block(Enum.Parse<BlockTypes>(hop.Type), section)
        {
            Name = hop.Name,
            ReplacementTag = hop.ReplacementTag,
        };
        _ = new BlockResponse(block)
        {
            RawResponse = hop.RawResponse,
        };
        block.SetExecutionState(false, []);

        return block;
    }

    /// <summary>
    /// The handler's post-strategy pass for a block that references one upstream block:
    /// <see cref="BlockAnnotationProcessor.InheritReferencedFacts"/> followed by
    /// <see cref="BlockAnnotationProcessor.SplitNumericalFacts"/>. The AI-side facts a live
    /// run would add first are absent here (see the class summary).
    /// </summary>
    private static BlockAnnotations PropagateFromUpstream(
        WorkflowAnnotationsContext annotationsContext,
        Block upstream,
        string rawResponse)
    {
        var annotations = new BlockAnnotations();

        BlockAnnotationProcessor.InheritReferencedFacts(
            annotations,
            annotationsContext.GetReferencedAnnotations([upstream]).SelectMany(a => a.Facts),
            rawResponse);
        BlockAnnotationProcessor.SplitNumericalFacts(annotations, []);

        return annotations;
    }

    private static StageReport DescribeStage(Block block, BlockAnnotations annotations, string description)
    {
        Fact? traced = annotations.Facts.FirstOrDefault(
            f => f.IsNumerical && DateEquivalence.AreSameDate(f.Value, TRACED_VALUE));
        bool hasUrlBackedTracedFact = traced?.Sources.OfType<UrlSource>().Any() == true;

        var detail = new
        {
            block = block.Name,
            type = block.Type.ToString(),
            description,
            facts = annotations.Facts.Count,
            numericalFacts = annotations.Facts.Count(f => f.IsNumerical),
            urlBackedNumericalFacts = annotations.Facts.Count(f => f.IsNumerical && f.Sources.OfType<UrlSource>().Any()),
            outputMentionsTracedValue = block.BlockResponse?.RawResponse?.Contains(TRACED_VALUE, StringComparison.OrdinalIgnoreCase) == true,
            tracedValueFact = traced is null ? null : DescribeFact(traced),
            factsMentioningTracedYear = annotations.Facts
                .Where(f => f.IsNumerical && f.Value.Contains(TRACED_YEAR, StringComparison.Ordinal))
                .Select(DescribeFact)
                .ToArray(),
        };

        return new StageReport(block.Name, hasUrlBackedTracedFact, detail);
    }

    private static object DescribeFact(Fact fact)
    {
        return new
        {
            value = fact.Value,
            isNumerical = fact.IsNumerical,
            sources = fact.Sources.Select(DescribeSource).ToArray(),
        };
    }

    private static object DescribeSource(BaseSource source)
    {
        return new
        {
            type = source.Type.ToString(),
            url = (source as UrlSource)?.Url,
            blockId = (source as BlockSource)?.BlockId,
            citedText = source.CitedText,
        };
    }

    /// <summary>
    /// Every basis excerpt that states the traced date in the source's own form, with the field
    /// and URL it hangs off — the evidence the parser had available to ground "May 2011".
    /// </summary>
    private static object[] CollectGroundingEvidence(JObject root)
    {
        var evidence = new List<object>();

        foreach (JObject entry in (root["output"]?["basis"] as JArray)?.OfType<JObject>() ?? [])
        {
            foreach (JObject citation in (entry["citations"] as JArray)?.OfType<JObject>() ?? [])
            {
                foreach (string excerpt in (citation["excerpts"] as JArray)?.Values<string>().OfType<string>() ?? [])
                {
                    if (!excerpt.Contains(TRACED_VALUE_SOURCE_FORM, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    evidence.Add(new
                    {
                        field = entry["field"]?.Value<string>(),
                        url = citation["url"]?.Value<string>(),
                        excerpt,
                        statesTracedValueLiterally = excerpt.Contains(TRACED_VALUE, StringComparison.OrdinalIgnoreCase),
                    });
                }
            }
        }

        return [.. evidence];
    }

    /// <summary>
    /// Per basis field: every readable date the extractor finds in the claim, and which of
    /// them survived grounding as a numerical fact on the Web API block.
    /// </summary>
    private static object AuditDateGrounding(JObject root, BlockAnnotations webAnnotations)
    {
        var content = root["output"]?["content"] as JObject;
        HashSet<string> groundedValues = new(
            webAnnotations.Facts.Where(f => f.IsNumerical).Select(f => f.Value),
            StringComparer.OrdinalIgnoreCase);

        var perField = (content?.Properties() ?? [])
            .Select(property =>
            {
                List<string> dateValues = [.. NumericalDataExtractor
                    .ExtractDates(property.Value.ToString())
                    .Where(v => DateNormalization.Read(v).Date is not null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)];

                return new
                {
                    field = property.Name,
                    dateShapedValues = dateValues.Count,
                    grounded = dateValues.Count(groundedValues.Contains),
                    dropped = dateValues.Where(v => !groundedValues.Contains(v)).ToArray(),
                };
            })
            .ToArray();

        return new
        {
            dateShapedValues = perField.Sum(f => f.dateShapedValues),
            grounded = perField.Sum(f => f.grounded),
            dropped = perField.Sum(f => f.dropped.Length),
            perField,
        };
    }

    [GeneratedRegex("<fact id=\"(c\\d+)\">" + TRACED_VALUE + "</fact>")]
    private static partial Regex FactMarkerRegex();
}
