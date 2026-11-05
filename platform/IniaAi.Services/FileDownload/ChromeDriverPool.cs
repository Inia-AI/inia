using IniaAi.Common.Hosting;
using IniaAi.Common.Http;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace IniaAi.Services.FileDownload;

internal class ChromeDriverPool : IDisposable
{
    private const int COMMAND_TIMEOUT_MINUTES = 5;
    private const string BROWSER_USER = "app";
    private const string PROC_STATUS_FILE = "/proc/self/status";
    private const string USER_NAMESPACES_FILE = "/proc/sys/user/max_user_namespaces";
    private const string PROBE_PAGE = "data:text/html,ok";
    private const string HOME_VARIABLE = "HOME";
    private const string DRIVER_PATH_VARIABLE = "CHROMEDRIVER_PATH";

    /// <summary>
    /// How long Chrome waits for a page to load before it gives up on the navigation.
    /// </summary>
    internal static readonly TimeSpan PageLoadTimeout = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentDictionary<ChromeDriver, CheckedForwardProxy> _activeDrivers = new();
    private readonly Lock _isolationLock = new();
    private Task<BrowserIsolation>? _isolation;
    private int _refusedPages;
    private volatile bool _isDisposed;

    public ChromeDriverPool(int maxDrivers)
    {
        _semaphore = new SemaphoreSlim(maxDrivers, maxDrivers);
    }

    /// <summary>
    /// A pool that takes <paramref name="isolation"/> as found instead of starting Chrome to find it.
    /// </summary>
    internal ChromeDriverPool(int maxDrivers, BrowserIsolation isolation)
        : this(maxDrivers)
    {
        _isolation = Task.FromResult(isolation);
    }

    /// <summary>
    /// How Chrome runs here, or null until the pool first starts it.
    /// </summary>
    public BrowserIsolation? KnownIsolation => _isolation is { IsCompletedSuccessfully: true } found ? found.Result : null;

    /// <summary>
    /// How many pages were refused because the sandbox does not start here.
    /// </summary>
    public int RefusedPages => Volatile.Read(ref _refusedPages);

    /// <summary>
    /// A Chrome for content the platform builds itself, such as a chart. It has no network:
    /// every request it makes is refused. It runs sandboxed when the host allows the sandbox,
    /// and without it otherwise.
    /// </summary>
    public async Task<ChromeDriver> AcquireOfflineDriverAsync(
        string storageDir,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        BrowserIsolation isolation = await IsolationAsync(logger);
        return await AcquireAsync(storageDir, isolation, () => CheckedForwardProxy.StartOffline(logger), cancellationToken);
    }

    /// <summary>
    /// A Chrome for pages that someone outside the deployment chose, such as links in an email.
    /// Every request goes through a <see cref="CheckedForwardProxy"/> that applies
    /// <paramref name="guard"/> and logs each refusal to <paramref name="logger"/>. Such a page
    /// opens only in a sandboxed Chrome: without the sandbox this throws
    /// <see cref="BrowserSandboxUnavailableException"/> and starts nothing.
    /// </summary>
    public async Task<ChromeDriver> AcquirePageDriverAsync(
        string storageDir,
        OutboundAddressGuard guard,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        BrowserIsolation isolation = await IsolationAsync(logger);

        if (!isolation.Sandboxed)
        {
            _ = Interlocked.Increment(ref _refusedPages);
            throw new BrowserSandboxUnavailableException(isolation.Description);
        }

        return await AcquireAsync(storageDir, isolation, () => guard.StartForwardProxy(logger), cancellationToken);
    }

    /// <summary>
    /// Quits <paramref name="driver"/> and frees its slot. A driver already released is ignored.
    /// </summary>
    public void ReleaseDriver(ChromeDriver driver)
    {
        if (driver is null || !_activeDrivers.TryRemove(driver, out CheckedForwardProxy? proxy))
        {
            return;
        }

        SafeDisposeDriver(driver);
        proxy.Dispose();
        _ = _semaphore.Release();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach ((ChromeDriver driver, CheckedForwardProxy proxy) in _activeDrivers)
        {
            SafeDisposeDriver(driver);
            proxy.Dispose();
        }

        _activeDrivers.Clear();
        _semaphore.Dispose();

        GC.SuppressFinalize(this);
    }

