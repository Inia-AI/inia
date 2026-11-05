using IniaAi.Testing;
using System.Text.Json;
using IniaAi.AiConnection.Abstractions;
using IniaAi.Core.Tests.Workflows;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.XunitAttributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// Guards every path a citation takes from a model answer to the stored report. Recorded model
/// answers, citations included, replay through the real workflow engine: the AI blocks, fact
/// extraction, propagation between blocks, report building and storage. The stored report's
/// citations must equal the expected file, so a change that drops a citation, moves it to another
/// source or changes its excerpt fails here. When a change is meant to alter the citations, run
/// the replay with <c>INIA_UPDATE_CITATION_REGRESSION=1</c> to rewrite the expected file, and
/// review that file's diff. When the workflow's prompts or the request the engine builds change,
/// record the answers again with the live recording test and <c>INIA_RECORD_CITATION_REGRESSION=1</c>.
/// </summary>
public class CitationRegressionTests : IClassFixture<WorkflowsFixture<CitationRegressionTests>>
{
    private const string ANSWERS_FILE = "citation-regression-answers.json";
    private const string EXPECTED_FILE = "citation-regression-expected.json";
    private const string FIXTURES_DIRECTORY = "CitationTraces/Fixtures";
    private const string TEST_PROJECT_DIRECTORY = "IniaAi.Core.Tests";
    private const string UPDATE_EXPECTED_VARIABLE = "INIA_UPDATE_CITATION_REGRESSION";
    private const string RECORD_ANSWERS_VARIABLE = "INIA_RECORD_CITATION_REGRESSION";
    private const string SWITCHED_ON = "1";
    private const string ORG_ID = "citation-regression";
    private const string USER_ID = "citation-regression-user";
    private const string FUND_FACTS_TITLE = "Fund facts";
    private const string WEB_PROFILES_TITLE = "Web profiles";
    private const string SUMMARY_TITLE = "Summary";

    private static readonly JsonSerializerOptions _answerOptions = new() { WriteIndented = true, NewLine = "\n" };

    private readonly WorkflowsFixture<CitationRegressionTests> _fixture;
    private readonly ITestOutputHelper _outputHelper;

    public CitationRegressionTests(WorkflowsFixture<CitationRegressionTests> fixture, ITestOutputHelper outputHelper)
    {
        _fixture = fixture;
        _outputHelper = outputHelper;
    }

    [Fact]
    public async Task RecordedAnswers_StoredReportCitationsMatchTheExpectedFileAsync()
    {
        RecordedModelAnswers answers = JsonSerializer.Deserialize<RecordedModelAnswers>(await ReadFixtureAsync(ANSWERS_FILE))
            ?? throw new InvalidOperationException($"{ANSWERS_FILE} holds no answers");

        (IReadOnlyList<ReportParagraph> paragraphs, IReadOnlyList<BlockCitationTrace> traces) = await RunAsync(answers.Replaying());
        string actual = CitationSnapshot.Of(paragraphs);

        AssertEveryPathIsCited(paragraphs);
        AssertTheTraceFollowsTheConvertedFigure(paragraphs, traces);

        if (Environment.GetEnvironmentVariable(UPDATE_EXPECTED_VARIABLE) == SWITCHED_ON)
        {
            _outputHelper.WriteLine($"Expected file rewritten: {WriteFixture(EXPECTED_FILE, actual)}");
            return;
        }

        Assert.Equal(await ReadFixtureAsync(EXPECTED_FILE), actual);
    }

    [PaidApiFact("Records the model answers live through the platform's model provider; a few cents per run")]
    public async Task RecordModelAnswersAsync()
    {
        if (Environment.GetEnvironmentVariable(RECORD_ANSWERS_VARIABLE) != SWITCHED_ON)
        {
            Assert.Skip($"Set {RECORD_ANSWERS_VARIABLE}=1 to record the answers again: it calls the model and rewrites both fixtures");
        }

        IChatConnector live = _fixture.BuildTestServiceProvider(_outputHelper).GetRequiredService<IChatConnector>();
        var answers = new RecordedModelAnswers();

        (IReadOnlyList<ReportParagraph> paragraphs, _) = await RunAsync(answers.Recording(live));

        AssertEveryPathIsCited(paragraphs);
        _outputHelper.WriteLine($"Answers recorded: {WriteFixture(ANSWERS_FILE, JsonSerializer.Serialize(answers, _answerOptions) + "\n")}");
        _outputHelper.WriteLine($"Expected file written: {WriteFixture(EXPECTED_FILE, CitationSnapshot.Of(paragraphs))}");
    }

