using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common;
using IniaAi.Core.Documents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows.Blocks;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Common.Helpers;

public partial class RequestContentSequenceHelper
{
    /// <summary>
    /// Hard per-request image limit of the model APIs. The content builder never attaches
    /// more page images than this; OCR-image-only pages keep their images first because
    /// the image is their only content.
    /// </summary>
    public const int MAX_IMAGES_PER_REQUEST = 100;

    public const string CITATIONS_APPENDAGE =
    """

    Use citations for each numerical or date value.

    When citing values from sources, reproduce numbers and units exactly as they appear in the source. Do not round, abbreviate, convert units, or change precision (e.g. do not change 18.05% to 18.1%, $183,866 million to $184 billion, or 1,250 bps to 12.5%). If a source gives a value to two decimal places, your output must use the same two decimal places.
    """;

    /// <summary>
    /// Splits the user prompt template into a content sequence, replacing placeholders with either text inputs
    /// or block reference contents.
    /// <para>
    /// Placeholders corresponding to block references are replaced with content chunks that include
    /// metadata such as title and context. These will be used for citation handling in the AI model.
    /// </para>
    /// </summary>
    /// <param name="userPromptTemplate">A user prompt template containing placeholders.</param>
    /// <param name="textInputs">All values for placeholders in the template.</param>
    /// <param name="referencedBlocks">A mapping of placeholders to their corresponding block references.</param>
    /// <param name="shouldGetCitationsFromModel">Indicates whether to treat block reference contents as citation sources.</param>
    /// <returns>A content sequence representing the populated user prompt.</returns>
    public static RequestContentSequence? BuildUserPromptContentSequence(
        string? userPromptTemplate,
        Dictionary<string, string?> textInputs,
        Dictionary<string, Block> referencedBlocks,
        bool shouldGetCitationsFromModel)
    {
        if (string.IsNullOrWhiteSpace(userPromptTemplate))
        {
            return null;
        }

        RequestContentSequence userPromptContent = [];

        ReplaceNonBlockPlaceholders(
            userPromptTemplate,
            textInputs,
            referencedBlocks,
            out string populatedPrompt);

        // Build case-insensitive lookup for block keys that have non-empty input.
        Dictionary<string, string> blockKeyLookup = new(StringComparer.InvariantCultureIgnoreCase);
        foreach (string key in referencedBlocks.Keys)
        {
            if (textInputs.TryGetValue(key, out string? inputText)
                && !string.IsNullOrWhiteSpace(inputText))
            {
                _ = blockKeyLookup.TryAdd(key, key);
            }
        }

        // Single regex pass finds all placeholder occurrences with their positions.
        // Walk matches left-to-right, emitting text segments and block content.
        MatchCollection matches = PlaceholderPattern().Matches(populatedPrompt);
        int lastEnd = 0;

        foreach (Match match in matches)
        {
            string placeholderName = match.Groups[1].Value;

            if (!blockKeyLookup.TryGetValue(placeholderName, out string? canonicalKey))
            {
                continue;
            }

            Block referencedBlock = referencedBlocks[canonicalKey];

            string chunkBeforePlaceholder = populatedPrompt[lastEnd..match.Index];

            if (!string.IsNullOrWhiteSpace(chunkBeforePlaceholder))
            {
                userPromptContent.Add(chunkBeforePlaceholder);
            }

            string blockText = textInputs[canonicalKey]!;

            string title = CitationTitleOf(referencedBlock, canonicalKey);

            if (!shouldGetCitationsFromModel)
            {
                // Insert as plain text without metadata so that it would not be
                // treated as a citation source.
                userPromptContent.Add(blockText);
            }
            else
            {
                userPromptContent.Add(new RequestCommonContent(blockText)
                {
                    IsBlockReference = true,
                    Title = title,
                    ReferenceId = referencedBlock.Id,
                    Context = $"Output from block {title}",
                });
            }

            lastEnd = match.Index + match.Length;
        }

        string remaining = populatedPrompt[lastEnd..];
        if (!string.IsNullOrWhiteSpace(remaining))
        {
            userPromptContent.Add(remaining);
        }

        if (shouldGetCitationsFromModel)
        {
            userPromptContent.Add(CITATIONS_APPENDAGE);
        }

        userPromptContent.ClearWhiteSpace();
        userPromptContent.Condense();

        return userPromptContent;
    }

    public static RequestContentSequence? BuildContentSequence(
        string? userPrompt,
        Dictionary<int, Page> inputContext,
        bool shouldIncludeImages,
        bool shouldUseOverviews,
        bool shouldGetCitationsFromModel)
    {
        if (userPrompt is null
            && inputContext.Count == 0)
        {
            return null;
        }

        RequestContentSequence contentSequence = [];

        FillContentSequenceWithInputContext(
            contentSequence,
            userPrompt,
            inputContext,
            shouldIncludeImages,
            shouldUseOverviews,
            shouldGetCitationsFromModel);

        contentSequence.ClearWhiteSpace();
        contentSequence.Condense();

        return contentSequence;
    }

