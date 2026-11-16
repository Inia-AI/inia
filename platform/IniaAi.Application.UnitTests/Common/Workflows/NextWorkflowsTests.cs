using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.UnitTests.Common.Workflows;

/// <summary>
/// A finished run leads to the current workflows whose inputs are made by its workflow, where the organization can
/// reach them. Each takes the run's output its input names, the workbook where the run made one, and the run's inputs
/// whose class it also has.
/// </summary>
public sealed class NextWorkflowsTests : IDisposable
{
    private const string ORG = "org-test";
    private const string PART_1 = "CorpPart1TypeA";
    private const string PART_2 = "CorpPart2TypeA";
    private const string RATING = "CorpRating";
    private const string SPREAD_OUTPUT = "Financial Spread Output";
    private const string SPREAD_INPUT = "CorporateFinancialModel";
    private const string ANNUAL_REPORT = "Annual Report";

    private static readonly NextWorkflows.RunDocument _workbook = new("doc-spread", "Spread", ".xlsm", SPREAD_OUTPUT, IsOutput: true, IsWorkbook: true);
    private static readonly NextWorkflows.RunDocument _wordCopy = new("doc-word", "Spread", ".docx", SPREAD_OUTPUT, IsOutput: true, IsWorkbook: false);
    private static readonly NextWorkflows.RunDocument _annualReport = new("doc-annual", "Annual report 2025", ".pdf", ANNUAL_REPORT, IsOutput: false, IsWorkbook: false);
    private static readonly NextWorkflows.RunDocument _otherInput = new("doc-other", "Notes", ".pdf", "Management Notes", IsOutput: false, IsWorkbook: false);

    private readonly SqliteCoreDatabase _core = new();

    public void Dispose()
    {
        _core.Dispose();
    }

    [Fact]
    public async Task AnOutputThatTwoWorkflowsRead_LeadsToBoth_EachWithItsOwnInputClass()
    {
        string part2 = Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow => MadeByPart1(workflow, SPREAD_INPUT));
        string rating = Seed(RATING, "INIA Corporate Rating", workflow => MadeByPart1(workflow, "Spread"));

        List<NextWorkflowDto> next = await OfAsync(_workbook);

