using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using ContractEnums = IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.AgentActivity;

namespace IniaAi.Application.Queries.AgentRuns.GetAgentRunActivity;

/// <summary>
/// Maps the shared projection model onto the wire DTOs. The enum mirrors are matched by
/// name so an addition on one side fails loudly here instead of serializing as a wrong
/// value.
/// </summary>
public static class AgentActivityDtoMapper
{
    public static AgentActivityDigestDto ToDto(AgentActivityDigest digest)
    {
        return new AgentActivityDigestDto
        {
            SchemaVersion = digest.SchemaVersion,
            Outcome = MapByName<AgentActivityOutcome, ContractEnums.AgentActivityOutcome>(digest.Outcome),
            StartedAtUtc = digest.StartedAtUtc,
            Phases = [.. digest.Phases.Select(static phase => new AgentActivityPhaseDto
            {
                Label = phase.Label,
                GapLabel = phase.GapLabel,
            })],
            Beats = [.. digest.Beats.Select(static beat => new AgentActivityBeatDto
            {
                AtSeconds = beat.AtSeconds,
                PhaseIndex = beat.PhaseIndex,
                Kind = MapByName<AgentActivityBeatKind, ContractEnums.AgentActivityBeatKind>(beat.Kind),
                Label = beat.Label,
                Detail = beat.Detail,
            })],
            Receipt = new AgentActivityReceiptDto
            {
                DurationSeconds = digest.Receipt.DurationSeconds,
                DocumentNames = [.. digest.Receipt.DocumentNames],
                SkillNames = [.. digest.Receipt.SkillNames],
                PagesViewed = digest.Receipt.PagesViewed,
                CodeRuns = digest.Receipt.CodeRuns,
                ChecksReported = digest.Receipt.ChecksReported,
            },
        };
    }

    private static TTarget MapByName<TSource, TTarget>(TSource value)
        where TSource : struct, Enum
        where TTarget : struct, Enum
    {
        return Enum.Parse<TTarget>(value.ToString());
    }
}
