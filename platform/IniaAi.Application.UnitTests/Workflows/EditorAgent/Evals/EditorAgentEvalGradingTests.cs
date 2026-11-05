using IniaAi.Application.Workflows.EditorAgent;
using Microsoft.Extensions.Logging.Abstractions;
using IniaAi.Infrastructure.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Proposals;
using T = IniaAi.Application.UnitTests.Workflows.EditorAgent.Evals.EditorAgentEvalWorkflow.Tags;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent.Evals;

/// <summary>
/// The eval graders that need no live host, on hand-made turns and workflows: each fails the
/// faults it exists to catch. The graders that accept a proposal through the service (apply and
/// check, the outbound Accept all check) run only in the live evals.
/// </summary>
public sealed class EditorAgentEvalGradingTests
{
    private const string MAX_TOKENS = "Properties." + nameof(AiQueryProperties.MaxTokens);
    private const string REPORT_ID = "report-1";
    private const string VARIABLE = "reportingCurrency";
    private const string USD = "USD";

    private readonly Workflow _workflow = EditorAgentEvalWorkflow.Build();

    [Fact]
    public void ExpectLinks_FailsWhenTheBlockIsNamedButNotLinked()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectLinks(Turn("The Fact tracking test block feeds it."), [T.FACT_TRACKING], failures);

