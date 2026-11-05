using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Testing;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class SharedComponentFanOutServiceTests : IDisposable
{
    private const string COMPONENT_ID = "component-1";
    private const string OTHER_COMPONENT_ID = "component-2";
    private static readonly string _hash = new('a', 64);

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly CoreDbContext _coreContext;
    private readonly UserDataDbContext _userDataContext;
    private readonly SharedComponentFanOutService _service;

    public SharedComponentFanOutServiceTests()
    {
        _coreContext = _harness.Context;
        _userDataContext = _harness.UserDataContext;
        _service = _harness.CreateFanOutService();
    }

    [Fact]
    public async Task GetConsumers_AggregatesLinkedInstancesPerWorkflowType_ExcludesBrokenOutAndOtherComponents()
    {
        var alpha = new Workflow("Alpha Report") { WorkflowTypeId = "WT-A" };
        var alphaSection = new Section("S1", alpha) { Title = "S1" };
        LinkBlock(alphaSection, "a1", COMPONENT_ID, SharedComponentLinkState.Linked);
        LinkBlock(alphaSection, "a2", COMPONENT_ID, SharedComponentLinkState.BrokenOut);
        LinkBlock(alphaSection, "a3", OTHER_COMPONENT_ID, SharedComponentLinkState.Linked);
        var alphaLinkedSection = new Section("S2", alpha) { Title = "S2" };
        alphaLinkedSection.SharedComponentId = COMPONENT_ID;
        alphaLinkedSection.SharedComponentLinkState = SharedComponentLinkState.Linked;
        alphaLinkedSection.SharedComponentVersionHash = _hash;

        var beta = new Workflow("Beta Report") { WorkflowTypeId = "WT-B" };
        var betaSection = new Section("S1", beta) { Title = "S1" };
        LinkBlock(betaSection, "b1", COMPONENT_ID, SharedComponentLinkState.Linked);
        LinkBlock(betaSection, "b2", COMPONENT_ID, SharedComponentLinkState.Linked);

        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-A", "Alpha Type"));
        _ = _coreContext.Add(alpha);
        _ = _coreContext.Add(beta);
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<SharedComponentConsumer> consumers = await _service.GetConsumersAsync(
            COMPONENT_ID, TestContext.Current.CancellationToken);

        Assert.Equal(2, consumers.Count);
        SharedComponentConsumer alphaConsumer = consumers.Single(c => c.WorkflowTypeId == "WT-A");
        Assert.Equal("Alpha Type", alphaConsumer.WorkflowTypeName);
        Assert.Equal(2, alphaConsumer.LinkedInstanceCount);
        SharedComponentConsumer betaConsumer = consumers.Single(c => c.WorkflowTypeId == "WT-B");
        Assert.Equal("Beta Report", betaConsumer.WorkflowTypeName);
        Assert.Equal(2, betaConsumer.LinkedInstanceCount);
    }

    [Fact]
    public async Task GetConsumers_NoLinkedInstances_ReturnsEmpty()
    {
        List<SharedComponentConsumer> consumers = await _service.GetConsumersAsync(
            COMPONENT_ID, TestContext.Current.CancellationToken);

        Assert.Empty(consumers);
    }

    /// <summary>
    /// The kept registration rows outlive the validation module, so a build without it still holds
    /// them; only the module reads them as affected baselines.
    /// </summary>
    [Fact]
    public async Task GetAffectedBaselines_InABuildWithoutValidation_ListsNone()
    {
        string headHash = new('2', 64);
        _ = _coreContext.Add(_harness.BuildWorkflowVersion("WT-A", headHash, DateTime.UtcNow.AddDays(-1)));
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _ = _userDataContext.Add(_harness.BuildRegistration("WT-A", "org-1", headHash));
        _ = await _userDataContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<SharedComponentBaselineImpact> impacts = await _service.GetAffectedBaselinesAsync(
            ["WT-A"], TestContext.Current.CancellationToken);

        Assert.Empty(impacts);
    }

    private static void LinkBlock(
        Section section,
        string tag,
        string componentId,
        SharedComponentLinkState linkState)
    {
        var block = new Block(BlockTypes.AiQuery, section, tag) { Name = $"Block {tag}" };
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = linkState;
        block.SharedComponentVersionHash = _hash;
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
