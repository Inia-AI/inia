using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks.Responses;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Extensions;
using MudBlazor.Services;
using TextField = MudBlazor.MudTextField<string>;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class ResponseInteractionIndentTests : BunitContext
{
    private const string MINIFIED = """{"rate":1.10,"rows":[{"id":12345678901234567890}]}""";
    private const string INDENTED = """
        {
          "rate": 1.10,
          "rows": [
            {
              "id": 12345678901234567890
            }
          ]
        }
        """;

    public ResponseInteractionIndentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
    }

    [Fact]
    public void TheRawResponseTab_ShowsJsonIndentedAndKeepsTheStoredText()
    {
        BlockResponse response = ResponseOf(MINIFIED, json: null);

        IRenderedComponent<RawResponseInteraction> tab = Render<RawResponseInteraction>(p => p.Add(c => c.Response, response));

        Assert.Equal(INDENTED, ShownText(tab));
        Assert.Equal(MINIFIED, response.RawResponse);
    }

    [Fact]
    public void TheJsonTab_ShowsJsonIndentedAndKeepsTheStoredText()
    {
        BlockResponse response = ResponseOf("raw", MINIFIED);

        IRenderedComponent<JsonResponseInteraction> tab = Render<JsonResponseInteraction>(p => p.Add(c => c.Response, response));

        Assert.Equal(INDENTED, ShownText(tab));
        Assert.Equal(MINIFIED, response.JsonResponse);
    }

    [Fact]
    public void AnAuthorEditInTheRawResponseTab_IsStoredAsTheAuthorLeftIt()
    {
        BlockResponse response = ResponseOf(MINIFIED, json: null);
        IRenderedComponent<RawResponseInteraction> tab = Render<RawResponseInteraction>(p => p.Add(c => c.Response, response));
        string edited = INDENTED.Replace("1.10", "2.20");

        tab.Find("textarea").Change(edited);

        Assert.Equal(edited, response.RawResponse);
    }

    [Fact]
    public void AMinifiedAuthorEdit_IsStoredMinifiedAndShownIndented()
    {
        BlockResponse response = ResponseOf(MINIFIED, json: null);
        IRenderedComponent<RawResponseInteraction> tab = Render<RawResponseInteraction>(p => p.Add(c => c.Response, response));
        const string EDITED = """{"rate":2.20}""";

        tab.Find("textarea").Change(EDITED);

        Assert.Equal(EDITED, response.RawResponse);
        Assert.Equal("{\n  \"rate\": 2.20\n}", ShownText(tab));
    }

    [Fact]
    public void LeavingTheFieldAndARerender_LeaveTheStoredTextAlone()
    {
        BlockResponse response = ResponseOf(MINIFIED, json: null);
        IRenderedComponent<RawResponseInteraction> tab = Render<RawResponseInteraction>(p => p.Add(c => c.Response, response));

        tab.Find("textarea").Blur();
        tab.Render();

        Assert.Equal(MINIFIED, response.RawResponse);
        Assert.Equal(INDENTED, ShownText(tab));
    }

    [Fact]
    public void ABlockRerun_ShowsTheNewOutput()
    {
        IRenderedComponent<RawResponseInteraction> tab = Render<RawResponseInteraction>(p => p.Add(c => c.Response, ResponseOf(MINIFIED, json: null)));

        tab.Render(p => p.Add(c => c.Response, ResponseOf("""{"rate":3.30}""", json: null)));

        Assert.Equal("{\n  \"rate\": 3.30\n}", ShownText(tab));
    }

    [Fact]
    public void AnAuthorEditInTheJsonTab_IsStoredAsTheAuthorLeftIt()
    {
        BlockResponse response = ResponseOf("raw", MINIFIED);
        IRenderedComponent<JsonResponseInteraction> tab = Render<JsonResponseInteraction>(p => p.Add(c => c.Response, response));
        const string EDITED = "not json any more";

        tab.Find("textarea").Change(EDITED);

        Assert.Equal(EDITED, response.JsonResponse);
    }

    [Fact]
    public void PlainText_ShowsAsStored()
    {
        const string STORED = "The total is 3 USD.\r\nNo JSON here.";
        BlockResponse response = ResponseOf(STORED, json: null);

        IRenderedComponent<RawResponseInteraction> tab = Render<RawResponseInteraction>(p => p.Add(c => c.Response, response));

        Assert.Equal(STORED, ShownText(tab));
        Assert.Equal(STORED, response.RawResponse);
    }

    private static BlockResponse ResponseOf(string raw, string? json)
    {
        var workflow = new Workflow("Run");
        Block block = new(BlockTypes.WebApiCall, new Section("Body", workflow), "api") { Name = "API" };
        return new BlockResponse(block) { RawResponse = raw, JsonResponse = json };
    }

    private static string? ShownText<TComponent>(IRenderedComponent<TComponent> tab)
        where TComponent : class, IComponent
    {
        return tab.FindComponent<TextField>().Instance.GetState(field => field.Value);
    }
}
