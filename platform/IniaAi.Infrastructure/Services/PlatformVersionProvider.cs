using System.Reflection;
using IniaAi.Application.Common.Interfaces.Services;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Resolves the platform version from the entry assembly's informational version attribute,
/// which the host csproj stamps as "1.0.0+$(SourceRevisionId)" when CI provides the revision.
/// </summary>
public class PlatformVersionProvider : IPlatformVersionProvider
{
    private const string UNKNOWN_VERSION = "Unknown";

    public string PlatformVersion { get; } =
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? UNKNOWN_VERSION;
}
