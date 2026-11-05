using ErrorOr;
using IniaAi.Application.Commands.Signatures.CancelPendingSignatureOverride;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.Signatures.CancelPendingSignatureOverride;

public class CancelPendingSignatureOverrideCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string PROPOSER_ID = "user-proposer";
    private const string REVIEWER_ID = "user-reviewer";
    private const string WORKFLOW_TYPE_ID = "WT-TestSignatures";
    private const string BLOCK_ID = "block-a";
    private const string FIELD_A = "field_a";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly CancelPendingSignatureOverrideCommandHandler _handler;

    public CancelPendingSignatureOverrideCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();

        ICoreUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        _handler = new CancelPendingSignatureOverrideCommandHandler(
            unitOfWork,
            NullLogger<CancelPendingSignatureOverrideCommandHandler>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_Proposer_DeletesPendingAndCascadesVotes()
    {
        PendingSignatureOverride pending = await SeedPendingWithVotesAsync();

        var command = new CancelPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, pending.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        bool pendingExists = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken);
        Assert.False(pendingExists);

        bool votesExist = await _context.SignatureOverrideVotes
            .AsNoTracking()
            .AnyAsync(v => v.PendingOverrideId == pending.Id, TestContext.Current.CancellationToken);
        Assert.False(votesExist);
    }

    [Fact]
    public async Task Handle_NonProposer_ReturnsForbidden_PendingStays()
    {
        PendingSignatureOverride pending = await SeedPendingWithVotesAsync();

        var command = new CancelPendingSignatureOverrideCommand(
            ORG_ID, REVIEWER_ID, pending.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
        Assert.Equal("Signatures.NotProposer", result.FirstError.Code);

        bool pendingExists = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken);
        Assert.True(pendingExists);
    }

    [Fact]
    public async Task Handle_PendingNotFound_ReturnsNotFound()
    {
        var command = new CancelPendingSignatureOverrideCommand(
            ORG_ID, PROPOSER_ID, PendingOverrideId: "does-not-exist");

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_PendingInDifferentOrg_ReturnsNotFound()
    {
        PendingSignatureOverride pending = await SeedPendingWithVotesAsync();

        var command = new CancelPendingSignatureOverrideCommand(
            OrganizationId: "other-org", PROPOSER_ID, pending.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    private async Task<PendingSignatureOverride> SeedPendingWithVotesAsync()
    {
        var pending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = "value",
            ProposerUserId = PROPOSER_ID,
        };

        var proposerApprove = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = PROPOSER_ID,
            VoteKind = SignatureVoteKind.Approve,
        };
        var reviewerReject = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = REVIEWER_ID,
            VoteKind = SignatureVoteKind.Reject,
            Comment = "no",
        };

        _ = _context.PendingSignatureOverrides.Add(pending);
        _context.SignatureOverrideVotes.AddRange(proposerApprove, reviewerReject);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return pending;
    }
}
