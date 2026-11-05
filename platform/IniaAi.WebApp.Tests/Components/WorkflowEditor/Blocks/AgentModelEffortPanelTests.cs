using Bunit;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Blocks;

/// <summary>
/// The agent-family model panel names the model each tier runs for the signed-in organization,
/// and says when a specific model overrides the tier.
/// </summary>
public class AgentModelEffortPanelTests : SignedInRenderTest
{
    private const string BEDROCK_OPUS = "us.anthropic.claude-opus-5-5";
    private const string BEDROCK_SONNET = "us.anthropic.claude-sonnet-5-5";
    private const string ADVANCED_TOGGLE = "[data-testid=agent-advanced-model]";
    private const string EFFECTIVE_MODEL = "[data-testid=agent-effective-model]";
    private const string NOT_ALLOWED = "[data-testid=agent-tier-model-not-allowed]";

    private void Configure(List<string> allowedModels, TenantModelOverride? organizationOverride = null)
    {
        _ = Services.AddSingleton(Options.Create(new AgentRuntimeSettings { Enabled = true, AllowedModels = allowedModels }));
        var platformSettings = new PlatformSettings();
        if (organizationOverride is not null)
        {
            platformSettings.ModelRouting.TenantModelOverrides[ORGANIZATION_ID] = organizationOverride;
        }

        _ = Services.AddSingleton(Options.Create(platformSettings));
    }

    private async Task<IRenderedComponent<AgentModelEffortPanel>> RenderAsync(AgentProperties props)
    {
        _ = await SignInAsync("author@example.com");

        return RenderInApp<AgentModelEffortPanel>(parameters => parameters.Add(panel => panel.Props, props));
    }

    // MudCheckBox puts the test id on its input; the label wraps it.
    private static string ToggleLabel(IRenderedComponent<AgentModelEffortPanel> panel)
    {
        return panel.Find(ADVANCED_TOGGLE).Closest("label")!.TextContent;
    }

    [Fact]
    public async Task AdvancedTier_LabelsTheToggleWithOpus_AndNamesBothTiers()
    {
        Configure([BEDROCK_SONNET, BEDROCK_OPUS]);

        IRenderedComponent<AgentModelEffortPanel> panel = await RenderAsync(new AgentProperties());

        Assert.Contains("Advanced model (Claude Opus 5.5)", ToggleLabel(panel), StringComparison.Ordinal);
        Assert.Equal(
            "Runs the advanced model, Claude Opus 5.5. Off runs the standard model, Claude Sonnet 5.5.",
            panel.Find(EFFECTIVE_MODEL).TextContent.Trim());
        Assert.Empty(panel.FindAll(NOT_ALLOWED));
    }

    [Fact]
    public async Task StandardTier_SaysItRunsSonnet()
    {
        Configure([BEDROCK_SONNET, BEDROCK_OPUS]);

        IRenderedComponent<AgentModelEffortPanel> panel = await RenderAsync(new AgentProperties { ShouldUseAdvancedModel = false });

        Assert.Equal("Runs the standard model, Claude Sonnet 5.5.", panel.Find(EFFECTIVE_MODEL).TextContent.Trim());
    }

    [Fact]
    public async Task SpecificModel_SaysItOverridesTheTier_AndDisablesTheToggle()
    {
        Configure([BEDROCK_SONNET, BEDROCK_OPUS]);

        IRenderedComponent<AgentModelEffortPanel> panel = await RenderAsync(new AgentProperties { Model = "claude-sonnet-5-5" });

        Assert.Equal(
            "Runs Claude Sonnet 5.5, the specific model below, which overrides the tier.",
            panel.Find(EFFECTIVE_MODEL).TextContent.Trim());
        Assert.True(panel.Find(ADVANCED_TOGGLE).HasAttribute("disabled"));
    }

    [Fact]
    public async Task OrganizationOverride_LabelsTheTierWithTheOrganizationsModel()
    {
        Configure([BEDROCK_SONNET, BEDROCK_OPUS], new TenantModelOverride { AgentAdvancedModel = "claude-sonnet-5-5" });

        IRenderedComponent<AgentModelEffortPanel> panel = await RenderAsync(new AgentProperties());

        Assert.Contains("Advanced model (Claude Sonnet 5.5)", ToggleLabel(panel), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TierModelOutsideTheAllowlist_WarnsThatASaveRefusesTheBlock()
    {
        Configure([BEDROCK_SONNET]);

        IRenderedComponent<AgentModelEffortPanel> panel = await RenderAsync(new AgentProperties());

        Assert.Contains("is not in this deployment's allowed models", panel.Find(NOT_ALLOWED).TextContent, StringComparison.Ordinal);
    }
}
