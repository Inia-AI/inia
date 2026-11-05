using IniaAi.Domain.Entities.Counterparties;

namespace IniaAi.Core.Services.Abstraction;

/// <summary>
/// Resolves entity-based placeholders (Counterparty, PeerCompanies, Organization) in workflow prompts.
/// Handles database lookups, caching, and reflection-based property extraction.
/// </summary>
public interface IEntityPlaceholderResolver
{
    /// <summary>
    /// Resolves the raw value a placeholder's first segment names on a counterparty, looked up by ID.
    /// A real entity column (<c>Name</c>, <c>Email</c>, ...) wins; otherwise the segment is a key into
    /// the freeform <see cref="Counterparty.Properties"/> bag, falling back to the parent when absent.
    /// A blank key reads the name. Any remaining dotted path (JSON/tag traversal) is applied by the caller.
    /// </summary>
    string? ResolveCounterparty(string? counterpartyId, string? key);

    /// <summary>
    /// Resolves the raw value for a counterparty's first placeholder segment, located by name within an
    /// organization (exact, case-insensitive, Name then NameVariants). Used for loop-item placeholders,
    /// where the iteration value is a fund name. Value selection matches <see cref="ResolveCounterparty"/>.
    /// </summary>
    string? ResolveCounterpartyByName(
        string? organizationId,
        string? counterpartyName,
        string? key);

    /// <summary>
    /// Resolves peer companies placeholders, supporting array access (PeerCompanies[]),
    /// indexed access (PeerCompanies[1]), and plain access (PeerCompanies).
    /// </summary>
    string? ResolvePeerCompanies(List<string>? peerCompanyIds, string[] parts);

    /// <summary>
    /// Resolves a single organization property by ID.
    /// </summary>
    string? ResolveOrganization(string? organizationId, string property);

    /// <summary>
    /// Gets a counterparty entity by ID, using cache when available.
    /// </summary>
    Counterparty? GetCounterparty(string? counterpartyId);

    /// <summary>
    /// Invalidates a cached counterparty entry, forcing a fresh DB lookup on next resolve.
    /// </summary>
    void InvalidateCounterparty(string counterpartyId);
}
