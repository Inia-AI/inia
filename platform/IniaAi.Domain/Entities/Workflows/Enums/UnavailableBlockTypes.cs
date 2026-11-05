using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// What a person reads about a block this build cannot run: a closed module's block in the open
/// build, a block of an extension's type when the extension is not installed, or a block of a type
/// a later release added.
/// </summary>
public static class UnavailableBlockTypes
{
    /// <summary>
    /// Whether a block of this type runs only when its strategy comes from outside the platform's
    /// own list: a closed module's type, an extension's type, or a type the catalog lacks.
    /// </summary>
    public static bool IsOutsideThePlatform(BlockTypes type)
    {
        return ModuleBlockTypes.Contains(type) || BlockTypeCatalog.IsExtensionType(type) || BlockTypeCatalog.Find(type) is null;
    }

    public static string RunMessage(BlockTypes type)
    {
        return Message(type, "so this block did not run.");
    }

    public static string EditorMessage(BlockTypes type)
    {
        return Message(type, "so this block cannot run. Its settings stay as they were saved.");
    }

    private static string Message(BlockTypes type, string consequence)
    {
        if (ModuleBlockTypes.Contains(type))
        {
            string label = BlockTypeLabels.Of(type);
            return $"The platform does not include {label} blocks, {consequence} Remove the block if the workflow does not need it. {ProductEditions.FULL_EDITION} includes {label} blocks.";
        }

        string remedy = BlockTypeCatalog.IsExtensionType(type)
            ? "install the extension that adds it"
            : "update the platform to a release that includes it";

        return $"The platform does not include the type of this block (number {(int)type}), {consequence} Remove the block if the workflow does not need it, or {remedy}.";
    }
}