    internal static ChromeOptions BuildOptions(string storageDir, Uri proxyServer, bool sandboxed)
    {
        var options = new ChromeOptions();

        // Chrome sends loopback requests around a proxy unless the bypass list removes that rule,
        // and QUIC and WebRTC's UDP would go around it too. The setuid sandbox helper cannot start
        // in a container, so Chrome uses the namespace sandbox or, where told, none.
        options.AddArguments(
            "--headless=new",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--no-first-run",
            "--no-default-browser-check",
            "--window-size=1920,1080",
            $"--proxy-server={proxyServer.Scheme}://{proxyServer.Authority}",
            "--proxy-bypass-list=<-loopback>",
            "--disable-quic",
            "--disable-setuid-sandbox");

        if (!sandboxed)
        {
            options.AddArgument("--no-sandbox");
        }

        options.PageLoadTimeout = PageLoadTimeout;
        options.AddUserProfilePreference("webrtc.ip_handling_policy", "disable_non_proxied_udp");
        options.AddUserProfilePreference("download.default_directory", storageDir);
        options.AddUserProfilePreference("download.prompt_for_download", false);
        options.AddUserProfilePreference("profile.default_content_setting_values.automatic_downloads", 1);

        return options;
    }

    private async Task<ChromeDriver> AcquireAsync(
        string storageDir,
        BrowserIsolation isolation,
        Func<CheckedForwardProxy> startProxy,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, nameof(ChromeDriverPool));

        await _semaphore.WaitAsync(cancellationToken);
        CheckedForwardProxy? proxy = null;

