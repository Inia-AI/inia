using IniaAi.AiConnection.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Testing.DependencyInjection.Mocks;

public class NoOpLimiter : TokenSlidingWindowRateLimiter
{
    public NoOpLimiter() : base(
        int.MaxValue,
        TimeSpan.Zero,
        new NullLogger<TokenSlidingWindowRateLimiter>())
    { }

    public static new Task WaitForPermissionAsync(int _, CancellationToken __)
    {
        // Instantly grant permission
        return Task.CompletedTask;
    }

    public static new void UpdateServerLimits(int _, DateTime __, int? ___ = null)
    {
        // No-op
    }
}
