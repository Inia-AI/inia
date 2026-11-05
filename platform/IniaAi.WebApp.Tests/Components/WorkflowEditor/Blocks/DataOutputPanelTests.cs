using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks;
using MudBlazor.Services;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Blocks;

/// <summary>
/// "Send as data" makes a block's JSON a named data output that other systems read beside the
/// report; its panel names the output and says what keeps a run from failing the block.
/// </summary>
public class DataOutputPanelTests : BunitContext
{
    private const string SEND_AS_DATA = "Send as data";

    private readonly Section _section = new("Extract", new Workflow("test"));

    public DataOutputPanelTests()
    {
        // MudBlazor makes JavaScript calls of its own that no test here is about.
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
        _ = Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public void SendAsData_NamesTheOutputAfterTheTag_AndASecondClickTurnsItOff()
    {
        Block block = AiQuery("facilities_json", OutputFormatType.Json);
        int toggles = 0;
        IRenderedComponent<BlockActionsMenu> header = Render<BlockActionsMenu>(parameters => parameters
            .Add(p => p.Block, block)
            .Add(p => p.OnDuplicate, () => { })
            .Add(p => p.OnRemove, () => { })
            .Add(p => p.OnOutputToggled, () => toggles++));

        header.FindAll("button").Single(button => button.TextContent.Contains(SEND_AS_DATA, StringComparison.Ordinal)).Click();

        Assert.Equal("facilities_json", block.DataOutputName);

        header.FindAll("button").Single(button => button.TextContent.Contains(SEND_AS_DATA, StringComparison.Ordinal)).Click();

        Assert.Null(block.DataOutputName);
        Assert.Equal(2, toggles);
    }

    [Fact]
    public void AiQueryWithoutAJsonFormat_IsToldTheRunWillFailIt()
    {
        Block block = AiQuery("facilities_json", OutputFormatType.None);
        block.DataOutputName = "facilities";

        string text = Render<DataOutputPanel>(parameters => parameters.Add(p => p.Block, block)).Markup;

        Assert.Contains("JSON or JSON in Tag", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AiQueryWithAJsonFormat_GetsNoWarning()
    {
        Block block = AiQuery("facilities_json", OutputFormatType.JsonInTag);
        block.DataOutputName = "facilities";

        IRenderedComponent<DataOutputPanel> panel = Render<DataOutputPanel>(parameters => parameters.Add(p => p.Block, block));

        Assert.Empty(panel.FindAll(".mud-alert"));
    }

    [Fact]
    public void PlainJsonWithModelCitations_IsToldToUseJsonInTag()
    {
        Block block = AiQuery("balance_json", OutputFormatType.Json);
        block.DataOutputName = "balance_sheet";
        ((AiQueryProperties)block.Properties).ShouldGetCitationsFromModel = true;

        string text = Render<DataOutputPanel>(parameters => parameters.Add(p => p.Block, block)).Markup;

        Assert.Contains("set Output format validation to JSON in Tag", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameAnotherBlockUses_IsFlagged()
    {
        Block first = AiQuery("facilities_json", OutputFormatType.Json);
        first.DataOutputName = "facilities";
        Block second = AiQuery("facilities_again", OutputFormatType.Json);
        second.Name = "Facilities again";
        second.DataOutputName = "facilities";

        string text = Render<DataOutputPanel>(parameters => parameters.Add(p => p.Block, first)).Markup;

        Assert.Contains("Facilities again", text, StringComparison.Ordinal);
    }

    private Block AiQuery(string tag, OutputFormatType format)
    {
        return new Block(BlockTypes.AiQuery, _section, tag)
        {
            Name = tag,
            Properties = new AiQueryProperties { OutputFormatType = format, OutputFormatTagName = "answer" },
        };
    }
}
