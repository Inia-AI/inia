using IniaAi.Common.Reflection;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace IniaAi.Domain.Entities.Workflows;

public class RagSettings : Entity<RagSettings>, ITemplatable
{
    #region Reset Configuration

    /// <summary>
    /// Properties that should never be cleared during reset, regardless of RAG type.
    /// </summary>
    private static readonly HashSet<string> _universalKeepProperties =
    [
        nameof(Id),
        nameof(BlockId),
        nameof(Block),
        nameof(Type),
        nameof(ExecutionState)
    ];

    /// <summary>
    /// Maps each RAG type to the properties that should be kept for that type.
    /// </summary>
    private static readonly Dictionary<RagTypes, HashSet<string>> _ragTypeKeepMap = new()
    {
        [RagTypes.WholeDocument] = [],
        [RagTypes.TopNPages] =
        [
            nameof(StartIndexInput),
            nameof(EndIndexInput),
            nameof(NumberOfPagesInput),
            nameof(SmartFilterExpressions),
        ],
        [RagTypes.TopNPagesPerFilterWithTwoNeighbors] =
        [
            nameof(StartIndexInput),
            nameof(EndIndexInput),
            nameof(NumberOfPagesInput),
            nameof(SmartFilterExpressions),
        ],
        [RagTypes.TopPage] =
        [
            nameof(StartIndexInput),
            nameof(EndIndexInput),
            nameof(NumberOfPagesInput),
            nameof(SmartFilterExpressions),
        ],
        [RagTypes.TopPageWithNFollowingPages] =
        [
            nameof(StartIndexInput),
            nameof(EndIndexInput),
            nameof(NumberOfPagesInput),
            nameof(SmartFilterExpressions),
        ],
        [RagTypes.NPagesStartingFromIndex] =
        [
            nameof(PageAdjustmentInput),
            nameof(StartIndexInput),
            nameof(EndIndexInput),
            nameof(NumberOfPagesInput),
        ],
        [RagTypes.KeywordBased] =
        [
            nameof(StartIndexInput),
            nameof(EndIndexInput),
            nameof(NumberOfPagesInput),
            nameof(Keywords),
            nameof(ShouldFilterKeyWordsExclusively),
            nameof(ShouldSearchOverviews),
            nameof(ShouldIncludeNeighboringPages),
            nameof(SmartFilterExpressions),
        ],
        [RagTypes.ExactPages] =
        [
            nameof(PageAdjustmentInput),
            nameof(ExactIndexesInput),
            nameof(NumberOfPagesInput),
        ],
        [RagTypes.UseTableOfContents] =
        [
            nameof(PageAdjustmentInput),
            nameof(TableOfContentsInput)
        ],
        [RagTypes.UseAutoDetectedTableOfContents] =
        [
            nameof(TableOfContentsInput),
            nameof(TopicsQuery),
        ],
        [RagTypes.RagAgent] =
        [
            nameof(QueryTemplate),
            nameof(IterationLimit),
            nameof(ContextLimit),
        ]
    };

    #endregion
    [JsonIgnore]
    [CloneIgnore]
    public string BlockId { get; set; } = string.Empty;

    [JsonIgnore]
    [ForeignKey(nameof(BlockId))]
    [CloneIgnore]
    public Block? Block { get; set; }

