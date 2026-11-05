using IniaAi.Common;
using IniaAi.Domain.Entities.Validation;

namespace IniaAi.Domain.Common;

/// <summary>
/// How a deployment names itself on cross-instance artifacts — the <c>SourceInstance</c> in
/// exported validation case bundles and the label on workflow-version currency cards. One
/// resolution chain for every consumer, so one deployment answers to one name: the configured
/// <c>PlatformSettings:InstanceName</c>, else the caller's ambient fallback (the request host
/// for an HTTP export), else the machine name. Truncated to the bundle column, the narrowest
/// store of the value.
/// </summary>
public static class InstanceIdentity
{
    public static string Resolve(string? configuredInstanceName, string? ambientFallback)
    {
        return (configuredInstanceName.NullIfWhiteSpace()
                ?? ambientFallback.NullIfWhiteSpace()
                ?? Environment.MachineName)
            .Truncate(ValidationCase.MAX_SOURCE_INSTANCE_LENGTH);
    }
}