        Assert.Equal([PART_2, RATING], next.Select(workflow => workflow.WorkflowTypeId));
        Assert.Equal([part2, rating], next.Select(workflow => workflow.WorkflowId));
        Assert.Equal("INIA Corporate Analysis - Part 2", next[0].WorkflowTypeName);
        Assert.Equal(new NextWorkflowDocumentDto { FileId = "doc-spread", Name = "Spread", Extension = ".xlsm", DocumentClass = SPREAD_INPUT }, Assert.Single(next[0].Documents));
        Assert.Equal("Spread", Assert.Single(next[1].Documents).DocumentClass);
    }

    [Fact]
    public async Task ARetiredType_ATypeTheOrganizationCannotReach_AndAnOlderWorkflowOfAType_AreLeftOut()
    {
        _ = Seed("RetiredType", "Retired", workflow => MadeByPart1(workflow, SPREAD_INPUT), retired: true);
        _ = Seed("NoAccess", "Another organization's report", workflow => MadeByPart1(workflow, SPREAD_INPUT), access: false);
        _ = Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow => MadeByPart1(workflow, SPREAD_INPUT), createdDaysAgo: 3);
        _ = Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow => _ = new DocumentClass(workflow, SPREAD_INPUT), createdDaysAgo: 1);

        Assert.Empty(await OfAsync(_workbook));
    }

    [Fact]
    public async Task TheWorkbookGoes_NotTheWordCopyOfTheSameOutput()
    {
        _ = Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow => MadeByPart1(workflow, SPREAD_INPUT));

        NextWorkflowDto next = Assert.Single(await OfAsync(_wordCopy, _workbook));

        Assert.Equal("doc-spread", Assert.Single(next.Documents).FileId);
    }

    [Fact]
    public async Task AnOutputMadeInNoWorkbook_GoesAsItIs()
    {
        _ = Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow => MadeByPart1(workflow, SPREAD_INPUT));

        NextWorkflowDto next = Assert.Single(await OfAsync(_wordCopy));

        Assert.Equal("doc-word", Assert.Single(next.Documents).FileId);
    }

    [Fact]
    public async Task TheRunsInputsWhoseClassTheNextWorkflowHas_GoWithTheOutput_AndTheOthersStay()
    {
        _ = Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow =>
        {
            MadeByPart1(workflow, SPREAD_INPUT);
            _ = new DocumentClass(workflow, ANNUAL_REPORT);
            _ = new DocumentClass(workflow, "Management Notes") { IsOutputDocument = true };
        });

        NextWorkflowDto next = Assert.Single(await OfAsync(_workbook, _annualReport, _otherInput));

        Assert.Equal(
            [("doc-spread", SPREAD_INPUT), ("doc-annual", ANNUAL_REPORT)],
            next.Documents.Select(document => (document.FileId, document.DocumentClass)));
    }

    [Fact]
    public async Task ARunInputInTheClassThatTheOutputFills_IsNotCarried()
    {
        _ = Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow => MadeByPart1(workflow, SPREAD_INPUT));
        NextWorkflows.RunDocument uploadedSpread = new("doc-uploaded", "Older spread", ".xlsx", SPREAD_INPUT, IsOutput: false, IsWorkbook: true);

        NextWorkflowDto next = Assert.Single(await OfAsync(_workbook, uploadedSpread));

        Assert.Equal("doc-spread", Assert.Single(next.Documents).FileId);
    }

    [Fact]
    public async Task ARunWhoseOutputIsGone_LeadsNowhere()
    {
        _ = Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow =>
        {
            MadeByPart1(workflow, SPREAD_INPUT);
            _ = new DocumentClass(workflow, ANNUAL_REPORT);
        });

        Assert.Empty(await OfAsync(_annualReport));
        Assert.Empty(await OfAsync(_annualReport, _workbook with { DocumentClass = "Another Output" }));
    }

    [Fact]
    public async Task AnInputMadeByAnotherWorkflowsOutput_IsNotThisRunsNextWorkflow()
    {
        _ = Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow =>
            _ = new DocumentClass(workflow, SPREAD_INPUT) { MadeByWorkflowTypeId = "SomeOtherType", MadeByDocumentClassKey = SPREAD_OUTPUT });

        Assert.Empty(await OfAsync(_workbook));
    }

    private Task<List<NextWorkflowDto>> OfAsync(params NextWorkflows.RunDocument[] documents)
    {
        return new NextWorkflows(_core.UnitOfWork, new EveryWorkflowAvailable(), new NoCreditCost())
            .OfAsync(ORG, PART_1, documents, TestContext.Current.CancellationToken);
    }

    private static void MadeByPart1(Workflow workflow, string inputKey)
    {
        _ = new DocumentClass(workflow, inputKey) { MadeByWorkflowTypeId = PART_1, MadeByDocumentClassKey = SPREAD_OUTPUT };
    }

    private string Seed(string workflowTypeId, string name, Action<Workflow> build, bool retired = false, bool access = true, int createdDaysAgo = 1)
    {
        CoreDbContext core = _core.Context;
        if (!core.WorkflowTypes.Any(type => type.Id == workflowTypeId))
        {
            _ = core.WorkflowTypes.Add(new WorkflowType { Id = workflowTypeId, Name = name, RetiredAt = retired ? DateTime.UtcNow : null });
            if (access)
            {
                core.GrantWorkflowTypeAccess(workflowTypeId, ORG);
            }
        }

        var workflow = new Workflow(name) { WorkflowTypeId = workflowTypeId };
        build(workflow);
        _ = core.Workflows.Add(workflow);
        _ = core.SaveChanges();
        workflow.CreatedAt = DateTime.UtcNow.AddDays(-createdDaysAgo);
        _ = core.SaveChanges();
        return workflow.Id;
    }
}
