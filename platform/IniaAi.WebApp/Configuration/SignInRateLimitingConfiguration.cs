using System.Threading.RateLimiting;
using IniaAi.Configuration.Security;

namespace IniaAi.WebApp.Configuration;

public static class SignInRateLimitingConfiguration
{
    private const string TOO_MANY_REQUESTS = "Too many attempts from your network. Wait a minute, then try again.";

    /// <summary>
    /// Adds the sign-in policy the API uses, sized by the same <c>RateLimiting:SignIn</c> settings:
    /// a fixed window per client and page, for the pages that take a credential or send a mail.
    /// </summary>
    public static IServiceCollection AddSignInRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        SignInRateLimitSettings settings = SignInRateLimitSettings.From(configuration);

        _ = services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = settings.WindowSeconds.ToString();
                return new ValueTask(context.HttpContext.Response.WriteAsync(TOO_MANY_REQUESTS, cancellationToken));
            };

            options.AddPolicy(SignInRateLimitSettings.POLICY_NAME, context => RateLimitPartition.GetFixedWindowLimiter(
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
