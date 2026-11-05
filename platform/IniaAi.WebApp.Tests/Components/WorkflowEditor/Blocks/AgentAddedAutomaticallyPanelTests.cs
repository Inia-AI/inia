using AngleSharp.Dom;
using Bunit;
using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Blocks;

/// <summary>
/// The Agent and Verification blocks show, collapsed and read-only, what every run is told
/// before the authored system prompt: the runtime's environment text and this block's run facts.
/// </summary>
public class AgentAddedAutomaticallyPanelTests : BunitContext
{
    private const string ENVIRONMENT_TEXT = "Your environment: no network, tools listed here.";

    private readonly StubAgentRuntimeClient _runtime = new();

    public AgentAddedAutomaticallyPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
        _ = Services.AddSingleton<IAgentRuntimeClient>(_runtime);
        _ = Services.AddSingleton<IAgentRuntimeEnvironmentProvider, AgentRuntimeEnvironmentCache>();
    }

    private static AgentProperties PropsWithSkill()
    {
        return new AgentProperties
        {
            WorkerSkills =
            [
                new AgentWorkerSkill
                {
                    Name = "balance-sheet",
                    Description = "Standardizes a balance sheet.",
                    Content = "c",
                    Assets = [new AgentWorkerSkillAsset { Name = "check.py", Content = "print(1)" }],
                },
            ],
        };
    }

    private IRenderedComponent<AgentAddedAutomaticallyPanel> RenderPanel(bool hasOutputSchema)
    {
        return Render<AgentAddedAutomaticallyPanel>(parameters => parameters
            .Add(panel => panel.Props, PropsWithSkill())
            .Add(panel => panel.HasOutputSchema, hasOutputSchema));
    }

    private static void Open(IRenderedComponent<AgentAddedAutomaticallyPanel> panel)
    {
        IElement header = panel.Find(".mud-expand-panel-header");
        Assert.Contains(AgentAddedAutomaticallyPanel.PANEL_TITLE, header.TextContent);
        header.Click();
    }

    [Fact]
    public void Collapsed_FetchesNothing()
    {
        _runtime.OnGetEnvironment = static () => new AgentRuntimeEnvironment("0.3.1030", ENVIRONMENT_TEXT);

        IRenderedComponent<AgentAddedAutomaticallyPanel> panel = RenderPanel(hasOutputSchema: false);

        Assert.Contains(AgentAddedAutomaticallyPanel.PANEL_TITLE, panel.Markup);
        Assert.Equal(0, _runtime.EnvironmentFetchCount);
    }

    [Fact]
    public void Opened_ShowsTheRuntimeTextAndThisBlocksRunFacts()
    {
        _runtime.OnGetEnvironment = static () => new AgentRuntimeEnvironment("0.3.1030", ENVIRONMENT_TEXT);

        IRenderedComponent<AgentAddedAutomaticallyPanel> panel = RenderPanel(hasOutputSchema: true);
        Open(panel);

        Assert.Contains(ENVIRONMENT_TEXT, panel.Markup);
        Assert.Contains("task_setup", panel.Markup);
        Assert.Contains(AgentRunFacts.TASK_SETUP_NOTE, panel.Markup);
        Assert.Contains("- Skills: skills:balance-sheet", panel.Markup);
        Assert.DoesNotContain("Standardizes a balance sheet.", panel.Markup);
        Assert.DoesNotContain("Documents", panel.Markup);
        Assert.Contains("$SKILL_ASSETS_DIR/balance-sheet/check.py", panel.Markup);
        Assert.Contains("The final answer is checked against an enforced output schema.", panel.Markup);
    }

    [Fact]
    public void Opened_AgainstARuntimeWithoutTheEndpoint_SaysTheTextIsNotAvailable()
    {
        _runtime.OnGetEnvironment = static () => Error.NotFound(AgentRuntimeEnvironmentErrors.NOT_SERVED, "absent");

        IRenderedComponent<AgentAddedAutomaticallyPanel> panel = RenderPanel(hasOutputSchema: false);
        Open(panel);

        Assert.Contains(AgentAddedAutomaticallyPanel.ENVIRONMENT_NOT_AVAILABLE, panel.Markup);
        Assert.DoesNotContain("enforced output schema", panel.Markup);
    }

    [Fact]
    public void Opened_WithFixedInstructions_ShowsThemAfterTheRunFacts()
    {
        const string FIXED = "You are a verification agent.";
        _runtime.OnGetEnvironment = static () => new AgentRuntimeEnvironment("0.3.1030", ENVIRONMENT_TEXT);

        IRenderedComponent<AgentAddedAutomaticallyPanel> panel = Render<AgentAddedAutomaticallyPanel>(parameters => parameters
            .Add(component => component.Props, PropsWithSkill())
            .Add(component => component.HasOutputSchema, true)
            .Add(component => component.FixedInstructions, FIXED)
            .Add(component => component.FixedInstructionsLabel, "Verification instructions"));
        Open(panel);

        IElement field = panel.Find("[data-testid=agent-added-fixed-instructions]");
        Assert.Contains(FIXED, field.OuterHtml);
        Assert.True(panel.Markup.IndexOf(FIXED, StringComparison.Ordinal)
            > panel.Markup.IndexOf("skills:balance-sheet", StringComparison.Ordinal));
    }
}
