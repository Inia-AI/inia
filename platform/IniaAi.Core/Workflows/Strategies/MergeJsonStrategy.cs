using IniaAi.Core.Common;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Strategies;

public class MergeJsonStrategy : IBlockStrategy
{
    // Match StringManipulationStrategy's option set so user-regex behavior is consistent.
    private const RegexOptions REGEX_OPTIONS = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Tag-name fragments. `*` glob → matches any one-or-more word/hyphen tag name; sub-`*` → zero-or-more.
    private const string TAG_NAME_ANY = @"[\w-]+";
    private const string TAG_NAME_GLOB_CHAR = @"[\w-]*";

    // Optional `(arg1, arg2, ...)` metadata after the tag name, e.g. <page1(file.pdf)>.
    private const string TAG_PARENS_SUFFIX = @"(?:\([^)]*\))?";

    // Glob wildcard literal users type — surfaced as a const so the UI default and strategy interpreter agree.
    private const string GLOB_WILDCARD = "*";

    // Capture-group indices into a successful match.
    // XmlTagPattern: 0=full, 1=tag name (for backreference \1), 2=JSON content.
    // Regex: 0=full, 1=user-defined JSON content.
    private const int XML_CONTENT_GROUP = 2;
    private const int REGEX_CONTENT_GROUP = 1;

    public Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BlockStrategyOutput output = new();

        block.ExecutionState ??= new BlockExecutionState(block);

