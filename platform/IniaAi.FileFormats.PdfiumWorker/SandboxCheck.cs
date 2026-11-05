using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>
/// How the worker started, which user it runs as, and what it reaches from inside its sandbox: a listener on the
/// loopback address, a file and a new process, each written to the error output as blocked or as what happened instead.
/// The exit code is 0 only when all three are blocked.
/// </summary>
internal static class SandboxCheck
{
    private const string BLOCKED = "blocked";
    private const string ANY_PROGRAM = "/bin/true";

    public static int Run(int port, string path, bool startedAsRoot)
    {
        string network = NetworkReach(port);
        string file = FileReach(path);
        string process = ProcessReach();
        using var report = new StreamWriter(Console.OpenStandardError());
        report.WriteLine($"started: {(startedAsRoot ? "root" : "user")}");
        report.WriteLine($"runs as: {Environment.UserName}");
        report.WriteLine($"network: {network}");
        report.WriteLine($"file: {file}");
        report.WriteLine($"process: {process}");
        return network == BLOCKED && file == BLOCKED && process == BLOCKED ? 0 : 1;
    }

    private static string NetworkReach(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(IPAddress.Loopback, port);
            return "connected";
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AccessDenied)
        {
            return BLOCKED;
        }
        catch (SocketException exception)
        {
            return $"not connected ({exception.SocketErrorCode})";
        }
    }

    private static string ProcessReach()
    {
        try
        {
            using Process child = Process.Start(new ProcessStartInfo(ANY_PROGRAM) { UseShellExecute = false })
                ?? throw new InvalidOperationException($"{ANY_PROGRAM} did not start.");
            child.WaitForExit();
            return "started";
        }
        catch (Win32Exception)
        {
            return BLOCKED;
        }
    }

    private static string FileReach(string path)
    {
        try
        {
            _ = File.ReadAllBytes(path);
            return "read";
        }
        catch (UnauthorizedAccessException)
        {
            return BLOCKED;
        }
        catch (IOException exception)
        {
            return $"not read ({exception.Message})";
        }
    }
}
