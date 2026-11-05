namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// What the import does, or would do, with one bundle row.
/// </summary>
public enum CounterpartyImportDisposition
{
    /// <summary>No counterparty on the target holds the row's id or name; the row is created with its source id.</summary>
    Create,

    /// <summary>A live counterparty of the organization holds the row's id; its fields are replaced by the row's.</summary>
    UpdateById,

    /// <summary>A soft-deleted counterparty of the organization holds the row's id; it is reactivated with the row's fields.</summary>
    Resurrect,

    /// <summary>No counterparty holds the row's id but exactly one live counterparty has its name; that one is updated and keeps its own id.</summary>
    UpdateByName,

    /// <summary>The matched counterparty already equals the row in every field; nothing is written.</summary>
    Unchanged,

    /// <summary>The row does not land; <see cref="CounterpartyImportSkipReason"/> says why.</summary>
    Skip,
}