    public static RequestContentSequence? BuildContentSequence(
        RequestContentSequence? userPromptContent,
        Dictionary<int, Page> inputContext,
        bool shouldIncludeImages,
        bool shouldUseOverviews,
        bool shouldGetCitationsFromModel)
    {
        if ((userPromptContent is null
            || !userPromptContent.Any())
            && inputContext.Count == 0)
        {
            return null;
        }

        RequestContentSequence contentSequence = [];

        FillContentSequenceWithInputContext(
            contentSequence,
            userPrompt: null,
            inputContext,
            shouldIncludeImages,
            shouldUseOverviews,
            shouldGetCitationsFromModel);

        foreach (RequestCommonContent content in userPromptContent ?? [])
        {
            contentSequence.Add(content);
        }

        contentSequence.ClearWhiteSpace();
        contentSequence.Condense();

        return contentSequence.Any()
            ? contentSequence
            : null;
    }

    /// <summary>
    /// The title a block's output carries when a model or the producing-block rule cites it: its
    /// resolved output title, else its name, else <paramref name="fallback"/>, so the reader never
    /// sees a citation with no name.
    /// </summary>
    public static string CitationTitleOf(Block block, string fallback)
    {
        return block.ExecutionState?.OutputTitle
            ?? (!string.IsNullOrWhiteSpace(block.Name) ? block.Name : fallback);
    }

    private static void FillContentSequenceWithInputContext(
        RequestContentSequence contentSequence,
        string? userPrompt,
        Dictionary<int, Page> inputContext,
        bool shouldIncludeImages,
        bool shouldUseOverviews,
        bool shouldGetCitationsFromModel)
    {
        HashSet<int> indicesWithImageBudget = SelectPageIndicesWithinImageCap(
            inputContext,
            shouldIncludeImages);

        foreach ((int index, Page page) in inputContext)
        {
            string pageName = PagesHelper.GetPageName(index, page);

            if (indicesWithImageBudget.Contains(index)
                && page.Image is not null)
            {
                contentSequence.Add($"<{pageName}-image>");
                contentSequence.Add(page.Image);
                contentSequence.Add($"</{pageName}-image>");
            }

            string? textContent = shouldUseOverviews
                ? page.Overview
                : page.Text;

            if (textContent is not null)
            {
                string formattedText = PageFormatting.FormatPageText(pageName, textContent, page.SourceUrl);

                if (!shouldGetCitationsFromModel)
                {
                    // Insert as plain text without metadata so that it would not be
                    // treated as a citation source.
                    contentSequence.Add(formattedText);
                }
                else
                {
                    contentSequence.Add(new RequestCommonContent(formattedText)
                    {
                        IsInputContext = true,
                        Title = pageName,
                        ReferenceId = page.Id,
                        Context = "A page of text from a file " + pageName,
                    });
                }
            }
        }

        if (userPrompt is not null)
        {
            contentSequence.Add(userPrompt);
        }
    }

    private static HashSet<int> SelectPageIndicesWithinImageCap(
        Dictionary<int, Page> inputContext,
        bool shouldIncludeImages)
    {
        List<KeyValuePair<int, Page>> candidates = [.. inputContext
            .Where(kvp => (shouldIncludeImages || kvp.Value.IsOcrImageOnly)
                && kvp.Value.Image is not null)];

        if (candidates.Count <= MAX_IMAGES_PER_REQUEST)
        {
            return [.. candidates.Select(kvp => kvp.Key)];
        }

        return [.. candidates
            .OrderByDescending(kvp => kvp.Value.IsOcrImageOnly)
            .ThenBy(kvp => kvp.Key)
            .Take(MAX_IMAGES_PER_REQUEST)
            .Select(kvp => kvp.Key)];
    }

    private static void ReplaceNonBlockPlaceholders(
        string userPromptTemplate,
        Dictionary<string, string?> textInputs,
        Dictionary<string, Block> referencedBlocks,
        out string populatedPrompt)
    {
        var nonBlockTextInputs = textInputs
            .Where(kvp => !referencedBlocks.ContainsKey(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        populatedPrompt = userPromptTemplate.ReplacePlaceholders(
            asPlaceHolder: true,
            nonBlockTextInputs,
            StringComparison.InvariantCultureIgnoreCase)
            ?? userPromptTemplate;
    }

    /// <summary>
    /// Matches placeholders in both single-brace {name} and double-brace {{name}} forms.
    /// The captured group contains the placeholder name.
    /// Must stay in sync with GET_PLACEHOLDERS in StringExtensions.
    /// </summary>
    [GeneratedRegex(@"\{\{?([a-zA-Z0-9._\-\[\] &/()%]+)\}?\}")]
    private static partial Regex PlaceholderPattern();
}
