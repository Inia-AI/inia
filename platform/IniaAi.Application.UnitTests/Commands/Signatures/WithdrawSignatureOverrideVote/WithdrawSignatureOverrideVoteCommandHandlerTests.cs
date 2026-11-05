using ErrorOr;
using IniaAi.Application.Commands.Signatures.WithdrawSignatureOverrideVote;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.Signatures.WithdrawSignatureOverrideVote;

public class WithdrawSignatureOverrideVoteCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string PROPOSER_ID = "user-proposer";
    private const string REVIEWER_ID = "user-reviewer";
    private const string OTHER_REVIEWER_ID = "user-other";
    private const string WORKFLOW_TYPE_ID = "WT-TestSignatures";
    private const string BLOCK_ID = "block-a";
    private const string FIELD_A = "field_a";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly WithdrawSignatureOverrideVoteCommandHandler _handler;

    public WithdrawSignatureOverrideVoteCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();

        ICoreUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        _handler = new WithdrawSignatureOverrideVoteCommandHandler(
            unitOfWork,
            NullLogger<WithdrawSignatureOverrideVoteCommandHandler>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_ReviewerHasReject_RemovesVote_LeavesPendingAndProposerApprove()
    {
        PendingSignatureOverride pending = await SeedPendingWithProposerAndRejectAsync();

        var command = new WithdrawSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, pending.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        PendingSignatureOverride stored = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .Include(p => p.Votes)
            .FirstAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken);

        SignatureOverrideVote only = Assert.Single(stored.Votes);
        Assert.Equal(PROPOSER_ID, only.VoterUserId);
        Assert.Equal(SignatureVoteKind.Approve, only.VoteKind);
    }

    [Fact]
    public async Task Handle_ProposerCannotWithdraw_ReturnsValidation()
    {
        PendingSignatureOverride pending = await SeedPendingWithProposerAndRejectAsync();

        var command = new WithdrawSignatureOverrideVoteCommand(
            ORG_ID, PROPOSER_ID, pending.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("Signatures.ProposerCannotWithdraw", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_NoPriorVote_ReturnsNotFound()
    {
        PendingSignatureOverride pending = await SeedPendingWithProposerAndRejectAsync();

        var command = new WithdrawSignatureOverrideVoteCommand(
            ORG_ID, OTHER_REVIEWER_ID, pending.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("SignatureOverrideVote.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_PendingNotFound_ReturnsNotFound()
    {
        var command = new WithdrawSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, PendingOverrideId: "does-not-exist");

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_PendingInDifferentOrg_ReturnsNotFound()
    {
        PendingSignatureOverride pending = await SeedPendingWithProposerAndRejectAsync();

        var command = new WithdrawSignatureOverrideVoteCommand(
            OrganizationId: "other-org", REVIEWER_ID, pending.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    private async Task<PendingSignatureOverride> SeedPendingWithProposerAndRejectAsync()
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