    /// <summary>
    /// Holds whatever the expected file says: each path the workflow exists to send a citation
    /// down must be present, so neither a recording nor a rewrite of the expected file can lose
    /// one unnoticed.
    /// </summary>
    private static void AssertEveryPathIsCited(IReadOnlyList<ReportParagraph> paragraphs)
    {
        ReportParagraph fundFacts = Assert.Single(paragraphs, p => p.Title == FUND_FACTS_TITLE);
        ReportParagraph webProfiles = Assert.Single(paragraphs, p => p.Title == WEB_PROFILES_TITLE);
        ReportParagraph summary = Assert.Single(paragraphs, p => p.Title == SUMMARY_TITLE);

        Assert.All(paragraphs, AssertEveryMarkerHasASource);

        Assert.Contains(fundFacts.Citations, c => CitesPageOf(c, CitationRegressionWorkflow.FACTSHEET_NAME));

        Dictionary<string, string> webValues = CitationSnapshot.MarkedValues(webProfiles.Content);
        Assert.Contains(webProfiles.Citations, c =>
            webValues.TryGetValue(c.Id, out string? value)
            && DateEquivalence.IsDate(value)
            && c.Sources.OfType<UrlSource>().Any(url =>
                url.Url == CitationRegressionWorkflow.TEAM_URL && !url.CitedText.Contains(value, StringComparison.OrdinalIgnoreCase)));

        Assert.Contains(summary.Citations, c => CitesPageOf(c, CitationRegressionWorkflow.FACTSHEET_NAME));
        Assert.Contains(summary.Citations, c => c.Sources.OfType<UrlSource>().Any(url => url.Url == CitationRegressionWorkflow.TEAM_URL));

        List<Citation> blockOnly = [.. summary.Citations.Where(c => c.Sources.Count > 0 && c.Sources.All(s => s is BlockSource))];
        Assert.All(blockOnly.SelectMany(c => c.Sources.OfType<BlockSource>()), blockSource =>
            Assert.Contains(summary.CitedSourcesByBlock, list => list.BlockId == blockSource.BlockId && list.Sources.Count > 0));

        Assert.Contains(blockOnly, c => c.Sources.OfType<BlockSource>().Any(blockSource =>
            blockSource.MatchingSources.OfType<DocumentSource>().Any(page => page.DocumentName == CitationRegressionWorkflow.FACTSHEET_NAME)));

        AssertConvertedFiguresCiteTheConversionBlock(Assert.Single(paragraphs, p => p.Title == CitationRegressionWorkflow.PERFORMANCE_DRAFT_TITLE));
        AssertTableCellInThousandsCitesItsPage(Assert.Single(paragraphs, p => p.Title == CitationRegressionWorkflow.STATEMENT_FIGURES_TITLE));
    }

    /// <summary>
    /// The balance sheet prints 2,506 under £'000; the block writes £2.506m. The page states that
    /// figure at its scale, so the figure cites the page.
    /// </summary>
    private static void AssertTableCellInThousandsCitesItsPage(ReportParagraph statementFigures)
    {
        Dictionary<string, string> values = CitationSnapshot.MarkedValues(statementFigures.Content);
        Citation scaled = Assert.Single(statementFigures.Citations, c =>
            values.TryGetValue(c.Id, out string? value) && value.Contains("2.506m"));
        Assert.Contains(scaled.Sources.OfType<DocumentSource>(), page =>
            page.DocumentName == CitationRegressionWorkflow.ANNUAL_REPORT_NAME && page.CitedText.Contains("2,506"));
    }

    /// <summary>
    /// A dollar figure the conversion block computed from a sterling page is printed on no page, so
    /// it cites the conversion block, with the sterling page in that block's Drawn-from list, and
    /// never the page itself.
    /// </summary>
    private static void AssertConvertedFiguresCiteTheConversionBlock(ReportParagraph draft)
    {
        Dictionary<string, string> draftValues = CitationSnapshot.MarkedValues(draft.Content);
        List<Citation> dollarFigures = [.. draft.Citations.Where(c =>
            draftValues.TryGetValue(c.Id, out string? value) && value.Contains('$'))];

        Assert.NotEmpty(dollarFigures);
        Assert.All(dollarFigures, citation =>
        {
            BlockSource cited = Assert.IsType<BlockSource>(Assert.Single(citation.Sources));
            Assert.Equal(CitationRegressionWorkflow.USD_CONVERSION_TITLE, cited.BlockTitle);
            Assert.Contains(draft.CitedSourcesByBlock, list =>
                list.BlockId == cited.BlockId
                && list.Sources.OfType<DocumentSource>().Any(page => page.DocumentName == CitationRegressionWorkflow.ANNUAL_REPORT_NAME));
        });
    }

