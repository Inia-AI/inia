using System.Reflection;

namespace IniaAi.WebApp;

public static class ApplicationVersion
{
    public static string Version => typeof(ApplicationVersion).Assembly.GetName().Version?.ToString() ?? "Unknown";
    public static string InformationalVersion => typeof(ApplicationVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";
}
