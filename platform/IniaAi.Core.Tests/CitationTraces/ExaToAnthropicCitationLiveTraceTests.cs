using IniaAi.Core.Tests.Workflows;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// Records the full Exa-to-Anthropic citation flow that an earlier draft of the
/// findings doc got wrong.
///
/// The actual mechanism (proven below):
/// 1. ExaSearchStrategy with CreatesDocumentClass set produces a real Document
///    entity with one Page per chunk of Exa-result text. Each Page.SourceUrl
///    holds the original web URL Exa returned.
/// 2. The Document is added to a downstream AiQuery block via block.Documents.
/// 3. AiQueryStrategy with ShouldGetCitationsFromModel=true sends those Pages
///    to Claude as input documents.
/// 4. Claude cites individual pages by DocumentIndex.
/// 5. FactExtractor.AddSourceReferenceToFact (line 237) reads Page.SourceUrl;
///    when SourceUrl is non-null (which it always is for Exa pages),
///    the citation produced is a UrlSource pointing to the original web URL.
///
/// In other words, Exa's "create a document with pages" pattern is the transport
/// mechanism for grounded URL citations — the Pages are a vehicle, the SourceUrl
/// is the payload, and FactExtractor unwraps them at the end of the chain.
/// </summary>
public class ExaToAnthropicCitationLiveTraceTests : IClassFixture<WorkflowsFixture<ExaToAnthropicCitationLiveTraceTests>>
{
    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _outputHelper;

    public ExaToAnthropicCitationLiveTraceTests(
        WorkflowsFixture<ExaToAnthropicCitationLiveTraceTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _testServiceProvider = fixture.BuildTestServiceProvider(_outputHelper);
    }

    [PaidApiFact("Hits live api.exa.ai AND live api.anthropic.com; both keys come from PlatformSettings")]
    public async Task Trace_ExaProducesDocumentWithUrlPages_AnthropicCitesThem_FactsHaveUrlSourcesAsync()
    {
        const string searchTerm = "Unilever merger acquisition divestment";
        const string documentClass = "isearch_News_MA";

        // ---------- Step 1: live Exa search produces a Document with URL-tagged Pages
        var exaWorkflow = new Workflow("exa-to-anthropic-trace");
        var exaSection = new Section("exa-to-anthropic-trace", exaWorkflow);
        var exaBlock = new Block(BlockTypes.IntelligentSearch, exaSection)
        {
            Name = "Search - M&A",
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = searchTerm,
                NumResults = 4,
                SearchType = "auto",
                Category = "news",
                LiveCrawlStrategy = "preferred",
                TextEnabled = true,
                CreatesDocumentClass = documentClass,
                ExcludeDomains = ["facebook.com", "twitter.com", "x.com", "linkedin.com", "wikipedia.org"],
            },
        };

        BlockExecutionContext exaContext = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var exaStrategy = new ExaSearchStrategy();
        BlockStrategyOutput exaOutput = await exaStrategy.ExecuteBlockAsync(
            exaBlock, exaContext, TestContext.Current.CancellationToken);

        Assert.NotEmpty(exaOutput.Documents);
        DocumentInfo spawnedDocInfo = exaOutput.Documents[0];
        Document spawnedDoc = spawnedDocInfo.Document
            ?? throw new InvalidOperationException("DocumentInfo.Document was null");

        Assert.NotEmpty(spawnedDoc.Pages);
        // Critical invariant: every Page from Exa has SourceUrl populated
        Assert.All(spawnedDoc.Pages, p => Assert.False(string.IsNullOrWhiteSpace(p.SourceUrl)));

        var exaUrlsBeforeClaude = spawnedDoc.Pages.Select(p => p.SourceUrl!).Distinct().ToList();

        // ---------- Step 2: AiQuery block consumes the Document with citations on
        var aiBlock = new Block(BlockTypes.AiQuery, exaSection)
        {
            Name = "Synthesize M&A Findings",
            SupportedDocumentClasses = ["Any"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate =
                    "From the provided news search results, list the four most important quantitative facts about " +
                    "Unilever's recent merger and acquisition activity. For each fact, quote the exact number " +
                    "with its unit. Output as a bulleted list, one fact per line.",
                SystemPromptTemplate =
                    "You are a financial analyst. Always cite specific figures from the source documents.",
                MaxTokens = 700,
                ShouldUseAdvancedModel = false,
                ShouldGetCitationsFromModel = true,
            },
        };
        aiBlock.Documents.Add(spawnedDoc);
        _ = new RagSettings(aiBlock) { Type = RagTypes.WholeDocument };

        BlockExecutionContext aiContext = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var aiStrategy = new AiQueryStrategy();

        _ = await aiStrategy.ExecuteBlockAsync(
            aiBlock, aiContext, TestContext.Current.CancellationToken);

        // ---------- Step 3: verify the citations are UrlSource, not DocumentSource
        Assert.NotNull(aiBlock.BlockResponse);
        Assert.NotEmpty(aiBlock.BlockResponse.RawResponse!);

        // Pull every BaseSource out of every fact
        var allSources = aiContext.BlockAnnotations.Facts
            .SelectMany(f => f.Sources)
            .ToList();

