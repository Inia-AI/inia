using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Workflows.Agents;

/// <summary>
/// Turns an agent run's claimed citations into <see cref="Fact"/>s whose sources are
/// <see cref="DocumentSource"/>s resolved against the platform's own pages, so agent
/// citations land in the existing citation pipeline instead of beside it.
///
/// Resolution is search-first: the quoted line is looked for across the staged document's
/// pages with <see cref="QuotedLineSearch"/>, and the page that carries it wins — preferring
/// the claimed file page when several do. A resolved source carries the real
/// <c>PageId</c>, so <c>VerifyNumericalFactSources</c> then checks every claimed figure
/// against the page's actual text for free. A quote found nowhere — an OCR-less scan, a
/// hallucinated line, an unknown document name — keeps its source flagged
/// <see cref="DocumentSource.Unverified"/> with no <c>PageId</c>: visible as unchecked,
/// never silently dropped and never rendered as confirmed.
/// </summary>
public static class AgentCitationFacts
{
    public static List<Fact> Build(
        string? runOutput,
        IReadOnlyDictionary<string, Document> stagedDocuments,
        IReadOnlyList<Page> allPages)
    {
        List<AgentCitationClaim> claims = AgentCitationClaims.Parse(runOutput);
        if (claims.Count == 0)
        {
            return [];
        }

        var pairs = new List<(string Claim, IReadOnlyList<BaseSource> Sources)>(claims.Count);
        foreach (AgentCitationClaim claim in claims)
        {
            pairs.Add((claim.AsPrinted, [ResolveClaim(claim, stagedDocuments, allPages)]));
        }

        return CitationFactBuilder.BuildFacts(pairs);
    }

    private static DocumentSource ResolveClaim(
        AgentCitationClaim claim,
        IReadOnlyDictionary<string, Document> stagedDocuments,
        IReadOnlyList<Page> allPages)
    {
        Document? document = FindStagedDocument(claim.Document, stagedDocuments);
        if (document is null)
        {
            return new DocumentSource
            {
                CitedText = claim.AsPrinted,
                DocumentName = claim.Document,
                FilePageNumber = claim.FilePage ?? 0,
                Unverified = true,
            };
        }

        List<Page> documentPages = [.. allPages
            .Where(page => page.DocumentId == document.Id)
            .OrderBy(static page => page.PageNumber)];

        Page? resolved = claim.FilePage is int filePage
            ? documentPages.FirstOrDefault(page =>
                page.FilePageNumber == filePage && QuotedLineSearch.Matches(PageText.ReadableOf(page), claim.AsPrinted))
            : null;
        resolved ??= documentPages.FirstOrDefault(page =>
            QuotedLineSearch.Matches(PageText.ReadableOf(page), claim.AsPrinted));

        if (resolved is null)
        {
            return new DocumentSource
            {
                CitedText = claim.AsPrinted,
                DocumentId = document.Id,
                DocumentName = document.FileName,
                FilePageNumber = claim.FilePage ?? 0,
                Unverified = true,
            };
        }

        return new DocumentSource
        {
            CitedText = claim.AsPrinted,
            DocumentId = document.Id,
            PageId = resolved.Id,
            PageNumber = resolved.PageNumber,
            FilePageNumber = resolved.FilePageNumber,
            DocumentName = document.FileName,
        };
    }

    private static Document? FindStagedDocument(
        string? claimedName,
        IReadOnlyDictionary<string, Document> stagedDocuments)
    {
        if (claimedName is null)
        {
            return stagedDocuments.Count == 1 ? stagedDocuments.Values.First() : null;
        }

        if (stagedDocuments.TryGetValue(claimedName, out Document? exact))
        {
            return exact;
        }

        foreach ((string stagedName, Document document) in stagedDocuments)
        {
            if (string.Equals(stagedName, claimedName, StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }
}
