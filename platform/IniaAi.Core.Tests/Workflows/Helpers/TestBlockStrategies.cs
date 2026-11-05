using System.Reflection;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Helpers;

/// <summary>
/// Swaps a block type's strategy on a live handler, returning the one it replaced so a caller can
/// put it back. <c>_blockStrategies</c> is populated in the handler's constructor and has no seam,
/// so reflection is the only way to put a stub node in a real run, and reaching in here is
/// preferred over widening <see cref="WorkflowHandler"/>'s surface for a test-only need.
/// </summary>
internal static class TestBlockStrategies
{
    internal static IBlockStrategy Swap(WorkflowHandler handler, BlockTypes blockType, IBlockStrategy newStrategy)
    {
        FieldInfo field = typeof(WorkflowHandler)
            .GetField("_blockStrategies", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_blockStrategies field not found on WorkflowHandler");

        var strategies = (Dictionary<BlockTypes, IBlockStrategy>)field.GetValue(handler)!;
        IBlockStrategy original = strategies[blockType];
        strategies[blockType] = newStrategy;

        return original;
    }
}
