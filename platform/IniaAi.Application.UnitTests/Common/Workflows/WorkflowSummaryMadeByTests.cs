using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Common.RunLimits;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Models.Dto;

namespace IniaAi.Application.UnitTests.Common.Workflows;

/// <summary>
/// The report type summary names, on an input made by another workflow, that workflow and its output, so the new
/// report form can say where the file comes from. A link whose output is gone names nothing.
/// </summary>
public sealed class WorkflowSummaryMadeByTests : IDisposable
{
    private const string ORG = "org-test";
    private const string PART_1 = "CorpPart1TypeA";
    private const string PART_2 = "CorpPart2TypeA";

    private readonly SqliteCoreDatabase _core = new();

    public void Dispose()
    {
        _core.Dispose();
    }

    [Fact]
    public async Task AnInputMadeByAnotherWorkflow_NamesThatWorkflowAndItsOutput()
    {
        Seed(PART_1, "INIA Corporate Analysis - Part 1", workflow =>
            _ = new DocumentClass(workflow, "Financial Spread Output") { DisplayName = "Financial Spread", IsOutputDocument = true });
        Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow =>
        {
            _ = new DocumentClass(workflow, "CorporateFinancialModel") { MadeByWorkflowTypeId = PART_1, MadeByDocumentClassKey = "Financial Spread Output" };
            _ = new DocumentClass(workflow, "Gone") { MadeByWorkflowTypeId = PART_1, MadeByDocumentClassKey = "An Output It No Longer Makes" };
            _ = new DocumentClass(workflow, "Annual Report");
        });

        WorkflowDto summary = (await new GetWorkflowSummaryByTypeQueryHandler(_core.UnitOfWork, new NoCreditCost(), new NoPageLimit())
            .Handle(new GetWorkflowSummaryByTypeQuery(ORG, PART_2), TestContext.Current.CancellationToken)).Value;

        Dictionary<string, DocumentClassMadeByDto?> madeBy = summary.DocumentClasses.ToDictionary(documentClass => documentClass.Key, documentClass => documentClass.MadeBy);
        Assert.Equal(new DocumentClassMadeByDto(PART_1, "INIA Corporate Analysis - Part 1", "Financial Spread"), madeBy["CorporateFinancialModel"]);
        Assert.Null(madeBy["Gone"]);
        Assert.Null(madeBy["Annual Report"]);
    }

    [Fact]
    public async Task AMakerTheOrganizationCannotReach_StaysUnnamed()
    {
        Seed(PART_1, "Another organization's workflow", workflow =>
            _ = new DocumentClass(workflow, "Financial Spread Output") { DisplayName = "Financial Spread", IsOutputDocument = true }, access: false);
        Seed(PART_2, "INIA Corporate Analysis - Part 2", workflow =>
            _ = new DocumentClass(workflow, "CorporateFinancialModel") { MadeByWorkflowTypeId = PART_1, MadeByDocumentClassKey = "Financial Spread Output" });

        WorkflowDto summary = (await new GetWorkflowSummaryByTypeQueryHandler(_core.UnitOfWork, new NoCreditCost(), new NoPageLimit())
            .Handle(new GetWorkflowSummaryByTypeQuery(ORG, PART_2), TestContext.Current.CancellationToken)).Value;

        Assert.Null(Assert.Single(summary.DocumentClasses).MadeBy);
    }

    private void Seed(string workflowTypeId, string name, Action<Workflow> build, bool access = true)
    {
        CoreDbContext core = _core.Context;
        _ = core.WorkflowTypes.Add(new WorkflowType { Id = workflowTypeId, Name = name });
        if (access)
        {
            core.GrantWorkflowTypeAccess(workflowTypeId, ORG);
        }

        var workflow = new Workflow(name) { WorkflowTypeId = workflowTypeId };
        build(workflow);
        _ = core.Workflows.Add(workflow);
        _ = core.SaveChanges();
    }
}
