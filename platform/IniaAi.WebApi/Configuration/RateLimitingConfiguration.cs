using System.Globalization;
using System.Threading.RateLimiting;
using IniaAi.Configuration.Security;
using Microsoft.AspNetCore.RateLimiting;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Limits how often one client may call each endpoint that takes a credential or sends a mail, and
/// answers every refused request of the host's policies with 429 and the wait its own window sets.
/// The account lockout bounds guesses at one account; this bounds guesses across accounts.
/// </summary>
public static class RateLimitingConfiguration
{
    /// <summary>The policy every sign-in and password-mail endpoint names.</summary>
    public const string SIGN_IN_POLICY = SignInRateLimitSettings.POLICY_NAME;

    private static readonly string _logCategory = typeof(RateLimitingConfiguration).FullName!;

    /// <summary>
    /// Adds the sign-in policy: a fixed window per client and endpoint, sized by
    /// <c>RateLimiting:SignIn</c>. One customer's users often share one address, and the token
    /// endpoint also serves refreshes, so the default leaves room for a busy office.
    /// </summary>
    public static IServiceCollection AddSignInRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        SignInRateLimitSettings settings = SignInRateLimitSettings.From(configuration);

        _ = services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                int retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : settings.WindowSeconds;
                context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                // The framework logs a refusal only at Debug, which no deployment keeps.
                Endpoint? endpoint = context.HttpContext.GetEndpoint();
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(_logCategory).LogWarning(
                    "Request refused by the {Policy} rate limit: client {ClientAddress}, endpoint {EndpointName}",
                    endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName,
                    context.HttpContext.Connection.RemoteIpAddress,
                    endpoint?.DisplayName);
                return ValueTask.CompletedTask;
            };

            options.AddPolicy(SIGN_IN_POLICY, context => RateLimitPartition.GetFixedWindowLimiter(
                SignInRateLimitSettings.PartitionKey(context.Connection.RemoteIpAddress, context.GetEndpoint()?.DisplayName),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                    QueueLimit = 0,
                }));
        });

        return services;
    }
}
