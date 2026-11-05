using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;
using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.StartWorkflowExecution;

/// <summary>
/// The start handler over a real database. Only the orchestrator, which starts the run, is a
/// stand-in, so each test can read what the start handed it.
/// </summary>
public sealed class StartWorkflowExecutionCommandHandlerTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-1";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly IWorkflowExecutionOrchestrator _orchestrator = Substitute.For<IWorkflowExecutionOrchestrator>();
    private readonly StartWorkflowExecutionCommandHandler _handler;

    public StartWorkflowExecutionCommandHandlerTests()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddTestDatabaseServices();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        _handler = ActivatorUtilities.CreateInstance<StartWorkflowExecutionCommandHandler>(_scope.ServiceProvider, _orchestrator);
        _ = _orchestrator.ExecuteWorkflowAsync(default!, default, default, default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(new WorkflowExecutionDto { Id = "exec-1" });
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private static JsonElement Str(string value)
    {
        return JsonSerializer.SerializeToElement(value);
    }

    private static Workflow CreateWorkflowWithVariable(string tag, string? defaultValue)
    {
        var workflow = new Workflow { Id = "wf-1", WorkflowTypeId = "wf-type-1" };
        _ = new WorkflowVariable(workflow, tag)
        {
            DefaultValue = defaultValue,
            IsRequired = false,
            Type = WorkflowVariableTypes.String,
        };

        return workflow;
    }

    private void SetupPipeline(Workflow workflow)
    {
        SeedWorkflow(workflow);
        CoreDbContext core = _scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        core.GrantWorkflowTypeAccess(workflow.WorkflowTypeId!, ORGANIZATION_ID);
        _ = core.SaveChanges();
    }

    private void SeedWorkflow(Workflow workflow)
    {
        CoreDbContext core = _scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _ = core.Workflows.Add(workflow);
        _ = core.SaveChanges();
    }

    private object?[] CapturedOrchestratorArguments()
    {
        NSubstitute.Core.ICall call = _orchestrator.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IWorkflowExecutionOrchestrator.ExecuteWorkflowAsync));
        return call.GetArguments();
    }

    private Dictionary<string, JsonElement>? CapturedForwardedVariables()
    {
        return (Dictionary<string, JsonElement>?)CapturedOrchestratorArguments()[2];
    }

    private string? CapturedEmailIntakeMode()
    {
        return (string?)CapturedOrchestratorArguments()[15];
    }

    private string? CapturedEmailAttachmentsClass()
    {
        return (string?)CapturedOrchestratorArguments()[16];
    }

    private async Task InvokeWithVariablesAsync(Dictionary<string, JsonElement>? variables)
    {
        var request = new StartWorkflowExecutionRequest { WorkflowId = "wf-1", Variables = variables };
        var command = new StartWorkflowExecutionCommand(request, ORGANIZATION_ID, "user-1", "test", "test@test.com");

        _ = await _handler.Handle(command, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        var request = new StartWorkflowExecutionRequest { WorkflowId = "wf-missing" };
        var command = new StartWorkflowExecutionCommand(request, ORGANIZATION_ID, "user-1", "test", "test@test.com");

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WhenTheOrganizationHasNoAccessToTheType_AnswersAsForAnUnknownWorkflow_AndStartsNothing()
    {
        SeedWorkflow(CreateWorkflowWithVariable("InterimPeriod", "NA"));

        var request = new StartWorkflowExecutionRequest { WorkflowId = "wf-1" };
        var command = new StartWorkflowExecutionCommand(request, ORGANIZATION_ID, "user-1", "test", "test@test.com");

        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("Workflow not found.", error.Description);
        Assert.Empty(_orchestrator.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_WhenCamelCaseKeySubmitted_CanonicalizesToReplacementTag_NoOrphan()
    {
        // Arrange — PascalCase tag; camelCase key submitted (edit-and-re-run shape).
        Workflow workflow = CreateWorkflowWithVariable("InterimPeriod", "NA");
        SetupPipeline(workflow);

        // Act
        await InvokeWithVariablesAsync(new Dictionary<string, JsonElement> { ["interimPeriod"] = Str("12") });

        // Assert — bound to the canonical tag, no camelCase orphan.
        Dictionary<string, JsonElement>? forwarded = CapturedForwardedVariables();
        Assert.NotNull(forwarded);
        Assert.True(forwarded!.ContainsKey("InterimPeriod"));
        Assert.Equal("12", forwarded["InterimPeriod"].GetString());
        Assert.False(forwarded.ContainsKey("interimPeriod"));
    }

    [Fact]
    public async Task Handle_WhenDefaultedVariableAbsent_DoesNotInjectDefault()
    {
        // Arrange — variable has a default but is not supplied. Start must NOT inject it
        // (the contract that distinguishes Start from Submit).
        Workflow workflow = CreateWorkflowWithVariable("InterimPeriod", "NA");
        SetupPipeline(workflow);

        // Act — supply an unrelated key only.
        await InvokeWithVariablesAsync(new Dictionary<string, JsonElement> { ["Other"] = Str("x") });

        // Assert — the defaulted variable is absent from the forwarded dict.
        Dictionary<string, JsonElement>? forwarded = CapturedForwardedVariables();
        Assert.NotNull(forwarded);
        Assert.False(forwarded!.ContainsKey("InterimPeriod"));
        Assert.True(forwarded.ContainsKey("Other"));
    }

    [Fact]
    public async Task Handle_ExpandEmailsTrue_PassesExtractAttachmentsModeAndClass()
    {
        Workflow workflow = CreateWorkflowWithVariable("InterimPeriod", "NA");
        SetupPipeline(workflow);

        var request = new StartWorkflowExecutionRequest
        {
            WorkflowId = "wf-1",
            ExpandEmails = true,
            AttachmentsDocumentClass = "Attachment",
        };
        var command = new StartWorkflowExecutionCommand(request, ORGANIZATION_ID, "user-1", "test", "test@test.com");

        _ = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.Equal(EmailIntakeModes.EXTRACT_ATTACHMENTS, CapturedEmailIntakeMode());
        Assert.Equal("Attachment", CapturedEmailAttachmentsClass());
    }

    /// <summary>
    /// The dormant regression path's captures and the freeze option are one choice. A start
    /// carrying both would freeze from one and stamp neither, so it is refused rather than
    /// silently resolved.
    /// </summary>
    [Fact]
    public async Task Handle_WithBothAReplaySetAndAFreezeSource_IsAProgrammingError()
    {
        Workflow workflow = CreateWorkflowWithVariable("InterimPeriod", "NA");
        SetupPipeline(workflow);

        var request = new StartWorkflowExecutionRequest
        {
            WorkflowId = "wf-1",
            FreezeSourceExecutionId = "exec-source",
        };
        var command = new StartWorkflowExecutionCommand(request, ORGANIZATION_ID, "user-1", "test", "test@test.com")
        {
            ReplayOutputs = [],
        };

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_ExpandEmailsFalse_PassesPdfOnlyMode()
    {
        Workflow workflow = CreateWorkflowWithVariable("InterimPeriod", "NA");
        SetupPipeline(workflow);

        var request = new StartWorkflowExecutionRequest { WorkflowId = "wf-1" };
        var command = new StartWorkflowExecutionCommand(request, ORGANIZATION_ID, "user-1", "test", "test@test.com");

        _ = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.Equal(EmailIntakeModes.PDF_ONLY, CapturedEmailIntakeMode());
        Assert.Null(CapturedEmailAttachmentsClass());
    }

    [Fact]
    public async Task Handle_ExpandEmailsFalse_DoesNotForwardAttachmentsClass()
    {
        Workflow workflow = CreateWorkflowWithVariable("InterimPeriod", "NA");
        SetupPipeline(workflow);

        var request = new StartWorkflowExecutionRequest
        {
            WorkflowId = "wf-1",
            AttachmentsDocumentClass = "Attachment",
        };
        var command = new StartWorkflowExecutionCommand(request, ORGANIZATION_ID, "user-1", "test", "test@test.com");

        _ = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.Equal(EmailIntakeModes.PDF_ONLY, CapturedEmailIntakeMode());
        Assert.Null(CapturedEmailAttachmentsClass());
    }
}
