using IniaAi.Common.Serialization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace IniaAi.Domain.Entities.Workflows;

[Index(nameof(DuplicatedFromWorkflowTypeId))]
public class WorkflowType : JsonSerializable<WorkflowType>
{

    public const string GENERIC = "Generic";
    public const string TEST_WORKFLOW_TYPE = "TestWorkflowType";

    public const int NAME_MAX_LENGTH = 250;
    public const int ID_MAX_LENGTH = 100;

    /// <summary>
    /// The EF-translatable form of "this is a real, user-facing template" — the reserved deployment
    /// pseudo-types and retired types are not. Read surfaces that list or sweep templates share this
    /// so they cannot disagree about which types exist.
    /// </summary>
    public static Expression<Func<WorkflowType, bool>> UserFacing { get; } =
        type => type.Id != GENERIC && type.Id != TEST_WORKFLOW_TYPE && type.RetiredAt == null;

    /// <summary>
    /// A retired type is gone from every read that asks whether a type exists, while its row stays
    /// until the weekly cleanup removes it.
    /// </summary>
    public static Expression<Func<WorkflowType, bool>> NotRetired { get; } = type => type.RetiredAt == null;

    [Key]
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(NAME_MAX_LENGTH)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; } = null;

    public string? CategoryId { get; set; }

    [StringLength(100)]
    public string? DuplicatedFromWorkflowTypeId { get; set; }

    /// <summary>
    /// When the one organization that could reach this type deleted it. The weekly cleanup removes a
    /// type only when this is set: a type no organization can reach is not retired by that alone.
    /// </summary>
    public DateTime? RetiredAt { get; set; }

    #region Navigation Properties
    public ICollection<Workflow> Workflows { get; set; } = [];
    public ICollection<WorkflowTypeCounterpartyTypeMap>? CounterpartyTypeMappings { get; set; }
    public ICollection<WorkflowTypeOrganizationAccess> OrganizationAccess { get; set; } = [];
    public WorkflowTypeCategory? Category { get; set; }

    #endregion Navigation Properties

    public WorkflowType()
    {
    }

    public WorkflowType(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        Id = id;
        Name = name;
    }
}
