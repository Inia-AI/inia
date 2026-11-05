using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using IniaAi.WebApp.Components.WorkflowEditor.State;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class BlockFailureSettingTests : SignedInRenderTest
{
    private const string EMAIL = "editor@inia.ai";

    [Theory]
    [InlineData(BlockTypes.MergeJson, BlockFailureSetting.FOLLOW_BLOCK_TYPE_CONTINUES_LABEL)]
    [InlineData(BlockTypes.Agent, BlockFailureSetting.FOLLOW_BLOCK_TYPE_STOPS_LABEL)]
    public async Task TheOptions_NameWhatTheBlockTypeDoes(BlockTypes type, string followLabel)
    {
        _ = await SignInAsync(EMAIL, Roles.ADMIN);
        Block block = NewBlock(type);

        IRenderedComponent<BlockFailureSetting> setting = RenderInApp<BlockFailureSetting>(p => p.Add(c => c.Block, block));

        Assert.Equal(
            [followLabel, BlockFailureSetting.STOP_THE_RUN_LABEL, BlockFailureSetting.CONTINUE_THE_RUN_LABEL],
            await OpenOptionsAsync(setting));
    }

    [Theory]
    [InlineData(BlockFailureSetting.STOP_THE_RUN_LABEL, true)]
    [InlineData(BlockFailureSetting.CONTINUE_THE_RUN_LABEL, false)]
    public async Task ChoosingAnOption_SetsTheBlock(string label, bool expected)
    {
        _ = await SignInAsync(EMAIL, Roles.ADMIN);
        Block block = NewBlock(BlockTypes.MergeJson);
        IRenderedComponent<BlockFailureSetting> setting = RenderInApp<BlockFailureSetting>(p => p.Add(c => c.Block, block));

        await ChooseAsync(setting, label);

        Assert.Equal(expected, block.Properties.IsCritical);
    }

    [Fact]
    public async Task ChoosingFollowTheBlockType_LeavesTheSettingUnset()
    {
        _ = await SignInAsync(EMAIL, Roles.ADMIN);
        Block block = NewBlock(BlockTypes.MergeJson);
        block.Properties.IsCritical = true;
        IRenderedComponent<BlockFailureSetting> setting = RenderInApp<BlockFailureSetting>(p => p.Add(c => c.Block, block));

        await ChooseAsync(setting, BlockFailureSetting.FOLLOW_BLOCK_TYPE_CONTINUES_LABEL);

        Assert.Null(block.Properties.IsCritical);
    }

    public static TheoryData<string, EditorSurfaceCapabilities> LibrarySurfaces => new()
    {
        { "library", EditorSurfaceCapabilities.Library },
        { "library block definition", EditorSurfaceCapabilities.Library with { IsSingleBlockHost = true } },
        { "library section definition", EditorSurfaceCapabilities.Library with { IsComponentDefinitionHost = true } },
    };

    [Theory]
    [MemberData(nameof(LibrarySurfaces))]
    public async Task InTheComponentLibrary_TheSettingIsHidden(string surface, EditorSurfaceCapabilities capabilities)
    {
        _ = await SignInAsync(EMAIL, Roles.ADMIN);
        Block block = NewBlock(BlockTypes.MergeJson);

        IRenderedComponent<BlockFailureSetting> setting = RenderInApp<BlockFailureSetting>(p => p
            .Add(c => c.Block, block)
            .AddCascadingValue(new EditorSurfaceState(capabilities)));

        Assert.True(string.IsNullOrWhiteSpace(setting.Markup), surface);
        Assert.Empty(setting.FindAll(".mud-select"));
    }

    [Fact]
    public async Task InTheWorkflowEditor_TheSettingIsShown()
    {
        _ = await SignInAsync(EMAIL, Roles.ADMIN);
        Block block = NewBlock(BlockTypes.MergeJson);

        IRenderedComponent<BlockFailureSetting> setting = RenderInApp<BlockFailureSetting>(p => p
            .Add(c => c.Block, block)
            .AddCascadingValue(new EditorSurfaceState(EditorSurfaceCapabilities.FullEditor)));

        Assert.Contains("On failure", setting.Markup);
        Assert.Contains(BlockFailureSetting.SELECT_LABEL, setting.Find(".mud-select").TextContent);
    }

    private static Block NewBlock(BlockTypes type)
    {
        var workflow = new Workflow("root");
        var section = new Section("Analysis", workflow);

        return new Block(type, section, "block") { Name = "Peer spread" };
    }

    private async Task<List<string>> OpenOptionsAsync(IRenderedComponent<BlockFailureSetting> setting)
    {
        await setting.InvokeAsync(() => setting.Find(".mud-select .mud-input-control").MouseDown());
        Popovers.WaitForAssertion(() => Assert.NotEmpty(Popovers.FindAll(".mud-popover-open .mud-list-item")));

        return [.. Popovers.FindAll(".mud-popover-open .mud-list-item").Select(item => item.TextContent.Trim())];
    }

    private async Task ChooseAsync(IRenderedComponent<BlockFailureSetting> setting, string label)
    {
        _ = await OpenOptionsAsync(setting);
        await setting.InvokeAsync(() => Popovers.FindAll(".mud-popover-open .mud-list-item")
            .Single(item => item.TextContent.Trim() == label)
            .Click());
    }
}