        // The whole point of this test: when Exa-spawned pages are cited, the resulting
        // sources must be UrlSource (because Page.SourceUrl was set), NOT DocumentSource.
        var urlSources = allSources.OfType<UrlSource>().ToList();
        var documentSources = allSources.OfType<DocumentSource>().ToList();

        Assert.NotEmpty(urlSources);
        // No DocumentSources should appear when every page has SourceUrl set
        Assert.Empty(documentSources);

        // Every UrlSource produced by Claude's citations should reference a URL Exa actually returned
        var citedUrls = urlSources.Select(s => s.Url).Distinct().ToList();
        foreach (string citedUrl in citedUrls)
        {
            Assert.Contains(citedUrl, exaUrlsBeforeClaude);
        }

        // ---------- Step 4: write the trace file
        var trace = new
        {
            scenario = "exa-to-anthropic-ma",
            note = "End-to-end live trace proving the actual Exa citation mechanism: " +
                   "Exa builds a Document with one Page per chunk where Page.SourceUrl = original web URL. " +
                   "AiQuery block consumes that Document via WholeDocument RAG. Claude cites individual pages. " +
                   "FactExtractor.AddSourceReferenceToFact reads Page.SourceUrl and produces a UrlSource — " +
                   "never a DocumentSource — for any cited Exa-spawned page.",
            stage1_exaProducesDocument = new
            {
                searchTerm,
                documentClass,
                resultCount = exaUrlsBeforeClaude.Count,
                pageCount = spawnedDoc.Pages.Count,
                eachPageHasSourceUrl = spawnedDoc.Pages.All(p => !string.IsNullOrWhiteSpace(p.SourceUrl)),
                pagesByUrl = spawnedDoc.Pages
                    .GroupBy(p => p.SourceUrl!)
                    .Select(g => new
                    {
                        url = g.Key,
                        pages = g.Select(p => new
                        {
                            pageNumber = p.PageNumber,
                            textPreview = p.Text is not null ? p.Text[..Math.Min(160, p.Text.Length)] : null,
                            textLength = p.Text?.Length ?? 0,
                        }).ToArray(),
                    })
                    .ToArray(),
            },
            stage2_aiQueryConsumes = new
            {
                inputDocumentName = spawnedDoc.FileMetadata?.Name,
                ragType = nameof(RagTypes.WholeDocument),
                shouldGetCitationsFromModel = true,
                userPromptTemplatePreview = "From the provided news search results, list the four most important quantitative facts...",
            },
            stage3_claudeResponse = new
            {
                rawResponse = aiBlock.BlockResponse.RawResponse,
                rawResponseLength = aiBlock.BlockResponse.RawResponse!.Length,
            },
            stage4_resultingCitations = new
            {
                description = "Every fact's source is a UrlSource pointing back to a URL Exa returned. Zero DocumentSources, despite the citation passing through a Document → Page entity round-trip.",
                factsTotal = aiContext.BlockAnnotations.Facts.Count,
                factsNumerical = aiContext.BlockAnnotations.Facts.Count(f => f.IsNumerical),
                factsBroad = aiContext.BlockAnnotations.Facts.Count(f => !f.IsNumerical),
                urlSourceCount = urlSources.Count,
                documentSourceCount = documentSources.Count,
                citedUniqueUrls = citedUrls,
                allUrlsAreFromExa = citedUrls.All(u => exaUrlsBeforeClaude.Contains(u)),
                facts = aiContext.BlockAnnotations.Facts.Select(f => new
                {
                    value = f.Value,
                    isNumerical = f.IsNumerical,
                    sources = f.Sources.Select(s => new
                    {
                        type = s.Type.ToString(),
                        url = (s as UrlSource)?.Url,
                        citedText = s.CitedText,
                    }).ToArray(),
                }).ToArray(),
            },
            mechanism_summary = new
            {
                step1 = "ExaSearchStrategy.CreateDocumentFromExaResults builds Document + Pages, each Page.SourceUrl = result.Url",
                step2 = "Document is consumed by an AiQuery block via block.Documents + RagSettings(WholeDocument)",
                step3 = "Anthropic citations come back referencing pages by DocumentIndex",
                step4 = "FactExtractor.AddSourceReferenceToFact:237 reads Page.SourceUrl; non-null → UrlSource (NEVER DocumentSource)",
                step5 = "Final ReportParagraph.Citations contains UrlSource entries pointing to original Exa URLs",
            },
        };

        string outputPath = CitationTraceWriter.Write("exa-to-anthropic-trace.json", trace);
        _outputHelper.WriteLine($"Trace written: {outputPath}");
        _outputHelper.WriteLine($"Exa returned {exaUrlsBeforeClaude.Count} unique URLs across {spawnedDoc.Pages.Count} pages");
        _outputHelper.WriteLine($"Claude produced {aiContext.BlockAnnotations.Facts.Count} facts, " +
            $"{urlSources.Count} UrlSources, {documentSources.Count} DocumentSources, " +
            $"covering {citedUrls.Count} URLs (all from Exa: {citedUrls.All(u => exaUrlsBeforeClaude.Contains(u))})");
    }
}
