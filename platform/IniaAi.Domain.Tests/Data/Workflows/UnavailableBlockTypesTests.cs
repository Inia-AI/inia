using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

/// <summary>
/// A person reads these messages on a block this build cannot run: in a run's log and in the
/// editor. A closed module's block names the edition that includes it. Any other block names its
/// type's number and the extension that would add it.
/// </summary>
public class UnavailableBlockTypesTests
{
    private const BlockTypes TYPE_NO_REGISTRATION_KNOWS = (BlockTypes)1500;

    [Fact]
    public void TheRunMessage_ForAModuleBlock_NamesTheFullEdition()
    {
        Assert.Equal(
            "The platform does not include JSON to XLSX blocks, so this block did not run. Remove the block if the workflow does not need it. The full edition includes JSON to XLSX blocks.",
            UnavailableBlockTypes.RunMessage(BlockTypes.Json2Xlsx));
    }

    [Fact]
    public void TheEditorMessage_ForAModuleBlock_NamesTheFullEdition()
    {
        Assert.Equal(
            "The platform does not include JSON to XLSX blocks, so this block cannot run. Its settings stay as they were saved. Remove the block if the workflow does not need it. The full edition includes JSON to XLSX blocks.",
            UnavailableBlockTypes.EditorMessage(BlockTypes.Json2Xlsx));
    }

    [Fact]
    public void TheRunMessage_ForABlockOfAnUnknownType_NamesItsNumber()
    {
        Assert.Equal(
            "The platform does not include the type of this block (number 1500), so this block did not run. Remove the block if the workflow does not need it, or install the extension that adds it.",
            UnavailableBlockTypes.RunMessage(TYPE_NO_REGISTRATION_KNOWS));
    }

    [Fact]
    public void TheRunMessage_ForAPlatformNumberThisReleaseLacks_SaysToUpdateThePlatform()
    {
        Assert.Equal(
            "The platform does not include the type of this block (number 31), so this block did not run. Remove the block if the workflow does not need it, or update the platform to a release that includes it.",
            UnavailableBlockTypes.RunMessage((BlockTypes)31));
    }

    [Fact]
    public void TheEditorMessage_ForABlockOfAnUnknownType_NamesItsNumber()
    {
        Assert.Equal(
            "The platform does not include the type of this block (number 1500), so this block cannot run. Its settings stay as they were saved. Remove the block if the workflow does not need it, or install the extension that adds it.",
            UnavailableBlockTypes.EditorMessage(TYPE_NO_REGISTRATION_KNOWS));
    }
}
