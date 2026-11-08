using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Application.Errors;

/// <summary>An error the API answers with 503: an outside service the request needs did not answer, or is closed for now.</summary>
public static class ServiceUnavailableError
{
    /// <summary>The <see cref="ErrorType"/> value of an error the API answers with 503.</summary>
    public const int ERROR_TYPE = 503;

    public static Error Of(string code, string description)
    {
        return Error.Custom(ERROR_TYPE, code, description);
    }

    /// <summary>
    /// An error the API answers with 503 and a <c>Retry-After</c> of <paramref name="retryAfter"/>, which
    /// rides under the agent runtime's retry-after key so a caller reads both the same way.
    /// </summary>
    public static Error Of(string code, string description, TimeSpan retryAfter)
    {
        return Error.Custom(ERROR_TYPE, code, description, new Dictionary<string, object> { [AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY] = retryAfter });
    }

    /// <summary>How long the caller should wait before it tries again, when the error says.</summary>
    public static TimeSpan? RetryAfterOf(Error error)
    {
        return error.Metadata?.TryGetValue(AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY, out object? retryAfter) == true && retryAfter is TimeSpan wait
            ? wait
            : null;
    }
}
