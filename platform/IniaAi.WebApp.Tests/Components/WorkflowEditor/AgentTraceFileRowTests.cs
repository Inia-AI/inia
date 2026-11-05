using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks.Responses;
using MudBlazor.Services;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class AgentTraceFileRowTests : BunitContext
{
    private const string TRACE_PATH = "trace/trace.ndjson";

    public AgentTraceFileRowTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
    }

    [Fact]
    public void EachTraceFile_OpensFromItsOwnButton_AndItsDownloadButtonSitsBesideIt()
    {
        var file = new AgentRunTraceFile { Path = TRACE_PATH, SizeBytes = 2, Content = "{}"u8.ToArray(), RunExecutionId = "run-1" };
        IRenderedComponent<AgentTraceInteraction> trace = Render<AgentTraceInteraction>(parameters => parameters
            .Add(p => p.Files, [file]));

        trace.Find(".mud-expand-panel-header").Click();

        IElement toggle = trace.FindAll("button[aria-expanded]").Single(button => button.TextContent.Contains(TRACE_PATH, StringComparison.Ordinal));
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Empty(toggle.QuerySelectorAll("button"));
        Assert.NotNull(trace.Find($"button[aria-label='Download {TRACE_PATH}']"));
    }
}
