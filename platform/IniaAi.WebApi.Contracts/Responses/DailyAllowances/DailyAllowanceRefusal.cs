namespace IniaAi.WebApi.Models.Responses.DailyAllowances;

/// <summary>The body of a request refused because the user's daily allowance of its kind is used.</summary>
/// <param name="Error">What the user reads.</param>
/// <param name="Code">The machine-readable code, <c>DailyAllowance.Exhausted</c>.</param>
/// <param name="ResetsAt">The next midnight UTC, when the allowance resets.</param>
public sealed record DailyAllowanceRefusal(string Error, string Code, DateTime ResetsAt);
