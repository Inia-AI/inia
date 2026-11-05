using AngleSharp.Dom;
using Bunit;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using Microsoft.AspNetCore.Components;
using MudBlazor.Services;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// A screen reader flattens whatever sits inside a button, so a control in a panel's header row
/// sits beside the button that opens the panel, never inside it.
/// </summary>
public class DisclosurePanelTests : BunitContext
{
    private const string TITLE = "Trace files";
    private const string ACTION = "Download trace";
    private const string CONTENT = "trace body";

    public DisclosurePanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
    }

    [Fact]
    public void AHeaderAction_SitsBesideTheToggle_AndPressingItLeavesThePanelAsItWas()
    {
        int actionPresses = 0;
        IRenderedComponent<DisclosurePanel> panel = Render<DisclosurePanel>(parameters => parameters
            .Add(p => p.TitleContent, TITLE)
            .Add(p => p.Actions, builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "type", "button");
                builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => actionPresses++));
                builder.AddContent(3, ACTION);
                builder.CloseElement();
            })
            .Add(p => p.ChildContent, CONTENT));

        IElement toggle = panel.Find("button[aria-expanded]");
        Assert.Equal(TITLE, toggle.TextContent.Trim());
        Assert.Empty(toggle.QuerySelectorAll("button"));

        Action(panel).Click();

        Assert.Equal(1, actionPresses);
        Assert.Equal("false", panel.Find("button[aria-expanded]").GetAttribute("aria-expanded"));
        Assert.DoesNotContain(CONTENT, panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenStateGivenByThePage_OpensThePanel_AndAToggleReportsTheNewState()
    {
        bool? reported = null;
        IRenderedComponent<DisclosurePanel> panel = Render<DisclosurePanel>(parameters => parameters
            .Add(p => p.TitleContent, TITLE)
            .Add(p => p.ChildContent, CONTENT)
            .Add(p => p.Expanded, true)
            .Add(p => p.ExpandedChanged, expanded => reported = expanded));

        Assert.Equal("true", panel.Find("button[aria-expanded]").GetAttribute("aria-expanded"));
        Assert.Contains(CONTENT, panel.Markup, StringComparison.Ordinal);

        panel.Find("button[aria-expanded]").Click();

        Assert.False(reported);
        Assert.Equal("false", panel.Find("button[aria-expanded]").GetAttribute("aria-expanded"));
    }

    private static IElement Action(IRenderedComponent<DisclosurePanel> panel)
    {
        return panel.FindAll("button").Single(button => button.TextContent.Trim() == ACTION);
    }
}
