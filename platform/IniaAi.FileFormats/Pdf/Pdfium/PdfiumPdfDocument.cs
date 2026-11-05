using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using Answer = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.Answer;
using Request = IniaAi.FileFormats.Pdf.Pdfium.PdfiumWorkerProtocol.Request;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>A PDF that a PDFium worker process holds open. Each call is one request to the worker.</summary>
internal sealed class PdfiumPdfDocument : IPdfDocument
{
    private readonly PdfiumWorker _worker;

    public PdfiumPdfDocument(PdfiumWorker worker, int pageCount, byte[]? repairedBytes)
    {
        _worker = worker;
        PageCount = pageCount;
        RepairedBytes = repairedBytes;
    }

    public int PageCount { get; }

    public byte[]? RepairedBytes { get; }

    internal int WorkerProcessId => _worker.ProcessId;

    public PdfBox Box(int pageNumber)
    {
        using BinaryReader answer = Ask(Request.Box, pageNumber, _ => { });
        (double left, double bottom, double right, double top) = PdfiumWorkerProtocol.ReadBox(answer);
        return new PdfBox(left, bottom, right, top);
    }

    public PdfBox DisplayedBox(int pageNumber)
    {
        using BinaryReader answer = Ask(Request.DisplayedBox, pageNumber, _ => { });
        return new PdfBox(0, 0, answer.ReadDouble(), answer.ReadDouble());
    }

    public string Text(int pageNumber, PdfBox? area = null)
    {
        using BinaryReader answer = Ask(Request.Text, pageNumber, writer => WriteArea(writer, area));
        return answer.ReadString();
    }

    public IReadOnlyList<PdfBox> TextFragmentBoxes(int pageNumber)
    {
        using BinaryReader answer = Ask(Request.TextFragmentBoxes, pageNumber, _ => { });
        int count = answer.ReadInt32();
        var boxes = new List<PdfBox>(count);
        for (int index = 0; index < count; index++)
        {
            (double left, double bottom, double right, double top) = PdfiumWorkerProtocol.ReadBox(answer);
            boxes.Add(new PdfBox(left, bottom, right, top));
        }

        return boxes;
    }

    public BinaryContent RenderPng(int pageNumber, int dpi, PdfBox? area = null)
    {
        return Render(pageNumber, dpi, area, PdfiumWorkerProtocol.ImageFormat.Png, quality: 0, MediaType.ImagePng);
    }

    public BinaryContent RenderJpeg(int pageNumber, int dpi, int quality, PdfBox? area = null)
    {
        return Render(pageNumber, dpi, area, PdfiumWorkerProtocol.ImageFormat.Jpeg, quality, MediaType.ImageJpeg);
    }

    public void Dispose()
    {
        _worker.Dispose();
    }

    private BinaryContent Render(int pageNumber, int dpi, PdfBox? area, PdfiumWorkerProtocol.ImageFormat format, int quality, MediaType mediaType)
    {
        using BinaryReader answer = Ask(Request.Render, pageNumber, writer =>
        {
            writer.Write(dpi);
            WriteArea(writer, area);
            writer.Write((byte)format);
            writer.Write(quality);
        });
        return new BinaryContent(BinaryData.FromBytes(PdfiumWorker.LengthPrefixedBytes(answer)), mediaType);
    }

    private BinaryReader Ask(Request request, int pageNumber, Action<BinaryWriter> write)
    {
        (Answer answer, BinaryReader body) = _worker.Ask(request, message =>
        {
            using var writer = new BinaryWriter(message, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(pageNumber);
            write(writer);
        });

        switch (answer)
        {
            case Answer.Done:
                return body;
            case Answer.NoSuchPage:
                string message = body.ReadString();
                body.Dispose();
                throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, message);
            default:
                string failure = body.ReadString();
                body.Dispose();
                throw new InvalidOperationException($"The PDF reader could not answer about page {pageNumber}: {failure}");
        }
    }

    private static void WriteArea(BinaryWriter writer, PdfBox? area)
    {
        PdfiumWorkerProtocol.WriteArea(writer, area is PdfBox box ? (box.Left, box.Bottom, box.Right, box.Top) : null);
    }
}
