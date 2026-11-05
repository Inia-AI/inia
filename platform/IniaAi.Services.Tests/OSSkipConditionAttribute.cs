using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IniaAi.Services.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class OSSkipFactAttribute : FactAttribute
{
    public OSSkipFactAttribute(
        string[] skipOn,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        foreach (string os in skipOn)
        {
            if (os.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase) &&
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Skip = "Skipped on Windows.";
            }

            if (os.Equals("LINUX", StringComparison.OrdinalIgnoreCase) &&
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Skip = "Skipped on Linux.";
            }

            if (os.Equals("OSX", StringComparison.OrdinalIgnoreCase) &&
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Skip = "Skipped on macOS.";
            }
        }
    }
}
