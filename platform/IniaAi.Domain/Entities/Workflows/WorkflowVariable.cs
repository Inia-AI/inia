using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using JsonSerializer = System.Text.Json.JsonSerializer;
using JsonElement = System.Text.Json.JsonElement;
using JsonDocument = System.Text.Json.JsonDocument;

namespace IniaAi.Domain.Entities.Workflows;

public class WorkflowVariable : JsonSerializable<WorkflowVariable>
{
    private const char LIST_ITEM_SEPARATOR = '|';
    private const char DATE_TIME_SEPARATOR = 'T';
    private const string MIDNIGHT_TIME_SUFFIX = "T00:00";
    private const string PLACEHOLDER_DATE_FORMAT = "yyyy-MM-dd";

    [Key]
    [JsonIgnore]
    [CloneIgnore]
    public string Id { get; internal set; } = Guid.NewGuid().ToString();

    [JsonIgnore]
    [CloneIgnore]
    public string WorkflowId { get; set; } = string.Empty;

    [JsonIgnore]
    [CloneIgnore]
    public Workflow? Workflow { get; set; }

    [StringLength(450)]
    [JsonProperty(nameof(ReplacementTag))]
    [CloneIgnore]
    public string ReplacementTag { get; set; } = string.Empty;

    [StringLength(1000)]
    [JsonProperty(nameof(DisplayName))]
    public string DisplayName { get; set; } = string.Empty;

    [JsonProperty(nameof(Description))]
    public string Description { get; set; } = string.Empty;

    [JsonProperty(nameof(Type))]
    [JsonConverter(typeof(StringEnumConverter))]
    public WorkflowVariableTypes Type { get; set; } = WorkflowVariableTypes.String;

    [JsonProperty(nameof(IsRequired))]
    public bool IsRequired { get; set; } = true;

    [JsonProperty(nameof(IsHidden))]
    public bool IsHidden { get; set; } = false;

    [JsonProperty(nameof(IsConfidential))]
    public bool IsConfidential { get; set; } = false;

    [JsonProperty(nameof(DefaultValue), NullValueHandling = NullValueHandling.Ignore)]
    public string? DefaultValue { get; set; }

    [JsonProperty(nameof(Options))]
    public Dictionary<string, string> Options { get; set; } = [];

    /// <summary>
    /// The default in the shape a submitted value of this type takes. Null when the variable
    /// has no default.
    /// </summary>
    public JsonElement? DefaultValueAsJson()
    {
        return ValueAsJson(DefaultValue);
    }

    /// <summary>
    /// A value of this variable, written as text, in the shape a submitted value of this type
    /// takes: a number as a JSON number, a list as a JSON array of its <c>|</c>-separated items,
    /// a date without a time as midnight. Null when there is no value.
    /// </summary>
    public JsonElement? ValueAsJson(string? value)
    {
        if (value is null)
        {
            return null;
        }

        switch (Type)
        {
            case WorkflowVariableTypes.Number:
            {
                if (decimal.TryParse(value, CultureInfo.InvariantCulture, out decimal number))
                {
                    return JsonDocument.Parse(number.ToString(CultureInfo.InvariantCulture)).RootElement.Clone();
                }

                return JsonSerializer.SerializeToElement(value);
            }

            case WorkflowVariableTypes.List:
            case WorkflowVariableTypes.MultiSelect:
            {
                return JsonSerializer.SerializeToElement(SplitListItems(value));
            }

            case WorkflowVariableTypes.DateTime:
            {
                string dateTime = value.Contains(DATE_TIME_SEPARATOR)
                    ? value
                    : value + MIDNIGHT_TIME_SUFFIX;
                return JsonSerializer.SerializeToElement(dateTime);
            }

            default:
            {
                return JsonSerializer.SerializeToElement(value);
            }
        }
    }

    /// <summary>
    /// The text this variable gives to its placeholder. A date reaches the workflow as
    /// <c>yyyy-MM-dd</c> whichever client sent it: the editor sends a date, the web app a date
    /// and a time. A value that does not parse as a date passes through unchanged.
    /// </summary>
    public string ToPlaceholderText(string value)
    {
        if (Type is WorkflowVariableTypes.DateTime && TryParseDate(value, out DateTimeOffset date))
        {
            return date.ToString(PLACEHOLDER_DATE_FORMAT, CultureInfo.InvariantCulture);
        }

        return value;
    }

    /// <summary>
    /// The one reading of a Date variable's text, shared by submission validation and placeholder
    /// binding, so a value the validator accepts always binds as a date. The clock time is kept as
    /// written, never shifted to another time zone.
    /// </summary>
    public static bool TryParseDate(string? value, out DateTimeOffset date)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);
    }

    public static string[] SplitListItems(string pipeSeparated)
    {
        return [.. pipeSeparated.Split(LIST_ITEM_SEPARATOR).Select(item => item.Trim())];
    }

    public static string JoinListItems(IEnumerable<string> items)
    {
        return string.Join(LIST_ITEM_SEPARATOR, items);
    }

    public bool ShouldSerializeOptions()
    {
        return Options is not null && Options.Count > 0
            && (Type is WorkflowVariableTypes.Dropdown
            or WorkflowVariableTypes.MultiSelect);
    }

    [JsonConstructor]
    internal WorkflowVariable()
    {
    }

    public WorkflowVariable(Workflow workflow, string? replacementTag = null)
    {
        Workflow = workflow;
        WorkflowId = workflow.Id;
        bool replacementTagExists = workflow.Variables
            .Any(v => v.ReplacementTag == replacementTag);
        int variableCount = workflow.Variables.Count + 1;
        ReplacementTag = replacementTagExists || string.IsNullOrEmpty(replacementTag)
            ? $"workflowVariable_{variableCount}"
            : replacementTag;
        DisplayName = ReplacementTag;
        workflow.Variables.Add(this);
    }

    public WorkflowVariable CloneToWorkflow(Workflow workflow)
    {
        var clone = new WorkflowVariable(workflow, ReplacementTag);
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }
}
