using IniaAi.Core.Tests.Workflows;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// Runs a three-block research workflow end-to-end through the real
/// <see cref="WorkflowHandler"/> against live providers (Perplexity → Anthropic
/// with citations → Anthropic with citations) so the cross-block matcher and
/// reference propagation paths actually fire on production-shaped data.
///
/// Block chain (chained via {ReplacementTag} placeholders, exactly the same way
/// production workflows do it):
///
///   1. WebApiCall → Perplexity sonar-pro: "AES Corporation ownership structure".
///      Produces numerical facts via PerplexityResponseParser. The Perplexity
///      parser inherits page-blurb snippets onto every numerical sub-fact, so
///      the user-visible CitedText for these will rarely contain the value.
///
///   2. AiQuery (Claude with citations): extracts the top quantitative claims
///      from Block 1's RawResponse. Claude produces native citations that
///      reference Block 1's content as a BlockSource. The cross-block matcher
///      runs (ExpandUpstreamSources + IsRelatedFactValue), which is where
///      the symmetric-Contains and 1% numeric-equivalence relations can pull
///      in unrelated upstream URLs.
///
///   3. AiQuery (Claude with citations): rewrites Block 2 as an executive
///      summary, rounding precise numbers to whole figures. Two-level chain
///      stresses the matcher again — when "85.05%" gets rounded to "85%",
///      whether the link survives depends on numeric-equivalence within 1%.
///
/// On completion the test serialises the executed workflow via
/// <see cref="JsonSerializable{T}.ToJson(bool)"/> and writes it to
/// <c>artifacts/citation-traces/sample-workflows/</c> alongside a
/// citation-analysis trace under <c>traces/</c>.
/// </summary>
public class RealisticWorkflowTraceTests : IClassFixture<WorkflowsFixture<RealisticWorkflowTraceTests>>
{
    private readonly WorkflowsFixture<RealisticWorkflowTraceTests> _fixture;
    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _outputHelper;
    private readonly WorkflowHandler _workflowHandler;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public RealisticWorkflowTraceTests(
        WorkflowsFixture<RealisticWorkflowTraceTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _fixture = fixture;
        _outputHelper = outputHelper;
        _testServiceProvider = _fixture.BuildTestServiceProvider(_outputHelper);
        _workflowHandler = _testServiceProvider.GetRequiredService<WorkflowHandler>();
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    [PaidApiFact("Live workflow — Perplexity (PERPLEXITY_API_KEY) + Anthropic (PlatformSettings AWS keys); ~$0.50–1 in API credits per run")]
    public async Task Trace_RealisticResearchWorkflow_DemonstratesPipelineEndToEndAsync()
    {
        string? perplexityKey = Environment.GetEnvironmentVariable("PERPLEXITY_API_KEY");
        if (string.IsNullOrWhiteSpace(perplexityKey))
        {
            Assert.Skip("PERPLEXITY_API_KEY env var not set — skipping live realistic-workflow trace");
            return;
        }

        // ----- Build the workflow -----
        var workflow = new Workflow("AES Ownership Research");
        var section = new Section("AES Ownership Research", workflow)
        {
            Title = "Ownership and Capital Structure",
        };

        const string PERPLEXITY_TAG = "perplexity_search";
        const string EXTRACT_TAG = "extracted_claims";

        // Block 1 — Perplexity sonar-pro web search.
        const string PERPLEXITY_PAYLOAD =
            "{\n  \"model\": \"sonar-pro\",\n  \"messages\": [\n    { \"role\": \"system\", \"content\": \"You are a credit research assistant. Be precise and factual. Always cite specific numbers with their sources.\" },\n    { \"role\": \"user\", \"content\": \"What is the ownership and capital structure of The AES Corporation (NYSE: AES)? Cover institutional, insider, and retail ownership percentages, top institutional holders by stake, total shares outstanding, and recent corporate transactions involving the company. Cite all sources.\" }\n  ]\n}";

        _ = new Block(BlockTypes.WebApiCall, section)
        {
            Name = "AES Ownership — Perplexity Search",
            ReplacementTag = PERPLEXITY_TAG,
            Order = 0,
            Properties = new WebApiCallProperties
            {
                UrlTemplate = "https://api.perplexity.ai/chat/completions",
                HttpMethod = "POST",
                ContentType = "application/json",
                AcceptHeader = "application/json",
                RequestPayloadTemplate = PERPLEXITY_PAYLOAD,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["authorization"] = $"Bearer {perplexityKey}",
                },
            },
        };

        // Block 2 — Anthropic AiQuery with citations: extract numerical claims.
        // The {perplexity_search} placeholder is replaced with Block 1's RawResponse.
        const string EXTRACT_PROMPT =
            "Below is a search result about AES Corporation's ownership structure. " +
            "Extract the **eight most important quantitative claims** as a bulleted list. " +
            "For each claim, quote the exact figure with its unit (percent, number of shares, dollar amount, year). " +
            "Be precise and conservative — do not round.\n\n" +
            "{" + PERPLEXITY_TAG + "}";

        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Extract Quantitative Claims",
            ReplacementTag = EXTRACT_TAG,
            Order = 1,
            SupportedDocumentClasses = ["None"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = EXTRACT_PROMPT,
                SystemPromptTemplate = "You are a financial analyst. Always cite specific figures from the source text.",
                MaxTokens = 1200,
                ShouldUseAdvancedModel = false,
                ShouldGetCitationsFromModel = true,
            },
        };

