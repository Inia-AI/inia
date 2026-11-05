using Bunit;
using IniaAi.WebApp.Components.Components;
using MudBlazor;
using MudBlazor.Services;

namespace IniaAi.WebApp.Tests.Components;

public class CategoryTileTests : BunitContext
{
    private const string WORKFLOW_TYPE = "Hedge Funds Monitoring";

    public CategoryTileTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
    }

    [Fact]
    public void TheTile_IsAButtonNamedAfterItsWorkflowType_WithNoGroupInsideToHideTheName()
    {
        IRenderedComponent<CategoryTile> tile = Render<CategoryTile>(parameters => parameters
            .Add(p => p.Title, WORKFLOW_TYPE)
            .Add(p => p.Icon, Icons.Material.Outlined.Insights)
            .Add(p => p.HrefInfo, new Dictionary<string, string> { [WORKFLOW_TYPE] = "/editors/HedgeFundsMonitoring" }));

        AngleSharp.Dom.IElement button = tile.Find("button");
        Assert.Equal(WORKFLOW_TYPE, button.TextContent.Trim());
        Assert.Empty(button.QuerySelectorAll("[role=group]"));
    }

    [Fact]
    public void TheTileButton_IsLabelledWithItsWorkflowType()
    {
        IRenderedComponent<CategoryTile> tile = Render<CategoryTile>(parameters => parameters
            .Add(p => p.Title, WORKFLOW_TYPE)
            .Add(p => p.Icon, Icons.Material.Outlined.Insights)
            .Add(p => p.HrefInfo, new Dictionary<string, string> { [WORKFLOW_TYPE] = "/editors/HedgeFundsMonitoring" }));

        Assert.Equal(WORKFLOW_TYPE, tile.Find("button").GetAttribute("aria-label"));
    }
}