        MergeJsonProperties props = block.Properties as MergeJsonProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(MergeJsonProperties)}");

        props.ExecutionState ??= new(props);

        List<JToken> validTokens = [];

        for (int i = 0; i < props.ExecutionState.JsonInputs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string value = props.ExecutionState.JsonInputs[i];
            MergeJsonInputExtraction config = ResolveExtractionConfig(props.ExecutionState, i);

            if (config.Mode is JsonExtractionMode.Single)
            {
                AddSingleJsonToken(value, i, validTokens, block, context, props.ExecutionState.PreParseTransforms);
                continue;
            }

            List<string> snippets = ExtractSnippets(value, config, block, context, i, cancellationToken);
            for (int s = 0; s < snippets.Count; s++)
            {
                AddSingleJsonToken(snippets[s], i, validTokens, block, context, props.ExecutionState.PreParseTransforms);
            }
        }

        // Skipping one invalid input is a designed pattern; a critical block with nothing left fails.
        if (validTokens.Count == 0 && block.AbortsExecutionOnFailure())
        {
            context.Logger.LogWarning("{block} No valid JSON input to merge", block);
            block.ExecutionState.Error =
                $"The {BlockTypeLabels.Of(block.Type)} block '{block.Name}' had no valid JSON input, so it failed. "
                + "Check that the blocks feeding it return JSON.";
            return Task.FromResult(output);
        }

        string result = MergeTokens(validTokens);

        _ = new BlockResponse(block)
        {
            RawResponse = result,
            JsonResponse = result,
        };

        context.Logger.LogTrace(
            "{block} Merged JSON ({InputCount} inputs):\n{result}",
            block, validTokens.Count, result);

        return Task.FromResult(output);
    }

    private static MergeJsonInputExtraction ResolveExtractionConfig(
        MergeJsonPropertiesExecutionState state,
        int index)
    {
        if (index < state.InputExtractions.Count)
        {
            return state.InputExtractions[index];
        }

        return new MergeJsonInputExtraction { Mode = JsonExtractionMode.Single };
    }

    private static void AddSingleJsonToken(
        string value,
        int inputIndex,
        List<JToken> validTokens,
        Block block,
        BlockExecutionContext context,
        IReadOnlyList<MergeJsonPreParseTransform> transforms)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            context.Logger.LogWarning(
                "{block} Skipping empty JSON input at index {Index}",
                block, inputIndex);
            return;
        }

        value = ApplyPreParseTransforms(value, transforms, inputIndex, block, context);

        try
        {
            var token = JToken.Parse(value);

            if (token.Type is JTokenType.Null)
            {
                context.Logger.LogWarning(
                    "{block} Skipping null JSON literal at index {Index}",
                    block, inputIndex);
                return;
            }

            validTokens.Add(token);
        }
        catch (Exception ex)
        {
            context.Logger.LogWarning(
                ex,
                "{block} Skipping invalid JSON input at index {Index}",
                block, inputIndex);
        }
    }

    // Applies each configured find/replace to the raw input BEFORE parsing — this is how an author
    // sanitises text that would otherwise be invalid JSON (e.g. digit-group separators). A rule with a
    // broken pattern or runaway backtracking is skipped with a warning so one bad rule can't sink the input.
    private static string ApplyPreParseTransforms(
        string value,
        IReadOnlyList<MergeJsonPreParseTransform>? transforms,
        int inputIndex,
        Block block,
        BlockExecutionContext context)
    {
        // Tolerate a persisted/deserialized null list (the property initializer only applies when the
        // key is absent; an explicit "PreParseTransforms": null would otherwise NPE the whole block).
        if (transforms is null || transforms.Count == 0)
        {
            return value;
        }

        for (int t = 0; t < transforms.Count; t++)
        {
            MergeJsonPreParseTransform transform = transforms[t];

            try
            {
                value = RegexReplacer.Replace(
                    value, transform.Find, transform.ReplaceWith, transform.IsRegex, transform.CaseSensitive);
            }
            catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
            {
                context.Logger.LogWarning(
                    ex,
                    "{block} Skipping pre-parse transform {TransformIndex} at input index {Index} "
                    + "(isRegex={IsRegex}, find={Find})",
                    block, t, inputIndex, transform.IsRegex, transform.Find);
            }
        }

        return value;
    }

    private static List<string> ExtractSnippets(
        string inputText,
        MergeJsonInputExtraction config,
        Block block,
        BlockExecutionContext context,
        int inputIndex,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputText))
        {
            return [];
        }

        string regexPattern = config.Mode switch
        {
            JsonExtractionMode.XmlTagPattern => BuildXmlTagRegex(config.Pattern),
            JsonExtractionMode.Regex => config.Pattern,
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(regexPattern))
        {
            context.Logger.LogWarning(
                "{block} Empty extraction pattern at input index {Index} (mode={Mode}); skipping",
                block, inputIndex, config.Mode);
            return [];
        }

        int contentGroup = config.Mode is JsonExtractionMode.XmlTagPattern
            ? XML_CONTENT_GROUP
            : REGEX_CONTENT_GROUP;

        try
        {
            var regex = new Regex(regexPattern, REGEX_OPTIONS, RegexReplacer.RegexTimeout);

            List<string> snippets = [];
            bool warnedMissingGroup = false;

            foreach (Match match in regex.Matches(inputText))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!match.Success)
                {
                    continue;
                }

                if (match.Groups.Count <= contentGroup)
                {
                    if (!warnedMissingGroup)
                    {
                        // User-supplied regex without a capture group will silently produce nothing
                        // unless we tell them — patterns like `\d+` (no parens) hit this path.
                        context.Logger.LogWarning(
                            "{block} Pattern at input index {Index} has no capture group {ContentGroup}; "
                            + "expected {ContentGroup} to wrap the JSON snippet (mode={Mode}, pattern={Pattern})",
                            block, inputIndex, contentGroup, contentGroup, config.Mode, config.Pattern);
                        warnedMissingGroup = true;
                    }

                    continue;
                }

                snippets.Add(match.Groups[contentGroup].Value);
            }

            return snippets;
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
        {
            context.Logger.LogWarning(
                ex,
                "{block} JSON extraction failed at input index {Index} (mode={Mode}, pattern={Pattern})",
                block, inputIndex, config.Mode, config.Pattern);
            return [];
        }
    }

    private static string BuildXmlTagRegex(string glob)
    {
        if (string.IsNullOrEmpty(glob))
        {
            return string.Empty;
        }

        string nameRegex = glob == GLOB_WILDCARD
            ? TAG_NAME_ANY
            : GlobToRegex(glob);

        return $@"<({nameRegex}{TAG_PARENS_SUFFIX})>([\s\S]*?)</\1>";
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new System.Text.StringBuilder(glob.Length * 2);

        foreach (char c in glob)
        {
            if (c == '*')
            {
                _ = sb.Append(TAG_NAME_GLOB_CHAR);
            }
            else
            {
                _ = sb.Append(Regex.Escape(c.ToString()));
            }
        }

        return sb.ToString();
    }

    private static string MergeTokens(List<JToken> validTokens)
    {
        if (validTokens.Count == 0)
        {
            return "{}";
        }

        if (validTokens.Count == 1)
        {
            return validTokens[0].ToString(Newtonsoft.Json.Formatting.Indented);
        }

        if (validTokens.All(t => t is JObject))
        {
            JObject merged = [];
            foreach (JObject obj in validTokens.Cast<JObject>())
            {
                JsonMergeHelpers.DeepMergeObjects(merged, obj);
            }

            return merged.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        if (validTokens.All(t => t is JArray))
        {
            JArray merged = [];
            foreach (JArray arr in validTokens.Cast<JArray>())
            {
                foreach (JToken item in arr)
                {
                    merged.Add(item.DeepClone());
                }
            }

            return merged.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        JArray collected = [];
        foreach (JToken token in validTokens)
        {
            collected.Add(token.DeepClone());
        }

        return collected.ToString(Newtonsoft.Json.Formatting.Indented);
    }
}
