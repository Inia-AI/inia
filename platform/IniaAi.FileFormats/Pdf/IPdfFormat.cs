namespace IniaAi.FileFormats.Pdf;

/// <summary>
/// Opens PDF files for the document pipeline. A file that still needs its password fails with
/// <see cref="Unlock.DocumentPasswordProtectedException"/> naming <c>documentName</c>.
/// </summary>
public interface IPdfFormat
{
    /// <param name="stream">Open takes it over: the document disposes it, or Open does when it fails.</param>
    /// <param name="rewriteBeforeRendering">
    /// Some scanned PDFs render only after the Aspose implementation saves and reopens them. Model page
    /// images ask for that rewrite; an implementation that renders them without it ignores the flag.
    /// </param>
    IPdfDocument Open(Stream stream, string documentName, bool rewriteBeforeRendering = false);
}
