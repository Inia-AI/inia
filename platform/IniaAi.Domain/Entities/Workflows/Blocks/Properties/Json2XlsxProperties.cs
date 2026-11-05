using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

public class Json2XlsxProperties : BlockProperties, IProducesDocumentBlockProperties
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the JSON input. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="Json2XlsxPropertiesExecutionState.JsonInput"/> for the
    /// actual JSON input after replacements.
    /// </summary>
    [JsonProperty(nameof(JsonInputTemplate))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? JsonInputTemplate { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the conversion type. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="Json2XlsxPropertiesExecutionState.ConversionType"/> for
    /// the actual conversion type after replacements.
    /// </summary>
    [JsonProperty(nameof(ConversionType))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? ConversionType { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the mapping JSON. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="Json2XlsxPropertiesExecutionState.MappingJson"/> for the
    /// actual mapping JSON after replacements.
    /// </summary>
    /// <remarks>
    /// The coordinate mapping of template-based Excel injection: which cell
    /// of the template each input value goes to. Required unless
    /// <see cref="UseTemplateTags"/> is on.
    /// </remarks>
    [JsonProperty(nameof(MappingJson), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? MappingJson { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the output file name. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="Json2XlsxPropertiesExecutionState.OutputFileName"/> for
    /// the actual output file name after replacements.
    /// </summary>
    /// <remarks>
    /// Optional custom output file name for the generated Excel file. When
    /// provided, replaces the default "xlsx-{blockTag}" naming pattern.
    /// </remarks>
    [JsonProperty(nameof(OutputFileName), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? OutputFileName { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the created document class. It can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="Json2XlsxPropertiesExecutionState.CreatesDocumentClass"/>
    /// for the actual document class after replacements.
    /// </summary>
    [JsonProperty(nameof(CreatesDocumentClass), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? CreatesDocumentClass { get; set; }

    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    [NotMapped]
    public bool RequiresDocumentClass => Direction is not Json2XlsxDirection.Extract;

    /// <summary>
    /// When true, the generated .xlsx carries a very-hidden analysis-config sheet
    /// marking it as block-generated so ExcelSheetAnalysisService applies rule-based
    /// chunking instead of LLM analysis. Defaults to false — existing blocks keep
    /// their current behaviour until opted in per-block.
    /// </summary>
    [JsonProperty(nameof(StampAnalysisConfig))]
    public bool StampAnalysisConfig { get; set; }

    public bool ShouldSerializeStampAnalysisConfig()
    {
        return StampAnalysisConfig;
    }

    /// <summary>
    /// When true, injection ignores <see cref="MappingJson"/> and resolves bindings from
    /// <c>{{tag}}</c> cells inside the template itself (tag-based injection): each input cell
    /// carries its own tag, e.g. <c>{{Core Revenues.FY0}}</c>. Values are injected verbatim
    /// with no unit scaling. Defaults to false — existing blocks keep the coordinate-mapping
    /// behaviour until opted in per block.
    /// </summary>
    [JsonProperty(nameof(UseTemplateTags))]
    public bool UseTemplateTags { get; set; }

    public bool ShouldSerializeUseTemplateTags()
    {
        return UseTemplateTags;
    }

    /// <summary>
    /// When true, every row of an Excel template that received injected text in a wrap-text cell
    /// is sized to fit that text (growing or shrinking, merged text boxes included), up to Excel's
    /// 409pt row limit. Rows that received no text keep the template's height. Applies to both
    /// coordinate-mapping and tag-based injection; ignored for Word templates. Defaults to false
    /// so existing blocks keep the template's row heights until opted in per block.
    /// </summary>
    [JsonProperty(nameof(AutoFitTextRows))]
    public bool AutoFitTextRows { get; set; }

    public bool ShouldSerializeAutoFitTextRows()
    {
        return AutoFitTextRows;
    }

    /// <summary>
    /// When true, the generated document (and its Word rendering, if any) is stored with
    /// <see cref="IniaAi.Domain.Entities.Documents.DocumentInfo.ShouldBeDisplayedInOutput"/> false,
    /// so the report leaves it out of its output files. Later blocks that read its document class
    /// still receive it. Use for internal working files. Defaults to false, so existing blocks
    /// keep showing their document.
    /// </summary>
    [JsonProperty(nameof(HideFromOutput))]
    public bool HideFromOutput { get; set; }

    public bool ShouldSerializeHideFromOutput()
    {
        return HideFromOutput;
    }

    /// <summary>
    /// Direction of the block. <see cref="Json2XlsxDirection.Inject"/> (default) writes JSON into a
    /// template and emits an .xlsx. <see cref="Json2XlsxDirection.Extract"/> reads an uploaded .xlsx
    /// (see <see cref="InputDocumentClass"/>) back into JSON using the same <see cref="MappingJson"/>.
    /// Defaults to Inject so existing blocks are unchanged.
    /// </summary>
    [JsonProperty(nameof(Direction))]
    [JsonConverter(typeof(StringEnumConverter))]
    public Json2XlsxDirection Direction { get; set; } = Json2XlsxDirection.Inject;

    public bool ShouldSerializeDirection()
    {
        return Direction is not Json2XlsxDirection.Inject;
    }

    /// <summary>
    /// Output format for the inject direction. <see cref="Json2XlsxOutputFormat.Xlsx"/> (default)
    /// produces the generated .xlsx; <see cref="Json2XlsxOutputFormat.XlsxAndWord"/> additionally
    /// produces a .docx rendering of it. Ignored for the extract direction. Defaults to Xlsx so
    /// existing blocks are unchanged.
    /// </summary>
    [JsonProperty(nameof(OutputFormat))]
    [JsonConverter(typeof(StringEnumConverter))]
    public Json2XlsxOutputFormat OutputFormat { get; set; } = Json2XlsxOutputFormat.Xlsx;

    public bool ShouldSerializeOutputFormat()
    {
        return OutputFormat is not Json2XlsxOutputFormat.Xlsx;
    }

    /// <summary>
    /// When <see cref="Direction"/> is <see cref="Json2XlsxDirection.Extract"/>, the DocumentClass of
    /// the uploaded .xlsx to read cell values from (e.g. "CorporateFinancialModel"). For the inject
    /// direction, optionally selects an uploaded document of that class as the injection target
    /// instead of the organization template — exactly one of this and <see cref="ConversionType"/>
    /// may be set. May contain placeholders resolved at execution time — use
    /// <see cref="Json2XlsxPropertiesExecutionState.InputDocumentClass"/> for the resolved value.
    /// </summary>
    [JsonProperty(nameof(InputDocumentClass), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? InputDocumentClass { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public Json2XlsxPropertiesExecutionState? ExecutionState { get; set; }

    public Json2XlsxProperties()
    {
        Type = BlockTypes.Json2Xlsx;
    }

    public override BlockProperties Clone()
    {
        var clone = new Json2XlsxProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new Json2XlsxPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new Json2XlsxPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
