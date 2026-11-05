using System.Text.RegularExpressions;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Mints and validates sidecar execution ids. The sidecar accepts exactly
/// <c>^[A-Za-z0-9._-]{1,128}$</c>; anything else must fail loudly before any HTTP leaves the
/// process — URL-escaping an illegal id would turn the sidecar's 400 into a silent
/// GET/POST path disagreement instead.
/// </summary>
public static partial class AgentRunIds
{
    public const int MAX_LENGTH = 128;
    public const int FIRST_ROUND = 1;
    public const int FIRST_PASS = 1;

    /// <summary>
    /// The allocated role letters, listed together because their uniqueness is the invariant:
    /// two legs sharing a letter (with the same round and pass) would collide on the runtime's
    /// frozen-terminal registry and replay each other's answers. A new leg kind allocates the
    /// next letter here, never privately — and names it in <see cref="RoleNameOf"/> in the same
    /// change, so no consumer ever meets a letter without a name.
    /// </summary>
    public const string WORKER_ROLE = "w";
    public const string VERIFIER_ROLE = "v";

    /// <summary>
    /// The named values public contracts carry for a leg's role. The letters above
    /// are an internal id alphabet; exposing them made every consumer re-declare the alphabet
    /// and map everything it did not know to a worker.
    /// </summary>
    public const string WORKER_ROLE_NAME = "worker";
    public const string VERIFIER_ROLE_NAME = "verifier";
    private const char COMPONENT_SEPARATOR = '_';
    private const char ROUND_PREFIX = 'r';
    private const char ILLEGAL_CHARACTER_REPLACEMENT = '-';
    private const char CONVERSATION_SEPARATOR = '-';
    private const char CONVERSATION_TURN_PREFIX = 't';
    private const string CONVERSATION_PASS_PREFIX = "-p";

    /// <summary>What follows a conversation turn's first-pass run id in the ids of its later passes.</summary>
    public const string CONVERSATION_PASS_SEPARATOR = CONVERSATION_PASS_PREFIX;

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$")]
    private static partial Regex ValidExecutionIdRegex();

    public static bool IsValid(string? executionId)
    {
        return executionId is not null && ValidExecutionIdRegex().IsMatch(executionId);
    }

    /// <summary>
    /// The one owner of the letter-to-name mapping. An unallocated letter passes through
    /// verbatim — never silently renamed to a worker — so a consumer that meets one can show
    /// that it does not recognise it.
    /// </summary>
    public static string RoleNameOf(string roleLetter)
    {
        return roleLetter switch
        {
            WORKER_ROLE => WORKER_ROLE_NAME,
            VERIFIER_ROLE => VERIFIER_ROLE_NAME,
            _ => roleLetter,
        };
    }

    /// <summary>
    /// Builds the canonical run id
    /// <c>{workflowExecutionId}_{stableBlockId}_{role}_r{round}_{pass}</c>, sanitising each
    /// component (illegal characters become '-') and enforcing the sidecar's length limit. The
    /// round is the verification-leg axis, the pass belongs to within-invocation machinery
    /// retries, and a distinct round mints a distinct id so the runtime's frozen-terminal
    /// registry can never replay an earlier leg's answer. Unsalvageable input — an empty
    /// component, an out-of-range round or pass, or a composed id over 128 characters — throws before
    /// any HTTP can be attempted; truncation is never applied because truncated ids can
    /// collide across runs.
    /// </summary>
    public static string For(string workflowExecutionId, string stableBlockId, string role, int round, int pass)
    {
        if (round < FIRST_ROUND)
        {
            throw new ArgumentOutOfRangeException(nameof(round), round, "Verification rounds count from one");
        }

        if (pass < FIRST_PASS)
        {
            throw new ArgumentOutOfRangeException(nameof(pass), pass, "Run passes count from one");
        }

        string executionId = string.Join(
            COMPONENT_SEPARATOR,
            Sanitize(workflowExecutionId, nameof(workflowExecutionId)),
            Sanitize(stableBlockId, nameof(stableBlockId)),
            Sanitize(role, nameof(role)),
            ROUND_PREFIX + round.ToString(System.Globalization.CultureInfo.InvariantCulture),
            pass.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (executionId.Length > MAX_LENGTH)
        {
            throw new ArgumentException(
                $"Composed run id is {executionId.Length} characters; the sidecar accepts at most {MAX_LENGTH}. " +
                "Shorten the components instead of truncating — truncated ids can collide.");
        }

        return executionId;
    }

    /// <summary>
    /// Builds the run id of one conversation turn,
    /// <c>{purpose}-{conversationId}-t{turn}</c>, with <c>-p{pass}</c> appended from the second
    /// pass on. Every run a conversation buys starts with <see cref="ConversationRunPrefix"/>, so
    /// its evidence rows can be found by prefix when the conversation is deleted.
    /// </summary>
    public static string ForConversationTurn(string purpose, string conversationId, int turn, int pass)
    {
        if (turn < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(turn), turn, "Conversation turns count from one");
        }

        if (pass < FIRST_PASS)
        {
            throw new ArgumentOutOfRangeException(nameof(pass), pass, "Run passes count from one");
        }

        string executionId = ConversationRunPrefix(purpose, conversationId)
            + turn.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (pass == FIRST_PASS
                ? string.Empty
                : CONVERSATION_PASS_PREFIX + pass.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (executionId.Length > MAX_LENGTH)
        {
            throw new ArgumentException(
                $"Composed run id is {executionId.Length} characters; the sidecar accepts at most {MAX_LENGTH}.");
        }

        return executionId;
    }

    /// <summary>The prefix every run id of one conversation starts with.</summary>
    public static string ConversationRunPrefix(string purpose, string conversationId)
    {
        return Sanitize(purpose, nameof(purpose))
            + CONVERSATION_SEPARATOR
            + Sanitize(conversationId, nameof(conversationId))
            + CONVERSATION_SEPARATOR
            + CONVERSATION_TURN_PREFIX;
    }

    private static string Sanitize(string component, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            throw new ArgumentException("Run id components must be non-empty", parameterName);
        }

        char[] sanitized = component.ToCharArray();
        for (int index = 0; index < sanitized.Length; index++)
        {
            if (!IsLegalCharacter(sanitized[index]))
            {
                sanitized[index] = ILLEGAL_CHARACTER_REPLACEMENT;
            }
        }

        return new string(sanitized);
    }

    private static bool IsLegalCharacter(char candidate)
    {
        return candidate
            is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_';
    }
}
