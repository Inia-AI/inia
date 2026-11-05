using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Specifications.AgentRuntime;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.AgentActivity;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.AgentRuns.GetAgentRunActivity;

public class GetAgentRunActivityQueryHandler
    : IRequestHandler<GetAgentRunActivityQuery, ErrorOr<AgentActivityDigestDto>>
{
    private const string TRACE_PACK_PATH = "trace/trace.ndjson";
    private const int TIDY_BUDGET_SECONDS = 10;

    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly IAgentNarrationTidier? _narrationTidier;

    public GetAgentRunActivityQueryHandler(
        IUserDataUnitOfWork unitOfWork,
        IAgentNarrationTidier? narrationTidier = null)
    {
        _unitOfWork = unitOfWork;
        _narrationTidier = narrationTidier;
    }

    public async Task<ErrorOr<AgentActivityDigestDto>> Handle(
        GetAgentRunActivityQuery request,
        CancellationToken cancellationToken)
    {
        IList<AgentRunTraceFile> files = await _unitOfWork.Repository<AgentRunTraceFile>()
            .GetAllAsync(
                new AgentRunTraceFilesByBlockSpec(request.OrganizationId, request.BlockId),
                cancellationToken);

        IReadOnlyCollection<string>? selectedRuns = files.Count == 0
            ? null
            : await ResolveSelectedRunsAsync(request, cancellationToken);

        // Newest by collection time, not by attempt number: an attempt counts machinery retries
        // inside one invocation and restarts at one every verification round, so ordering on it
        // first answered round one's retry over round two's only pass. Attempt still breaks a
        // tie, which is the single-round case the old order was written for.
        AgentRunTraceFile? tracePack = files
            .Where(file => file.Path == TRACE_PACK_PATH
                && (selectedRuns is null || selectedRuns.Contains(file.RunExecutionId)))
            .OrderByDescending(static file => file.CollectedAt)
            .ThenByDescending(static file => file.Attempt)
            .FirstOrDefault();

        byte[] traceBytes = tracePack?.Content ?? [];
        AgentActivityBeatSource beatSource = ParseBeatSource(request.BeatSource);

        // Tidy everything the agent wrote, whatever the source — its progress notes and the
        // descriptions it wrote for its own tool calls. This used to skip Classified runs,
        // which is now every run, and it used to see the notes only, which left the tool
        // beats reading as workspace chatter the analyst has no use for.
        IReadOnlyDictionary<string, string?>? tidiedNarration = null;
        if (_narrationTidier is not null)
        {
            IReadOnlyList<AgentNarrationLine> narration = AgentActivityProjector.ExtractNarration(traceBytes);
            if (narration.Count > 0)
            {
                // The analyst is holding a poll open on this. The tidier's own budget is 20s
                // and it fails open to raw words, so a Bedrock stall costs a coarser feed
                // rather than a hung request — but only if something above it is stricter
                // than that budget. The live publish path is capped the same way.
                using var tidyBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                tidyBudget.CancelAfter(TimeSpan.FromSeconds(TIDY_BUDGET_SECONDS));
                tidiedNarration = await _narrationTidier.TidyAsync(narration, tidyBudget.Token);
            }
        }

        AgentActivityDigest digest = AgentActivityProjector.Project(
            traceBytes,
            new AgentActivityProjectionOptions { BeatSource = beatSource, TidiedNarration = tidiedNarration });
        return AgentActivityDtoMapper.ToDto(digest);
    }

    /// <summary>
    /// Which runs the caller asked for, or null when it asked for the newest. A run selector is
    /// already the answer; a round resolves through the executed block's run ledger, the only
    /// place a run's round is recorded — a trace row carries none, deliberately, because run
    /// bookkeeping lives on the block. A round the block bought nothing in resolves to an empty
    /// set, which projects an empty digest instead of falling back to another round's run.
    /// <para>
    /// The block read needs no tenant filter of its own: it only ever filters trace rows the
    /// caller's organization already owns, and a run id embeds the workflow execution that
    /// minted it, so a foreign block's ledger can never name one of them. The caller skips this
    /// entirely when the org-scoped read found no trace rows — there is nothing left to filter,
    /// so a block id from another tenant is never read at all.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyCollection<string>?> ResolveSelectedRunsAsync(
        GetAgentRunActivityQuery request,
        CancellationToken cancellationToken)
    {
        if (request.RunExecutionId is { Length: > 0 } runExecutionId)
        {
            return new HashSet<string>([runExecutionId], StringComparer.Ordinal);
        }

        if (request.Round is not { } round)
        {
            return null;
        }

        Block? block = await _unitOfWork.Repository<Block>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == request.BlockId, cancellationToken);

        return new HashSet<string>(
            (block?.Properties as IAgentRunProperties)?.RunLedger
                ?.Where(pass => pass.Round == round)
                .Select(static pass => pass.ExecutionId) ?? [],
            StringComparer.Ordinal);
    }

    private static AgentActivityBeatSource ParseBeatSource(string? requested)
    {
        return Enum.TryParse(requested, ignoreCase: true, out AgentActivityBeatSource parsed)
            && parsed != AgentActivityBeatSource.Auto
            ? parsed
            : AgentActivityBeatSource.Auto;
    }
}
