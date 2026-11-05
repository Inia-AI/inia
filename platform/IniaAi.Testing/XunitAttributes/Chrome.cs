namespace IniaAi.Testing.XunitAttributes;

/// <summary>
/// Says whether a test that starts Google Chrome can run here. CI skips such tests. Off CI they run
/// when Chrome is installed, and skip with what to install when it is not.
/// </summary>
internal static class Chrome
{
    private static readonly string[] _commandNames = ["google-chrome", "google-chrome-stable"];

    private static readonly Lazy<bool> _installed = new(IsInstalled);

    internal static string? SkipReason()
    {
        bool onCi = Utils.IsRunningOnCI();

        return SkipReason(onCi, onCi || _installed.Value);
    }

    internal static string? SkipReason(bool onCi, bool installed)
    {
        if (onCi)
        {
            return "Ignored in CI: starts Google Chrome through ChromeDriver.";
        }

        return installed ? null : "Starts Google Chrome, which this machine does not have. Install Google Chrome to run it.";
    }

    private static bool IsInstalled()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        const string WINDOWS_CHROME = @"Google\Chrome\Application\chrome.exe";
        const string MAC_CHROME = "Applications/Google Chrome.app/Contents/MacOS/Google Chrome";

        string[] installPaths =
        [
            "/" + MAC_CHROME,
            Path.Combine(home, MAC_CHROME),
            "/opt/google/chrome/chrome",
            Path.Combine(programFiles, WINDOWS_CHROME),
            Path.Combine(programFilesX86, WINDOWS_CHROME),
            Path.Combine(localAppData, WINDOWS_CHROME),
        ];

        return installPaths.Any(File.Exists) || IsOnPath();
    }

    private static bool IsOnPath()
    {
        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return directories.Any(directory => _commandNames.Any(name => File.Exists(Path.Combine(directory, name))));
    }
}
