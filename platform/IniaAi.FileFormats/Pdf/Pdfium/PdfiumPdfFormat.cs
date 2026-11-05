using IniaAi.FileFormats.Unlock;
using Answer = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.Answer;
using Request = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.Request;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>
/// The open build's PDF format, on PDFium in one worker process per open file: the server never loads PDFium, and a
/// file that crashes or hangs it fails alone. A file that PDFium finds damaged is rebuilt by
/// <see cref="PdfUtils.TryRepairStartxref"/> and handed back as <see cref="IPdfDocument.RepairedBytes"/>. PDFium
/// renders without a save and reopen, so <c>rewriteBeforeRendering</c> changes nothing.
/// </summary>
public sealed class PdfiumPdfFormat : IPdfFormat
{
    private readonly TimeSpan _answerTimeLimit;
    private readonly PdfiumWorkerCommand? _command;
    private readonly PdfiumWorkerLimit _workers;

    public PdfiumPdfFormat()
        : this(new PdfiumWorkerLimit())
    {
    }

    public PdfiumPdfFormat(PdfiumWorkerLimit workers)
        : this(workers, PdfiumWorker.DefaultAnswerTimeLimit, command: null)
    {
    }

    internal PdfiumPdfFormat(PdfiumWorkerLimit workers, TimeSpan answerTimeLimit, PdfiumWorkerCommand? command)
    {
        _workers = workers;
        _answerTimeLimit = answerTimeLimit;
        _command = command;
    }

    public IPdfDocument Open(Stream stream, string documentName, bool rewriteBeforeRendering = false)
    {
        PdfiumWorker? worker = null;
        try
        {
            using Stream file = stream.CanSeek ? stream : Buffered(stream);
            worker = PdfiumWorker.Start(documentName, _workers, _answerTimeLimit, _command);
            (int pageCount, byte[]? repairedBytes) = OpenIn(worker, file, documentName);
            return new PdfiumPdfDocument(worker, pageCount, repairedBytes);
        }
        catch
        {
            worker?.Dispose();
            throw;
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static (int PageCount, byte[]? RepairedBytes) OpenIn(PdfiumWorker worker, Stream file, string documentName)
    {
        long start = file.Position;
        (Answer answer, BinaryReader body) = worker.Ask(Request.Open, file.Length - start, file.CopyTo);
        using (body)
        {
            switch (answer)
            {
                case Answer.Done:
                    return (body.ReadInt32(), null);
                case Answer.NeedsPassword:
                    throw new DocumentPasswordProtectedException(documentName);
                case Answer.Damaged:
                    file.Position = start;
                    return OpenRepaired(worker, file, documentName);
                case Answer.CannotOpen:
                    throw new InvalidDataException($"The PDF reader cannot open {documentName} (PDFium error {body.ReadInt32()}).");
                default:
                    throw new InvalidDataException($"The PDF reader cannot open {documentName}: {body.ReadString()}");
            }
        }
    }

    private static (int PageCount, byte[]? RepairedBytes) OpenRepaired(PdfiumWorker worker, Stream file, string documentName)
    {
        byte[] repairedBytes;
        using (var repaired = (MemoryStream)PdfUtils.TryRepairStartxref(file, logger: null))
        {
            repairedBytes = repaired.ToArray();
        }

        if (!PdfUtils.HasValidStructure(repairedBytes))
        {
            throw new InvalidDataException($"{documentName} is damaged, and its repair did not give a PDF the PDF reader can open.");
        }

        (Answer answer, BinaryReader body) = worker.Ask(Request.Open, repairedBytes.Length, input => input.Write(repairedBytes));
        using (body)
        {
            return answer == Answer.Done
                ? (body.ReadInt32(), repairedBytes)
                : throw new InvalidDataException($"{documentName} is damaged, and the PDF reader cannot open its repair either.");
        }
    }

    private static MemoryStream Buffered(Stream stream)
    {
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    }
}
