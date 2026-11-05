using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.Word;

/// <summary>Opens the Word documents a workflow fills with values.</summary>
public interface IWordTemplateFormat
{
    /// <summary>Opens the document in the bytes. It fails when the bytes hold no Word document it reads.</summary>
    IWordTemplate Open(byte[] templateBytes);

    /// <summary>
    /// The extension of the file that <see cref="IWordTemplate.Save"/> writes for the document in the bytes:
    /// ".docm" for a macro-enabled document and ".docx" for any other, or null when <see cref="Open"/> cannot read
    /// the bytes or <see cref="IWordTemplate.Save"/> cannot keep what they hold. It reads the bytes, never a file
    /// name, which can lie.
    /// </summary>
    string? SaveExtension(byte[] documentBytes);
}

/// <summary>
/// A Word document open for filling. Content that <see cref="IWordTemplateParagraph.ReplaceWithMarkdown"/> or
/// <see cref="IWordTemplateParagraph.ReplaceWithLines"/> puts in is a value, not template text: no later call finds
/// or changes it.
/// </summary>
public interface IWordTemplate : IDisposable
{
    /// <summary>
    /// Every paragraph the document had when it was opened, in every part (body, tables, headers, footers and text
    /// boxes), in document order.
    /// </summary>
    IReadOnlyList<IWordTemplateParagraph> Paragraphs { get; }

    /// <summary>
    /// Replaces each match of <paramref name="pattern"/> in the template text with the text that
    /// <paramref name="replacement"/> returns, in the formatting of the text it replaces, also when the match spans
    /// differently formatted text. A "\n" in the replacement becomes a line break inside the paragraph. A match never
    /// spans two paragraphs.
    /// </summary>
    void ReplaceText(Regex pattern, Func<Match, string> replacement);

    /// <summary>The document as Markdown, or null when the implementation cannot write it.</summary>
    string? ToMarkdown();

    /// <summary>
    /// The document as a file in its own format: a document with macros stays .docm with them, and any other
    /// document becomes .docx.
    /// </summary>
    byte[] Save();
}

/// <summary>One paragraph of an <see cref="IWordTemplate"/>.</summary>
public interface IWordTemplateParagraph
{
    /// <summary>The paragraph's text, with each line break as "\n" and no white space at either end.</summary>
    string Text { get; }

    /// <summary>
    /// Puts the Markdown in this paragraph's place as Word headings, paragraphs, lists and tables, in this
    /// paragraph's font, size, colour and alignment. A bullet is a round bullet (U+2022) flush with the left edge,
    /// and the list level sets its text at 14.4 pt; a numbered list keeps its numbers.
    /// </summary>
    void ReplaceWithMarkdown(string markdown);

    /// <summary>
    /// Puts one paragraph per line in this paragraph's place, each with this paragraph's style and the formatting
    /// of its first run.
    /// </summary>
    void ReplaceWithLines(IReadOnlyList<string> lines);

    /// <summary>Removes every occurrence of <paramref name="text"/> from the paragraph.</summary>
    void RemoveText(string text);
}
