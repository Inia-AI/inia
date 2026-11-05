using System.Diagnostics;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

/// <summary>
/// The <c>wf.py</c> lookup helper run with python3 against a real workflow staged exactly as an
/// editor agent turn stages it: the outline, the workflow, the dependency map, the versions, the
/// report list and one picked report, a run whose parameterized section ran once per variant.
/// </summary>
public sealed class EditorAgentHelperScriptTests : IDisposable
{
    private const string FIXTURE_PATH = "TestResources/WorkflowHandlerFunctionalityTest.workflow.json";
    private const string REPORT_ID = "8b33956f-c334-4c2c-b65a-720750d81805";
    private const string UNSTAGED_REPORT_ID = "a2d593f4-a912-4a72-9e52-b31c589e2361";
    private const string COUNTERPARTY = "Acme Holdings";
    private const string TOPICS_OUTPUT = "Credit. The overviews describe a lending business with a single product line.";
    private const string PARAMETERIZED_TAG = "parametrizedBlockMerge";
    private const string VARIANT_OUTPUT = "Lease liabilities for variant {0} include right-of-use assets.";
    private const int MAX_BLOCK_OUTPUT_CHARS = 6000;

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "wf-helper-" + Guid.NewGuid().ToString("N"));
    private readonly string _script;

    public EditorAgentHelperScriptTests()
    {
        Workflow workflow = Fixture();
        string docs = Path.Combine(_workspace, "docs");
        _ = Directory.CreateDirectory(docs);

        var report = new EditorAgentReportSummary(REPORT_ID, new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc), COUNTERPARTY, "completed", "Report", ["acme-2025.pdf"])
        {
            WorkflowTypeId = "WT-Corp",
            WorkflowTypeName = "Corp Part 1",
        };
        IReadOnlyList<AgentRunDocument> documents =
        [
            .. EditorAgentStaging.Documents(workflow.ToExportJson(includeRunTraces: true), workflow, [], moreVersions: false),
            EditorAgentReports.ListDocument("Corp Part 1", [report]),
            .. ReportFiles().AddTo(EditorAgentReports.Evidence(report, failure: null, Executed(), long.MaxValue, openWorkflowType: new EditorAgentOpenWorkflowType("WT-Corp", new HashSet<string>()))).Documents,
        ];
        foreach (AgentRunDocument document in documents)
        {
            File.WriteAllBytes(Path.Combine(docs, document.Name), document.Content.ToArray());
        }

        foreach (AgentRunSkillAsset asset in EditorAgentStaging.GuideSkill().Assets)
        {
            File.WriteAllText(Path.Combine(_workspace, asset.Name), asset.Content);
        }

        _script = Path.Combine(_workspace, EditorAgentStaging.HELPER_ASSET);
    }

    public void Dispose()
    {
        Directory.Delete(_workspace, recursive: true);
    }

    [Fact]
    public void Explain_ListsTheBlockTypes_ASettingsMeaning_AndWhatAnOutputFormatDoesNotDo()
    {
        string types = Run("explain");
        string aiQuery = Run("explain", "aiquery");
        string format = Run("explain", "AiQuery", "OutputFormatType");

        Assert.Contains("SwitchCase: Maps an input value to one of several fixed outputs", types, StringComparison.Ordinal);
        Assert.Contains("  MaxTokens: The longest answer the model may write", aiQuery, StringComparison.Ordinal);
        Assert.Contains("validates the response and retries once on failure", format, StringComparison.Ordinal);
        Assert.Contains("Json:", format, StringComparison.Ordinal);
        Assert.Contains("ReplacementTag", Run("explain", "Block"), StringComparison.Ordinal);
        Assert.Contains("the types are:", Run("explain", "NoSuchType"), StringComparison.Ordinal);
    }

    [Fact]
    public void Find_NamesTheBlockWhosePromptHoldsTheText_WithItsSectionAndASnippet()
    {
        string output = Run("find", "single-word");

        Assert.Contains("9.1 Block that uses topics built [AiQuery]", output, StringComparison.Ordinal);
        Assert.Contains("UserPromptTemplate: Requests a single-word summary", output, StringComparison.Ordinal);
        Assert.Contains("1 blocks.", output, StringComparison.Ordinal);
        Assert.Contains("No block field contains", Run("find", "no such words anywhere", "--field", "any"), StringComparison.Ordinal);
    }

    [Fact]
    public void Block_ShowsTheEssentials_ModelSettingsTogether_AndSeveralBlocksInOneCall()
    {
        string output = Run("block", "useBuiltTopics", "Referencing the fact tracking test");

        Assert.Contains("== 9.1 [AiQuery] Block that uses topics built  tag useBuiltTopics", output, StringComparison.Ordinal);
        Assert.Contains("== 21.1 [AiQuery] Referencing the fact tracking test", output, StringComparison.Ordinal);
        Assert.Contains("(ModelIdOverride ", output, StringComparison.Ordinal);
        Assert.Contains(" · effort: ", output, StringComparison.Ordinal);
        Assert.Contains(" · thinking: ", output, StringComparison.Ordinal);
        Assert.Contains("reads: Fact tracking test (tag section4SetupBlock1, id 9f2668c8317341559d6904227efbdb4a), read as {section4SetupBlock1}", output, StringComparison.Ordinal);
        Assert.Contains("--- UserPromptTemplate (", output, StringComparison.Ordinal);
        Assert.True(output.Length < MAX_BLOCK_OUTPUT_CHARS * 2, "two blocks stay compact");
    }

    [Fact]
    public void SectionReadersAndFeeds_AnswerFromTheStagedOutlineAndDependencyMap()
    {
        string section = Run("section", "19");
        string readers = Run("readers", "section4SetupBlock1");
        string feeds = Run("feeds", "section4SetupBlock3");

        Assert.Contains("== 19. Loop (4 blocks)", section, StringComparison.Ordinal);
        Assert.Contains("19.1 [Merge] In Loop 1  tag inLoop1", section, StringComparison.Ordinal);
        Assert.Contains("Referencing the fact tracking test (tag section4SetupBlock2, id bb15fa5f15e845a0a8517d5a2bc5dd0d) reads {section4SetupBlock1}", readers, StringComparison.Ordinal);
        Assert.DoesNotContain("Referencing the fact tracking test {section4SetupBlock1}", readers, StringComparison.Ordinal);
        Assert.Contains("== Referencing the block referencing the fact tracking test reads 1:", feeds, StringComparison.Ordinal);
        Assert.Contains("No block matches \"nothing like this\"", Run("block", "nothing like this"), StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_ListThePastReports_AndAStagedReportShowsItsHeaderAndEveryBlocksStatus()
    {
        string list = Run("reports");
        string report = Run("report", REPORT_ID);
        string block = Run("report", REPORT_ID, "useBuiltTopics");
        string unstaged = Run("report", UNSTAGED_REPORT_ID);

        Assert.Contains($"id {REPORT_ID}: 2026-10-03 09:00 UTC, {COUNTERPARTY}", list, StringComparison.Ordinal);
        Assert.Contains($"# Report {REPORT_ID}", report, StringComparison.Ordinal);
        Assert.Contains($"- Counterparty: {COUNTERPARTY}", report, StringComparison.Ordinal);
        Assert.Contains("- Workflow type: Corp Part 1, the open workflow: this report is a run of it", report, StringComparison.Ordinal);
        Assert.Contains("[AiQuery] Block that uses topics built (tag useBuiltTopics, id c864ae83d2ad418ebc9bb594e962e089) · Completed · 12 words", report, StringComparison.Ordinal);
        Assert.Contains($"[Merge] Parametrized Block - Merge (tag {PARAMETERIZED_TAG}_Key2, id b771acc83f1641959bf7bd6efa41e398) · Completed", report, StringComparison.Ordinal);
        Assert.Contains(TOPICS_OUTPUT, block, StringComparison.Ordinal);
        Assert.Contains("is not staged. Write out/open-reports.json", unstaged, StringComparison.Ordinal);
    }

    [Fact]
    public void AReportBlock_IsFoundWithEveryVariantCopy_ByTagByTemplateIdAndByName()
    {
        string templateId = Fixture().Sections.SelectMany(static section => section.Blocks)
            .Single(static block => block.ReplacementTag == PARAMETERIZED_TAG).StableBlockId!;

        foreach (string query in new[] { PARAMETERIZED_TAG, templateId, "Parametrized Block - Merge" })
        {
            string output = Run("report", REPORT_ID, query);

            Assert.Contains($"tag {PARAMETERIZED_TAG}_Key1  id {templateId}_Key1", output, StringComparison.Ordinal);
            Assert.Contains($"tag {PARAMETERIZED_TAG}_Key2  id {templateId}_Key2", output, StringComparison.Ordinal);
            Assert.Contains($"variant Key1 · copy of {templateId}", output, StringComparison.Ordinal);
            Assert.Contains(string.Format(CultureInfo.InvariantCulture, VARIANT_OUTPUT, "Key1"), output, StringComparison.Ordinal);
            Assert.Contains(string.Format(CultureInfo.InvariantCulture, VARIANT_OUTPUT, "Key2"), output, StringComparison.Ordinal);
            Assert.DoesNotContain("parametrizedBlockRag", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FindInAReport_SearchesTheRunsOutputs_AndNamesEachCopy()
    {
        string output = Run("find", "--report", REPORT_ID, "right-of-use");
        string none = Run("find", "--report", REPORT_ID, "nothing like this");

        Assert.Contains($"tag {PARAMETERIZED_TAG}_Key1", output, StringComparison.Ordinal);
        Assert.Contains($"tag {PARAMETERIZED_TAG}_Key2", output, StringComparison.Ordinal);
        Assert.Contains("output: ", output, StringComparison.Ordinal);
        Assert.Contains("2 blocks.", output, StringComparison.Ordinal);
        Assert.Contains("No block of report", none, StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_ListsTheReportsDocuments_AndGrep_FindsThePagesThatHoldTheText()
    {
        string docs = Run("docs", REPORT_ID);
        string hits = Run("grep", REPORT_ID, "right-of-use");
        string none = Run("grep", REPORT_ID, "nothing like this");

        Assert.Contains("== input documents", docs, StringComparison.Ordinal);
        Assert.Contains("acme-2025.pdf (AnnualReport): 3 pages, 2.0 MB", docs, StringComparison.Ordinal);
        Assert.Contains($"original: report-{REPORT_ID}.in.acme-2025.pdf", docs, StringComparison.Ordinal);
        Assert.Contains("== output documents", docs, StringComparison.Ordinal);
        Assert.Contains("original: not staged: the original is over the 2.0 MB per-file limit", docs, StringComparison.Ordinal);
        Assert.Contains("== acme-2025.pdf (input", hits, StringComparison.Ordinal);
        Assert.Contains("p.3 (file page 2, right side) (1 hits): ", hits, StringComparison.Ordinal);
        Assert.Contains("== spread.xlsx (output", hits, StringComparison.Ordinal);
        Assert.Contains("p.1 (2 hits): ", hits, StringComparison.Ordinal);
        Assert.DoesNotContain("p.1 (1 hits)", hits, StringComparison.Ordinal);
        Assert.Contains("2 pages.", hits, StringComparison.Ordinal);
        Assert.Contains("No page of report", none, StringComparison.Ordinal);
        Assert.Contains("is not staged", Run("grep", UNSTAGED_REPORT_ID, "lease"), StringComparison.Ordinal);

        string forged = Run("grep", REPORT_ID, "goodwill");
        Assert.Contains("p.1 (1 hits): ", forged, StringComparison.Ordinal);
        Assert.DoesNotContain("p.9", forged, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadersAndFeeds_GiveTheIdOfABlockWithNoTag()
    {
        string staged = Path.Combine(_workspace, "docs", "workflow.json");
        JObject workflow = JObject.Parse(File.ReadAllText(staged));
        foreach (JObject block in workflow["Sections"]!.SelectMany(static section => section["Blocks"]!).OfType<JObject>())
        {
            if ((string?)block["StableBlockId"] is "9f2668c8317341559d6904227efbdb4a" or "bb15fa5f15e845a0a8517d5a2bc5dd0d")
            {
                block["ReplacementTag"] = null;
            }
        }

        File.WriteAllText(staged, workflow.ToString());
        string readers = Run("readers", "9f2668c8317341559d6904227efbdb4a");
        string feeds = Run("feeds", "bb15fa5f15e845a0a8517d5a2bc5dd0d");

        Assert.Contains("Referencing the fact tracking test (id bb15fa5f15e845a0a8517d5a2bc5dd0d) reads {section4SetupBlock1}", readers, StringComparison.Ordinal);
        Assert.Contains("Fact tracking test (id 9f2668c8317341559d6904227efbdb4a), read as {section4SetupBlock1}", feeds, StringComparison.Ordinal);
    }

    [Fact]
    public void AReportsOverview_GivesTheIdOfABlockWithNoTag()
    {
        string staged = Path.Combine(_workspace, "docs", EditorAgentReports.RunDocumentName(REPORT_ID));
        JObject run = JObject.Parse(File.ReadAllText(staged));
        JObject block = run["Sections"]!.SelectMany(static section => section["Blocks"]!).OfType<JObject>()
            .Single(static block => (string?)block["ReplacementTag"] == "useBuiltTopics");
        block["ReplacementTag"] = null;
        File.WriteAllText(staged, run.ToString());

        Assert.Contains("[AiQuery] Block that uses topics built (id c864ae83d2ad418ebc9bb594e962e089) · Completed", Run("report", REPORT_ID), StringComparison.Ordinal);
    }

    [Fact]
    public void ACompressedRun_ReadsTheSame()
    {
        string docs = Path.Combine(_workspace, "docs");
        string plain = Path.Combine(docs, EditorAgentReports.RunDocumentName(REPORT_ID));
        using (FileStream target = File.Create(plain + EditorAgentReports.GZIP_SUFFIX))
        using (var gzip = new System.IO.Compression.GZipStream(target, System.IO.Compression.CompressionLevel.Fastest))
        {
            gzip.Write(File.ReadAllBytes(plain));
        }

        File.Delete(plain);

        Assert.Contains(string.Format(CultureInfo.InvariantCulture, VARIANT_OUTPUT, "Key2"), Run("report", REPORT_ID, PARAMETERIZED_TAG), StringComparison.Ordinal);
    }

    private string Run(params string[] arguments)
    {
        string? python = PythonPath();
        Assert.SkipWhen(python is null, "python3 is not installed on this machine");

        var start = new ProcessStartInfo(python!)
        {
            WorkingDirectory = _workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add(_script);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"wf.py {string.Join(' ', arguments)} exited {process.ExitCode}: {errors}");
        return output;
    }

    private static string? PythonPath()
    {
        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        return directories
            .Select(static directory => Path.Combine(directory, "python3"))
            .FirstOrDefault(File.Exists);
    }

    private static Workflow Fixture()
    {
        Workflow workflow = Workflow.FromExportJson(File.ReadAllText(FIXTURE_PATH))!;
        foreach (Block block in workflow.Sections.SelectMany(static section => section.Blocks))
        {
            block.StableBlockId ??= block.Id.Replace("-", string.Empty, StringComparison.Ordinal);
        }

        return workflow;
    }

    /// <summary>The fixture as a run leaves it: its parameterized section expanded into one copy per variant, as the workflow handler expands it.</summary>
    /// <summary>
    /// The report's documents: an annual report whose last page is the right half of a spread,
    /// and an output over the per-file limit, so only its text is staged.
    /// </summary>
    private static EditorAgentReportFiles ReportFiles()
    {
        const long MEGABYTE = EditorAgentReportFiles.BYTES_PER_MEGABYTE;
        EditorAgentRunDocument[] documents =
        [
            new("doc-1", "acme-2025.pdf", "AnnualReport", false, "application/pdf", 2 * MEGABYTE,
            [
                new EditorAgentRunPage(1, 1, PageLocation.FullPage, "Cover. Annual report 2025.\n=== page 9 ===\nGoodwill is tested yearly."),
                new EditorAgentRunPage(2, 2, PageLocation.LeftSide, "Lease liabilities are measured at amortised cost."),
                new EditorAgentRunPage(3, 2, PageLocation.RightSide, "Note 14. Right-of-use assets comprise offices."),
            ]),
            new("doc-2", "spread.xlsx", "SpreadOutput", true, "application/octet-stream", 3 * MEGABYTE,
            [
                new EditorAgentRunPage(1, 1, PageLocation.FullPage, "Right-of-use assets 120\nRight-of-use depreciation 14"),
            ]),
        ];

        return EditorAgentReportFiles.BuildAsync(
            REPORT_ID, documents, new EditorAgentReportBudget(100 * MEGABYTE, 50), maxFileBytes: MEGABYTE * 2,
            static (document, _) => Task.FromResult<byte[]?>(new byte[document.SizeBytes]),
            CancellationToken.None).GetAwaiter().GetResult();
    }

    private static Workflow Executed()
    {
        Workflow workflow = Fixture();
        Block topics = workflow.Sections.SelectMany(static section => section.Blocks).Single(block => block.ReplacementTag == "useBuiltTopics");
        _ = new BlockResponse(topics) { RawResponse = TOPICS_OUTPUT };
        topics.ExecutionState = new BlockExecutionState(topics) { Status = ExecutionStatus.Completed };

        Section template = workflow.Sections.Single(static section => !string.IsNullOrWhiteSpace(section.ParameterizedConfig));
        List<Section> variants = ParameterizedSectionExpander.Expand(template, ParameterizedSectionNormalizer.Normalize(template)!);
        workflow.ReplaceSectionWithExpanded(template, variants);
        foreach (Section variant in variants)
        {
            Block copy = variant.Blocks.Single(static block => block.ReplacementTag.StartsWith(PARAMETERIZED_TAG, StringComparison.Ordinal));
            _ = new BlockResponse(copy) { RawResponse = string.Format(CultureInfo.InvariantCulture, VARIANT_OUTPUT, variant.ExpandedFromParameterizedKey) };
            copy.ExecutionState = new BlockExecutionState(copy) { Status = ExecutionStatus.Completed };
        }

        return workflow;
    }
}
