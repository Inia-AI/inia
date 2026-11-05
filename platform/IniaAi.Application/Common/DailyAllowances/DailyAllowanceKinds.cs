using System.Collections.Frozen;
using System.Globalization;
using IniaAi.Domain.Entities.DailyAllowances;

namespace IniaAi.Application.Common.DailyAllowances;

/// <param name="PolicyName">The name the limit headers give the kind.</param>
/// <param name="ExhaustedMessageForOne">The refusal the user reads when the limit is 1.</param>
/// <param name="ExhaustedMessage">The refusal the user reads for any other limit, with the limit as <c>{0}</c>.</param>
public sealed record DailyAllowanceKindDefinition(string PolicyName, string ExhaustedMessageForOne, string ExhaustedMessage)
{
    public string ExhaustedMessageFor(int limit)
    {
        return limit == 1 ? ExhaustedMessageForOne : string.Format(CultureInfo.InvariantCulture, ExhaustedMessage, limit);
    }
}

/// <summary>What the API says about each kind of counted request. A new kind is one entry here.</summary>
public static class DailyAllowanceKinds
{
    public static readonly FrozenDictionary<DailyAllowanceKind, DailyAllowanceKindDefinition> All =
        new Dictionary<DailyAllowanceKind, DailyAllowanceKindDefinition>
        {
            [DailyAllowanceKind.Explore] = new(
                "explore",
                "You have used your 1 daily explore question. You can ask more from midnight UTC.",
                "You have used your {0} daily explore questions. You can ask more from midnight UTC."),
            [DailyAllowanceKind.Refine] = new(
                "refine",
                "You have used your 1 daily refine request. You can refine again from midnight UTC.",
                "You have used your {0} daily refine requests. You can refine again from midnight UTC."),
            [DailyAllowanceKind.HelpRequest] = new(
                "help-request",
                "You have filed your 1 daily help request. You can file another from midnight UTC, or reply to the request you filed.",
                "You have filed your {0} daily help requests. You can file more from midnight UTC, or reply to a request you filed."),
            [DailyAllowanceKind.HelpRequestReply] = new(
                "help-request-reply",
                "You have sent your 1 daily reply to support. You can reply again from midnight UTC.",
                "You have sent your {0} daily replies to support. You can reply again from midnight UTC."),
        }.ToFrozenDictionary();
}
