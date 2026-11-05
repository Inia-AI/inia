using IniaAi.Core.Common;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Strategies;

public class StringManipulationStrategy : IBlockStrategy
{
    // Visible, unambiguous tokens — Zapier-style. Always substituted on every string
    // field the strategy reads, regardless of which operation is selected.
    // Canonical definitions live on RegexReplacer; aliased here for existing call sites.
    public const string TOKEN_NEWLINE = RegexReplacer.TOKEN_NEWLINE;
    public const string TOKEN_TAB = RegexReplacer.TOKEN_TAB;
    public const string TOKEN_SPACE = RegexReplacer.TOKEN_SPACE;
    public const string TOKEN_RETURN = RegexReplacer.TOKEN_RETURN;

    public Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        BlockStrategyOutput output = new();
        block.ExecutionState ??= new BlockExecutionState(block);

        StringManipulationProperties props = block.Properties as StringManipulationProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(StringManipulationProperties)}");

        props.ExecutionState ??= new (props);

        string? rawInput = props.ExecutionState.Input;

        if (rawInput is null)
        {
            context.Logger.LogWarning("{block} No input available for string manipulation", block);
            return Task.FromResult(output);
        }

        string input = NormalizeTokens(rawInput);
        string find = NormalizeTokens(props.ExecutionState.Find ?? string.Empty);
        string replaceWith = NormalizeTokens(props.ExecutionState.ReplaceWith ?? string.Empty);
        string separator = NormalizeTokens(props.ExecutionState.Separator ?? string.Empty);
        string trimCharacters = NormalizeTokens(props.ExecutionState.TrimCharacters ?? string.Empty);

        string result;
        try
        {
            result = props.Operation switch
            {
                StringManipulationOperation.Replace => RegexReplacer.Replace(input, find, replaceWith, isRegex: false, props.CaseSensitive),
                StringManipulationOperation.ReplaceUsingPattern => RegexReplacer.Replace(input, find, replaceWith, isRegex: true, props.CaseSensitive),
                StringManipulationOperation.Substring => Substring(input, props),
                StringManipulationOperation.Trim => Trim(input, trimCharacters, props.TrimSide),
                StringManipulationOperation.Split => Split(input, separator, props.SplitIndex),
                StringManipulationOperation.Join => Join(input, separator),
                StringManipulationOperation.ChangeCase => ChangeCase(input, props.CaseType),
                StringManipulationOperation.Length => input.Length.ToString(CultureInfo.InvariantCulture),
                StringManipulationOperation.EscapeSpecialCharacters => EscapeSpecialCharacters(input, props),
                StringManipulationOperation.UnescapeSpecialCharacters => UnescapeSpecialCharacters(input, props),
                StringManipulationOperation.Distinct => Distinct(input, props.CaseSensitive),
                _ => throw new NotImplementedException($"Operation {props.Operation} not yet implemented")
            };
        }
        catch (Exception ex) when (ex is not NotImplementedException)
        {
            context.Logger.LogWarning(ex, "{block} String manipulation failed", block);

            if (block.AbortsExecutionOnFailure())
            {
                block.ExecutionState.Error =
                    $"The {BlockTypeLabels.Of(block.Type)} block '{block.Name}' could not process its input, so it failed. "
                    + FailureNextStep(props.Operation, ex);
            }

            return Task.FromResult(output);
        }

        props.ExecutionState.Output = result;

        _ = new BlockResponse(block)
        {
            RawResponse = result,
        };

        return Task.FromResult(output);
    }

    internal static string FailureNextStep(StringManipulationOperation operation, Exception failure)
    {
        if (failure is RegexMatchTimeoutException)
        {
            return "Its Find (regex pattern) took too long on this input. Simplify the pattern, or split the input.";
        }

        return operation switch
        {
            StringManipulationOperation.ReplaceUsingPattern => "Check that its Find (regex pattern) is a valid regular expression.",
            StringManipulationOperation.Join => "Check that its input is a JSON list of text values, such as [\"a\", \"b\"].",
            _ => "Check its settings and the input it receives.",
        };
    }

    // Every TOKEN_NEWLINE / TOKEN_TAB / TOKEN_SPACE / TOKEN_RETURN becomes its actual character.
    // Users never type backslashes; they type visible tokens that mean exactly what they say.
    private static string NormalizeTokens(string value)
    {
        return RegexReplacer.NormalizeTokens(value);
    }

    private static string Substring(string input, StringManipulationProperties props)
    {
        int start = Math.Max(0, props.StartIndex ?? 0);
        if (start >= input.Length)
        {
            return string.Empty;
        }

        if (props.Length is null)
        {
            return input[start..];
        }

        int length = Math.Max(0, props.Length.Value);
        length = Math.Min(length, input.Length - start);
        return input.Substring(start, length);
    }

    private static string Trim(string input, string trimCharacters, StringManipulationTrimSide side)
    {
        char[]? chars = trimCharacters.Length == 0 ? null : trimCharacters.ToCharArray();
        return side switch
        {
            StringManipulationTrimSide.Start => chars is null ? input.TrimStart() : input.TrimStart(chars),
            StringManipulationTrimSide.End => chars is null ? input.TrimEnd() : input.TrimEnd(chars),
            _ => chars is null ? input.Trim() : input.Trim(chars),
        };
    }

    private static string Split(string input, string separator, int? splitIndex)
    {
        if (separator.Length == 0)
        {
            return input;
        }

        string[] parts = input.Split(separator, StringSplitOptions.None);
        if (splitIndex is null)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(parts);
        }

        int index = splitIndex.Value;
        if (index < 0)
        {
            index = parts.Length + index;
        }

        if (index < 0 || index >= parts.Length)
        {
            return string.Empty;
        }

        return parts[index];
    }

    private static string Join(string input, string separator)
    {
        string[]? items = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(input)
            ?? throw new InvalidOperationException("Join operation expects input to resolve to a JSON array of strings.");

        return string.Join(separator, items);
    }

    private static string ChangeCase(string input, StringManipulationCaseType caseType)
    {
        return caseType switch
        {
            StringManipulationCaseType.Upper => input.ToUpperInvariant(),
            StringManipulationCaseType.Lower => input.ToLowerInvariant(),
            StringManipulationCaseType.Title => CultureInfo.InvariantCulture.TextInfo
                .ToTitleCase(input.ToLowerInvariant()),
            StringManipulationCaseType.Sentence => ToSentenceCase(input),
            _ => input,
        };
    }

    private static string ToSentenceCase(string input)
    {
        if (input.Length == 0)
        {
            return input;
        }

        string lower = input.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private static string EscapeSpecialCharacters(string input, StringManipulationProperties props)
    {
        // Backslash first — otherwise the '\' in subsequently-inserted '\n' etc. would be re-escaped.
        string result = input;
        if (props.EscapeBackslashes)
        {
            result = result.Replace("\\", "\\\\");
        }

        if (props.EscapeNewlines)
        {
            result = result.Replace("\n", "\\n");
        }

        if (props.EscapeCarriageReturns)
        {
            result = result.Replace("\r", "\\r");
        }

        if (props.EscapeTabs)
        {
            result = result.Replace("\t", "\\t");
        }

        if (props.EscapeDoubleQuotes)
        {
            result = result.Replace("\"", "\\\"");
        }

        return result;
    }

    private static string Distinct(string input, bool caseSensitive)
    {
        StringComparer comparer = caseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

        return string.Join('\n', input
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(comparer));
    }

    private static string UnescapeSpecialCharacters(string input, StringManipulationProperties props)
    {
        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c != '\\' || i + 1 >= input.Length)
            {
                sb.Append(c);
                continue;
            }

            char next = input[i + 1];
            char? replacement = next switch
            {
                '\\' when props.EscapeBackslashes => '\\',
                'n' when props.EscapeNewlines => '\n',
                'r' when props.EscapeCarriageReturns => '\r',
                't' when props.EscapeTabs => '\t',
                '"' when props.EscapeDoubleQuotes => '"',
                _ => null,
            };

            if (replacement is null)
            {
                sb.Append(c);
                continue;
            }

            sb.Append(replacement.Value);
            i++;
        }

        return sb.ToString();
    }
}
