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
/// cannot draw a sign-in box that sends what the reader types to another site.
/// </summary>
public static class ModelOutputMarkup
{
    private const string BLOCKED_LINK = "#";
    private const string FORM_ACTION = "action";

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
        sanitizer.AllowedCssProperties.ExceptWith(_overlayStyles);
        sanitizer.AllowedSchemes.UnionWith(LinkAddress.SafeSchemes);
        return sanitizer;
    }
}
