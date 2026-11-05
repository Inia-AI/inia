using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

/// <summary>
/// An answer can quote document text the agent did not write, so it is rendered from its parsed
/// structure with nothing able to run or reach out: raw HTML is shown as text, images as their
/// description, and every link as its text, except a link to a block the workflow has, which
/// becomes an in-page link carrying the block's id.
/// </summary>
public static partial class EditorAgentMarkdown
{
    public const string BLOCK_ID_ATTRIBUTE = "data-block-id";

    private const string IN_PAGE_HREF = "#";
    private const int MAX_PARSED_LENGTH = 60_000;
    private const int MAX_TABLE_CELLS_PER_LINE = 64;
    private const char TABLE_CELL_SEPARATOR = '|';

    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseEmphasisExtras()
        .Build();

    /// <summary>
    /// Pipe tables pad every row to the widest row, so one very wide line makes rendering quadratic.
    /// An answer with such a line is rendered without tables.
    /// </summary>
    private static readonly MarkdownPipeline _pipelineWithoutTables = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseEmphasisExtras()
        .Build();

    /// <param name="hasBlock">Whether the workflow has a block with the id; a link to any other id
    /// shows as its text. Null keeps every well-formed block link.</param>
    public static string ToHtml(string? markdown, Func<string, bool>? hasBlock = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        if (markdown.Length > MAX_PARSED_LENGTH)
        {
            return PlainText(markdown);
        }

        try
        {
            return Render(markdown, HasOverwideTableLine(markdown) ? _pipelineWithoutTables : _pipeline, hasBlock);
        }
        catch (ArgumentException)
        {
            // Markdig refuses input nested too deeply; the answer still shows, as plain text.
            return PlainText(markdown);
        }
    }

    private static string Render(string markdown, MarkdownPipeline pipeline, Func<string, bool>? hasBlock)
    {
        MarkdownDocument document = Markdown.Parse(markdown, pipeline);

        // The info string becomes a class attribute at parse time, and a form feed inside it splits into extra classes.
        foreach (FencedCodeBlock fence in document.Descendants<FencedCodeBlock>())
        {
            fence.Info = null;
            fence.Arguments = null;
            fence.SetAttributes(new HtmlAttributes());
        }

        foreach (LinkInline image in document.Descendants<LinkInline>().Where(static link => link.IsImage).ToList())
        {
            image.ReplaceBy(new LiteralInline(TextOf(image)), copyChildren: false);
        }

        foreach (LinkInline link in document.Descendants<LinkInline>().ToList())
        {
            if (BlockIdOf(link.Url) is string blockId && (hasBlock is null || hasBlock(blockId)))
            {
                link.Url = IN_PAGE_HREF;
                link.Title = null;
                link.GetAttributes().AddProperty(BLOCK_ID_ATTRIBUTE, blockId);
                continue;
            }

            Unwrap(link);
        }

        foreach (AutolinkInline autolink in document.Descendants<AutolinkInline>().ToList())
        {
            autolink.ReplaceBy(new CodeInline(autolink.Url) { Delimiter = '`', DelimiterCount = 1 }, copyChildren: false);
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);
        _ = renderer.Render(document);
        writer.Flush();

        return writer.ToString();
    }

    private static bool HasOverwideTableLine(string markdown)
    {
        foreach (string line in markdown.Split('\n'))
        {
            if (line.Count(static character => character == TABLE_CELL_SEPARATOR) > MAX_TABLE_CELLS_PER_LINE)
            {
                return true;
            }
        }

        return false;
    }

    private static string PlainText(string markdown)
    {
        return $"<pre>{System.Net.WebUtility.HtmlEncode(markdown)}</pre>";
    }

    /// <summary>The block id a <c>block:&lt;id&gt;</c> link names, or null for any other link.</summary>
    public static string? BlockIdOf(string? url)
    {
        if (!EditorAgentBlockLinks.IsBlockLink(url))
        {
            return null;
        }

        string id = Uri.UnescapeDataString(url![EditorAgentBlockLinks.SCHEME.Length..]).Trim();

        return BlockId().IsMatch(id) ? id : null;
    }

    private static void Unwrap(LinkInline link)
    {
        Inline? child = link.FirstChild;
        while (child is not null)
        {
            Inline? next = child.NextSibling;
            child.Remove();
            link.InsertBefore(child);
            child = next;
        }

        link.Remove();
    }

    private static string TextOf(ContainerInline container)
    {
        var text = new StringBuilder();
        AppendText(container, text);
        return text.ToString();
    }

    private static void AppendText(ContainerInline container, StringBuilder text)
    {
        for (Inline? inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    _ = text.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    _ = text.Append(code.Content);
                    break;
                case LineBreakInline:
                    _ = text.Append(' ');
                    break;
                case ContainerInline nested:
                    AppendText(nested, text);
                    break;
                default:
                    break;
            }
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,200}$")]
    private static partial Regex BlockId();
}
