namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Why a bundle row does not land. A skipped row never fails the import; it is reported.
/// </summary>
public enum CounterpartyImportSkipReason
{
    /// <summary>The row breaks a field rule the API applies to every counterparty write.</summary>
    Invalid,

    /// <summary>An earlier row of the bundle carries the same id.</summary>
    DuplicateIdInBundle,

    /// <summary>The row's id is held by a counterparty of another organization on this deployment.</summary>
    IdHeldByAnotherOrganization,

    /// <summary>More than one live counterparty of the organization has the row's name, so the name fallback cannot choose.</summary>
    AmbiguousName,

    /// <summary>Landing the row would give two live counterparties one name, or a second row of the bundle already landed on the same counterparty.</summary>
    NameCollision,

    /// <summary>The row's parent did not land: it was skipped, or it is neither in the bundle nor on this deployment.</summary>
    ParentNotLanded,
}
