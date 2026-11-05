using IniaAi.Domain.Common;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Counterparties;

/// <summary>
/// Represents a type of counterparty. Shared across all organizations.
/// </summary>
public class CounterpartyType : Entity<CounterpartyType>
{
    public const string TYPE_REAL_MONEY_FUND_MANAGER = "Real Money Fund Manager";
    public const string TYPE_REAL_MONEY_FUND = "Real Money Fund";
    public const string TYPE_CORPORATION = "Corporation";
    public const int NAME_MAX_LENGTH = 250;
    public const int DESCRIPTION_MAX_LENGTH = 1000;

    [Required]
    [StringLength(NAME_MAX_LENGTH)]
    [JsonProperty(nameof(Name))]
    public string Name { get; set; } = string.Empty;

    [StringLength(DESCRIPTION_MAX_LENGTH)]
    [JsonProperty(nameof(Description))]
    public string? Description { get; set; }

    public ICollection<Counterparty> Counterparties { get; private set; } = [];

    public CounterpartyType()
    {
    }

    public CounterpartyType(string name)
    {
        Name = name;
    }
}
