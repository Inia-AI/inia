using IniaAi.Domain.Entities.Counterparties;

namespace IniaAi.Application.Queries.Counterparties.ListPeerCandidates;

/// <summary>
/// Judges whether a counterparty's stored financials can give the Peer Financials block a peer
/// column. The PeerFinancials module judges them; a build without it judges nothing.
/// </summary>
public interface IPeerCandidateJudge
{
    PeerCandidateVerdict Judge(Counterparty counterparty, string peerDataPath);
}

/// <summary>What the peer picker shows about one candidate's stored financials.</summary>
public sealed record PeerCandidateVerdict(
    bool IsEligible,
    string Availability,
    string? ReportedName = null,
    string? Currency = null,
    DateTime? LatestPeriodEnd = null);
