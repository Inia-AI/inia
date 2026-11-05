using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.Pdf.Pdfium;

namespace IniaAi.FileFormats.Tests.Pdf;

/// <summary>
/// Starts the real worker the way the server does, in the mode that probes its own sandbox and writes what it could
/// reach to its error output, which the failure message carries. CI runs this class a second time as root.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class PdfiumWorkerSandboxTests : IDisposable
{
    private static readonly TimeSpan _answerTimeLimit = TimeSpan.FromSeconds(30);
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("inia-pdf-worker-sandbox");

    [Fact]
    public void Worker_CannotConnectToAListenerOnTheLoopbackAddress()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The worker's sandbox is Linux only.");

        Assert.Contains("network: blocked", SandboxReport(), StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_CannotStartAProcessThatOutlivesIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The worker's sandbox is Linux only.");

        Assert.Contains("process: blocked", SandboxReport(), StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_StartedByARootServer_CannotReadAFileOnlyTheServersUserCanRead()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.IsPrivilegedProcess, "Only a server that runs as root starts the worker as its own user.");

        Assert.Contains("file: blocked", SandboxReport(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A worker that started as its own user would be dumpable by that user's other processes until its sandbox was on,
    /// so another worker could open its memory first and keep the handle. It starts as root and drops to its user itself.
    /// </summary>
    [Fact]
    public void Worker_StartedByARootServer_StartsAsRoot_AndRunsAsItsOwnUser()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.IsPrivilegedProcess, "Only a server that runs as root starts the worker as its own user.");

        string report = SandboxReport();

        Assert.Contains("started: root", report, StringComparison.Ordinal);
        Assert.Contains("runs as: pdf-reader", report, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    /// <summary>
    /// What the worker reached of a listener on the loopback address and of a file that only this process's user can
    /// read, in a folder that every user can enter.
    /// </summary>
    private string SandboxReport()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        File.SetUnixFileMode(_folder.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        string privateFile = Path.Combine(_folder.FullName, "private.txt");
        new FileStream(privateFile, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        }).Dispose();

        PdfiumWorkerCommand check = PdfiumWorker.DefaultCommand(
            PdfiumWorkerProtocol.CHECK_SANDBOX,
            ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture),
            privateFile);
        var format = new PdfiumPdfFormat(new PdfiumWorkerLimit(1), _answerTimeLimit, check);

        return Assert.Throws<PdfiumWorkerException>(
            () => format.Open(ContractFixture.Stream(ContractFixture.THREE_PAGES_PDF), "sandbox-check.pdf")).Message;
    }
}
