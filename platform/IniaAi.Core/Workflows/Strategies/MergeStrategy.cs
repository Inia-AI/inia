using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Workflows.Strategies;

public class MergeStrategy : IBlockStrategy
{
    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        BlockStrategyOutput output = new();

        MergeProperties props = block.Properties as MergeProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(MergeProperties)}");

        props.ExecutionState ??= new (props);

        if (props.ExecutionState.Text is null)
        {
            context.Logger.LogWarning(
                "{block} No text available",
                block);

            await Task.CompletedTask;

            return output;
        }

        _ = new BlockResponse(block)
        {
            RawResponse = props.ExecutionState.Text,
        };

        context.Logger.LogTrace(
            "{block} Merged text:\n{mergedText}",
            block,
            block.BlockResponse?.RawResponse);

        await Task.CompletedTask;

        return output;
    }
}
