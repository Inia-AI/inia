using Bunit;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.WebApp.Components.WorkflowEditor.Agent;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Agent;

public sealed class ActivityStepsTests : BunitContext
{
    private const string BLOCK_ID = "036f04c8eb3943319fdfc96424f83aee";

    public ActivityStepsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
    }

    [Fact]
    public void Steps_ArePlainText_NameBlocksByName_AndCallTheWorkflowFilesTheWorkflow()
    {
        AgentActivityDigest digest = new()
        {
            Beats =
            [
                Beat(AgentActivityMapping.READ_CONVERTED_TEXT_LABEL, null, EditorAgentStaging.WORKFLOW_DOCUMENT),
                Beat(AgentActivityMapping.READ_CONVERTED_TEXT_LABEL, null, EditorAgentStaging.DEPENDENCY_MAP_DOCUMENT),
                Beat(AgentActivityMapping.READ_CONVERTED_TEXT_LABEL, null),
                Beat(
                    "**This workflow has no revenue blocks.** The closest is [Franchise Facts](block:" + BLOCK_ID + "), which…",
                    "…reads `aum` from [Franchise Facts](block:" + BLOCK_ID),
            ],
        };

        IRenderedComponent<ActivitySteps> steps = Render<ActivitySteps>(p => p.Add(c => c.Digest, digest));

        List<string> lines = [.. steps.FindAll(".editor-agent-step").Select(static step => step.TextContent.Trim())];
        Assert.Equal(EditorAgentSteps.READ_WORKFLOW, lines[0]);
        Assert.Equal(EditorAgentSteps.READ_CONNECTIONS, lines[1]);
        Assert.Equal(EditorAgentSteps.READ_WORKFLOW, lines[2]);
        Assert.StartsWith("This workflow has no revenue blocks. The closest is Franchise Facts, which…", lines[3]);
        Assert.Contains("…reads aum from Franchise Facts", lines[3]);
        Assert.DoesNotContain(AgentActivityMapping.READ_CONVERTED_TEXT_LABEL, steps.Markup);
        Assert.DoesNotContain(BLOCK_ID, steps.Markup);
        Assert.DoesNotContain("**", steps.Markup);
        Assert.DoesNotContain("block:", steps.Markup);
    }

    [Fact]
    public void LiveSteps_ShowOpenWithTheLatestRunning_AndSettledOnesFoldIntoOneLineWithTimeAndSteps()
    {
        AgentActivityDigest digest = new()
        {
            Beats =
            [
                Beat("Read the outline of the workflow", null) with { Icon = EditorAgentStepIcons.READ },
                Beat("Checking the writeups next.", null) with { Icon = EditorAgentStepIcons.REMARK },
                Beat("Opened Revenue breakdown", null) with { Icon = EditorAgentStepIcons.OPEN },
            ],
            Receipt = new AgentActivityReceipt { DurationSeconds = 34 },
        };

        IRenderedComponent<ActivitySteps> live = Render<ActivitySteps>(p => p
            .Add(c => c.Digest, digest)
            .Add(c => c.IsLive, true)
            .Add(c => c.WorkingLabel, "Reading the workflow..."));
        IRenderedComponent<ActivitySteps> settled = Render<ActivitySteps>(p => p.Add(c => c.Digest, digest));

        Assert.True(live.Find("details").HasAttribute("open"));
        Assert.Equal(["done", "running"], live.FindAll("[data-testid=editor-agent-step]").Select(static step => step.GetAttribute("data-state")));
        Assert.Equal("Checking the writeups next.", live.Find("[data-testid=editor-agent-remark]").TextContent.Trim());
        Assert.False(settled.Find("details").HasAttribute("open"));
        Assert.Equal("Worked for 34 s · 2 steps", settled.Find("summary").TextContent.Trim());
        Assert.All(settled.FindAll("[data-testid=editor-agent-step]"), static step => Assert.Equal("done", step.GetAttribute("data-state")));
    }

    [Fact]
    public void TheSameStepInARow_ShowsOnceWithItsCount_AndALiveRunOfThemIsTheRunningStep()
    {
        AgentActivityDigest digest = new()
        {
            Beats =
            [
                Beat("Opened a block", null) with { Icon = EditorAgentStepIcons.OPEN },
                Beat("Looked through the workflow", null) with { Icon = EditorAgentStepIcons.SEARCH },
                Beat("Looked through the workflow", null) with { Icon = EditorAgentStepIcons.SEARCH },
                Beat("Looked through the workflow", null) with { Icon = EditorAgentStepIcons.SEARCH },
            ],
        };

        IRenderedComponent<ActivitySteps> live = Render<ActivitySteps>(p => p.Add(c => c.Digest, digest).Add(c => c.IsLive, true));

        List<AngleSharp.Dom.IElement> steps = [.. live.FindAll("[data-testid=editor-agent-step]")];
        Assert.Equal(2, steps.Count);
        Assert.Null(steps[0].QuerySelector("[data-testid=editor-agent-step-count]"));
        Assert.Equal("×3", steps[1].QuerySelector("[data-testid=editor-agent-step-count]")!.TextContent.Trim());
        Assert.Equal(["done", "running"], steps.Select(static step => step.GetAttribute("data-state")));
    }

    private static AgentActivityBeat Beat(string label, string? detail, params string[] documents)
    {
        return new AgentActivityBeat(0, 0, AgentActivityBeatKind.Normal, label, detail) { DocumentNames = documents };
    }
}
