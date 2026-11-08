using System.Globalization;

namespace IniaAi.WebApi.Filters;

/// <summary>The limit headers of draft-ietf-httpapi-ratelimit-headers-11, which every limited response carries.</summary>
public static class RateLimitHeaders
{
    /// <summary>The header that names the limit and its window.</summary>
    public const string POLICY_HEADER = "RateLimit-Policy";

    /// <summary>The header that gives what is left and the seconds until it resets.</summary>
    public const string LIMIT_HEADER = "RateLimit";

    /// <summary>The limit and what is left of it.</summary>
    public static void Write(HttpResponse response, string policyName, int quota, int windowSeconds, int remaining, long secondsToReset)
    {
        response.Headers[POLICY_HEADER] = $"\"{policyName}\";q={quota};w={windowSeconds}";
        response.Headers[LIMIT_HEADER] = $"\"{policyName}\";r={remaining};t={secondsToReset}";
    }

    /// <summary>The refusal's headers: nothing left, and <c>Retry-After</c> with the seconds until one more fits.</summary>
    public static void WriteRefusal(HttpResponse response, string policyName, int quota, int windowSeconds, long secondsToReset)
    {
        Write(response, policyName, quota, windowSeconds, remaining: 0, secondsToReset);
        WriteRetryAfter(response, secondsToReset);
    }

    /// <summary><c>Retry-After</c>: the seconds until the caller should try again.</summary>
    public static void WriteRetryAfter(HttpResponse response, long seconds)
    {
        response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
    }
}
