using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Samples.WordCount;

/// <summary>Runs a Count Words block: its output is the number of words in its text.</summary>
public sealed class WordCountStrategy : IBlockStrategy
{
    public Task<BlockStrategyOutput> ExecuteBlockAsync(Block block, BlockExecutionContext context, CancellationToken cancellationToken = default)
    {
        WordCountProperties properties = block.Properties as WordCountProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(WordCountProperties)}");

        properties.ExecutionState ??= new WordCountExecutionState(properties);
        int words = context.Services.GetRequiredService<IWordCounter>().Count(properties.ExecutionState.Text ?? string.Empty);

        _ = new BlockResponse(block)
        {
            RawResponse = words.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        return Task.FromResult(new BlockStrategyOutput());
    }
}
