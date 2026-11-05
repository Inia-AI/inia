using Bunit;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks;
using MudBlazor.Services;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Blocks;

/// <summary>
/// A block whose module this build lacks shows a panel that names its type and tells the author
/// that the saved settings stay.
/// </summary>
public class StubBlockPanelTests : BunitContext
{
    public StubBlockPanelTests()
    {
        // MudBlazor makes JavaScript calls of its own that no test here is about.
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
    }

    [Theory]
    [InlineData(BlockTypes.Json2Xlsx)]
    [InlineData(BlockTypes.PeerFinancials)]
    [InlineData(BlockTypes.Verification)]
    public void NamesTheBlockType_AndKeepsItsSettings(BlockTypes type)
    {
        IRenderedComponent<StubBlockPanel> panel = Render<StubBlockPanel>(parameters => parameters
            .Add(component => component.Type, type));

        string text = panel.Find(".mud-alert").TextContent;
        Assert.Equal(
            $"The platform does not include {BlockTypeVisuals.Label(type)} blocks, so this block cannot run. Its settings stay as they were saved. Remove the block if the workflow does not need it. The full edition includes {BlockTypeVisuals.Label(type)} blocks.",
            string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
    }
}
