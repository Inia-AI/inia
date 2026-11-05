using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// The prompts a workflow summarises a document's pages with, carried by a document class or, as
/// the workflow's default, by its configuration. <see cref="Pages"/> reads each page with its
/// neighbours; <see cref="SinglePage"/> reads a page alone, as spreadsheets and CSV files are read.
/// </summary>
public class PageOverviewPrompts
{
    [JsonProperty(nameof(Pages), NullValueHandling = NullValueHandling.Ignore)]
    public PageOverviewPrompt? Pages { get; set; }

    [JsonProperty(nameof(SinglePage), NullValueHandling = NullValueHandling.Ignore)]
    public PageOverviewPrompt? SinglePage { get; set; }

    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => !IsUsable(Pages) && !IsUsable(SinglePage);

    /// <summary>
    /// The prompt a page of a class gets: the class's own, then the workflow's default. A page read
    /// alone takes the one-page versions first and falls back to the page prompts. Null means the
    /// page gets no overview.
    /// </summary>
    public static PageOverviewPrompt? Resolve(PageOverviewPrompts? classPrompts, PageOverviewPrompts? workflowDefault, bool singlePage)
    {
        PageOverviewPrompt? pages = Usable(classPrompts?.Pages) ?? Usable(workflowDefault?.Pages);

        return singlePage
            ? Usable(classPrompts?.SinglePage) ?? Usable(workflowDefault?.SinglePage) ?? pages
            : pages;
    }

    public bool ShouldSerializePages()
    {
        return IsUsable(Pages);
    }

    public bool ShouldSerializeSinglePage()
    {
        return IsUsable(SinglePage);
    }

    /// <summary>A copy without the blank prompts, or null when no prompt has text.</summary>
    public PageOverviewPrompts? WithoutBlankPrompts()
    {
        return IsEmpty
            ? null
            : new PageOverviewPrompts { Pages = Usable(Pages)?.Clone(), SinglePage = Usable(SinglePage)?.Clone() };
    }

    public static bool AreSame(PageOverviewPrompts? left, PageOverviewPrompts? right)
    {
        return left is null
            ? right is null
            : right is not null
                && PageOverviewPrompt.AreSame(left.Pages, right.Pages)
                && PageOverviewPrompt.AreSame(left.SinglePage, right.SinglePage);
    }

    public PageOverviewPrompts Clone()
    {
        return new PageOverviewPrompts
        {
            Pages = Pages?.Clone(),
            SinglePage = SinglePage?.Clone(),
        };
    }

    private static PageOverviewPrompt? Usable(PageOverviewPrompt? prompt)
    {
        return IsUsable(prompt) ? prompt : null;
    }

    private static bool IsUsable(PageOverviewPrompt? prompt)
    {
        return prompt is not null && !prompt.IsBlank;
    }
}

public class PageOverviewPrompt
{
    public const string ANSWER_TAG = "answer";
    public const string ANSWER_OPEN_TAG = $"<{ANSWER_TAG}>";
    public const string ANSWER_CLOSE_TAG = $"</{ANSWER_TAG}>";

    [JsonProperty(nameof(SystemPrompt), NullValueHandling = NullValueHandling.Ignore)]
    public string? SystemPrompt { get; set; }

    [JsonProperty(nameof(UserPrompt))]
    public string UserPrompt { get; set; } = string.Empty;

    [JsonProperty(nameof(ShouldUseAdvancedModel))]
    public bool ShouldUseAdvancedModel { get; set; }

    /// <summary>
    /// True without a user prompt, whatever the system prompt says: the user prompt carries the
    /// instruction and the answer format, so a page sent without it gets no usable overview.
    /// </summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsBlank => string.IsNullOrWhiteSpace(UserPrompt);

    /// <summary>
    /// What is wrong with the answer format the prompt shows, or null. A page's overview is read
    /// from between the answer tags, and the model copies the format it is shown, so a format that
    /// never closes the tag gets replies that never close it either.
    /// </summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AnswerFormatProblem
    {
        get
        {
            if (IsBlank)
            {
                return null;
            }

            if (!Mentions(ANSWER_OPEN_TAG))
            {
                return $"The answer format has no {ANSWER_OPEN_TAG} tag. Put the summary between {ANSWER_OPEN_TAG} and {ANSWER_CLOSE_TAG}, or pages get no overview.";
            }

            return Mentions(ANSWER_CLOSE_TAG)
                ? null
                : $"The answer format opens {ANSWER_OPEN_TAG} but never closes it. End it with {ANSWER_CLOSE_TAG}.";
        }
    }

    private bool Mentions(string tag)
    {
        return UserPrompt.Contains(tag, StringComparison.OrdinalIgnoreCase)
            || SystemPrompt?.Contains(tag, StringComparison.OrdinalIgnoreCase) is true;
    }

    public bool ShouldSerializeSystemPrompt()
    {
        return !string.IsNullOrEmpty(SystemPrompt);
    }

    public bool ShouldSerializeShouldUseAdvancedModel()
    {
        return ShouldUseAdvancedModel;
    }

    public static bool AreSame(PageOverviewPrompt? left, PageOverviewPrompt? right)
    {
        return left is null
            ? right is null
            : right is not null
                && left.SystemPrompt == right.SystemPrompt
                && left.UserPrompt == right.UserPrompt
                && left.ShouldUseAdvancedModel == right.ShouldUseAdvancedModel;
    }

    public PageOverviewPrompt Clone()
    {
        return new PageOverviewPrompt
        {
            SystemPrompt = SystemPrompt,
            UserPrompt = UserPrompt,
            ShouldUseAdvancedModel = ShouldUseAdvancedModel,
        };
    }
}
