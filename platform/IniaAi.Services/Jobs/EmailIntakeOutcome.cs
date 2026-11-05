using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Services.Jobs;

/// <summary>A document an email expansion minted, with the email row it came from.</summary>
public sealed record EmailIntakeProduct(string SourceEmailId, DocumentInfo Document);

/// <summary>
/// What one email expansion produced: the documents it minted, the parts it skipped with their
/// reasons, the identifier of each email it handled in the order it handled them, and the rows of
/// the emails it handled. The caller owns it, so what an expansion minted before a failure is
/// still at hand.
/// </summary>
public sealed class EmailIntakeOutcome
{
    public bool HadEmails { get; internal set; }

    public List<EmailIntakeProduct> Products { get; } = [];

    public List<SkippedProtectedFile> SkippedFiles { get; } = [];

    public List<string> TriggerEmailIdentifiers { get; } = [];

    public List<string> HandledEmailIds { get; } = [];
}