    [JsonProperty(nameof(Type))]
    public RagTypes Type { get; set; } = RagTypes.TopNPagesPerFilterWithTwoNeighbors;

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the exact page indexes input. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.ExactIndexesInput"/> for the
    /// actual exact indexes after replacements.
    /// </summary>
    [JsonProperty(nameof(ExactIndexesInput))]
    [StringLength(450)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string ExactIndexesInput { get; set; } = string.Empty;

    public bool ShouldSerializeExactIndexesInput()
    {
        return Type is RagTypes.ExactPages;
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the number-of-pages input. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.NumberOfPagesInput"/> for the
    /// actual number of pages after replacements.
    /// </summary>
    [JsonProperty(nameof(NumberOfPagesInput))]
    [StringLength(450)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string NumberOfPagesInput { get; set; } = string.Empty;

    public bool ShouldSerializeNumberOfPagesInput()
    {
        return !string.IsNullOrWhiteSpace(NumberOfPagesInput);
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the start-index input. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.StartIndexInput"/> for the actual
    /// start index after replacements.
    /// </summary>
    [JsonProperty(nameof(StartIndexInput))]
    [StringLength(450)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string StartIndexInput { get; set; } = string.Empty;

    public bool ShouldSerializeStartIndexInput()
    {
        return !string.IsNullOrWhiteSpace(StartIndexInput);
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the end-index input. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.EndIndexInput"/> for the actual
    /// end index after replacements.
    /// </summary>
    [JsonProperty(nameof(EndIndexInput))]
    [StringLength(450)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string EndIndexInput { get; set; } = string.Empty;

    public bool ShouldSerializeEndIndexInput()
    {
        return !string.IsNullOrWhiteSpace(EndIndexInput);
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the page-adjustment input. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.PageAdjustmentInput"/> for the
    /// actual page adjustment after replacements.
    /// </summary>
    [JsonProperty(nameof(PageAdjustmentInput))]
    [StringLength(450)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string PageAdjustmentInput { get; set; } = string.Empty;

    public bool ShouldSerializePageAdjustmentInput()
    {
        return !string.IsNullOrWhiteSpace(PageAdjustmentInput);
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the topics query. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.TopicsQuery"/> for the actual
    /// topics query after replacements.
    /// </summary>
    [JsonProperty(nameof(TopicsQuery), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? TopicsQuery { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the table-of-contents input. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.TableOfContentsInput"/> for the
    /// actual table-of-contents input after replacements.
    /// </summary>
    [JsonProperty(nameof(TableOfContentsInput), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? TableOfContentsInput { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the RAG-agent query. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.QueryTemplate"/> for the actual
    /// query after replacements.
    /// </summary>
    [JsonProperty(nameof(QueryTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? QueryTemplate { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the iteration-limit input. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.IterationLimit"/> for the actual
    /// iteration limit after replacements.
    /// </summary>
    [JsonProperty(nameof(IterationLimit))]
    [StringLength(450)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string IterationLimit { get; set; } = string.Empty;

    public bool ShouldSerializeIterationLimit()
    {
        return !string.IsNullOrWhiteSpace(IterationLimit);
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the context-limit input. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.ContextLimit"/> for the actual
    /// context limit after replacements.
    /// </summary>
    [JsonProperty(nameof(ContextLimit))]
    [StringLength(450)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string ContextLimit { get; set; } = string.Empty;

    public bool ShouldSerializeContextLimit()
    {
        return !string.IsNullOrWhiteSpace(ContextLimit);
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the keywords. Entries can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.Keywords"/> for the actual
    /// keyword list after replacements.
    /// </summary>
    [JsonProperty(nameof(Keywords))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public List<string> Keywords { get; set; } = [];

    public bool ShouldSerializeKeywords()
    {
        return Keywords.Count > 0;
    }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the smart filter expressions. Entries can contain
    /// placeholders that get replaced at execution time. Use
    /// <see cref="RagSettingsExecutionState.SmartFilterExpressions"/> for
    /// the actual filter expressions after replacements.
    /// </summary>
    [JsonProperty(nameof(SmartFilterExpressions))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public List<string> SmartFilterExpressions { get; set; } = [];

    public bool ShouldSerializeSmartFilterExpressions()
    {
        return SmartFilterExpressions.Count > 0;
    }

    [JsonProperty(nameof(ShouldFilterKeyWordsExclusively))]
    [DefaultValue(false)]
    public bool ShouldFilterKeyWordsExclusively { get; set; } = false;

    public bool ShouldSerializeShouldFilterKeyWordsExclusively()
    {
        return Type is RagTypes.KeywordBased;
    }

    [JsonProperty(nameof(ShouldSearchOverviews))]
    [DefaultValue(false)]
    public bool ShouldSearchOverviews { get; set; } = false;

    public bool ShouldSerializeShouldSearchOverviews()
    {
        return Type is RagTypes.KeywordBased;
    }

    [JsonProperty(nameof(ShouldIncludeNeighboringPages))]
    [DefaultValue(false)]
    public bool ShouldIncludeNeighboringPages { get; set; }

    public bool ShouldSerializeShouldIncludeNeighboringPages()
    {
        return Type is RagTypes.KeywordBased;
    }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public RagSettingsExecutionState? ExecutionState { get; set; }

    public RagSettings()
    {
    }

    public RagSettings(Block block)
    {
        Block = block;
        BlockId = block.Id;
        block.RagSettings.Add(this);
    }

    /// <summary>
    /// True when this retrieval reads page overviews, directly or through their embeddings, so it
    /// finds nothing in a page the workflow writes no overview for. A keyword search reads them
    /// only when it searches overviews or also matches by similarity.
    /// </summary>
    public bool ReadsPageOverviews()
    {
        return RanksPagesByEmbeddings()
            || Type is RagTypes.RagAgent
            || (Type is RagTypes.KeywordBased && ShouldSearchOverviews);
    }

    /// <summary>
    /// True when this retrieval picks pages by comparing their embeddings with its filter
    /// expressions. A keyword search does so beside its keywords, when it carries filter expressions.
    /// </summary>
    public bool RanksPagesByEmbeddings()
    {
        return Type switch
        {
            RagTypes.TopNPages
                or RagTypes.TopNPagesPerFilterWithTwoNeighbors
                or RagTypes.TopPage
                or RagTypes.TopPageWithNFollowingPages => true,
            RagTypes.KeywordBased => SmartFilterExpressions.Count > 0,
            _ => false,
        };
    }

    public RagSettings CloneToBlock(Block block)
    {
        var clone = new RagSettings(block);
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new RagSettingsExecutionState(this);

        ExecutionState.ReplaceAllPlaceholders(
            asPlaceHolder,
            inputs,
            stringComparison);
    }

    public void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new RagSettingsExecutionState(this);

        ExecutionState.ReplaceAllPlaceholders(
            asPlaceHolder,
            inputs,
            stringComparison);
    }

    public void ClearExecutionState()
    {
        ExecutionState = null;
    }

    public bool HasPlaceholder(
        string placeholder,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        return PlaceholderReflectionHelper.HasPlaceholder(this, placeholder, stringComparison);
    }

    public List<string> GetAllPlaceholders()
    {
        return [.. PlaceholderReflectionHelper.GetAllPlaceholders(this)];
    }

    public void ReplaceAll(
        Dictionary<string, string?> replacements,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        PlaceholderReflectionHelper.ReplaceAll(this, false, replacements, stringComparison);
    }

    /// <summary>
    /// Resets the RAG settings to a clean state for the current type.
    /// Keeps universal properties and type-specific properties, clears everything else.
    /// </summary>
    public void Reset()
    {
        HashSet<string> typeSpecificKeep = _ragTypeKeepMap.GetValueOrDefault(Type, []);
        var keepProperties = new HashSet<string>(_universalKeepProperties);
        keepProperties.UnionWith(typeSpecificKeep);

        IEnumerable<PropertyInfo> allProperties = GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite || (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(List<>)));

        foreach (PropertyInfo? property in allProperties)
        {
            if (keepProperties.Contains(property.Name))
            {
                continue;
            }

            if (property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            {
                object? list = property.GetValue(this);

                if (list is not null)
                {
                    MethodInfo? clearMethod = property.PropertyType.GetMethod("Clear");
                    _ = (clearMethod?.Invoke(list, null));
                }

                continue;
            }

            if (property.CanWrite)
            {
                if (property.PropertyType.IsValueType)
                {
                    DefaultValueAttribute? defaultValueAttr = property.GetCustomAttribute<DefaultValueAttribute>();

                    if (defaultValueAttr is not null)
                    {
                        property.SetValue(this, defaultValueAttr.Value);
                    }
                    else
                    {
                        property.SetValue(this, Activator.CreateInstance(property.PropertyType));
                    }
                }
                else if (property.PropertyType == typeof(string))
                {
                    StringLengthAttribute? stringLengthAttr = property.GetCustomAttribute<StringLengthAttribute>();
                    property.SetValue(this, stringLengthAttr is not null ? string.Empty : null);
                }
                else if (property.PropertyType.IsClass)
                {
                    property.SetValue(this, null);
                }
            }
        }
    }
}
