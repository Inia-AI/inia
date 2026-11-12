using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks.Responses;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// The admin app's HTML view of a block response shows model output that a customer's documents can steer, to staff
/// who may hold a support grant: every construct the Vue app's sanitiser is tested on comes out inert here too.
/// </summary>
public sealed class ModelOutputMarkupSecurityTests(ITestOutputHelper output)
{
    private static readonly string[] _runningElements = ["script", "iframe", "object", "embed", "link", "meta", "base", "style", "form", "svg", "math"];
    private static readonly string[] _addressAttributes = ["href", "src", "xlink:href", "action", "formaction", "srcdoc", "data", "poster", "background", "ping", "srcset"];
    private static readonly string[] _scriptAddresses = ["javascript:", "vbscript:", "data:text/html"];

    public static TheoryData<string> Constructs =>
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<img src=\"x\" OnErRoR=\"alert(1)\">",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<a href=\"JaVaScRiPt:alert(1)\">x</a>",
        "<a href=\"&#106;avascript:alert(1)\">x</a>",
        "<a href=\"data:text/html,<script>alert(1)</script>\">x</a>",
        "<iframe srcdoc=\"<script>alert(1)</script>\"></iframe>",
        "<svg onload=alert(1)>",
        "<svg><a xlink:href=\"javascript:alert(1)\"><text>x</text></a></svg>",
        "<math><mtext><script>alert(1)</script></mtext></math>",
        "<details open ontoggle=alert(1)>",
        "<div onpointerenter=alert(1)>x</div>",
        "<object data=\"javascript:alert(1)\">",
        "<embed src=\"javascript:alert(1)\">",
        "<base href=\"https://example.invalid/\">",
        "<meta http-equiv=\"refresh\" content=\"0;url=https://example.invalid/\">",
        "<link rel=\"stylesheet\" href=\"https://example.invalid/x.css\">",
        "<div style=\"background:url(https://example.invalid/x)\">x</div>",
        "<div style=\"background:\\75 rl(https://example.invalid/x)\">x</div>",
        "<a href=\"https://example.invalid/\" ping=\"https://example.invalid/p\">x</a>",
        "<form action=\"https://example.invalid/\"><input name=a></form>",
        "<fact label=\"x\" onmouseover=\"alert(1)\">y</fact>",
        "<cited-text type=\"x\" page-id=\"&quot;><img src=x onerror=alert(1)>\">y</cited-text>",
        "<img src=\"data:image/svg+xml,<svg onload=alert(1)>\">",
    ];

    [Theory]
    [MemberData(nameof(Constructs))]
    public void AConstructInModelOutput_IsShownInert(string input)
    {
        string shown = ModelOutputMarkup.Html(input).Value;

        Assert.Empty(ProblemsOf(shown));
    }

    [Fact]
    public void ATableALinkAndAnImageOfThePage_AreShown()
    {
        IElement[] shown = Parse(ModelOutputMarkup.Html("<table><tr><td>Revenue</td></tr></table><a href=\"https://example.com/method\">method</a><img src=\"/chart.png\">").Value);

        Assert.Equal("Revenue", shown.Single(element => element.LocalName == "td").TextContent);
        Assert.Equal("https://example.com/method", shown.Single(element => element.LocalName == "a").GetAttribute("href"));
        Assert.Equal("/chart.png", shown.Single(element => element.LocalName == "img").GetAttribute("src"));
    }

    [Theory]
    [InlineData("<img name=\"getElementById\" src=\"/x.png\">")]
    [InlineData("<a name=\"cookie\" href=\"https://example.com/\">x</a>")]
    public void ANameInModelOutput_CannotShadowAPropertyOfThePage(string input)
    {
        IElement[] shown = Parse(ModelOutputMarkup.Html(input).Value);

        Assert.NotEmpty(shown);
        Assert.All(shown, element => Assert.False(element.HasAttribute("name"), $"<{element.LocalName}> keeps name=\"{element.GetAttribute("name")}\""));
    }

    [Fact]
    public void WhatARemoteImage_ALinkTarget_AndAnIdOrName_Become_IsRecorded()
    {
        string[] inputs =
        [
            "<img src=\"https://example.invalid/p?d=QUOKKA\">",
            "<img srcset=\"https://example.invalid/p 1x\">",
            "<a href=\"https://example.invalid/\" target=\"_blank\">x</a>",
            "<a id=\"report\" name=\"main\" href=\"https://example.invalid/\">x</a>",
            "<img name=\"getElementById\" src=\"/x.png\">",
        ];

        foreach (string input in inputs)
        {
            output.WriteLine($"{input} => {ModelOutputMarkup.Html(input).Value}");
        }
    }

    private static IElement[] Parse(string html)
    {
        return [.. new HtmlParser().ParseDocument($"<body>{html}</body>").Body!.Descendants<IElement>()];
    }

    private static List<string> ProblemsOf(string html)
    {
        List<string> problems = [];
        foreach (IElement element in Parse(html))
        {
            if (_runningElements.Contains(element.LocalName))
            {
                problems.Add($"<{element.LocalName}> element");
            }

            foreach (IAttr attribute in element.Attributes)
            {
                string name = attribute.Name.ToLowerInvariant();
                string value = attribute.Value.Trim().ToLowerInvariant();
                if (name.StartsWith("on", StringComparison.Ordinal))
                {
                    problems.Add($"{name} on <{element.LocalName}>");
                }

                if (_addressAttributes.Contains(name) && _scriptAddresses.Any(scheme => value.StartsWith(scheme, StringComparison.Ordinal)))
                {
                    problems.Add($"{name}=\"{attribute.Value}\" on <{element.LocalName}>");
                }

                if (name == "style" && (value.Contains("url(", StringComparison.Ordinal) || value.Contains('\\')))
                {
                    problems.Add($"style \"{attribute.Value}\" on <{element.LocalName}>");
                }
            }
        }

        return problems;
    }
}
