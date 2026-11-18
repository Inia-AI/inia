using ErrorOr;
using IniaAi.Application.Commands.Signatures.SubmitPendingSignatureOverride;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
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

namespace IniaAi.Application.UnitTests.Commands.Signatures.SubmitPendingSignatureOverride;

public class SubmitPendingSignatureOverrideCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string PROPOSER_ID = "user-proposer";
    private const string WORKFLOW_TYPE_ID = "WT-TestSignatures";
    private const string FIELD_A = "field_a";
    private const string STAFF_DEFAULT_A = "staff-default-a";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly SubmitPendingSignatureOverrideCommandHandler _handler;

    public SubmitPendingSignatureOverrideCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();

        ICoreUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        _handler = new SubmitPendingSignatureOverrideCommandHandler(
            unitOfWork,
            _serviceProvider.GetRequiredService<WorkflowTypeAccess>(),
            NullLogger<SubmitPendingSignatureOverrideCommandHandler>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_HappyPath_CreatesPending_AndSeedsProposerApprove()
    {
        _ = await SeedAiQueryBlockAsync(ORG_ID, WORKFLOW_TYPE_ID);

        var command = new SubmitPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, WORKFLOW_TYPE_ID, FIELD_A, "new-value");

        ErrorOr<string> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(string.IsNullOrEmpty(result.Value));

        PendingSignatureOverride stored = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .Include(p => p.Votes)
            .FirstAsync(p => p.Id == result.Value, TestContext.Current.CancellationToken);

        Assert.Equal(ORG_ID, stored.OrganizationId);
        Assert.Equal(WORKFLOW_TYPE_ID, stored.WorkflowTypeId);
        Assert.Equal(FIELD_A, stored.PlaceholderName);
        Assert.Equal("new-value", stored.ProposedValue);
        Assert.Equal(PROPOSER_ID, stored.ProposerUserId);

        SignatureOverrideVote seed = Assert.Single(stored.Votes);
        Assert.Equal(PROPOSER_ID, seed.VoterUserId);
        Assert.Equal(SignatureVoteKind.Approve, seed.VoteKind);
    }

    [Fact]
    public async Task Handle_ExistingPending_ForSameField_ReplacedAndVotesCleared()
    {
        _ = await SeedAiQueryBlockAsync(ORG_ID, WORKFLOW_TYPE_ID);

        var firstPending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = "old-value",
            ProposerUserId = "user-old-proposer",
        };
        _ = _context.PendingSignatureOverrides.Add(firstPending);
        _ = _context.SignatureOverrideVotes.Add(new SignatureOverrideVote
        {
            PendingOverrideId = firstPending.Id,
            VoterUserId = "user-old-proposer",
            VoteKind = SignatureVoteKind.Approve,
        });
        _ = _context.SignatureOverrideVotes.Add(new SignatureOverrideVote
        {
            PendingOverrideId = firstPending.Id,
            VoterUserId = "user-another-voter",
            VoteKind = SignatureVoteKind.Reject,
        });
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new SubmitPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, WORKFLOW_TYPE_ID, FIELD_A, "replacement-value");

        ErrorOr<string> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.NotEqual(firstPending.Id, result.Value);

        List<PendingSignatureOverride> allPending = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
        PendingSignatureOverride only = Assert.Single(allPending);
        Assert.Equal(result.Value, only.Id);
        Assert.Equal("replacement-value", only.ProposedValue);

        List<SignatureOverrideVote> allVotes = await _context.SignatureOverrideVotes
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
        SignatureOverrideVote remaining = Assert.Single(allVotes);
        Assert.Equal(result.Value, remaining.PendingOverrideId);
        Assert.Equal(PROPOSER_ID, remaining.VoterUserId);
        Assert.Equal(SignatureVoteKind.Approve, remaining.VoteKind);
    }

    [Fact]
    public async Task Handle_DefinedButUnreferencedField_ReturnsSignaturesStaleConflict()
    {
        // The field is defined at workflow level but no bearing block's templates
        // carry its {token} anymore — the client raced a staff edit and must refresh.
        _ = await SeedAiQueryBlockAsync(
            ORG_ID, WORKFLOW_TYPE_ID, promptTemplate: "prompt without any token");

        var command = new SubmitPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, WORKFLOW_TYPE_ID, FIELD_A, "x");

        ErrorOr<string> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("Signatures.Stale", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_BlockNoLongerCarriesSignatures_ReturnsSignaturesStale()
    {
        // SwitchCase doesn't implement IHasSignatureFields, so even a template that
        // textually contains the token does not count as a reference. A defined field
        // referenced by no bearing block returns a Stale conflict so the FE refreshes.
        _ = await SeedNonSignatureBlockAsync(ORG_ID, WORKFLOW_TYPE_ID, BlockTypes.SwitchCase);

        var command = new SubmitPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, WORKFLOW_TYPE_ID, FIELD_A, "x");

        ErrorOr<string> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("Signatures.Stale", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_PlaceholderRemovedFromDefinitions_ReturnsValidationError()
    {
        // Race: client opened the page when "old_field" was a defined signature, then
        // staff edited the workflow to remove the definition. By submit time the name
        // is no longer in workflow.SignatureFields. The handler must reject with
        // UnknownField rather than create an orphan pending row.
        _ = await SeedAiQueryBlockAsync(ORG_ID, WORKFLOW_TYPE_ID);

        var command = new SubmitPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, WORKFLOW_TYPE_ID, "old_field_no_longer_defined", "x");

        ErrorOr<string> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("Signatures.UnknownField", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_MergeBlockHappyPath_CreatesPending()
    {
        // Merge blocks also expose signature templates via IHasSignatureFields.
        // Submitting a pending override for a field referenced only by a Merge
        // block must succeed.
        _ = await SeedMergeBlockWithSignatureFieldAsync(ORG_ID, WORKFLOW_TYPE_ID);

        var command = new SubmitPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, WORKFLOW_TYPE_ID, FIELD_A, "merge-proposal");

        ErrorOr<string> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        PendingSignatureOverride stored = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .FirstAsync(p => p.Id == result.Value, TestContext.Current.CancellationToken);

        Assert.Equal(FIELD_A, stored.PlaceholderName);
        Assert.Equal("merge-proposal", stored.ProposedValue);
    }

    [Fact]
    public async Task Handle_UnknownPlaceholder_ReturnsValidationError()
    {
        _ = await SeedAiQueryBlockAsync(ORG_ID, WORKFLOW_TYPE_ID);

        var command = new SubmitPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, WORKFLOW_TYPE_ID, "not_a_declared_field", "x");

        ErrorOr<string> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("Signatures.UnknownField", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_NullProposedValue_CreatesRemovalProposal()
    {
        _ = await SeedAiQueryBlockAsync(ORG_ID, WORKFLOW_TYPE_ID);

        var command = new SubmitPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, WORKFLOW_TYPE_ID, FIELD_A, ProposedValue: null);

        ErrorOr<string> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        PendingSignatureOverride stored = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .FirstAsync(p => p.Id == result.Value, TestContext.Current.CancellationToken);
        Assert.Null(stored.ProposedValue);
    }

    [Fact]
    public async Task Handle_NoAccessJunctionForOrg_ReturnsNotFound()
    {
        // Workflow exists owned by OTHER_ORG_ID (with access junction for OTHER_ORG_ID
        // only). ORG_ID has no junction row — must 404 even though the workflow row
        // is reachable, because the access gate is the primary tenant scope.
        _ = await SeedAiQueryBlockAsync(OTHER_ORG_ID, WORKFLOW_TYPE_ID);

        var command = new SubmitPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, WORKFLOW_TYPE_ID, FIELD_A, "x");

        ErrorOr<string> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_OrgHasAccess_WorkflowOwnedByDifferentOrg_CreatesPending()
    {
        // Direct repro of the shared-row bug shape on the write path: ORG_ID submits
        // against a Workflow row owned by OTHER_ORG_ID; access is granted via
        // junction. Pre-fix this 404'd; post-fix it must create the pending under
        // ORG_ID against the shared row's field definition.
        _ = await SeedAiQueryBlockAsync(OTHER_ORG_ID, WORKFLOW_TYPE_ID);
        _ = _context.WorkflowTypeOrganizationAccess.Add(new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            OrganizationId = ORG_ID,
        });
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new SubmitPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, WORKFLOW_TYPE_ID, FIELD_A, "shared-tenant-proposal");

        ErrorOr<string> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        PendingSignatureOverride stored = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .FirstAsync(p => p.Id == result.Value, TestContext.Current.CancellationToken);

        Assert.Equal(ORG_ID, stored.OrganizationId);
        Assert.Equal(FIELD_A, stored.PlaceholderName);
        Assert.Equal("shared-tenant-proposal", stored.ProposedValue);
    }

    private async Task<Block> SeedAiQueryBlockAsync(
        string orgId,
        string workflowTypeId,
        string promptTemplate = "prompt with {" + FIELD_A + "}")
    {
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = orgId,
            WorkflowTypeId = workflowTypeId,
        };
        _ = new WorkflowSignatureField(root, FIELD_A)
        {
            DisplayName = "Field A",
            Description = "desc",
            DefaultValue = STAFF_DEFAULT_A,
        };
        var section = new Section("Section A", root) { Order = 0 };
        var block = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Block A",
            Properties = new AiQueryProperties { UserPromptTemplate = promptTemplate },
        };

        _ = _context.Workflows.Add(root);
        SeedAccessIfMissing(orgId, workflowTypeId);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return block;
    }

    private async Task<Block> SeedNonSignatureBlockAsync(
        string orgId,
        string workflowTypeId,
        BlockTypes blockType)
    {
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = orgId,
            WorkflowTypeId = workflowTypeId,
        };
        _ = new WorkflowSignatureField(root, FIELD_A)
        {
            DisplayName = "Field A",
            Description = "desc",
            DefaultValue = STAFF_DEFAULT_A,
        };
        var section = new Section("Section A", root) { Order = 0 };
        var block = new Block(blockType, section) { Name = "Block A" };

        _ = _context.Workflows.Add(root);
        SeedAccessIfMissing(orgId, workflowTypeId);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return block;
    }

    private async Task<Block> SeedMergeBlockWithSignatureFieldAsync(
        string orgId,
        string workflowTypeId)
    {
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = orgId,
            WorkflowTypeId = workflowTypeId,
        };
        _ = new WorkflowSignatureField(root, FIELD_A)
        {
            DisplayName = "Field A",
            Description = "desc",
            DefaultValue = STAFF_DEFAULT_A,
        };
        var section = new Section("Section A", root) { Order = 0 };
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Boilerplate Block",
            Properties = new MergeProperties { TextTemplate = "Use {field_a} please." },
        };

        _ = _context.Workflows.Add(root);
        SeedAccessIfMissing(orgId, workflowTypeId);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return block;
    }

    private void SeedAccessIfMissing(string orgId, string workflowTypeId)
    {
        bool tracked = _context.WorkflowTypeOrganizationAccess.Local
            .Any(a => a.WorkflowTypeId == workflowTypeId && a.OrganizationId == orgId);
        if (tracked)
        {
            return;
        }

        bool persisted = _context.WorkflowTypeOrganizationAccess
            .Any(a => a.WorkflowTypeId == workflowTypeId && a.OrganizationId == orgId);
        if (persisted)
        {
            return;
        }

        _ = _context.WorkflowTypeOrganizationAccess.Add(new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = workflowTypeId,
            OrganizationId = orgId,
        });
    }
}
