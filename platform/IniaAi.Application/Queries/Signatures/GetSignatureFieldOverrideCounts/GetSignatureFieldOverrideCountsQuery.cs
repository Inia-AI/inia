using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Queries.Signatures.GetSignatureFieldOverrideCounts;

/// <summary>
/// Per-field counts of active and pending signature overrides across ALL organizations for a
/// workflow type. Staff-side: powers the editor's delete-confirmation dialog ("N client
/// override(s) will be deleted"). Keyed by <c>PlaceholderName</c> (case as stored).
/// </summary>
public record GetSignatureFieldOverrideCountsQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId) : IRequest<ErrorOr<IReadOnlyDictionary<string, SignatureFieldOverrideCounts>>>;

/// <param name="ActiveOverrides">Active override rows across all organizations.</param>
/// <param name="PendingOverrides">Pending (unresolved) proposal rows across all organizations.</param>
public record SignatureFieldOverrideCounts(int ActiveOverrides, int PendingOverrides);
