using IniaAi.Domain.Entities.Counterparties;

namespace IniaAi.Application.Queries.Counterparties.ListPeerCandidates;

/// <summary>
/// The verdict of a build without the PeerFinancials module: nothing there reads stored
/// financials, so no counterparty is held back for them.
/// </summary>
public sealed class UnjudgedPeerCandidates : IPeerCandidateJudge
{
    public const string NOT_JUDGED = "NotJudged";

    public PeerCandidateVerdict Judge(Counterparty counterparty, string peerDataPath)
    {
        return new PeerCandidateVerdict(IsEligible: true, NOT_JUDGED);
    }
}
