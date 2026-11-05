using System.Reflection;

namespace IniaAi.WebApi;

/// <summary>
/// Provides versioning and build metadata about the application.
/// </summary>
public static class ApplicationVersion
{
    /// <summary>
    /// Semantic version or assembly version.
    /// </summary>
    public static string Version =>
        typeof(ApplicationVersion).Assembly.GetName().Version?.ToString() ?? "Unknown";

    /// <summary>
    /// Informational version (typically includes semantic version + metadata).
    /// </summary>
    public static string InformationalVersion =>
        typeof(ApplicationVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";

    /// <summary>
    /// Commit SHA from which the app was built.
    /// </summary>
    public static string CommitSha =>
        typeof(ApplicationVersion).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => attr.Key == "CommitSha")?.Value ?? "Unknown";

    /// <summary>
    /// Git branch name used during build.
    /// </summary>
    public static string Branch =>
        typeof(ApplicationVersion).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => attr.Key == "Branch")?.Value ?? "Unknown";

    /// <summary>
    /// UTC build timestamp.
    /// </summary>
    public static DateTime BuildDateUtc =>
        DateTime.TryParse(
            typeof(ApplicationVersion).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attr => attr.Key == "BuildDateUtc")?.Value,
            out DateTime dt) ? dt : DateTime.MinValue;
}
