using IniaAi.Common.Reflection;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Workflows;

public class DocumentClass : Entity<DocumentClass>
{
    public const string KEY_ANY = "Any";
    public const string KEY_NONE = "None";

    [JsonIgnore]
    [CloneIgnore]
    public string WorkflowId { get; set; } = string.Empty;

    [JsonIgnore]
    [CloneIgnore]
    public Workflow? Workflow { get; set; }

    [StringLength(450)]
    [JsonProperty(nameof(Key))]
    [CloneIgnore]
    public string Key { get; set; } = string.Empty;

    [StringLength(1000)]
    [JsonProperty(nameof(DisplayName))]
    public string DisplayName { get; set; } = string.Empty;

    [JsonProperty(nameof(Description))]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// What qualifies a document as this class, written for a model rather than a person:
    /// distinguishing features, near-miss classes, what the document is not.
    /// </summary>
    [JsonProperty(nameof(LlmDescription), NullValueHandling = NullValueHandling.Ignore)]
    public string? LlmDescription { get; set; }

    public bool ShouldSerializeLlmDescription()
    {
        return !string.IsNullOrEmpty(LlmDescription);
    }

    [JsonProperty(nameof(Requirement))]
    [JsonConverter(typeof(StringEnumConverter))]
    public DocumentClassRequirement Requirement { get; set; } = DocumentClassRequirement.Optional;

    [JsonProperty(nameof(ShouldBeSingular))]
    public bool ShouldBeSingular { get; set; } = false;

    [JsonProperty(nameof(IsOutputDocument))]
    [DefaultValue(false)]
    public bool IsOutputDocument { get; set; } = false;

    public bool ShouldSerializeIsOutputDocument()
    {
        return IsOutputDocument;
    }

    [StringLength(100)]
    [JsonProperty(nameof(DocumentCategoryId))]
    public string? DocumentCategoryId { get; set; }

    /// <summary>
    /// The prompts this class's pages are summarised with. Empty means the workflow's default
    /// prompts apply, and without those the pages get no overview.
    /// </summary>
    [JsonProperty(nameof(PageOverviews), NullValueHandling = NullValueHandling.Ignore)]
    [CloneIgnore]
    public PageOverviewPrompts? PageOverviews { get; set; }

    public bool ShouldSerializePageOverviews()
    {
        return PageOverviews is { IsEmpty: false };
    }

    /// <summary>
    /// The workflow type whose output a file of this class is, with <see cref="MadeByDocumentClassKey"/>: Part 2's
    /// spread input is made by Part 1's spread output. Empty when any file of the class will do.
    /// </summary>
    [StringLength(100)]
    [JsonProperty(nameof(MadeByWorkflowTypeId), NullValueHandling = NullValueHandling.Ignore)]
    public string? MadeByWorkflowTypeId { get; set; }

    /// <summary>The output class of <see cref="MadeByWorkflowTypeId"/> that a file of this class is.</summary>
    [StringLength(450)]
    [JsonProperty(nameof(MadeByDocumentClassKey), NullValueHandling = NullValueHandling.Ignore)]
    public string? MadeByDocumentClassKey { get; set; }

    [JsonIgnore]
    public bool IsMadeByAnotherWorkflow => !IsOutputDocument && !string.IsNullOrWhiteSpace(MadeByWorkflowTypeId) && !string.IsNullOrWhiteSpace(MadeByDocumentClassKey);

    [JsonConstructor]
    internal DocumentClass()
    {
    }

    public DocumentClass(Workflow workflow, string? key = null)
    {
        Workflow = workflow;
        WorkflowId = workflow.Id;

        bool keyExists = workflow.DocumentClasses.Any(dc => dc.Key == key);
        int documentClassCount = workflow.DocumentClasses.Count + 1;

        Key = keyExists || string.IsNullOrWhiteSpace(key)
            ? $"documentClass_{documentClassCount}"
            : key;

        DisplayName = Key;

        workflow.DocumentClasses.Add(this);
    }

    public DocumentClass CloneToWorkflow(Workflow workflow)
    {
        var clone = new DocumentClass(workflow, Key);
        CloneHelper.CopyScalars(this, clone);
        clone.PageOverviews = PageOverviews?.Clone();
        return clone;
    }
}
