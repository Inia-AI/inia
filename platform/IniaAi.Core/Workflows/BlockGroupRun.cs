using System.Collections.Concurrent;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Core.Workflows;

/// <summary>One run of a group block, with the dispatch batch its members belong to.</summary>
internal sealed record BlockGroupRun(
    IBlockGroupHost Host,
    Section Section,
    Block Block,
    IReadOnlyCollection<string> MemberTags,
    ConcurrentDictionary<string, string> AnonymisationMap,
    IUserDataUnitOfWork UserDataUnitOfWork,
    WorkflowOutputContext? WorkflowOutputContext,
    IReadOnlyList<(Section Section, Block Block, WorkflowOutputContext? Context)> DispatchedBlocks)
{
    /// <summary>
    /// The blocks of the dispatch batch that carry <paramref name="memberTag"/>. Cross-section tag
    /// reuse yields every carrier: each is a member in the graph, so each gets its own redo leg.
    /// </summary>
    public List<(Section Section, Block Block, WorkflowOutputContext? Context)> MembersCarrying(string memberTag)
    {
        return [.. DispatchedBlocks.Where(entry =>
            string.Equals(entry.Block.ReplacementTag, memberTag, StringComparison.Ordinal))];
    }
}
