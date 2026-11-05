using IniaAi.Common.Content;

namespace IniaAi.FileFormats.Word;

/// <summary>
/// One page of a Word document, as the <see cref="IWordPageReader"/> splits it: the page's markdown text plus the
/// embedded image regions on that page, in document order, ready for model OCR.
/// </summary>
public sealed record WordPageContent(string Text, IReadOnlyList<BinaryContent> ImageRegions);
