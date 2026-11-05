using IniaAi.Common;
using IniaAi.Common.Numerics;
using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace IniaAi.Domain.Entities.Workflows;

public class RagSettingsExecutionState : JsonSerializable<RagSettingsExecutionState>
{
    private string _numberOfPagesInput = string.Empty;
    private string _startIndexInput = string.Empty;
    private string _endIndexInput = string.Empty;
    private string _pageAdjustmentInput = string.Empty;

    [JsonProperty(nameof(ExactIndexesInput), NullValueHandling = NullValueHandling.Ignore)]
    public string ExactIndexesInput { get; set; } = string.Empty;

    [JsonProperty(nameof(NumberOfPagesInput), NullValueHandling = NullValueHandling.Ignore)]
    public string NumberOfPagesInput
    {
        get => string.IsNullOrEmpty(_numberOfPagesInput)
            ? $"1"
            : _numberOfPagesInput;

        set => _numberOfPagesInput = value;
    }

    [JsonProperty(nameof(StartIndexInput))]
    public string StartIndexInput
    {
        get => string.IsNullOrEmpty(_startIndexInput)
            ? $"0"
            : _startIndexInput;

        set => _startIndexInput = value;
    }
    [JsonProperty(nameof(EndIndexInput))]
    public string EndIndexInput
    {
        get => string.IsNullOrEmpty(_endIndexInput)
            ? $"{int.MaxValue}"
            : _endIndexInput;

        set => _endIndexInput = value;
    }

    [JsonProperty(nameof(PageAdjustmentInput))]
    public string PageAdjustmentInput
    {
        get => string.IsNullOrEmpty(_pageAdjustmentInput)
            ? $"0"
            : _pageAdjustmentInput;

        set => _pageAdjustmentInput = value;
    }

    [JsonProperty(nameof(TopicsQuery), NullValueHandling = NullValueHandling.Ignore)]
    public string? TopicsQuery { get; set; }

    [JsonProperty(nameof(TableOfContentsInput), NullValueHandling = NullValueHandling.Ignore)]
    public string? TableOfContentsInput { get; set; }

    [JsonProperty(nameof(QueryTemplate), NullValueHandling = NullValueHandling.Ignore)]
    public string? QueryTemplate { get; set; }

    [JsonProperty(nameof(IterationLimit), NullValueHandling = NullValueHandling.Ignore)]
    public string? IterationLimit { get; set; }

    [JsonProperty(nameof(ContextLimit), NullValueHandling = NullValueHandling.Ignore)]
    public string? ContextLimit { get; set; }

    [JsonProperty(nameof(DebugInfo), NullValueHandling = NullValueHandling.Ignore)]
    public string? DebugInfo { get; set; }

    [JsonProperty(nameof(Keywords))]
    public List<string> Keywords { get; set; } = [];

    public bool ShouldSerializeKeywords()
    {
        return Keywords.Count > 0;
    }

    [JsonProperty(nameof(SmartFilterExpressions))]
    public List<string> SmartFilterExpressions { get; set; } = [];

    public bool ShouldSerializeSmartFilterExpressions()
    {
        return SmartFilterExpressions.Count > 0;
    }

    [JsonIgnore]
    [NotMapped]
    public int NumberOfPagesToUse => ParseNumber(_numberOfPagesInput) ?? 1;

    [JsonIgnore]
    [NotMapped]
    public int StartIndex
    {
        get
        {
            int startIndex = ParseNumber(_startIndexInput) ?? 0;
            int endIndex = ParseNumber(_endIndexInput) ?? int.MaxValue;

            return startIndex > endIndex ? 0 : startIndex;
        }
    }

    [JsonIgnore]
    [NotMapped]
    public int EndIndex
    {
        get
        {
            int startIndex = ParseNumber(_startIndexInput) ?? 0;
            int endIndex = ParseNumber(_endIndexInput) ?? int.MaxValue;

            return endIndex < startIndex ? int.MaxValue : endIndex;
        }
    }

    [JsonIgnore]
    [NotMapped]
    public List<int> ExactIndexes
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExactIndexesInput))
            {
                return [];
            }

            string cleaned = NumericSanitizer.StripCommonListNoise(ExactIndexesInput);

            try
            {
                return Utils.ParseStringForNumbersList(cleaned, skipInvalidSegments: true);
            }
            catch
            {
                return [];
            }
        }
    }

    [JsonIgnore]
    [NotMapped]
    public int PageAdjustment => ParseNumber(_pageAdjustmentInput) ?? 0;

    [JsonConstructor]
    internal RagSettingsExecutionState()
    {
    }

    public RagSettingsExecutionState(RagSettings ragSettings)
    {
        ExactIndexesInput = ragSettings.ExactIndexesInput;
        NumberOfPagesInput = ragSettings.NumberOfPagesInput;
        StartIndexInput = ragSettings.StartIndexInput;
        EndIndexInput = ragSettings.EndIndexInput;
        PageAdjustmentInput = ragSettings.PageAdjustmentInput;
        TopicsQuery = ragSettings.TopicsQuery;
        TableOfContentsInput = ragSettings.TableOfContentsInput;
        QueryTemplate = ragSettings.QueryTemplate;
        IterationLimit = ragSettings.IterationLimit;
        ContextLimit = ragSettings.ContextLimit;

        foreach (string keyword in ragSettings.Keywords)
        {
            Keywords.Add(keyword);
        }

        foreach (string smartFilterExpression in ragSettings.SmartFilterExpressions)
        {
            SmartFilterExpressions.Add(smartFilterExpression);
        }
    }

    public void ReplaceAllPlaceholders(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        PlaceholderReflectionHelper.ReplaceAll(this, asPlaceHolder, inputs, stringComparison);
    }


    private static int? ParseNumber(string input)
    {
        try
        {
            return Convert.ToInt32(new DataTable().Compute(input, null));
        }
        catch
        {
            return input.Any(char.IsLetter) ? NumericSanitizer.ExtractInt(input) : null;
        }
    }
}
