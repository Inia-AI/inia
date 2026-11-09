using IniaAi.Common;
using IniaAi.Common.Constants;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Workflows.Strategies;

public class SwitchCaseStrategy : IBlockStrategy
{
    public Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        BlockStrategyOutput output = new();

        SwitchCaseProperties props = block.Properties as SwitchCaseProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(SwitchCaseProperties)}");

        props.ExecutionState ??= new (props);

        string? input = props.ExecutionState.Input?.Trim();

        if (string.IsNullOrEmpty(input))
        {
            context.Logger.LogWarning("{block} No input available for switch case matching", block);
            return Task.FromResult(output);
        }

        if (props.Cases.Count == 0)
        {
            context.Logger.LogWarning("{block} No cases configured", block);
            return Task.FromResult(output);
        }

        // Case-insensitive lookup with trimmed keys
        string? matchedValue = props.Cases
            .FirstOrDefault(c => string.Equals(c.Key.Trim(), input, StringComparison.OrdinalIgnoreCase))
            .Value;

        if (matchedValue is null)
        {
            if (props.DefaultValue is not null)
            {
                matchedValue = props.DefaultValue;
                context.Logger.LogInformation(
                    "{block} No case matched the input, using default value",
                    block);
            }
            else
            {
                context.Logger.LogWarning(
                    "{block} No case matched the input and no default value configured",
                    block);
                return Task.FromResult(output);
            }
        }
        else
        {
            context.Logger.LogInformation(
                "{block} Matched the input to a case",
                block);
        }

        // Resolve {placeholder} references using the shared extension method
        string resolvedValue = matchedValue.ReplacePlaceholders(
            asPlaceHolder: true,
            context.TextInputs,
            ReplacementTags.Comparison) ?? matchedValue;

        _ = new BlockResponse(block)
        {
            RawResponse = resolvedValue,
        };

        return Task.FromResult(output);
    }
}
