using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Ganss.Xss;
using IniaAi.Common.Text;
using Markdig;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks.Responses;

/// <summary>
/// A block response is model output, and text in an uploaded document can steer it, so the HTML and
/// Markdown views show it with nothing able to run: the HTML goes through a sanitiser, raw HTML inside
/// markdown shows as text, and a link keeps its address only for http, https, mailto or a relative path.
/// Math draws through MathJax, which wwwroot/js/mathjax-config.js keeps from making links or styles.
/// The HTML view also drops forms and the styles that lay an element over the editor, so the output
/// cannot draw a sign-in box that sends what the reader types to another site, and drops a style that
/// loads a resource, which the browser would fetch from another site as soon as the view shows, and
/// every name attribute, which could shadow a property of the page such as <c>document.getElementById</c>.
/// </summary>
public static partial class ModelOutputMarkup
{
    private const string BLOCKED_LINK = "#";
    private const string FORM_ACTION = "action";
    private const string STYLE = "style";
    private const string NAME = "name";

    private static readonly string[] _formTags = ["form", "input", "button", "textarea", "select", "option", "optgroup", "datalist", "fieldset", "legend", "output"];
    private static readonly string[] _overlayStyles = ["position", "z-index"];
    private static readonly HtmlSanitizer _htmlSanitizer = CreateHtmlSanitizer();

    public static MarkdownPipeline MarkdownPipeline { get; } = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static MudMarkdownProps MarkdownProps { get; } = new()
    {
        Link = { OverrideUrl = static link => LinkAddress.IsSafe(link.Url) ? null : BLOCKED_LINK },
    };

    public static MarkupString Html(string html)
    {
        return new MarkupString(_htmlSanitizer.Sanitize(html));
    }

    private static HtmlSanitizer CreateHtmlSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.ExceptWith(_formTags);
        sanitizer.AllowedAttributes.Remove(FORM_ACTION);
        sanitizer.AllowedAttributes.Remove(NAME);
        sanitizer.AllowedCssProperties.ExceptWith(_overlayStyles);
        sanitizer.AllowedSchemes.UnionWith(LinkAddress.SafeSchemes);
        sanitizer.PostProcessNode += static (_, processed) =>
        {
            if (processed.Node is IElement element && element.GetAttribute(STYLE) is { } style && ResourceFunction().IsMatch(style))
            {
                element.RemoveAttribute(STYLE);
            }
        };
        return sanitizer;
    }

    [GeneratedRegex(@"(?:url|image|image-set|cross-fade|element)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceFunction();
}
