using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Application.UnitTests.Common.Workflows;

/// <summary>
/// The outputs an input can be made by come from the current workflow of every other user-facing type, and an output
/// knows which current workflows' inputs it makes. The queries run on SQLite, a relational provider, as they do on the
/// deployed databases.
/// </summary>
public sealed class MadeByLinksTests : IDisposable
{
    private const string PART_1 = "CorpPart1TypeA";
    private const string PART_2 = "CorpPart2TypeA";

    private readonly DateTime _now = DateTime.UtcNow;
    private readonly SqliteCoreDatabase _core = new();

    public void Dispose()
    {
        _core.Dispose();
    }

    [Fact]
    public async Task TheChoices_AreTheOutputsOfEveryOtherTypesCurrentWorkflow()
    {
        Seed(PART_1, "INIA Corporate Analysis - Part 1", createdDaysAgo: 3, workflow => Output(workflow, "Old Output", "Old Spread"));
        Seed(PART_1, "INIA Corporate Analysis - Part 1", createdDaysAgo: 1, workflow => Output(workflow, "Financial Spread Output", "Financial Spread"));
        Seed(PART_2, "INIA Corporate Analysis - Part 2", createdDaysAgo: 1, workflow => Output(workflow, "Report", "Report"));
        Seed("RetiredType", "Retired", createdDaysAgo: 1, workflow => Output(workflow, "Gone", "Gone"), retired: true);

        List<MadeByChoice> choices = await MadeByLinks.OutputsAsync(_core.UnitOfWork, PART_2, TestContext.Current.CancellationToken);

        MadeByChoice choice = Assert.Single(choices);
        Assert.Equal(new MadeByChoice(PART_1, "INIA Corporate Analysis - Part 1", "Financial Spread Output", "Financial Spread"), choice);
    }

    [Fact]
    public async Task AnOutput_KnowsTheInputsOfCurrentWorkflowsThatItMakes()
    {
        Seed(PART_1, "INIA Corporate Analysis - Part 1", createdDaysAgo: 1, workflow => Output(workflow, "Financial Spread Output", "Financial Spread"));
        Seed(PART_2, "INIA Corporate Analysis - Part 2", createdDaysAgo: 1, workflow =>
            _ = new DocumentClass(workflow, "CorporateFinancialModel") { DisplayName = "Financial Spreads", MadeByWorkflowTypeId = PART_1, MadeByDocumentClassKey = "Financial Spread Output" });
        Seed("Other", "Other report", createdDaysAgo: 1, workflow =>
            _ = new DocumentClass(workflow, "Spread") { DisplayName = "Spread", MadeByWorkflowTypeId = PART_1, MadeByDocumentClassKey = "Another Output" });

        List<MadeByChoice> users = await MadeByLinks.UsersOfAsync(_core.UnitOfWork, PART_1, "Financial Spread Output", TestContext.Current.CancellationToken);

        Assert.Equal(new MadeByChoice(PART_2, "INIA Corporate Analysis - Part 2", "CorporateFinancialModel", "Financial Spreads"), Assert.Single(users));
    }

    [Fact]
    public async Task TwoWorkflowsOfATypeSavedAtTheSameMoment_GiveOneChoice()
    {
        Seed(PART_1, "INIA Corporate Analysis - Part 1", createdDaysAgo: 1, workflow => Output(workflow, "Financial Spread Output", "Financial Spread"));
        Seed(PART_1, "INIA Corporate Analysis - Part 1", createdDaysAgo: 1, workflow => Output(workflow, "Financial Spread Output", "Financial Spread"));

        List<MadeByChoice> choices = await MadeByLinks.OutputsAsync(_core.UnitOfWork, PART_2, TestContext.Current.CancellationToken);

        _ = Assert.Single(choices);
    }

    [Fact]
    public async Task AnOutputClassThatCarriesALink_IsNotAUser()
    {
        Seed(PART_2, "INIA Corporate Analysis - Part 2", createdDaysAgo: 1, workflow =>
            _ = new DocumentClass(workflow, "Report") { DisplayName = "Report", IsOutputDocument = true, MadeByWorkflowTypeId = PART_1, MadeByDocumentClassKey = "Financial Spread Output" });

        Assert.Empty(await MadeByLinks.UsersOfAsync(_core.UnitOfWork, PART_1, "Financial Spread Output", TestContext.Current.CancellationToken));
    }

    private static void Output(Workflow workflow, string key, string displayName)
    {
        _ = new DocumentClass(workflow, key) { DisplayName = displayName, IsOutputDocument = true };
    }

    private void Seed(string workflowTypeId, string name, int createdDaysAgo, Action<Workflow> build, bool retired = false)
    {
        CoreDbContext core = _core.Context;
        if (!core.WorkflowTypes.Any(type => type.Id == workflowTypeId))
        {
            _ = core.WorkflowTypes.Add(new WorkflowType { Id = workflowTypeId, Name = name, RetiredAt = retired ? DateTime.UtcNow : null });
        }

        var workflow = new Workflow(name) { WorkflowTypeId = workflowTypeId };
        build(workflow);
        _ = core.Workflows.Add(workflow);
        _ = core.SaveChanges();
        workflow.CreatedAt = _now.AddDays(-createdDaysAgo);
        _ = core.SaveChanges();
    }

}
