using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Errors;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqliteCoreDatabase = IniaAi.Testing.DependencyInjection.SqliteCoreDatabase;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Application.UnitTests.Commands.Workflows.SaveWorkflow;

/// <summary>
/// An editor that opened a workflow before its type was deleted cannot save it back afterwards.
/// </summary>
public sealed class SaveWorkflowCommandHandlerRetiredTypeTests
{
    private const string TYPE = "tmw_test_detect";

    [Fact]
    public async Task SavingARetiredType_ReportsItDeleted_AndWritesNothing()
    {
        using var database = new SqliteCoreDatabase();
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = database.Context.WorkflowTypes.Add(new WorkflowType(TYPE, TYPE) { RetiredAt = DateTime.UtcNow });
        _ = await database.Context.SaveChangesAsync(ct);
        var handler = new SaveWorkflowCommandHandler(
            new WorkflowSavePipeline(
                database.UnitOfWork,
                database.Context,
                new WorkflowCanonicalizer(),
                new WorkflowDiffService(),
                new WorkflowNarrativeGenerator(),
                Options.Create(new AgentRuntimeSettings()),
                NullLogger<WorkflowSavePipeline>.Instance, SuperAdminLookups.OverEmptyUserData()),
            database.Context,
            NullLogger<SaveWorkflowCommandHandler>.Instance);

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand("org-test", "user-test", TYPE, new Workflow("Report") { Title = "Report" }),
            ct);

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(WorkflowTypeErrorFactory.NOT_FOUND_CODE, result.FirstError.Code);
        Assert.False(await database.Context.Workflows.IgnoreQueryFilters().AnyAsync(ct));
        Assert.False(await database.Context.WorkflowVersions.IgnoreQueryFilters().AnyAsync(ct));
    }
}
