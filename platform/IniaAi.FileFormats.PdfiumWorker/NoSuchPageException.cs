namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>A page number outside the document, which the worker answers as no such page.</summary>
internal sealed class NoSuchPageException(string message) : Exception(message);
