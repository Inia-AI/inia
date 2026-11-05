using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

public class DateVariableBindingTests : IClassFixture<WorkflowsFixture<WorkflowHandler>>
{
    private const string VARIABLE_TAG = "AsOfDate";

    private readonly WorkflowHandler _workflowHandler;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public DateVariableBindingTests(
        WorkflowsFixture<WorkflowHandler> fixture,
        ITestOutputHelper testOutputHelper)
    {
        IServiceProvider serviceProvider = fixture.BuildTestServiceProvider(testOutputHelper);
        _workflowHandler = serviceProvider.GetRequiredService<WorkflowHandler>();
        IServiceScope scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    [Theory]
    [InlineData("2026-09-25")]
    [InlineData("2026-09-25T00:00")]
    [InlineData("2026-09-25T17:45")]
    public async Task DateVariable_FromEitherClient_ReachesThePlaceholderAsADate(string submitted)
    {
        string resolved = await RunAsync(
            WorkflowVariableTypes.DateTime,
            defaultValue: null,
            inputs: new() { [VARIABLE_TAG] = submitted });

        Assert.Equal("As of 2026-09-25", resolved);
    }

    [Fact]
    public async Task DateVariable_StoredDefaultWithATime_ReachesThePlaceholderAsADate()
    {
        string resolved = await RunAsync(
            WorkflowVariableTypes.DateTime,
            defaultValue: "2026-09-25T00:00",
            inputs: null);

        Assert.Equal("As of 2026-09-25", resolved);
    }

    [Fact]
    public async Task DateVariable_UnparseableValue_IsPassedThrough()
    {
        string resolved = await RunAsync(
            WorkflowVariableTypes.DateTime,
            defaultValue: null,
            inputs: new() { [VARIABLE_TAG] = "end of quarter" });

        Assert.Equal("As of end of quarter", resolved);
    }

    [Fact]
    public async Task StringVariable_HoldingADateTime_IsPassedThrough()
    {
        string resolved = await RunAsync(
            WorkflowVariableTypes.String,
            defaultValue: null,
            inputs: new() { [VARIABLE_TAG] = "2026-09-25T00:00" });

        Assert.Equal("As of 2026-09-25T00:00", resolved);
    }

    private async Task<string> RunAsync(
        WorkflowVariableTypes type,
        string? defaultValue,
        Dictionary<string, string>? inputs)
    {
        var workflow = new Workflow("Date variable binding");
        _ = new WorkflowVariable(workflow, VARIABLE_TAG)
        {
            Type = type,
            DefaultValue = defaultValue,
        };

        var section = new Section("Section", workflow);
        var block = new Block(BlockTypes.Merge, section, "reader")
        {
            Properties = new MergeProperties { TextTemplate = "As of {AsOfDate}" },
        };

        _ = await _workflowHandler.ExecuteWorkflowAsync(
            workflow,
            [],
            "testOrg",
            Guid.NewGuid().ToString(),
            _userDataUnitOfWork,
            inputs: inputs,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        return block.BlockResponse.RawResponse!;
    }
}