    /// <summary>
    /// Each block's citation trace is stored with the run. The conversion block's trace shows the
    /// dollar figure dropped there, because the sterling text it cites does not print it; the
    /// draft's trace shows the same figure cited to the conversion block, and marked in its
    /// paragraph with that block as its only source.
    /// </summary>
    private static void AssertTheTraceFollowsTheConvertedFigure(
        IReadOnlyList<ReportParagraph> paragraphs,
        IReadOnlyList<BlockCitationTrace> traces)
    {
        Assert.Equal(paragraphs.Count, traces.Count);
        CitationTraceContent conversion = TraceOf(CitationRegressionWorkflow.USD_CONVERSION_TITLE, paragraphs, traces);
        CitationTraceContent draft = TraceOf(CitationRegressionWorkflow.PERFORMANCE_DRAFT_TITLE, paragraphs, traces);

        List<string> droppedDollars = [.. conversion.Rejections
            .Where(r => r.Reason == CitationTraceReason.NoSourceStatesValue && r.Value.StartsWith('$'))
            .Select(r => r.Value)];
        Assert.NotEmpty(droppedDollars);

        string conversionBlockId = Assert.Single(paragraphs, p => p.Title == CitationRegressionWorkflow.USD_CONVERSION_TITLE).BlockId!;
        List<TracedFact> producerFacts = [.. draft.Facts.Where(fact =>
            fact.Entry == TracedFactEntry.Producer && droppedDollars.Contains(fact.Value))];
        Assert.NotEmpty(producerFacts);
        Assert.All(producerFacts, fact =>
        {
            Assert.All(fact.Sources, index => Assert.Equal(conversionBlockId, draft.Sources[index].BlockId));

            TracedCitation cited = Assert.Single(draft.Report!.Citations, citation => citation.Value == fact.Value);
            Assert.Equal(TracedFlattening.BlockFallback, cited.Flattening);
            Assert.NotNull(cited.Marker);
            Assert.Equal([conversionBlockId], cited.Sources.Select(index => draft.Sources[index].BlockId));
        });
    }

    private static CitationTraceContent TraceOf(
        string title,
        IReadOnlyList<ReportParagraph> paragraphs,
        IReadOnlyList<BlockCitationTrace> traces)
    {
        string blockId = Assert.Single(paragraphs, p => p.Title == title).BlockId!;
        CitationTraceContent? trace = CitationTraceContent.Parse(Assert.Single(traces, t => t.BlockId == blockId).Content);
        Assert.NotNull(trace);
        return trace;
    }

    private static void AssertEveryMarkerHasASource(ReportParagraph paragraph)
    {
        foreach (string citationId in CitationSnapshot.MarkedValues(paragraph.Content).Keys)
        {
            Citation citation = Assert.Single(paragraph.Citations, c => c.Id == citationId);
            Assert.NotEmpty(citation.Sources);
        }
    }

    private static bool CitesPageOf(Citation citation, string documentName)
    {
        return citation.Sources.OfType<DocumentSource>().Any(page => page.DocumentName == documentName);
    }

    private async Task<(IReadOnlyList<ReportParagraph> Paragraphs, IReadOnlyList<BlockCitationTrace> Traces)> RunAsync(IChatConnector connector)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IServiceProvider services = _fixture.BuildTestServiceProvider(_outputHelper, s => s.AddSingleton(connector));
        WorkflowHandler handler = services.GetRequiredService<WorkflowHandler>();
        Workflow workflow = CitationRegressionWorkflow.Build();

        using (IServiceScope scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
            await handler.CreateRootOutputAsync(unitOfWork, jobId: Guid.NewGuid().ToString(), workflow, ORG_ID, USER_ID);
            _ = await handler.ExecuteWorkflowAsync(
                workflow,
                anonymisationMap: new(),
                organizationId: ORG_ID,
                jobId: Guid.NewGuid().ToString(),
                unitOfWork,
                USER_ID,
                inputs: [],
                cancellationToken: cancellationToken);
        }

        string executionId = handler.WorkflowExecutionId
            ?? throw new InvalidOperationException("The run created no execution to read the report from");

        IDbContextFactory<UserDataDbContext> factory = services.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        await using UserDataDbContext read = await factory.CreateDbContextAsync(cancellationToken);
        List<string> sectionIds = await read.ReportSections.AsNoTracking()
            .Where(section => section.WorkflowReportId == executionId)
            .Select(section => section.Id)
            .ToListAsync(cancellationToken);

        List<ReportParagraph> paragraphs = await read.ReportParagraphs.AsNoTracking()
            .Where(paragraph => sectionIds.Contains(paragraph.SectionId))
            .OrderBy(paragraph => paragraph.Order)
            .ToListAsync(cancellationToken);
        List<string> blockIds = [.. paragraphs.Select(paragraph => paragraph.BlockId).OfType<string>()];
        List<BlockCitationTrace> traces = await read.BlockCitationTraces.AsNoTracking()
            .Where(trace => blockIds.Contains(trace.BlockId))
            .ToListAsync(cancellationToken);

        return (paragraphs, traces);
    }

    private static async Task<string> ReadFixtureAsync(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, FIXTURES_DIRECTORY, fileName);
        Assert.True(File.Exists(path), $"Fixture not found at {path}: record the answers with the live recording test");
        return await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
    }

    private static string WriteFixture(string fileName, string contents)
    {
        string path = Path.Combine(RepositoryRoot.Find(), TEST_PROJECT_DIRECTORY, FIXTURES_DIRECTORY, fileName);
        File.WriteAllText(path, contents);
        return path;
    }
}
