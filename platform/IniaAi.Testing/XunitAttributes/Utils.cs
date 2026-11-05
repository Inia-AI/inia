using System.Runtime.InteropServices;

namespace IniaAi.Testing.XunitAttributes;

public static class Utils
{
    public static bool IsRunningOnCI()
    {
        return Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null
            || Environment.GetEnvironmentVariable("SYSTEM_DEFINITIONID") is not null
            || Environment.GetEnvironmentVariable("TEAMCITY_VERSION") is not null;
    }

    public static bool IsRunningOnMac()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    public static bool IsRunningOnWindows()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    public static bool IsRunningOnLinux()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }
}
