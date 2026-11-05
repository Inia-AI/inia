using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Reruns;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Counterparties;

/// <summary>
/// Represents a counterparty in the business domain.
/// </summary>
[LiveData]
public class Counterparty : Entity<Counterparty>
{
    public string OrganizationId { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    [InverseProperty(nameof(Children))]
    [ForeignKey(nameof(ParentId))]
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public Counterparty? Parent { get; set; }
    [ForeignKey(nameof(OrganizationId))]
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public Organization? Organization { get; set; }

    [Required]
    [StringLength(250)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string NameVariants { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Email { get; set; }

    [StringLength(20)]
    public string? PhoneNumber { get; set; }

    [StringLength(1000)]
    public string? Website { get; set; }

    [StringLength(1000)]
    public string? Address { get; set; }

    public string? LastFinancialAnalysisResult { get; set; }

    private Dictionary<string, string>? _properties;

    // Normalized to a case-insensitive map on every assignment (the one enforced invariant): author-coined
    // keys are read by {Counterparty.key} placeholders and written by blocks in inconsistent casing, and the
    // API camelCases keys outbound, so key case is never stable. See CounterpartyPropertyBag.
    public Dictionary<string, string>? Properties
    {
        get => _properties;
        set => _properties = CounterpartyPropertyBag.Normalize(value);
    }

    public ICollection<CounterpartyType> CounterpartyTypes { get; set; } = [];
    public ICollection<Counterparty> Children { get; private set; } = [];
    public ICollection<Document> Documents { get; private set; } = [];
    public ICollection<User> Users { get; private set; } = [];
    public ICollection<WorkflowExecutionCounterparty> WorkflowExecutionCounterparties { get; set; } = [];

    public Counterparty()
    {
    }

    public Counterparty(string organizationId, string name)
    {
        OrganizationId = organizationId;
        Name = name;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (obj == this)
        {
            return true;
        }

        if (obj is Counterparty counterparty)
        {
            // TODO: does it make sense to compare other fields, such as address, email, ..?
            return Id == counterparty.Id;
        }

        return false;
    }

    public override int GetHashCode()
    {
        // TODO: probably sufficient, or should we include collections somehow?
        return HashCode.Combine(Id, OrganizationId, Name, Address, PhoneNumber, Email, Website);
    }
}
