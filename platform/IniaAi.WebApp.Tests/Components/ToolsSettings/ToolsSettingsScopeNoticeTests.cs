using Bunit;
using IniaAi.Domain.Entities.DocumentAnalysis;
using IniaAi.Domain.Tools.GraphRag;
using IniaAi.Domain.Tools.PdfOcr;
using IniaAi.Domain.Tools.RagStrategies;
using IniaAi.Domain.Tools.Reconciliation;
using IniaAi.WebApp.Components.ToolsSettings.Components;
using Microsoft.AspNetCore.Components;

namespace IniaAi.WebApp.Tests.Components.ToolsSettings;

/// <summary>
/// Every prompt set on Tools Settings is global, so every tab says, in its header and in the same
/// words, that one edit there reaches every organization. The Validation and Screening tabs belong
/// to their modules, and their tests sit with them.
/// </summary>
public class ToolsSettingsScopeNoticeTests : SignedInRenderTest
{
    private static readonly Dictionary<string, (Type Component, Func<object> NewPrompts)> _tabs = new()
    {
        [nameof(PdfOcrPromptsInteraction)] = (typeof(PdfOcrPromptsInteraction), () => new PdfOcrPrompts()),
        [nameof(ExcelAnalysisPromptsInteraction)] = (typeof(ExcelAnalysisPromptsInteraction), () => new ExcelAnalysisPrompts()),
        [nameof(TopicsPromptsInteraction)] = (typeof(TopicsPromptsInteraction), () => new TopicsPrompts()),
        [nameof(ReconciliationPromptsInteraction)] = (typeof(ReconciliationPromptsInteraction), () => new ReconciliationPrompts()),
        [nameof(UseAutoDetectedTableOfContentsPromptsInteraction)] = (typeof(UseAutoDetectedTableOfContentsPromptsInteraction), () => new UseAutoDetectedTableOfContentsPrompts()),
        [nameof(GraphRagPromptsInteraction)] = (typeof(GraphRagPromptsInteraction), () => new GraphRagPrompts()),
    };

    public static TheoryData<string> EveryWorkflowTabs => [.. _tabs.Keys];

    [Theory]
    [MemberData(nameof(EveryWorkflowTabs))]
    public void TheTabHeader_SaysOneEditChangesEveryWorkflowForEveryOrganization(string tab)
    {
        IRenderedComponent<IComponent> rendered = RenderTab(tab);

        Assert.Equal(1, Occurrences(rendered.Find(".mud-card-header").TextContent, ToolsSettingsScopeNotice.EVERY_WORKFLOW));
    }

    private IRenderedComponent<IComponent> RenderTab(string tab)
    {
        (Type component, Func<object> newPrompts) = _tabs[tab];

        return Render<DynamicComponent>(parameters => parameters
            .Add(dynamic => dynamic.Type, component)
            .Add(dynamic => dynamic.Parameters, new Dictionary<string, object> { ["Prompts"] = newPrompts() }))
            .FindComponent<IComponent>();
    }

    public static int Occurrences(string text, string sentence)
    {
        return (text.Length - text.Replace(sentence, string.Empty, StringComparison.Ordinal).Length) / sentence.Length;
    }
}
