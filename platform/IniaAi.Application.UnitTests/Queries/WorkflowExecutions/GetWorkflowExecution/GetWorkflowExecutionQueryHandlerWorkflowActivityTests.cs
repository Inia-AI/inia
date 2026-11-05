using ErrorOr;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;
using IniaAi.Core.Workflows.Activity;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetWorkflowExecution;

public class GetWorkflowExecutionQueryHandlerWorkflowActivityTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string USER_ID = "user-test";
    private const string VERSION_ID = "version-1";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _userDataContext;
    private readonly CoreDbContext _coreContext;
    private readonly GetWorkflowExecutionQueryHandler _handler;

    public GetWorkflowExecutionQueryHandlerWorkflowActivityTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _userDataContext = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _coreContext = _serviceProvider.GetRequiredService<CoreDbContext>();

        _handler = new GetWorkflowExecutionQueryHandler(
            NullLogger<GetWorkflowExecutionQueryHandler>.Instance,
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            _serviceProvider.GetRequiredService<ICoreUnitOfWork>(),
            new NoBaselineReader());
    }

    private const string FIRST_BLOCK_STABLE_ID = "stablefirst0000000000000000000001";
    private const string SECOND_BLOCK_STABLE_ID = "stablesecond000000000000000000002";

    private static WorkflowActivityScript TwoStepScript()
    {
        return new WorkflowActivityScript(
            [
                new WorkflowActivityGroup(0, 0, "Checking the loaded reports"),
                new WorkflowActivityGroup(1, 1, "Assembling the report"),
            ],
            [FIRST_BLOCK_STABLE_ID, SECOND_BLOCK_STABLE_ID]);
    }

    private async Task SeedVersionAsync(string? activityScriptJson)
    {
        var version = new WorkflowVersion
        {
            Id = VERSION_ID,
            WorkflowTypeId = "workflow-type-1",
            ContentHash = new string('a', 64),
            CreatedByUserId = USER_ID,
            ActivityScriptJson = activityScriptJson,
        };
        _ = await _coreContext.Set<WorkflowVersion>().AddAsync(version, TestContext.Current.CancellationToken);
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _coreContext.ChangeTracker.Clear();
    }

    private async Task<WorkflowExecution> SeedExecutionAsync(
        WorkflowState state = WorkflowState.Running,
        string? templateVersionId = VERSION_ID)
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = "workflow-type-1",
            WorkflowState = state,
            TemplateVersionId = templateVersionId,
        };

        _ = await _userDataContext.WorkflowExecutions.AddAsync(execution, TestContext.Current.CancellationToken);
        _ = await _userDataContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _userDataContext.ChangeTracker.Clear();

        return execution;
    }

    private async Task SeedCompletedBlockLogAsync(
        WorkflowExecution execution,
        string stableBlockId)
    {
        var workflow = new Workflow($"Executed-{stableBlockId}");
        var section = new Section("Section", workflow);
        var block = new IniaAi.Domain.Entities.Workflows.Blocks.Block(
            IniaAi.Domain.Entities.Workflows.Enums.BlockTypes.Merge, section, $"tag{stableBlockId[..8]}")
        {
            StableBlockId = stableBlockId,
        };

        var log = new WorkflowExecutionLog
        {
            WorkflowExecutionId = execution.Id,
            WorkflowId = execution.Id,
            BlockId = block.Id,
            Block = block,
            BlockName = block.Name,
            SectionName = section.Title,
        };

        _ = await _userDataContext.Set<Workflow>().AddAsync(workflow, TestContext.Current.CancellationToken);
        _ = await _userDataContext.Set<WorkflowExecutionLog>().AddAsync(log, TestContext.Current.CancellationToken);
        _ = await _userDataContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _userDataContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ARunningExecutionWithAScriptCarriesItsActivitySteps()
    {
        await SeedVersionAsync(WorkflowActivityScriptSerializer.Serialize(TwoStepScript()));
        WorkflowExecution execution = await SeedExecutionAsync();

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        WorkflowActivityDto activity = Assert.IsType<WorkflowActivityDto>(result.Value.WorkflowActivity);
        Assert.Equal(0, activity.CurrentStepIndex);
        Assert.Equal(
            ["Checking the loaded reports", "Assembling the report"],
            activity.Steps.Select(step => step.Label));
    }

    [Fact]
    public async Task ACompletedFirstGroupAdvancesTheCurrentStep()
    {
        await SeedVersionAsync(WorkflowActivityScriptSerializer.Serialize(TwoStepScript()));
        WorkflowExecution execution = await SeedExecutionAsync();
        await SeedCompletedBlockLogAsync(execution, FIRST_BLOCK_STABLE_ID + "_AR");

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.WorkflowActivity?.CurrentStepIndex);
        Assert.Equal(
            [WorkflowActivityStepStatus.DONE, WorkflowActivityStepStatus.ACTIVE],
            result.Value.WorkflowActivity?.Steps.Select(step => step.Status));
    }

    [Fact]
    public async Task ATerminalExecutionCarriesNoActivity()
    {
        await SeedVersionAsync(WorkflowActivityScriptSerializer.Serialize(TwoStepScript()));
        WorkflowExecution execution = await SeedExecutionAsync(state: WorkflowState.Draft);

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.WorkflowActivity);
    }

    [Fact]
    public async Task AnExecutionWithoutAStampedVersionCarriesNoActivity()
    {
        WorkflowExecution execution = await SeedExecutionAsync(templateVersionId: null);

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.WorkflowActivity);
    }

    [Fact]
    public async Task AVersionWithoutAScriptCarriesNoActivity()
    {
        await SeedVersionAsync(activityScriptJson: null);
        WorkflowExecution execution = await SeedExecutionAsync();

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, execution.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.WorkflowActivity);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
