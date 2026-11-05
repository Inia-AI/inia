using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

/// <summary>
/// Every platform block type has a definition, so none of its blocks reads as unavailable, and
/// every platform type stays below the numbers extensions take, so an extension's stored block never
/// collides with a later platform type.
/// </summary>
public class BlockTypeCatalogTests
{
    public static TheoryData<BlockTypes> PlatformTypes()
    {
        return [.. Enum.GetValues<BlockTypes>()];
    }

    [Theory]
    [MemberData(nameof(PlatformTypes))]
    public void EveryPlatformType_HasADefinition(BlockTypes type)
    {
        Assert.NotNull(BlockTypeCatalog.Find(type));
    }

    [Theory]
    [MemberData(nameof(PlatformTypes))]
    public void EveryPlatformType_StaysBelowTheExtensionNumbers(BlockTypes type)
    {
        Assert.False(BlockTypeCatalog.IsExtensionType(type));
    }
}
