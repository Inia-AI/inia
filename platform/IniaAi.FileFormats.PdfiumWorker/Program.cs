using System.Globalization;
using IniaAi.FileFormats.Pdf.Pdfium;
using Answer = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.Answer;
using Request = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.Request;

// The worker answers one request at a time, as PdfiumWorkerProtocol describes, until its input closes. Nothing else
// writes to its standard output.
bool startedAsRoot = Environment.IsPrivilegedProcess;
KilledFirstWhenMemoryRunsOut();
WorkerSandbox.Enter();
if (args is [PdfiumWorkerProtocol.CHECK_SANDBOX, string port, string path])
{
    return SandboxCheck.Run(int.Parse(port, CultureInfo.InvariantCulture), path, startedAsRoot);
}

Stream input = Console.OpenStandardInput();
Stream output = Console.OpenStandardOutput();
PdfiumNative.FPDF_InitLibrary();
PdfiumDocument? document = null;

try
{
    while (PdfiumWorkerProtocol.ReadLength(input) is int length)
    {
        int request = input.ReadByte();
        if (request < 0)
        {
            return PdfiumWorkerProtocol.CUT_OFF_EXIT_CODE;
        }

        using MemoryStream answer = (Request)request switch
        {
            Request.Open => Open(length - 1),
            Request.Unlock => Unlocked(PdfiumWorkerProtocol.ReadMessage(input, length - 1)),
            _ => Answered(PdfiumWorkerProtocol.ReadMessage(input, length - 1), (Request)request),
        };
        PdfiumWorkerProtocol.Send(output, answer);
    }
}
catch (EndOfStreamException)
{
    return PdfiumWorkerProtocol.CUT_OFF_EXIT_CODE;
}
finally
{
    document?.Dispose();
}

return 0;

MemoryStream Open(int length)
{
    document?.Dispose();
    document = null;
    try
    {
        document = new PdfiumDocument(PdfiumFile.Load(input, length));
        return Message(writer =>
        {
            writer.Write((byte)Answer.Done);
            writer.Write(document.File.PageCount);
        });
    }
    catch (PdfiumLoadException exception)
    {
        return exception.ErrorCode switch
        {
            PdfiumNative.FPDF_ERR_PASSWORD => Message(writer => writer.Write((byte)Answer.NeedsPassword)),
            PdfiumNative.FPDF_ERR_FORMAT => Message(writer => writer.Write((byte)Answer.Damaged)),
            _ => Message(writer =>
            {
                writer.Write((byte)Answer.CannotOpen);
                writer.Write(exception.ErrorCode);
            }),
        };
    }
}

MemoryStream Answered(byte[] body, Request request)
{
    try
    {
        PdfiumDocument open = document ?? throw new InvalidOperationException("No document is open.");
        using var reader = new BinaryReader(new MemoryStream(body));
        int pageNumber = reader.ReadInt32();
        return request switch
        {
            Request.Box => Message(writer =>
            {
                PageBox box = open.Box(pageNumber);
                writer.Write((byte)Answer.Done);
                PdfiumWorkerProtocol.WriteBox(writer, box.Left, box.Bottom, box.Right, box.Top);
            }),
            Request.DisplayedBox => Message(writer =>
            {
                (double width, double height) = open.DisplayedSize(pageNumber);
                writer.Write((byte)Answer.Done);
                writer.Write(width);
                writer.Write(height);
            }),
            Request.Text => Message(writer =>
            {
                string text = open.Text(pageNumber, Area(reader));
                writer.Write((byte)Answer.Done);
                writer.Write(text);
            }),
            Request.TextFragmentBoxes => Message(writer =>
            {
                List<PageBox> boxes = open.TextFragmentBoxes(pageNumber);
                writer.Write((byte)Answer.Done);
                writer.Write(boxes.Count);
                foreach (PageBox box in boxes)
                {
                    PdfiumWorkerProtocol.WriteBox(writer, box.Left, box.Bottom, box.Right, box.Top);
                }
            }),
            Request.Render => Message(writer =>
            {
                int dpi = reader.ReadInt32();
                PageBox? area = Area(reader);
                var format = (PdfiumWorkerProtocol.ImageFormat)reader.ReadByte();
                byte[] image = open.Render(pageNumber, dpi, area, format, reader.ReadInt32());
                writer.Write((byte)Answer.Done);
                writer.Write(image.Length);
                writer.Write(image);
            }),
            _ => throw new InvalidDataException($"Request {request} is not known."),
        };
    }
    catch (NoSuchPageException exception)
    {
        return Failure(Answer.NoSuchPage, exception.Message);
    }
    catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or EndOfStreamException or ArgumentException)
    {
        return Failure(Answer.Failed, exception.Message);
    }
}

static MemoryStream Unlocked(byte[] body)
{
    using var reader = new BinaryReader(new MemoryStream(body, writable: false));
    int passwordLength = reader.ReadInt32();
    string? password = passwordLength < 0 ? null : System.Text.Encoding.UTF8.GetString(reader.ReadBytes(passwordLength));
    (PdfiumWorkerProtocol.UnlockOutcome outcome, bool signed, byte[]? copy) unlocked;
    try
    {
        unlocked = PdfiumUnlock.Unlock(body.AsMemory((int)reader.BaseStream.Position), password);
    }
    catch (InvalidOperationException exception)
    {
        return Failure(Answer.Failed, exception.Message);
    }

    (PdfiumWorkerProtocol.UnlockOutcome outcome, bool signed, byte[]? copy) = unlocked;
    return Message(writer =>
    {
        writer.Write((byte)Answer.Done);
        writer.Write((byte)outcome);
        writer.Write(signed);
        writer.Write(copy?.Length ?? 0);
        writer.Write(copy ?? []);
    });
}

// A crafted file can make PDFium allocate until the container runs out of memory. On Linux the kernel then kills the
// process with the highest score, so the worker raises its own, and it goes before the server. It does so before its
// sandbox makes it undumpable, which hands its own score file to root.
static void KilledFirstWhenMemoryRunsOut()
{
    if (!OperatingSystem.IsLinux())
    {
        return;
    }

    try
    {
        File.WriteAllText("/proc/self/oom_score_adj", "1000");
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        // A system that does not allow it leaves the default score.
    }
}

static PageBox? Area(BinaryReader reader)
{
    return PdfiumWorkerProtocol.ReadArea(reader) is (double left, double bottom, double right, double top)
        ? new PageBox(left, bottom, right, top)
        : null;
}

static MemoryStream Failure(Answer answer, string message)
{
    return Message(writer =>
    {
        writer.Write((byte)answer);
        writer.Write(message);
    });
}

static MemoryStream Message(Action<BinaryWriter> write)
{
    var message = new MemoryStream();
    using (var writer = new BinaryWriter(message, System.Text.Encoding.UTF8, leaveOpen: true))
    {
        write(writer);
    }

    return message;
}