        Assert.Single(failures);
    }

    [Fact]
    public void ExpectOnlyLinks_FailsABlockOutsideTheAnswer_ButAllowsASection()
    {
        List<string> failures = [];
        string section = $"[Exa Search](block:{EditorAgentEvalWorkflow.SectionStableId("Exa Search")})";

        EditorAgentEvalGrading.ExpectOnlyLinks(Turn($"{Link(T.FACT_READER)} in {section}, and {Link(T.DELAY)}."), [T.FACT_READER], _workflow, failures);

        string failure = Assert.Single(failures);
        Assert.Contains(T.DELAY, failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectOnlyLinks_AllowsABlockOneHopFromAnExpectedOne()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectOnlyLinks(Turn($"{Link(T.FACT_READER)} reads {Link(T.FACT_TRACKING)}, which {Link(T.NEXT_SECTION_READER)} also reads."), [T.FACT_READER], _workflow, failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void Neighbours_AreTheBlocksABlockReadsAndTheBlocksThatReadIt()
    {
        Assert.Equal([T.NEXT_SECTION_READER, T.FACT_READER], EditorAgentEvalGrading.Neighbours(_workflow, T.FACT_TRACKING).Order(StringComparer.Ordinal));
        Assert.Equal([T.NEXT_SECTION_READER, T.FACT_TRACKING, T.FACT_READER_READER], EditorAgentEvalGrading.Neighbours(_workflow, T.FACT_READER).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ExpectLinksResolve_FailsALinkToAnIdTheWorkflowDoesNotHave()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectLinksResolve([Turn($"See {Link(T.FACT_TRACKING)} and [Fact tracking test](block:fact_tracking_test).")], _workflow, failures);

        string failure = Assert.Single(failures);
        Assert.Contains("block:fact_tracking_test", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectLinkTextsMatch_FailsALinkWhoseTextNamesAnotherBlock()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectLinkTextsMatch([Turn($"[Delay Amount](block:{EditorAgentEvalWorkflow.StableId(T.FACT_TRACKING)})")], _workflow, failures);

        string failure = Assert.Single(failures);
        Assert.Contains("Delay Amount", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectLinkTextsMatch_PassesTheName_TheTag_ACloseVariant_ASectionTitle_AndLeavesUnknownIdsAlone()
    {
        List<string> failures = [];
        string answer = string.Join(
            " ",
            $"[Fact tracking test](block:{EditorAgentEvalWorkflow.StableId(T.FACT_TRACKING)})",
            $"[`{T.DELAY}`](block:{EditorAgentEvalWorkflow.StableId(T.DELAY)})",
            $"[Summarizing the outputs of the search blocks](block:{EditorAgentEvalWorkflow.StableId(T.SEARCH_SUMMARY)})",
            $"[the Outside Loop section](block:{EditorAgentEvalWorkflow.SectionStableId(T.OUTSIDE_LOOP_SECTION)})",
            "[New merge](block:new-outside-merge)");

        EditorAgentEvalGrading.ExpectLinkTextsMatch([Turn(answer)], _workflow, failures);

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("Fact tracking test", T.FACT_READER)]
    [InlineData("Referencing the fact tracking test", T.FACT_TRACKING)]
    [InlineData("In Loop 1", T.IN_LOOP_SECOND)]
    [InlineData("the delay block", T.FACT_TRACKING)]
    [InlineData("Perplexity Search (section 5)", T.PARALLEL)]
    [InlineData("Parallel Search", T.PERPLEXITY)]
    [InlineData("block", T.FAILING_MODEL)]
    public void LinkTextFits_FailsTextThatNamesAnotherBlockBetter(string text, string target)
    {
        LinkIndex names = EditorAgentEvalGrading.LinkNames(_workflow, []);

        Assert.False(EditorAgentEvalGrading.LinkTextFits(text, EditorAgentEvalWorkflow.StableId(target), names));
    }

    [Theory]
    [InlineData("Fact tracking test", T.FACT_TRACKING)]
    [InlineData("`section4SetupBlock2`", T.FACT_READER)]
    [InlineData("Summarizing the outputs of the search blocks", T.SEARCH_SUMMARY)]
    [InlineData("the In Loop 2 block", T.IN_LOOP_SECOND)]
    [InlineData("SWITCH", T.SWITCH)]
    [InlineData("the SWITCH block", T.SWITCH)]
    [InlineData("the Merge JSON block", "mergeJson")]
    [InlineData("the Image Content block", "ImageContent")]
    [InlineData("the Exchange Rate block", "getExchangeRate")]
    [InlineData("the JSON2XLSX block", "JSON2XLSX")]
    [InlineData("the Test \"Answer\" block", "iterateNormally_get")]
    [InlineData("Perplexity Search (section 5)", T.PERPLEXITY)]
    public void LinkTextFits_PassesTheNameTheTagAndCloseVariants(string text, string target)
    {
        LinkIndex names = EditorAgentEvalGrading.LinkNames(_workflow, []);

        Assert.True(EditorAgentEvalGrading.LinkTextFits(text, EditorAgentEvalWorkflow.StableId(target), names));
    }

    [Theory]
    [InlineData("the SWITCH section", "Switch")]
    [InlineData("the Exchange Rate section", "Exchange Rate")]
    public void LinkTextFits_ASectionSharingABlocksTitle_TakesTheTextThatSaysSection(string text, string sectionTitle)
    {
        LinkIndex names = EditorAgentEvalGrading.LinkNames(_workflow, []);
        Section section = _workflow.Sections.Single(candidate => string.Equals(candidate.Title, sectionTitle, StringComparison.OrdinalIgnoreCase));

        Assert.True(EditorAgentEvalGrading.LinkTextFits(text, section.StableSectionId!, names));
    }

    [Fact]
    public void LinkTextFits_NeedsTheMatchedNamesNumbersInTheText()
    {
        LinkIndex names = EditorAgentEvalGrading.LinkNames(_workflow, []);

        Assert.True(EditorAgentEvalGrading.LinkTextFits("In Loop 2 (section 19)", EditorAgentEvalWorkflow.StableId(T.IN_LOOP_SECOND), names));
        Assert.False(EditorAgentEvalGrading.LinkTextFits("In Loop 1", EditorAgentEvalWorkflow.StableId(T.IN_LOOP_SECOND), names));
    }

    [Fact]
    public void LinkTextFits_NoFixtureBlockNameOrTagNamesAnotherBlock()
    {
        LinkIndex names = EditorAgentEvalGrading.LinkNames(_workflow, []);
        List<Block> blocks = [.. _workflow.Sections.SelectMany(static section => section.Blocks).Where(static block => block.Name.Length > 0)];
        List<string> crossMatches = [];

        foreach (Block block in blocks)
        {
            foreach (string text in new[] { block.Name, block.ReplacementTag })
            {
                Assert.True(EditorAgentEvalGrading.LinkTextFits(text, block.StableBlockId!, names), $"'{text}' does not name its own block.");
                crossMatches.AddRange(blocks
                    .Where(other => other != block && !string.Equals(other.Name, block.Name, StringComparison.OrdinalIgnoreCase))
                    .Where(other => EditorAgentEvalGrading.LinkTextFits(text, other.StableBlockId!, names))
                    .Select(other => $"'{text}' names '{other.Name}'"));
            }
        }

        Assert.Empty(crossMatches);
    }

    [Fact]
    public void LinkTextFits_PassesANameWithoutAsciiLettersByExactMatch()
    {
        Block revenue = EditorAgentEvalWorkflow.Block(_workflow, T.DELAY);
        revenue.Name = "収益";
        LinkIndex names = EditorAgentEvalGrading.LinkNames(_workflow, []);

        Assert.True(EditorAgentEvalGrading.LinkTextFits("収益", revenue.StableBlockId!, names));
    }

    [Fact]
    public void ExpectLinkTextsMatch_ReadsLinkTextWithBrackets_AndTakesAProposedNewName()
    {
        List<string> failures = [];
        Block delay = EditorAgentEvalWorkflow.Block(_workflow, T.DELAY);
        delay.Name = "Revenue [FY]";
        ProposedChange rename = Edit(T.FACT_TRACKING, nameof(Block.Name)) with { NewJson = "\"Fact check\"" };
        string answer = $"[Revenue [FY]](block:{delay.StableBlockId}) and [Fact check](block:{EditorAgentEvalWorkflow.StableId(T.FACT_TRACKING)})";

        EditorAgentEvalGrading.ExpectLinkTextsMatch([Turn(answer, rename)], _workflow, failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void ExpectFacts_FailsAMissingFact_AndIgnoresCase()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectFacts(Turn("It runs at LOW effort."), [("the effort", @"\blow\b"), ("the budget", "4096")], failures);

        string failure = Assert.Single(failures);
        Assert.Contains("the budget", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("No changes are needed.")]
    [InlineData("No change required.")]
    [InlineData("There is nothing to do here.")]
    [InlineData("No update is needed.")]
    [InlineData("It already reads that answer.")]
    [InlineData("There is nothing to change.")]
    public void AlreadyMet_MatchesEachWayOfSayingTheRequestIsMet(string answer)
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectFacts(Turn(answer), [("that the request is already met", EditorAgentEvalCases.ALREADY_MET)], failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void AlreadyMet_FailsAnAnswerThatDoesNotSaySo()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectFacts(Turn("I changed the max tokens to 8000 for you."), [("that the request is already met", EditorAgentEvalCases.ALREADY_MET)], failures);

        Assert.Single(failures);
    }

    [Fact]
    public void ExpectNoChanges_FailsAnyProposedChange()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectNoChanges(Turn("Done.", Edit(T.FACT_TRACKING, MAX_TOKENS)), failures);

        Assert.Single(failures);
    }

    [Fact]
    public void ExpectOnlyTheseChanges_PassesTheAskedField_AndIgnoresAnOrderRenumbering()
    {
        List<string> failures = [];
        EditorAgentTurnView turn = Turn("Done.", Edit(T.FACT_TRACKING, MAX_TOKENS), Edit(T.FACT_READER, nameof(Block.Order)));

        EditorAgentEvalGrading.ExpectOnlyTheseChanges(Outcome(turn), [(T.FACT_TRACKING, MAX_TOKENS)], added: null, variable: null, failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void ExpectOnlyTheseChanges_FailsAMoveOfAnUnrelatedBlock()
    {
        List<string> failures = [];
        ProposedChange moved = Edit(T.DELAY, field: null) with { Kind = ProposedChangeKind.Moved };

        EditorAgentEvalGrading.ExpectOnlyTheseChanges(Outcome(Turn("Done.", Edit(T.FACT_TRACKING, MAX_TOKENS), moved)), [(T.FACT_TRACKING, MAX_TOKENS)], added: null, variable: null, failures);

        string failure = Assert.Single(failures);
        Assert.Contains("Moved", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectOnlyTheseChanges_FailsAnUnaskedChange_AFailedCheck_AndAChangeMissingFromTheSummary()
    {
        List<string> failures = [];
        EditorAgentTurnView turn = Turn(
            "Done.",
            [Edit(T.FACT_TRACKING, MAX_TOKENS) with { MissingFromSummary = true }, Edit(T.DELAY, nameof(Block.Name))],
            [new ProposalCheckError("tags", "A tag nothing supplies.")]);

        EditorAgentEvalGrading.ExpectOnlyTheseChanges(Outcome(turn), [(T.FACT_TRACKING, MAX_TOKENS)], added: null, variable: null, failures);

        Assert.Equal(3, failures.Count);
    }

    [Fact]
    public void ExpectOnlyTheseChanges_FailsWhenNothingWasProposed()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectOnlyTheseChanges(Outcome(Turn("Done.")), [(T.FACT_TRACKING, MAX_TOKENS)], added: null, variable: null, failures);

        string failure = Assert.Single(failures);
        Assert.Contains("no change", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Revenue was 4,812.3m.", "4,812.3m")]
    [InlineData("Revenue of about 4,812.", "4,812")]
    [InlineData("Revenue near 4.8bn.", "4.8bn")]
    [InlineData("Revenue near 4.8 billion.", "4.8 billion")]
    [InlineData("EBITDA of 612m.", "612m")]
    [InlineData("Net debt 1.2bn.", "1.2bn")]
    [InlineData("A company like NORTHWIND.", "Northwind")]
    public void FindReportText_FindsTheNameAndTheFiguresInAnyRoundingOrUnit(string text, string expected)
    {
        Assert.Equal(expected, EditorAgentEvalGrading.FindReportText(text));
    }

    [Theory]
    [InlineData("Respond with the year the company was founded, such as 1925.")]
    [InlineData("Use the FY2025 figures and at most 8000 tokens.")]
    [InlineData("Revenue grew 4.8 percent.")]
    [InlineData("{\"Id\": \"4812a0b1-0000-4000-8000-000000000000\", \"Name\": \"Joined\"}")]
    [InlineData("Block c614-4802-9f0a was added.")]
    [InlineData("Revenue near 0.6bn.")]
    public void FindReportText_IgnoresTheFoundingYearAndUnrelatedNumbers(string text)
    {
        Assert.Null(EditorAgentEvalGrading.FindReportText(text));
    }

    [Fact]
    public void ExpectReportSeen_FailsAReportNotAttached_AndAnAnswerThatNeverShowsIt()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectReportSeen(Outcome(Turn("I cannot copy report content into the workflow.")), failures);

        Assert.Equal(2, failures.Count);
    }

    [Fact]
    public void ExpectReportSeen_PassesAnAttachedReportTheAnswerNames()
    {
        List<string> failures = [];
        EditorAgentTurnView turn = Turn("That is Northwind Traders' revenue; I cannot copy it.") with
        {
            AttachedReports = [new EditorAgentReportSummary(REPORT_ID, DateTime.UtcNow, "Northwind Traders", "completed", "Report", [])],
        };

        EditorAgentEvalGrading.ExpectReportSeen(Outcome(turn), failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void ExpectReportSeen_PassesAGenericAnswer_WhenTheReportsRunFileWasStaged()
    {
        List<string> failures = [];
        EditorAgentTurnView turn = Attached(Turn("It gives the counterparty's founding year and a few headline financial figures."));

        EditorAgentEvalGrading.ExpectReportSeen(Outcome(turn, staged: [EditorAgentReports.RunDocumentName(REPORT_ID) + EditorAgentReports.GZIP_SUFFIX]), failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void ExpectReportSeen_PassesAGenericAnswer_WhenAToolCallReadTheRunFile()
    {
        List<string> failures = [];
        EditorAgentTurnView turn = Attached(Turn("It gives a few headline figures.")) with
        {
            Activity = Steps(new AgentActivityBeat(3, 0, AgentActivityBeatKind.Normal, "Read the report", null) { DocumentNames = [EditorAgentReports.RunDocumentName(REPORT_ID)] }),
        };

        EditorAgentEvalGrading.ExpectReportSeen(Outcome(turn), failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void ExpectReportSeen_DoesNotCountAReadOfTheHeaderOnly_OrAnErroredRead()
    {
        List<string> failures = [];
        EditorAgentTurnView turn = Attached(Turn("It gives a few headline figures.")) with
        {
            Activity = Steps(
                new AgentActivityBeat(3, 0, AgentActivityBeatKind.Normal, "Read the report", null) { DocumentNames = [EditorAgentReports.HeaderDocumentName(REPORT_ID)] },
                new AgentActivityBeat(4, 0, AgentActivityBeatKind.Normal, AgentActivityMapping.RETRY_LABEL, null) { DocumentNames = [EditorAgentReports.RunDocumentName(REPORT_ID)] }) with
            {
                Receipt = new AgentActivityReceipt { DocumentNames = [EditorAgentReports.RunDocumentName(REPORT_ID)] },
            },
        };

        EditorAgentEvalGrading.ExpectReportSeen(Outcome(turn), failures);

        Assert.Single(failures);
    }

    [Fact]
    public async Task ExpectReportSeen_TheRecordedStartDecidesTheCase_WhenNoAnswerNamesTheReport()
    {
        var recorder = new EditorAgentEvalStartRecorder(new AcceptingRuntime());
        var client = new AgentRuntimeClient(new HttpClient(recorder) { BaseAddress = new Uri("http://runtime.example/") }, NullLogger<AgentRuntimeClient>.Instance);
        const string EXECUTION_ID = "edchat-recorded-t1";
        _ = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, "What does the report say?", RunToken: null)
            {
                Documents = [new AgentRunDocument(EditorAgentReports.RunDocumentName(REPORT_ID) + EditorAgentReports.GZIP_SUFFIX, new byte[] { 1, 2, 3 })],
            },
            TestContext.Current.CancellationToken);
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectReportSeen(Outcome(Attached(Turn("It gives a few headline figures.")), recorder.DocumentNames(EXECUTION_ID)), failures);

        Assert.Empty(failures);
        Assert.Empty(new List<string>([.. recorder.DocumentNames("another-run")]));
    }

    [Fact]
    public void ExpectReportSeen_FailsAGenericAnswer_WhenTheReportWasPickedButNotStaged()
    {
        List<string> failures = [];
        EditorAgentTurnView turn = Attached(Turn("It gives a few headline figures."));

        EditorAgentEvalGrading.ExpectReportSeen(Outcome(turn, staged: ["workflow.json", EditorAgentReports.HeaderDocumentName(REPORT_ID)]), failures);

        Assert.Single(failures);
    }

    [Fact]
    public void ExpectReportSeen_PassesAnAttachedReport_WhenAChangeWasRefusedAsReportText()
    {
        List<string> failures = [];
        ProposedChange refused = Edit(T.FACT_TRACKING, MAX_TOKENS) with { Refusal = ProposalRefusal.ReportText };
        EditorAgentTurnView turn = Turn("I cannot put that into the workflow.", refused) with
        {
            AttachedReports = [new EditorAgentReportSummary(REPORT_ID, DateTime.UtcNow, "Northwind Traders", "completed", "Report", [])],
        };

        EditorAgentEvalGrading.ExpectReportSeen(Outcome(turn), failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void ExpectNoAcceptableReportText_FailsAnAcceptableChange_ButNotARefusedOne()
    {
        List<string> failures = [];
        ProposedChange leaked = Edit(T.FACT_TRACKING, "Properties." + nameof(AiQueryProperties.UserPromptTemplate)) with { NewJson = "\"Expect revenue near 4.8bn.\"" };
        ProposedChange refused = leaked with { Id = "refused", Refusal = ProposalRefusal.ReportText };

        EditorAgentEvalGrading.ExpectNoAcceptableReportText(Outcome(Turn("Done.", leaked, refused)), failures);

        Assert.Single(failures);
    }

    [Fact]
    public void ExpectProperty_FailsAnotherValue_AndPassesTheExpectedOne()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectProperty(_workflow, T.TOPICS_READER, nameof(AiQueryProperties.ThinkingBudget), "4096", failures);
        EditorAgentEvalGrading.ExpectProperty(_workflow, T.TOPICS_READER, nameof(AiQueryProperties.Effort), "Medium", failures);

        string failure = Assert.Single(failures);
        Assert.Contains("'Low'", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectPropertyContainsLacksAndMatches_EachFailTheirFault()
    {
        List<string> failures = [];
        const string PROMPT = nameof(AiQueryProperties.UserPromptTemplate);

        EditorAgentEvalGrading.ExpectPropertyContains(_workflow, T.FACT_READER, PROMPT, "{" + T.FACT_TRACKING + "}", failures);
        EditorAgentEvalGrading.ExpectPropertyMatches(_workflow, T.FACT_READER, PROMPT, "OUTPUT", failures);
        EditorAgentEvalGrading.ExpectPropertyLacks(_workflow, T.FACT_READER, PROMPT, "bullet", failures);
        Assert.Empty(failures);

        EditorAgentEvalGrading.ExpectPropertyContains(_workflow, T.FACT_READER, PROMPT, "bullet", failures);
        EditorAgentEvalGrading.ExpectPropertyMatches(_workflow, T.FACT_READER, PROMPT, "bullet", failures);
        EditorAgentEvalGrading.ExpectPropertyLacks(_workflow, T.FACT_READER, PROMPT, "{" + T.FACT_TRACKING + "}", failures);
        Assert.Equal(3, failures.Count);
    }

    [Fact]
    public void ExpectName_FailsAnotherName()
    {
        List<string> failures = [];

        EditorAgentEvalGrading.ExpectName(_workflow, T.DELAY, "Delay Amount", failures);
        EditorAgentEvalGrading.ExpectName(_workflow, T.DELAY, "Delay in seconds", failures);

        Assert.Single(failures);
    }

    [Fact]
    public void ExpectVariable_FailsAMissingVariable_AWrongType_AndAWrongDefault()
    {
        List<string> missing = [];
        EditorAgentEvalGrading.ExpectVariable(_workflow, VARIABLE, WorkflowVariableTypes.String, USD, missing);
        _ = new WorkflowVariable(_workflow, VARIABLE) { Type = WorkflowVariableTypes.Number, DefaultValue = "EUR" };
        List<string> wrong = [];
        EditorAgentEvalGrading.ExpectVariable(_workflow, VARIABLE, WorkflowVariableTypes.String, USD, wrong);

        Assert.Single(missing);
        Assert.Equal(2, wrong.Count);
    }

    [Fact]
    public void ExpectVariable_PassesTheAskedTypeAndDefault()
    {
        List<string> failures = [];
        _ = new WorkflowVariable(_workflow, VARIABLE) { Type = WorkflowVariableTypes.String, DefaultValue = USD };

        EditorAgentEvalGrading.ExpectVariable(_workflow, VARIABLE, WorkflowVariableTypes.String, USD, failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void ExpectAddedMerge_PassesANewMergeAtTheEndReadingBothBlocks()
    {
        List<string> failures = [];
        AddMerge("{" + T.OUTSIDE_FIRST + "}\n\n{" + T.OUTSIDE_SECOND + "}", order: 99);

        EditorAgentEvalGrading.ExpectAddedMerge(_workflow, T.OUTSIDE_LOOP_SECTION, [T.OUTSIDE_FIRST, T.OUTSIDE_SECOND], failures);

        Assert.Empty(failures);
    }

    [Fact]
    public void ExpectAddedMerge_FailsNoNewBlock_AMissingRead_AndAPlaceBeforeTheEnd()
    {
        List<string> none = [];
        EditorAgentEvalGrading.ExpectAddedMerge(_workflow, T.OUTSIDE_LOOP_SECTION, [T.OUTSIDE_FIRST, T.OUTSIDE_SECOND], none);
        AddMerge("{" + T.OUTSIDE_FIRST + "}", order: -1);
        List<string> wrong = [];
        EditorAgentEvalGrading.ExpectAddedMerge(_workflow, T.OUTSIDE_LOOP_SECTION, [T.OUTSIDE_FIRST, T.OUTSIDE_SECOND], wrong);

        Assert.Single(none);
        Assert.Equal(2, wrong.Count);
    }

    private void AddMerge(string template, int order)
    {
        Section section = _workflow.Sections.Single(section => section.Title == T.OUTSIDE_LOOP_SECTION);
        _ = new Block(BlockTypes.Merge, section, "joinedOutside")
        {
            Order = order,
            Properties = new MergeProperties { TextTemplate = template },
        };
    }

    private static AgentActivityDigest Steps(params AgentActivityBeat[] beats)
    {
        return new AgentActivityDigest { Beats = beats };
    }

    /// <summary>The runtime's side of a start, at the HTTP boundary: it accepts the run.</summary>
    private sealed class AcceptingRuntime : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
            {
                Content = new StringContent("""{"executionId":"edchat-recorded-t1","status":"running"}""", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static EditorAgentTurnView Attached(EditorAgentTurnView turn)
    {
        return turn with
        {
            AttachedReports = [new EditorAgentReportSummary(REPORT_ID, DateTime.UtcNow, "Northwind Traders", "completed", "Report", [])],
        };
    }

    private EditorAgentEvalOutcome Outcome(EditorAgentTurnView turn, IReadOnlyList<string>? staged = null)
    {
        return new EditorAgentEvalOutcome(Host: null!, _workflow, [REPORT_ID], [turn], staged ?? []);
    }

    private static string Link(string tag)
    {
        return $"[{tag}](block:{EditorAgentEvalWorkflow.StableId(tag)})";
    }

    private static ProposedChange Edit(string tag, string? field)
    {
        return new ProposedChange
        {
            Id = $"{tag}:{field}",
            Scope = ProposedChangeScope.Block,
            Kind = ProposedChangeKind.FieldEdited,
            TargetKey = EditorAgentEvalWorkflow.StableId(tag),
            Field = field,
            Tag = tag,
            BaseJson = "8000",
            NewJson = "4000",
        };
    }

    private static EditorAgentTurnView Turn(string answer, params ProposedChange[] changes)
    {
        return Turn(answer, changes, []);
    }

    private static EditorAgentTurnView Turn(string answer, ProposedChange[] changes, ProposalCheckError[] checkErrors)
    {
        return new EditorAgentTurnView(
            "turn-1", 1, "message", answer, AgentConversationTurnStatus.Completed, "model", 0m, null, null, 1,
            DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, IsWaiting: false)
        {
            Proposal = changes.Length == 0 && checkErrors.Length == 0
                ? null
                : new EditorAgentProposalView(
                    "proposal-1",
                    1,
                    [.. changes.Select(static change => new EditorAgentChangeView(change, null, null))],
                    [],
                    checkErrors,
                    CanUndo: false),
        };
    }
}
