using ErrorOr;
using IniaAi.Application.Common.Signatures;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Common.Signatures;

public class SignatureFieldSaveSynchronizerTests : IDisposable
{
    private const string ORG_A = "org-a";
    private const string ORG_B = "org-b";
    private const string ACTOR_ID = "user-staff-editor";
    private const string WORKFLOW_TYPE_ID = "WT-TestSignatures";
    private const string OLD_NAME = "old_name";
    private const string NEW_NAME = "new_name";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;

    public SignatureFieldSaveSynchronizerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Sync_Rename_MovesOverridesAcrossAllOrgs_EmitsRenamedAudits_AndMovesPendings()
    {
        // Override rows for TWO orgs plus one pending, all on the old name. A staff
        // rename must carry every org's rows to the new name, not just the saver's.
        SeedOverride(ORG_A, OLD_NAME, "value-a");
        SeedOverride(ORG_B, OLD_NAME, "value-b");
        SeedPending(ORG_A, OLD_NAME, "pending-a");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        Workflow savedWorkflow = BuildWorkflow(NEW_NAME, "prompt {" + NEW_NAME + "}");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            [new SignatureFieldRename(OLD_NAME, NEW_NAME)],
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.OverridesRenamed);
        Assert.Equal(0, result.Value.OverridesDeleted);
        Assert.Equal(0, result.Value.PendingsDeleted);

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        List<SignatureFieldOverride> overrides = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, overrides.Count);
        Assert.All(overrides, o => Assert.Equal(NEW_NAME, o.PlaceholderName));
        Assert.Contains(overrides, o => o.OrganizationId == ORG_A && o.Value == "value-a");
        Assert.Contains(overrides, o => o.OrganizationId == ORG_B && o.Value == "value-b");

        PendingSignatureOverride pending = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NEW_NAME, pending.PlaceholderName);

        List<SignatureFieldOverrideAuditLog> audits = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .OrderBy(a => a.OrganizationId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a =>
        {
            Assert.Equal(SignatureAuditAction.RENAMED, a.Action);
            Assert.Equal(NEW_NAME, a.PlaceholderName);
            Assert.Equal(ACTOR_ID, a.ActorUserId);
            Assert.Equal(a.PreviousValue, a.NewValue);
            Assert.NotNull(a.ReferencedBlockNamesJson);
            Assert.Contains("Block A", a.ReferencedBlockNamesJson);
        });
        Assert.Contains(audits, a => a.OrganizationId == ORG_A && a.NewValue == "value-a");
        Assert.Contains(audits, a => a.OrganizationId == ORG_B && a.NewValue == "value-b");
    }

    [Fact]
    public async Task Sync_DeletedDefinition_CascadesOverridePendingAndVotes_EmitsRemovedAudit()
    {
        SeedOverride(ORG_A, OLD_NAME, "override-value");
        PendingSignatureOverride pending = SeedPending(ORG_A, OLD_NAME, "pending-value");
        SeedVote(pending.Id, "user-voter-1");
        SeedVote(pending.Id, "user-voter-2");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        // The definition and its token are both gone from the saved shape.
        Workflow savedWorkflow = BuildWorkflow(definitionName: null, "prompt without token");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            renames: null,
            confirmDeletions: true,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.OverridesRenamed);
        Assert.Equal(1, result.Value.OverridesDeleted);
        Assert.Equal(1, result.Value.PendingsDeleted);

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(await _context.SignatureFieldOverrides
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await _context.SignatureOverrideVotes
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SignatureAuditAction.REMOVED, audit.Action);
        Assert.Equal(OLD_NAME, audit.PlaceholderName);
        Assert.Equal("override-value", audit.PreviousValue);
        Assert.Null(audit.NewValue);
        Assert.Equal(ACTOR_ID, audit.ActorUserId);
        Assert.Equal(ORG_A, audit.OrganizationId);
    }

    [Fact]
    public async Task Sync_DeletedDefinition_TokenStillReferenced_RefusesWithValidationError()
    {
        SeedOverride(ORG_A, OLD_NAME, "override-value");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        // Definition removed, but a bearing block's template still carries the token.
        Workflow savedWorkflow = BuildWorkflow(definitionName: null, "prompt {" + OLD_NAME + "}");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            renames: null,
            confirmDeletions: true,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(
            SignatureFieldSaveSynchronizer.DELETED_FIELD_STILL_REFERENCED_CODE,
            result.FirstError.Code);

        // Nothing was flushed; the override row survives untouched.
        SignatureFieldOverride survivor = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OLD_NAME, survivor.PlaceholderName);
    }

    [Fact]
    public async Task Sync_Rename_FieldReferencedInAgentSkill_RefusesWithNamedCode()
    {
        SeedOverride(ORG_A, OLD_NAME, "override-value");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        // The editor's rename rewrite is blind to ignore-flagged skill content, so the saved
        // workflow arrives with normal templates rewritten but the skill still on the old name.
        Workflow savedWorkflow = BuildWorkflow(NEW_NAME, "prompt {" + NEW_NAME + "}");
        AddAgentBlock(savedWorkflow, "Sign as {{" + OLD_NAME + "}}.");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            [new SignatureFieldRename(OLD_NAME, NEW_NAME)],
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(
            SignatureFieldSaveSynchronizer.RENAME_TOUCHES_AGENT_SKILL_CODE,
            result.FirstError.Code);
        Assert.Contains(OLD_NAME, result.FirstError.Description);
        Assert.Contains("helper_agent", result.FirstError.Description);

        SignatureFieldOverride survivor = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OLD_NAME, survivor.PlaceholderName);
    }

    [Fact]
    public async Task Sync_Rename_FieldReferencedInAnAgentTaskPrompt_RefusesAndNamesThePrompt()
    {
        SeedOverride(ORG_A, OLD_NAME, "override-value");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        Workflow savedWorkflow = BuildWorkflow(NEW_NAME, "prompt {" + NEW_NAME + "}");
        AddAgentBlock(savedWorkflow, "No field reference here.");
        ((AgentProperties)savedWorkflow.GetAllBlocks().Single(b => b.Type == BlockTypes.Agent).Properties)
            .PromptTemplate = "Sign as {{" + OLD_NAME + "}}.";

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            [new SignatureFieldRename(OLD_NAME, NEW_NAME)],
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SignatureFieldSaveSynchronizer.RENAME_TOUCHES_AGENT_SKILL_CODE, result.FirstError.Code);
        Assert.Contains("the task prompt of agent block 'helper_agent'", result.FirstError.Description);
    }

    [Fact]
    public async Task Sync_Rename_AgentSkillWithoutFieldReference_Proceeds()
    {
        SeedOverride(ORG_A, OLD_NAME, "override-value");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        Workflow savedWorkflow = BuildWorkflow(NEW_NAME, "prompt {" + NEW_NAME + "}");
        AddAgentBlock(savedWorkflow, "No field reference here, only {{some_block_tag}}.");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            [new SignatureFieldRename(OLD_NAME, NEW_NAME)],
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.OverridesRenamed);
    }

    private static void AddAgentBlock(Workflow workflow, string skillContent)
    {
        Section section = workflow.Sections.First();
        var agent = new Block(BlockTypes.Agent, section, replacementTag: "helper_agent");
        var props = (AgentProperties)agent.Properties;
        props.WorkerSkills.Add(AgentTestData.Skill("signing", skillContent));
        props.Model = "test-model-a";
    }

    [Fact]
    public async Task Sync_NoRenamesNoDeletions_ReturnsZeros_AndEmitsNoAudits()
    {
        SeedOverride(ORG_A, OLD_NAME, "override-value");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        Workflow savedWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "} edited");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            renames: [],
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(SignatureSyncResult.Empty, result.Value);

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
        SignatureFieldOverride untouched = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OLD_NAME, untouched.PlaceholderName);
        Assert.Equal("override-value", untouched.Value);
    }

    [Fact]
    public async Task Sync_ChainedRenames_CollapseToSingleHop_SoRowsLandOnFinalName()
    {
        SeedOverride(ORG_A, OLD_NAME, "value-a");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        Workflow savedWorkflow = BuildWorkflow("final_name", "prompt {final_name}");

        // Session renamed old_name -> temp_name -> final_name; only the collapsed hop may
        // touch the DB, because the intermediate name never existed in stored rows.
        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            [new SignatureFieldRename(OLD_NAME, "temp_name"), new SignatureFieldRename("temp_name", "final_name")],
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.OverridesRenamed);

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("final_name", row.PlaceholderName);
    }

    [Fact]
    public async Task Sync_RevertedRename_IsDroppedEntirely()
    {
        SeedOverride(ORG_A, OLD_NAME, "value-a");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        Workflow savedWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            [new SignatureFieldRename(OLD_NAME, NEW_NAME), new SignatureFieldRename(NEW_NAME, OLD_NAME)],
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(SignatureSyncResult.Empty, result.Value);

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OLD_NAME, row.PlaceholderName);
        Assert.False(await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sync_StaleRename_TargetNotDefinedOnSavedWorkflow_IsIgnored()
    {
        SeedOverride(ORG_A, OLD_NAME, "value-a");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // The saved workflow still defines OLD_NAME — the rename record came from a
        // replaced editor session (discarded upload/restore) and must not re-key rows.
        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        Workflow savedWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            [new SignatureFieldRename(OLD_NAME, NEW_NAME)],
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.OverridesRenamed);

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OLD_NAME, row.PlaceholderName);
    }

    [Fact]
    public async Task Sync_RenameOntoNameDeletedInSameSave_DeletesOldRowsThenCarriesRename()
    {
        // Field NEW_NAME existed with rows; it was deleted this session and OLD_NAME was
        // renamed onto its name. The stale NEW_NAME rows must cascade (not survive to
        // collide or be adopted), and OLD_NAME's rows must land under NEW_NAME.
        SeedOverride(ORG_A, OLD_NAME, "kept-value");
        SeedOverride(ORG_A, NEW_NAME, "stale-deleted-value");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        AddDefinition(oldWorkflow, NEW_NAME);
        Workflow savedWorkflow = BuildWorkflow(NEW_NAME, "prompt {" + NEW_NAME + "}");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            [new SignatureFieldRename(OLD_NAME, NEW_NAME)],
            confirmDeletions: true,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.OverridesDeleted);
        Assert.Equal(1, result.Value.OverridesRenamed);

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NEW_NAME, row.PlaceholderName);
        Assert.Equal("kept-value", row.Value);
    }

    [Fact]
    public async Task Sync_DeletionWithRows_WithoutConfirmation_RefusesWithCounts()
    {
        SeedOverride(ORG_A, OLD_NAME, "value-a");
        SeedPending(ORG_B, OLD_NAME, "pending-b");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        Workflow savedWorkflow = BuildWorkflow(definitionName: null, "prompt without token");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            renames: null,
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(
            SignatureFieldSaveSynchronizer.DELETION_REQUIRES_CONFIRMATION_CODE,
            result.FirstError.Code);
        Assert.Contains(OLD_NAME, result.FirstError.Description);
        Assert.Contains("1 override(s)", result.FirstError.Description);
        Assert.Contains("1 pending(s)", result.FirstError.Description);

        // Nothing deleted.
        Assert.True(await _context.SignatureFieldOverrides
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
        Assert.True(await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sync_DeletionWithoutRows_NeedsNoConfirmation()
    {
        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        Workflow savedWorkflow = BuildWorkflow(definitionName: null, "prompt without token");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            renames: null,
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(SignatureSyncResult.Empty, result.Value);
    }

    [Fact]
    public async Task Sync_PendingOnlyRename_EmitsAuditRow()
    {
        _ = SeedPending(ORG_A, OLD_NAME, "pending-a");
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Workflow oldWorkflow = BuildWorkflow(OLD_NAME, "prompt {" + OLD_NAME + "}");
        Workflow savedWorkflow = BuildWorkflow(NEW_NAME, "prompt {" + NEW_NAME + "}");

        ErrorOr<SignatureSyncResult> result = await SignatureFieldSaveSynchronizer.SyncAsync(
            _context,
            WORKFLOW_TYPE_ID,
            ACTOR_ID,
            oldWorkflow,
            savedWorkflow,
            [new SignatureFieldRename(OLD_NAME, NEW_NAME)],
            confirmDeletions: false,
            NullLogger<SignatureFieldSaveSynchronizerTests>.Instance,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SignatureAuditAction.RENAMED, audit.Action);
        Assert.Equal(NEW_NAME, audit.PlaceholderName);
        Assert.Equal(ORG_A, audit.OrganizationId);
        Assert.Null(audit.PreviousValue);
        Assert.Null(audit.NewValue);
    }

    private static void AddDefinition(Workflow workflow, string name)
    {
        _ = new WorkflowSignatureField(workflow, name)
        {
            DisplayName = "Field " + name,
            Description = "desc",
            DefaultValue = "staff-default",
        };
    }

    private static Workflow BuildWorkflow(string? definitionName, string promptTemplate)
    {
        var workflow = new Workflow("Test Workflow")
        {
            OrganizationId = ORG_A,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        if (definitionName is not null)
        {
            _ = new WorkflowSignatureField(workflow, definitionName)
            {
                DisplayName = "Field",
                Description = "desc",
                DefaultValue = "staff-default",
            };
        }

        var section = new Section("Section A", workflow) { Order = 0 };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Block A",
            Properties = new AiQueryProperties { UserPromptTemplate = promptTemplate },
        };

        return workflow;
    }

    private void SeedOverride(string orgId, string placeholderName, string value)
    {
        _ = _context.SignatureFieldOverrides.Add(new SignatureFieldOverride
        {
            OrganizationId = orgId,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = placeholderName,
            Value = value,
        });
    }

    private PendingSignatureOverride SeedPending(string orgId, string placeholderName, string proposedValue)
    {
        var pending = new PendingSignatureOverride
        {
            OrganizationId = orgId,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = placeholderName,
            ProposedValue = proposedValue,
            ProposerUserId = "user-proposer",
        };
        _ = _context.PendingSignatureOverrides.Add(pending);
        return pending;
    }

    private void SeedVote(string pendingId, string voterId)
    {
        _ = _context.SignatureOverrideVotes.Add(new SignatureOverrideVote
        {
            PendingOverrideId = pendingId,
            VoterUserId = voterId,
            VoteKind = SignatureVoteKind.Approve,
        });
    }
}
