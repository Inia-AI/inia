using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Common;

/// <summary>
/// Base class for all entities in the system.
/// </summary>
public class Entity<T> : JsonSerializable<T>, IAuditableEntity where T : class
{
    [Key]
    [JsonProperty]
    [CloneIgnore]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty(nameof(CreatedAt))]
    [CloneIgnore]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool ShouldSerializeCreatedAt()
    {
        return !IsWorkflowEntity();
    }

    [JsonProperty(nameof(UpdatedAt))]
    [CloneIgnore]
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool ShouldSerializeUpdatedAt()
    {
        return UpdatedAt is not null && !IsWorkflowEntity();
    }

    [JsonProperty(nameof(UpdatedByUserId), NullValueHandling = NullValueHandling.Ignore)]
    [CloneIgnore]
    public string? UpdatedByUserId { get; set; }

    [JsonProperty(nameof(IsDeleted))]
    [CloneIgnore]
    public bool IsDeleted { get; set; } = false;

    public bool ShouldSerializeIsDeleted()
    {
        return IsDeleted;
    }

    [JsonProperty(nameof(DeletedAt), NullValueHandling = NullValueHandling.Ignore)]
    [CloneIgnore]
    public DateTime? DeletedAt { get; set; }

    [JsonProperty(nameof(DeletedByUserId), NullValueHandling = NullValueHandling.Ignore)]
    [CloneIgnore]
    public string? DeletedByUserId { get; set; }

    private bool IsWorkflowEntity()
    {
        return this is Block
            or TextInput
            or WorkflowVariable
            or DocumentClass
            or Section
            or Workflow
            or RagSettings;
    }
}
