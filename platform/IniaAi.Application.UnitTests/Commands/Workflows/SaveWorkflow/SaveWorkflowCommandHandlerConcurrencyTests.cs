using System.Data.Common;
using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using SqliteCoreDatabase = IniaAi.Testing.DependencyInjection.SqliteCoreDatabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Application.UnitTests.Commands.Workflows.SaveWorkflow;

/// <summary>
/// Covers the concurrency-loss catch path of <see cref="SaveWorkflowCommandHandler"/>: when the
/// Workflow-row delete loses the race the loser's CompleteAsync raises DbUpdateConcurrencyException,
/// which must become a recoverable 409 carrying the winner's head metadata — never a 500. SQLite
/// never loses that race on its own, so an interceptor raises the exception on the save that
/// deletes the old template.
/// </summary>
public class SaveWorkflowCommandHandlerConcurrencyTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";

    [Fact]
    public async Task ConcurrentDelete_DbUpdateConcurrencyException_ReturnsConflictWithHeadMetadata()
    {
        using var harness = new SqliteConcurrencyHarness();
        SaveWorkflowCommandHandler handler = harness.CreateHandler();
        ErrorOr<SaveWorkflowResult> head = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow("winner prompt")),
            TestContext.Current.CancellationToken);
        Assert.False(head.IsError);
        harness.LoseTheTemplateDeleteRace();

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow("loser prompt")),
            TestContext.Current.CancellationToken);

        Assert.True(harness.RaceLost);
        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("WorkflowTemplate.ConcurrentSave", result.FirstError.Code);
        Assert.NotNull(result.FirstError.Metadata);
        // The 409 must carry the winner's head so the caller can recover inline.
        Assert.Equal(head.Value.VersionId, result.FirstError.Metadata!["currentHeadVersionId"]);
        Assert.Equal(head.Value.ContentHash, result.FirstError.Metadata["currentContentHash"]);
        Assert.True(result.FirstError.Metadata.ContainsKey("headCreatedAtUtc"));
        // The editor's user id is deliberately not disclosed in the error body.
        Assert.False(result.FirstError.Metadata.ContainsKey("headCreatedByUserId"));
    }

    [Fact]
    public async Task ConcurrentDelete_HeadReReadFails_StillReturnsConflictNot500()
    {
        // The re-read of the winning head is guarded: if it throws, the 409 must still be returned
        // (without metadata) rather than escalating to a 500.
        using var harness = new SqliteConcurrencyHarness();
        SaveWorkflowCommandHandler handler = harness.CreateHandler();
        _ = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow("winner prompt")),
            TestContext.Current.CancellationToken);
        harness.LoseTheTemplateDeleteRace(failTheHeadReRead: true);

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow("loser prompt")),
            TestContext.Current.CancellationToken);

        Assert.True(harness.HeadReReadFailed);
        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("WorkflowTemplate.ConcurrentSave", result.FirstError.Code);
    }

    private static Workflow BuildWorkflow(string prompt)
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };

        return workflow;
    }

    private sealed class SqliteConcurrencyHarness : IDisposable
    {
        private readonly LostDeleteRaceInterceptor _race = new();
        private readonly FailingHeadReReadInterceptor _headReRead = new();
        private readonly SqliteCoreDatabase _database;

        public SqliteConcurrencyHarness()
        {
            _database = new SqliteCoreDatabase(_race, _headReRead);
            _ = _database.Context.WorkflowTypes.Add(new WorkflowType(TYPE, "Report"));
            _ = _database.Context.SaveChanges();
        }

        public bool RaceLost => _race.HasFired;

        public bool HeadReReadFailed => _headReRead.HasFired;

        public void LoseTheTemplateDeleteRace(bool failTheHeadReRead = false)
        {
            _race.Arm(onFired: failTheHeadReRead ? _headReRead.Arm : null);
        }

        public SaveWorkflowCommandHandler CreateHandler()
        {
            return new SaveWorkflowCommandHandler(
                new WorkflowSavePipeline(
                    _database.UnitOfWork,
                    _database.Context,
                    new WorkflowCanonicalizer(),
                    new WorkflowDiffService(),
                    new WorkflowNarrativeGenerator(),
                    Options.Create(new AgentRuntimeSettings()),
                    NullLogger<WorkflowSavePipeline>.Instance, SuperAdminLookups.OverEmptyUserData()),
                _database.Context,
                NullLogger<SaveWorkflowCommandHandler>.Instance);
        }

        public void Dispose()
        {
            _database.Dispose();
        }
    }

    /// <summary>Fails the save that deletes the old template, the way a lost race does.</summary>
    private sealed class LostDeleteRaceInterceptor : SaveChangesInterceptor
    {
        private bool _armed;
        private Action? _onFired;

        public bool HasFired { get; private set; }

        public void Arm(Action? onFired)
        {
            _armed = true;
            _onFired = onFired;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            bool deletesTemplate = eventData.Context!.ChangeTracker.Entries<Workflow>()
                .Any(entry => entry.State == EntityState.Deleted || (entry.State == EntityState.Modified && entry.Entity.IsDeleted));
            if (_armed && deletesTemplate)
            {
                _armed = false;
                HasFired = true;
                _onFired?.Invoke();
                throw new DbUpdateConcurrencyException("simulated lost race");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailingHeadReReadInterceptor : DbCommandInterceptor
    {
        private bool _armed;

        public bool HasFired { get; private set; }

        public void Arm()
        {
            _armed = true;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_armed && command.CommandText.Contains("WorkflowVersions", StringComparison.Ordinal))
            {
                _armed = false;
                HasFired = true;
                throw new InvalidOperationException("re-read failed");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
