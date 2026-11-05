namespace IniaAi.WebApi.Contracts.Responses.Counterparty;

/// <summary>
/// A counterparty offered to the peer picker, with whether it can actually contribute a peer
/// column and — when it cannot — why. Unusable candidates are still listed: "no financial
/// analysis yet" tells the user what to do about it, where omission tells them nothing.
/// When a workflow type is given, counterparties of a type that workflow does not accept never
/// appear at all — they are not peers lacking data, they are not peers — so within the
/// requested page and search, absence means wrong type and presence-with-a-reason means right
/// type, unusable data.
/// </summary>
public sealed class PeerCandidateDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the counterparty.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the display name of the counterparty.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets alternative name variants or aliases, for searching and matching.
    /// </summary>
    public string NameVariants { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name the peer column will actually carry. The stored analysis may name
    /// the company differently from the local record, and the report shows the stored name — so
    /// the picker shows it too rather than letting the two disagree silently.
    /// </summary>
    public string? ReportedName { get; set; }

    /// <summary>
    /// Gets or sets whether this counterparty's stored financials can produce a peer column.
    /// </summary>
    public required bool IsEligible { get; set; }

    /// <summary>
    /// Gets or sets why the counterparty is or is not usable: <c>Available</c>,
    /// <c>NotAnalysed</c>, <c>Unreadable</c>, or <c>NoUsablePeriods</c>. A build without the
    /// PeerFinancials module judges no stored financials and answers <c>NotJudged</c>, with every
    /// candidate eligible.
    /// </summary>
    public required string Availability { get; set; }

    /// <summary>
    /// Gets or sets the currency of the stored figures, when known. Conversion to the target's
    /// currency is only attempted during the run, so this is shown rather than filtered on.
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Gets or sets the end date of the most recent stored period. Whether that period overlaps
    /// the target's depends on the target, so this is shown rather than filtered on — it is also
    /// the only honest measure of how current a peer's figures are.
    /// </summary>
    public DateTime? LatestPeriodEnd { get; set; }
}
