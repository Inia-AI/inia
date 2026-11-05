using System.Globalization;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// An organization's workflow agent spend today (UTC) against its daily batches: one batch, plus
/// those users unlocked today. A message waiting or running counts at its per-message cap until
/// it settles, so messages queued behind a busy conversation cannot pass the warning unseen.
/// </summary>
public sealed record EditorAgentDailySpend(decimal SpentUsd, decimal CommittedUsd, decimal BatchUsd, int Batches)
{
    public decimal UsedUsd => SpentUsd + CommittedUsd;

    public bool IsUsedUp => UsedUsd >= BatchUsd * Batches;

    /// <summary>The batches that bring the day's allowance one batch above what is used.</summary>
    public int BatchesToUnlock => Math.Max(1, (int)decimal.Floor(UsedUsd / BatchUsd) + 1 - Batches);

    public static async Task<EditorAgentDailySpend> ReadAsync(
        IUserDataUnitOfWork userData,
        string organizationId,
        decimal defaultBatchUsd,
        decimal perMessageUsd,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(nowUtc);
        DateTime dayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        decimal? batchUsd = await userData.Repository<Organization>()
            .GetAll()
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(static organization => organization.EditorAgentDailySpendBatchUsd)
            .FirstOrDefaultAsync(cancellationToken);

        int unlocked = await userData.Repository<EditorAgentSpendUnlock>()
            .GetAll()
            .AsNoTracking()
            .Where(unlock => unlock.OrganizationId == organizationId && unlock.Day == today)
            .SumAsync(static unlock => unlock.Batches, cancellationToken);

        decimal spentUsd = await userData.Repository<Domain.Entities.TokenUsage.TokenUsage>()
            .GetAll()
            .AsNoTracking()
            .Where(usage => usage.OrganizationId == organizationId
                && usage.UsageType == UsageType.LlmEditorAgentChat
                && usage.CreatedAt >= dayStart)
            .SelectMany(static usage => usage.Components)
            .SumAsync(static component => (decimal?)component.Cost, cancellationToken) ?? 0m;

        int inFlight = await userData.Repository<AgentConversationTurn>()
            .GetAll()
            .AsNoTracking()
            .CountAsync(
                turn => (turn.Status == AgentConversationTurnStatus.Queued || turn.Status == AgentConversationTurnStatus.Running)
                    && turn.Conversation!.OrganizationId == organizationId
                    && turn.Conversation.Purpose == WorkflowEditorAgentService.PURPOSE,
                cancellationToken);

        return new EditorAgentDailySpend(spentUsd, inFlight * perMessageUsd, batchUsd ?? defaultBatchUsd, 1 + unlocked);
    }
}

public static class DailySpendText
{
    /// <summary>A dollar amount as the panel and its warning write it: "$50", "$12.50".</summary>
    public static string Usd(decimal amount)
    {
        return "$" + amount.ToString(amount == decimal.Truncate(amount) ? "0" : "0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>The warning shown when today's batches are used, with the day's spend so far.</summary>
    public static string Warning(decimal usedUsd, decimal batchUsd)
    {
        return $"Your organization's workflow agent spend today, counting messages still running, is {Usd(usedUsd)}, "
            + $"past its daily {Usd(batchUsd)}. Continue with another {Usd(batchUsd)}? Your name is recorded against it.";
    }
}