        // Block 3 — Anthropic AiQuery with citations: executive summary that rounds.
        // Two-level chain: {extracted_claims} resolves to Block 2's RawResponse,
        // and Claude's citations on this block become BlockSources pointing at
        // Block 2 — which in turn has BlockSources pointing at Block 1. The
        // matcher walks both levels via ExpandUpstreamSources.
        const string SUMMARY_PROMPT =
            "Rewrite the following findings as a single-paragraph executive summary suitable " +
            "for a board memo. Round every precise percentage to the nearest whole number, " +
            "round every dollar amount to the nearest billion (or hundred million), and " +
            "describe share counts in approximate terms (\"around 700 million shares\"). " +
            "Preserve all factual claims; just round the numbers.\n\n" +
            "{" + EXTRACT_TAG + "}";

        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Executive Summary (Rounded)",
            Order = 2,
            SupportedDocumentClasses = ["None"],
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = SUMMARY_PROMPT,
                SystemPromptTemplate = "You are a senior credit analyst preparing an executive memo. Round numbers but preserve the meaning of every claim.",
                MaxTokens = 700,
                ShouldUseAdvancedModel = false,
                ShouldGetCitationsFromModel = true,
            },
        };

        // ----- Save the pre-execution definition for later replay/inspection -----
        string preExecutionPath = WriteWorkflowJson(
            "realistic-research-workflow.workflow",
            workflow,
            includeExecutionState: false);
        _outputHelper.WriteLine($"Pre-execution workflow definition saved: {preExecutionPath}");

        // ----- Execute end-to-end through the real WorkflowHandler -----
        const string ORG_ID = "trace-realistic-workflow";
        string jobId = Guid.NewGuid().ToString();

        _ = await _workflowHandler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: [],
            organizationId: ORG_ID,
            jobId: jobId,
            _userDataUnitOfWork,
            inputs: [],
            cancellationToken: TestContext.Current.CancellationToken);

        // ----- Structural invariants -----
        IReadOnlyList<Block> blocks = section.Blocks.OrderBy(b => b.Order).ToList();
        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b => Assert.NotNull(b.BlockResponse));
        Assert.All(blocks, b => Assert.False(string.IsNullOrWhiteSpace(b.BlockResponse?.RawResponse)));

        Block perplexityBlock = blocks[0];
        Block extractBlock = blocks[1];
        Block summaryBlock = blocks[2];

        // ----- Save the post-execution workflow JSON (the "demonstrates the pipeline" artifact) -----
        string executedPath = WriteWorkflowJson(
            "realistic-research-workflow.executed.workflow",
            workflow,
            includeExecutionState: true);
        _outputHelper.WriteLine($"Executed workflow JSON saved: {executedPath}");

        // ----- Build the analysis trace -----
        var trace = new
        {
            scenario = "realistic-research-workflow-end-to-end",
            note = "Three-block live workflow (Perplexity → Anthropic w/ citations → Anthropic w/ citations) " +
                   "executed through the real WorkflowHandler. Captures every false-positive path that fires " +
                   "in production-shaped flow: Perplexity snippet-vs-value mismatch (Path 1), cross-block " +
                   "matcher with symmetric Contains and 1% numeric equivalence (Paths 2 and 3), and " +
                   "AddReferencedFacts substring propagation (Path 4) where applicable.",
            organizationId = ORG_ID,
            jobId,
            block1_perplexity = SummarizeBlock(perplexityBlock, "Perplexity raw search — produces numerical facts via PerplexityResponseParser. CitedText is search_results.snippet — page-blurb, rarely contains the cited value."),
            block2_extract = SummarizeBlock(extractBlock, "Anthropic AiQuery with citations. Native citations on Block 1's content become BlockSources; ExpandUpstreamSources walks Block 1's facts and pulls upstream URLs via Tier 1/Tier 2 matching."),
            block3_summary = SummarizeBlock(summaryBlock, "Anthropic AiQuery rewriting Block 2 with rounding. Two-level chain — citations point at Block 2, which points at Block 1. Numeric equivalence within 1% is what determines whether a rounded value re-attaches to the original URL."),
            falsePositiveScan = ScanForFalsePositives(perplexityBlock, extractBlock, summaryBlock),
        };

        string tracePath = CitationTraceWriter.Write("realistic-workflow-trace.json", trace);
        _outputHelper.WriteLine($"Citation analysis trace saved: {tracePath}");

        _outputHelper.WriteLine(
            $"Workflow shape — block 1 facts: {perplexityBlock.ExecutionState?.OutputTitle ?? "(set)"} | " +
            $"block 2 raw: {extractBlock.BlockResponse?.RawResponse?.Length ?? 0} chars | " +
            $"block 3 raw: {summaryBlock.BlockResponse?.RawResponse?.Length ?? 0} chars");
    }

    private static string WriteWorkflowJson(string filename, Workflow workflow, bool includeExecutionState)
    {
        string json;
        if (includeExecutionState)
        {
            json = workflow.ToJson(pretty: true);
        }
        else
        {
            // Clear execution state on a deep clone so the saved definition is
            // an importable template, not a frozen-mid-run snapshot.
            Workflow clone = CloneForDefinitionExport(workflow);
            json = clone.ToJson(pretty: true);
        }

        return CitationTraceWriter.WriteRaw("sample-workflows", filename, json);
    }

    private static Workflow CloneForDefinitionExport(Workflow workflow)
    {
        // Round-trip via ToJson/FromJson to get a clean independent graph,
        // then strip BlockResponse + ExecutionState recursively so the saved
        // file looks like a fresh template.
        string json = workflow.ToJson(pretty: false);
        Workflow clone = Workflow.FromJson(json)
            ?? throw new InvalidOperationException("Round-tripping workflow JSON returned null");
        StripExecutionStateRecursively(clone);
        return clone;
    }

    private static void StripExecutionStateRecursively(Workflow workflow)
    {
        workflow.ExecutionState = null;
        foreach (Block block in workflow.GetAllBlocks())
        {
            block.BlockResponse = null;
            block.ExecutionState = null;
        }
    }

    private static object SummarizeBlock(Block block, string description)
    {
        string? raw = block.BlockResponse?.RawResponse;
        return new
        {
            blockName = block.Name,
            blockId = block.Id,
            replacementTag = block.ReplacementTag,
            order = block.Order,
            description,
            rawResponseLength = raw?.Length ?? 0,
            rawResponsePreview = raw is null ? null : raw[..Math.Min(800, raw.Length)],
            executionStatus = block.ExecutionState?.Status.ToString(),
            durationMs = (int?)block.ExecutionState?.ExecutionDuration?.TotalMilliseconds,
            // (Block-level annotations are tracked on context inside the handler;
            // here we capture the outward-facing artefacts: the raw response and
            // anything serialised onto the BlockResponse.)
            blockResponseId = block.BlockResponse?.Id,
        };
    }

    private static object ScanForFalsePositives(Block perplexity, Block extract, Block summary)
    {
        // Per-block heuristic: the smoking-gun count for each false-positive
        // path. We measure what's empirically observable from the workflow's
        // own state without re-implementing the matcher.

        return new
        {
            note = "Heuristic post-execution scan. The trace is descriptive, not a regression assertion — provider responses vary run-to-run.",
            block1_perplexitySnippetMismatch = new
            {
                description = "Path 1 — Perplexity parser emits numerical sub-facts whose CitedText is the search_results.snippet (page blurb). For the rendered tooltip to contain the cited value, the snippet must literally contain the number. It rarely does.",
                rawResponseContainsCitations = perplexity.BlockResponse?.RawResponse?.Contains("citations", StringComparison.OrdinalIgnoreCase) == true,
            },
            block2_crossBlockMatcher = new
            {
                description = "Path 2/3 — Block 2 cites Block 1's content. Anthropic's native citations become BlockSources. ExpandUpstreamSources walks Block 1's annotated facts and pulls URL sources via Tier 1 substring + 1% numeric equivalence, gated by IsRelatedFactValue's symmetric Contains.",
                blockResponseExists = extract.BlockResponse is not null,
            },
            block3_twoLevelChainAndRounding = new
            {
                description = "Block 3 rounds Block 2's numbers, which forces the matcher to use Tier 1 numeric equivalence (or fail and fall back to BlockSource only). Whichever happens, the resulting attribution is observable in the saved workflow JSON and the rendered Citations array.",
                blockResponseExists = summary.BlockResponse is not null,
            },
        };
    }
}
