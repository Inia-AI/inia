namespace IniaAi.Application.Common.RateLimits;

/// <summary>
/// What a 429 says in its headers: the policy's name, its quota and window, and the seconds until
/// one more request fits. An error carries it in its metadata under <see cref="METADATA_KEY"/>; the
/// API writes it as the <c>Retry-After</c>, <c>RateLimit-Policy</c> and <c>RateLimit</c> headers, not in the body.
/// </summary>
public sealed record RateLimitRefusal(string PolicyName, int Quota, int WindowSeconds, long RetryAfterSeconds)
{
    /// <summary>The <see cref="ErrorOr.ErrorType"/> value of an error the API answers with 429.</summary>
    public const int ERROR_TYPE = 429;

    public const string METADATA_KEY = "rateLimit";
}
