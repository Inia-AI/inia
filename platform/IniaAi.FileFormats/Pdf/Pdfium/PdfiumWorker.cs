using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Answer = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.Answer;
using Request = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.Request;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>
/// One PDFium worker process, which reads one document for the server. It starts with an empty environment, so it
/// inherits none of the server's settings, and on Linux it sandboxes itself before it reads the file
/// (<c>WorkerSandbox</c>). An answer that does not come within the time limit kills the worker, and a worker that stops
/// fails only its own document.
/// </summary>
internal sealed class PdfiumWorker : IDisposable
{
    private const string WORKER_ASSEMBLY = "IniaAi.FileFormats.PdfiumWorker.dll";
    private const string DIAGNOSTICS_VARIABLE = "DOTNET_EnableDiagnostics";
    private const int STANDARD_ERROR_KEPT = 2_000;

    /// <summary>Long enough for PDFium to draw or save a large page on a busy server.</summary>
    public static readonly TimeSpan DefaultAnswerTimeLimit = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan _exitWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _errorOutputEndWait = TimeSpan.FromSeconds(1);

    private readonly Process _process;
    private readonly PdfiumWorkerLimit _workers;
    private readonly string _documentName;
    private readonly TimeSpan _answerTimeLimit;
    private readonly StringBuilder _standardError = new();
    private readonly ManualResetEventSlim _standardErrorEnded = new();
    private readonly Lock _gate = new();
    private volatile bool _timedOut;
    private volatile object? _pendingRequest;
    private int _disposed;

    private PdfiumWorker(Process process, PdfiumWorkerLimit workers, string documentName, TimeSpan answerTimeLimit)
    {
        _process = process;
        _workers = workers;
        _documentName = documentName;
        _answerTimeLimit = answerTimeLimit;
        _process.ErrorDataReceived += (_, line) => KeepStandardError(line.Data);
        _process.BeginErrorReadLine();
    }

    internal int ProcessId => _process.Id;

