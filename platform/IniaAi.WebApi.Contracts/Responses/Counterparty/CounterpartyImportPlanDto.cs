using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.Counterparty;

/// <summary>
/// What a counterparty bundle import did, or, for a dry run, what it would do: one row per
/// bundled counterparty with its disposition, the counts, and the types the import creates.
/// </summary>
public class CounterpartyImportPlanDto
{
    /// <summary>False for a dry run: the plan was computed and nothing was written.</summary>
    public bool Applied { get; set; }

    /// <summary>Rows created with their source id.</summary>
    public int CreateCount { get; set; }

    /// <summary>Rows that replaced a live counterparty holding their id.</summary>
    public int UpdateByIdCount { get; set; }

    /// <summary>Rows that reactivated a soft-deleted counterparty holding their id.</summary>
    public int ResurrectCount { get; set; }

    /// <summary>Rows that updated the one live counterparty carrying their name.</summary>
    public int UpdateByNameCount { get; set; }

    /// <summary>Rows whose counterparty already equalled them in every field.</summary>
    public int UnchangedCount { get; set; }

    /// <summary>Rows that did not land; each carries its reason.</summary>
    public int SkipCount { get; set; }

    /// <summary>Counterparty types this deployment lacks that the import creates, by name.</summary>
    public List<string> CounterpartyTypesToCreate { get; set; } = [];

    /// <summary>One entry per bundled counterparty, in bundle order.</summary>
    public List<CounterpartyImportRowDto> Rows { get; set; } = [];
}

/// <summary>The disposition of one bundled counterparty.</summary>
public class CounterpartyImportRowDto
{
    /// <summary>The row's id in the bundle.</summary>
    public required string SourceId { get; set; }

    /// <summary>The row's name in the bundle.</summary>
    public required string Name { get; set; }

    /// <summary>What the import does, or would do, with the row.</summary>
    public CounterpartyImportDisposition Disposition { get; set; }

    /// <summary>The counterparty the row lands on; equals <see cref="SourceId"/> unless the row matched by name. Null for a skipped row.</summary>
    public string? TargetId { get; set; }

    /// <summary>Set when <see cref="Disposition"/> is Skip.</summary>
    public CounterpartyImportSkipReason? SkipReason { get; set; }

    /// <summary>A sentence for the reader: the field rule broken, the colliding name, the parent that did not land.</summary>
    public string? Detail { get; set; }
}
