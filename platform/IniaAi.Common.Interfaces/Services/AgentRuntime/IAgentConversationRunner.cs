using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Runs one turn of an <see cref="AgentConversation"/> on the agent runtime: the shared layer's
/// conversation capability, usable by any consumer that holds a conversation with an agent. It
/// owns the turn's run identity, the continuation from the previous turn's session bundle (with
/// a fresh start that replays the transcript when there is nothing to resume), bundle custody,
/// the live activity feed, the spend record and the turn row's status. The consumer supplies
/// what the run reads: prompt, documents, skills and model.
/// </summary>
public interface IAgentConversationRunner
{
    /// <summary>
    /// Runs the turn to its end and returns how it settled. The turn row must exist with status
    /// Queued. Never throws: a run that failed, and any fault of the runner's own (loading the
    /// turn, reading the session bundle, writing the row), settles the turn Failed with the
    /// spend so far recorded. The one exception is a cancelled token, which cancels the run,
    /// records the spend so far, marks the turn Cancelled and then propagates the cancellation.
    /// The row is written only while it is still in flight, so a turn another party settled
    /// first keeps its verdict and only gains spend it was missing.
    /// </summary>
    Task<AgentConversationTurnOutcome> RunTurnAsync(
        AgentConversationTurnRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// What one turn's run reads, and how its spend is billed.
/// </summary>
/// <param name="TurnId">The Queued turn row this run answers.</param>
/// <param name="Prompt">The turn's message as the agent reads it.</param>
public sealed record AgentConversationTurnRequest(string TurnId, string Prompt)
{
    public string? SystemPrompt { get; init; }

    public IReadOnlyList<AgentRunDocument> Documents { get; init; } = [];

    public IReadOnlyList<AgentRunSkillFile> SkillFiles { get; init; } = [];

    public long? DeadlineMs { get; init; }

    public decimal? MaxBudgetUsd { get; init; }

    public int? MaxTurns { get; init; }

    /// <summary>How long the turn waits out capacity refusals before it fails.</summary>
    public TimeSpan AdmissionPatience { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Whether the turn may continue the previous turn's session. False starts every turn fresh
    /// with the transcript in its prompt and keeps no session bundle.
    /// </summary>
    public bool ResumeSession { get; init; } = true;

    public required AgentConversationBilling Billing { get; init; }

    /// <summary>
    /// The consumer's look at each round of the turn that completed: it reads the answer and the
    /// files the agent wrote, and decides whether the agent goes on within the same turn, with a
    /// message of the consumer's own. Null ends the turn on its first answer.
    /// <para>
    /// A further round continues the session of the round before it when the request resumes
    /// sessions; otherwise, or when that session cannot be resumed, it starts fresh with the
    /// turn's message and the consumer's message after it, and the earlier round's files are gone. It runs within what is left of
    /// <see cref="MaxBudgetUsd"/> and <see cref="DeadlineMs"/>, and none starts with less than a
    /// minimum of either left; a round that fails leaves the turn
    /// with the previous round's answer. The turn's answer is the last completed round's.
    /// </para>
    /// </summary>
    public Func<AgentConversationRound, CancellationToken, Task<AgentConversationRoundReview>>? ReviewRound { get; init; }

    /// <summary>How many rounds one turn may run, the first included.</summary>
    public int MaxRounds { get; init; } = 1;

    /// <summary>
    /// How the turn's steps are named and how often the live feed refreshes; the settled steps
    /// are named the same way. Null keeps the shared defaults.
    /// </summary>
    public AgentRunLiveActivityOptions? LiveActivity { get; init; }
}

/// <summary>A completed round of a turn, as <see cref="AgentConversationTurnRequest.ReviewRound"/> sees it.</summary>
/// <param name="Round">The round, counting from one.</param>
/// <param name="IsLastRound">True when no further round can run: the round cap, the budget or the
/// deadline is reached. The review's verdict on it is final.</param>
/// <param name="OutFiles">The files under <c>out/</c> when the round ended, paths without the prefix.</param>
public sealed record AgentConversationRound(
    int Round,
    bool IsLastRound,
    string Answer,
    IReadOnlyList<AgentRunRecarriedFile> OutFiles);

/// <param name="ContinueWith">The message the agent reads next, within the same turn; null ends the turn.</param>
public sealed record AgentConversationRoundReview(string? ContinueWith)
{
    public static AgentConversationRoundReview Finish { get; } = new((string?)null);

    /// <summary>
    /// Documents staged beside the turn's own for the next round, such as the round's work for a
    /// round that has to start fresh. Their names must not collide with the turn's documents.
    /// </summary>
    public IReadOnlyList<AgentRunDocument> Documents { get; init; } = [];

    public static AgentConversationRoundReview Continue(string message, IReadOnlyList<AgentRunDocument>? documents = null)
    {
        return new AgentConversationRoundReview(message) { Documents = documents ?? [] };
    }
}

/// <summary>
/// The usage record dimensions of a conversation's spend: recorded per organization and user,
/// under the consumer's usage type, like any workflow run.
/// </summary>
public sealed record AgentConversationBilling(
    UsageType UsageType,
    TokenExecutionContext ExecutionContext,
    string Description)
{
    public string? WorkflowTypeId { get; init; }
}

/// <param name="ResumedSession">True when the turn continued the previous turn's session.</param>
public sealed record AgentConversationTurnOutcome(
    AgentConversationTurnStatus Status,
    string? AnswerMarkdown,
    decimal CostUsd,
    string? Error,
    bool ResumedSession);
