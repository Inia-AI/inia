using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// Every platform type that runs has a strategy, so a type added without one fails here and not in
/// a customer's run. Loop markers run no strategy: the engine expands the loop before the run. A
/// closed module's types get their strategy from the module.
/// </summary>
public class BlockStrategiesTests
{
    public static TheoryData<BlockTypes> PlatformTypesThatRun()
    {
        return [.. Enum.GetValues<BlockTypes>().Where(static type => !ModuleBlockTypes.Contains(type) && !new Block(type).IsLoopBoundary)];
    }

    [Theory]
    [MemberData(nameof(PlatformTypesThatRun))]
    public void EveryPlatformType_HasAStrategy(BlockTypes type)
    {
        Assert.True(
            new BlockStrategies([]).ByType.ContainsKey(type),
            $"{type} has no strategy. Add one to the platform list in IniaAi.Core/Workflows/Strategies/BlockStrategies.cs.");
    }
}