    /// <summary>
    /// Starts a worker with <paramref name="command"/> once <paramref name="workers"/> has a free place, and holds that
    /// place until the worker is disposed. The default command runs the worker assembly on this process's runtime.
    /// </summary>
    public static PdfiumWorker Start(string documentName, PdfiumWorkerLimit workers, TimeSpan answerTimeLimit, PdfiumWorkerCommand? command)
    {
        PdfiumWorkerCommand start = command ?? DefaultCommand();
        var startInfo = new ProcessStartInfo(start.FileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (string argument in start.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        startInfo.Environment[DIAGNOSTICS_VARIABLE] = "0";
        workers.Take();
        try
        {
            Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("The PDF reader process did not start.");
            return new PdfiumWorker(process, workers, documentName, answerTimeLimit);
        }
        catch
        {
            workers.Return();
            throw;
        }
    }

    /// <summary>Sends the short request that <paramref name="write"/> writes after its <paramref name="request"/> byte, and reads the answer.</summary>
    public (Answer Answer, BinaryReader Body) Ask(Request request, Action<Stream> write)
    {
        return Exchange(input =>
        {
            using var message = new MemoryStream();
            message.WriteByte((byte)request);
            write(message);
            PdfiumWorkerProtocol.Send(input, message);
        });
    }

    /// <summary>
    /// Sends a request whose <paramref name="bodyLength"/> bytes <paramref name="writeBody"/> writes straight to the worker,
    /// so a whole file goes out without a copy of it in memory, and reads the answer.
    /// </summary>
    public (Answer Answer, BinaryReader Body) Ask(Request request, long bodyLength, Action<Stream> writeBody)
    {
        if (bodyLength >= int.MaxValue)
        {
            throw new InvalidDataException($"{_documentName} is too large for the PDF reader.");
        }

        return Exchange(input =>
        {
            PdfiumWorkerProtocol.WriteLength(input, (int)bodyLength + 1);
            input.WriteByte((byte)request);
            writeBody(input);
            input.Flush();
        });
    }

    /// <summary>The length-prefixed bytes at the answer's position, as a slice of the answer rather than a copy.</summary>
    public static ReadOnlyMemory<byte> LengthPrefixedBytes(BinaryReader body)
    {
        int length = body.ReadInt32();
        var answer = (MemoryStream)body.BaseStream;
        _ = answer.TryGetBuffer(out ArraySegment<byte> buffer);
        ReadOnlyMemory<byte> bytes = buffer.AsMemory((int)answer.Position, length);
        answer.Position += length;
        return bytes;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            _process.StandardInput.Close();
            if (!_process.WaitForExit(_exitWait))
            {
                _process.Kill();
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
        {
            // The worker already stopped.
        }
        finally
        {
            _workers.Return();
            _process.Dispose();
            _standardErrorEnded.Dispose();
        }
    }

    /// <summary>
    /// The worker assembly on this process's runtime, with the runtime and dependency files of this application, and
    /// <paramref name="workerArguments"/> after it.
    /// </summary>
    internal static PdfiumWorkerCommand DefaultCommand(params string[] workerArguments)
    {
        string? entry = Assembly.GetEntryAssembly()?.Location;
        string[] files =
        [
            Path.Combine(AppContext.BaseDirectory, WORKER_ASSEMBLY),
            Path.ChangeExtension(entry ?? string.Empty, ".runtimeconfig.json"),
            Path.ChangeExtension(entry ?? string.Empty, ".deps.json"),
        ];
        string? missing = files.FirstOrDefault(file => !File.Exists(file));
        if (string.IsNullOrEmpty(entry) || missing is not null)
        {
            throw new InvalidOperationException($"The PDF reader cannot start: {missing ?? "this application's entry assembly"} is missing.");
        }

        return new PdfiumWorkerCommand(DotnetHost(), ["exec", "--runtimeconfig", files[1], "--depsfile", files[2], files[0], .. workerArguments]);
    }

    /// <summary>The dotnet host of this runtime, which sits three folders above the runtime's own folder.</summary>
    private static string DotnetHost()
    {
        string root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        return Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
    }

    /// <summary>One request and its answer, under the time limit; a worker that stops or breaks the pipe fails the request.</summary>
    private (Answer Answer, BinaryReader Body) Exchange(Action<Stream> send)
    {
        lock (_gate)
        {
            object request = new();
            _pendingRequest = request;
            using var timer = new Timer(TimeOut, request, _answerTimeLimit, Timeout.InfiniteTimeSpan);
            try
            {
                send(_process.StandardInput.BaseStream);
                Stream output = _process.StandardOutput.BaseStream;
                int length = PdfiumWorkerProtocol.ReadLength(output) ?? throw Stopped();
                byte[] message = PdfiumWorkerProtocol.ReadMessage(output, length);
                var body = new BinaryReader(new MemoryStream(message, 0, message.Length, writable: false, publiclyVisible: true));
                return ((Answer)body.ReadByte(), body);
            }
            catch (IOException exception)
            {
                throw Stopped(exception);
            }
            finally
            {
                _pendingRequest = null;
            }
        }
    }

    /// <summary>
    /// Kills the worker when <paramref name="request"/> still waits for its answer. A timer callback that fires after
    /// the answer came finds another request or none, and leaves the worker alone. It runs on a pool thread, so no
    /// exception may leave it.
    /// </summary>
    private void TimeOut(object? request)
    {
        if (!ReferenceEquals(request, _pendingRequest))
        {
            return;
        }

        _timedOut = true;
        try
        {
            _process.Kill();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // The worker already stopped, or the system refused the kill; the request fails on its broken pipe or on the next answer.
        }
    }

    private PdfiumWorkerException Stopped(Exception? cause = null)
    {
        if (_timedOut)
        {
            return new PdfiumWorkerException($"The PDF reader did not answer within {_answerTimeLimit.TotalSeconds:0} seconds for {_documentName}, so it was stopped.", cause);
        }

        // The last lines of error output can arrive after the exit. A child the worker started can hold the output open
        // for good, so the wait for its end is short.
        if (_process.WaitForExit(_exitWait))
        {
            _ = _standardErrorEnded.Wait(_errorOutputEndWait);
        }

        string exitCode = _process.HasExited ? $" (exit code {_process.ExitCode})" : string.Empty;
        string detail;
        lock (_standardError)
        {
            detail = _standardError.Length == 0 ? string.Empty : $": {_standardError.ToString().Trim()}";
        }

        return new PdfiumWorkerException($"The PDF reader stopped while reading {_documentName}{exitCode}{detail}", cause);
    }

    private void KeepStandardError(string? line)
    {
        if (line is null)
        {
            _standardErrorEnded.Set();
            return;
        }

        lock (_standardError)
        {
            if (_standardError.Length < STANDARD_ERROR_KEPT)
            {
                _ = _standardError.AppendLine(line);
            }
        }
    }
}

/// <summary>How to start a worker process.</summary>
internal sealed record PdfiumWorkerCommand(string FileName, IReadOnlyList<string> Arguments);

/// <summary>The PDF reader process stopped or did not answer in time, so its document cannot be read.</summary>
public sealed class PdfiumWorkerException(string message, Exception? innerException = null) : IOException(message, innerException);