        try
        {
            proxy = startProxy();
            ChromeDriver driver = CreateNewDriver(storageDir, proxy.Address, isolation);
            _activeDrivers[driver] = proxy;
            return driver;
        }
        catch
        {
            proxy?.Dispose();
            _ = _semaphore.Release();
            throw;
        }
    }

    private Task<BrowserIsolation> IsolationAsync(ILogger logger)
    {
        lock (_isolationLock)
        {
            return _isolation ??= Task.Run(() => FindIsolationAsync(logger));
        }
    }

    /// <summary>
    /// Starts Chrome once with its sandbox on, as the image's own unprivileged user when the app
    /// runs as root, since Chrome refuses root without <c>--no-sandbox</c>. The outcome holds for
    /// the life of the process, because the container's runtime settings cannot change under it.
    /// </summary>
    private static async Task<BrowserIsolation> FindIsolationAsync(ILogger logger)
    {
        string? user = BrowserUser();
        DirectoryInfo probeDir = Directory.CreateTempSubdirectory("inia-browser-probe");
        BrowserIsolation isolation;

        try
        {
            await ProbeAsync(probeDir.FullName, user, logger);
            isolation = new BrowserIsolation(true, user, $"Chrome runs {AsUser(user)} with its sandbox on.");
            logger.LogInformation("{Isolation}", isolation.Description);
        }
        catch (Exception ex) when (user is not null && FindUserSwitchFailure(ex) is Win32Exception failure)
        {
            isolation = Unsandboxed(null, $"the app cannot start Chrome as {user} ({failure.Message})");
            LogUnsandboxed(logger, isolation);
        }
        catch (Exception ex)
        {
            string chromeSaid = ex.Message.Split('\n', 2)[0].Trim();
            isolation = Unsandboxed(user, $"Chrome's sandbox does not start here ({HostFacts()}; Chrome said: {chromeSaid})");
            LogUnsandboxed(logger, isolation);
        }
        finally
        {
            probeDir.Delete(recursive: true);
        }

        return isolation;
    }

    private static async Task ProbeAsync(string storageDir, string? user, ILogger logger)
    {
        using CheckedForwardProxy proxy = CheckedForwardProxy.StartOffline(logger);
        ChromeDriver driver = CreateNewDriver(storageDir, proxy.Address, new BrowserIsolation(true, user, string.Empty));

        try
        {
            await driver.Navigate().GoToUrlAsync(PROBE_PAGE);
        }
        finally
        {
            SafeDisposeDriver(driver);
        }
    }

    private static BrowserIsolation Unsandboxed(string? user, string reason)
    {
        return new BrowserIsolation(false, user, $"Chrome runs {AsUser(user)} without its sandbox: {reason}.");
    }

    private static void LogUnsandboxed(ILogger logger, BrowserIsolation isolation)
    {
        logger.LogWarning(
            "{Isolation} Charts render without network access, and links from emails skip the browser step.",
            isolation.Description);
    }

    private static string AsUser(string? user)
    {
        return user is null ? "as the app's own user" : $"as the user {user}";
    }

    private static Win32Exception? FindUserSwitchFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception failure)
            {
                return failure;
            }
        }

        return null;
    }

    /// <summary>
    /// The unprivileged user that the .NET images ship, when the app runs as root on Linux;
    /// otherwise null, and Chrome runs as the app's own user.
    /// </summary>
    private static string? BrowserUser()
    {
        return UnixAccount.ToStartChildrenAs(BROWSER_USER)?.Name;
    }

    /// <summary>
    /// Hands <paramref name="folder"/> to <paramref name="account"/> alone. It fails as a refused switch to that user
    /// does, so the probe falls back to running Chrome as the app's own user.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static void GiveTo(string folder, UnixAccount account)
    {
        if (ChangeOwner(folder, account.UserId, account.GroupId) != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [DllImport("libc", EntryPoint = "chown", SetLastError = true)]
    private static extern int ChangeOwner([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint owner, uint group);

    private static string HostFacts()
    {
        string seccomp = File.Exists(PROC_STATUS_FILE)
            ? File.ReadLines(PROC_STATUS_FILE)
                .FirstOrDefault(line => line.StartsWith("Seccomp:", StringComparison.Ordinal))?
                .Split(':')[1].Trim() ?? "unknown"
            : "unknown";
        string namespaces = File.Exists(USER_NAMESPACES_FILE) ? File.ReadAllText(USER_NAMESPACES_FILE).Trim() : "unknown";

        return $"seccomp mode {seccomp}, user namespaces allowed {namespaces}";
    }

    /// <summary>
    /// The chromedriver that <paramref name="driverPath"/> names, or, with none, the one Selenium Manager
    /// finds. Selenium Manager has no build for Linux on arm64 and calls out to the internet each time
    /// it runs, usage statistics included, so an image names its driver in CHROMEDRIVER_PATH.
    /// </summary>
    internal static ChromeDriverService CreateService(string? driverPath)
    {
        return string.IsNullOrEmpty(driverPath)
            ? ChromeDriverService.CreateDefaultService()
            : ChromeDriverService.CreateDefaultService(Path.GetDirectoryName(driverPath)!, Path.GetFileName(driverPath));
    }

    private static ChromeDriver CreateNewDriver(string storageDir, Uri proxyServer, BrowserIsolation isolation)
    {
        ChromeOptions options = BuildOptions(storageDir, proxyServer, isolation.Sandboxed);

        ChromeDriverService service = CreateService(Environment.GetEnvironmentVariable(DRIVER_PATH_VARIABLE));
        service.HideCommandPromptWindow = true;

        if (isolation.User is string user && OperatingSystem.IsLinux())
        {
            // The browser writes its downloads here as its own user, and the app reads them back as root. No other
            // user, the PDF reader's included, enters the folder.
            UnixAccount account = UnixAccount.Find(user) ?? throw new Win32Exception(0, $"This host has no user {user}.");
            GiveTo(storageDir, account);
            service.DriverProcessStarting += (_, starting) => RunAs(starting.DriverServiceProcessStartInfo, account.Name, account.Home);
        }

        return new ChromeDriver(service, options, TimeSpan.FromMinutes(COMMAND_TIMEOUT_MINUTES));
    }

    private static void RunAs(ProcessStartInfo startInfo, string user, string home)
    {
        startInfo.UserName = user;
        startInfo.Environment[HOME_VARIABLE] = home;
    }

    private static void SafeDisposeDriver(ChromeDriver driver)
    {
        try
        {
            driver.Quit();
            driver.Dispose();
        }
        catch (Exception)
        {

        }
    }
}
