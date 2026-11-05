using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using MudBlazor.Services;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;
using MudPopoverProvider = MudBlazor.MudPopoverProvider;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// A screen reader lists a page's buttons by name, so a block's action buttons each name the block
/// they act on, and a toggle says whether it is on.
/// </summary>
public class BlockActionNameTests : BunitContext
{
    private readonly Section _section = new("Extract", new Workflow("test"));

    public BlockActionNameTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
        _ = Render<MudPopoverProvider>();
    }

    [Fact]
    public void EveryActionButton_NamesItsBlock_SoTwoBlocksShareNoName()
    {
        List<string> first = ButtonNames(RenderBlockHeader(NewBlock("Block A")));
        List<string> second = ButtonNames(RenderBlockHeader(NewBlock("Block B")));

        Assert.NotEmpty(first);
        Assert.All(first, name => Assert.EndsWith(": Block A", name, StringComparison.Ordinal));
        Assert.Empty(first.Intersect(second));
        Assert.Contains("Run block: Block A", first);
        Assert.Contains("Remove: Block A", first);
        Assert.Contains("More block actions: Block A", first);
    }

    [Fact]
    public void TheOutputToggles_SayWhetherTheyAreOn()
    {
        Block block = NewBlock("Block A");
        block.IsOutputSource = true;

        IRenderedComponent<BlockActionsMenu> header = RenderBlockHeader(block);

        Assert.Equal("true", ButtonNamed(header, "Show in Output: Block A").GetAttribute("aria-pressed"));
        Assert.Equal("false", ButtonNamed(header, "Send as data: Block A").GetAttribute("aria-pressed"));
    }

    private static List<string> ButtonNames(IRenderedComponent<BlockActionsMenu> header)
    {
        return [.. header.FindAll("button").Select(button => button.GetAttribute("aria-label") ?? button.TextContent.Trim())];
    }

    private static AngleSharp.Dom.IElement ButtonNamed(IRenderedComponent<BlockActionsMenu> header, string name)
    {
        return header.FindAll("button").Single(button => button.GetAttribute("aria-label") == name);
    }

    private Block NewBlock(string name)
    {
        return new Block(BlockTypes.AiQuery, _section, name.Replace(' ', '_'))
        {
            Name = name,
            Properties = new AiQueryProperties(),
        };
    }

    private IRenderedComponent<BlockActionsMenu> RenderBlockHeader(Block block)
    {
        return Render<BlockActionsMenu>(parameters => parameters
            .Add(p => p.Block, block)
            .Add(p => p.OnRun, () => { })
            .Add(p => p.OnDuplicate, () => { })
            .Add(p => p.OnMove, () => { })
            .Add(p => p.OnCopyToWorkflow, () => { })
            .Add(p => p.OnCopyAsText, () => { })
            .Add(p => p.OnRemove, () => { }));
    }
}
