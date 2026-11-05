using IniaAi.Core.Workflows.Dependencies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Proposals;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// Marks the changes that can alter what a workflow sends to an outside service. A block that
/// calls out (a web call, a search, an exchange rate or peer financials lookup) sends what its
/// templates resolve to, so any change that feeds such a block, directly or through other blocks,
/// a variable, a signature field or a document class, is marked: a prompt three blocks upstream
/// can put document text into a web call's payload. The flow is read from the dependency map of
/// both the workflow the agent was given and the one it proposed, so a change that adds a path
/// and a change that removes one are both caught. Agent blocks run in a sandbox without network
/// and AI blocks only call the model, so neither counts as outbound.
/// </summary>
public static class EditorAgentOutboundFlow
{
    private const string ID_KEY_PREFIX = "id:";

    /// <summary>Block fields that change only how a block is shown, never what it produces.</summary>
    private static readonly HashSet<string> _cosmeticFields = new(StringComparer.Ordinal)
    {
        "Name", "Order", "Description", "OutputTitleTemplate", "ShouldDisplayInLogs",
    };

    public static void Flag(WorkflowProposal proposal, Workflow? baseWorkflow, Workflow? proposedWorkflow)
    {
        var reach = new Reach();
        reach.Add(baseWorkflow);
        reach.Add(proposedWorkflow);

        foreach (ProposedChange change in proposal.Changes)
        {
            if (!change.SendsDataOutside && reach.Feeds(change))
            {
                change.SendsDataOutside = true;
            }
        }
    }

    private sealed class Reach
    {
        private readonly HashSet<string> _blocks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _variables = new(StringComparer.Ordinal);
        private readonly HashSet<string> _signatureFields = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _documentClasses = new(StringComparer.Ordinal);
        private readonly HashSet<string> _sections = new(StringComparer.Ordinal);

        /// <summary>Walks back from every outbound block to everything that feeds it.</summary>
        public void Add(Workflow? workflow)
        {
            if (workflow is null)
            {
                return;
            }

            WorkflowDependencyMap map = WorkflowDependencyMap.ForTemplate(workflow);
            var pending = new Stack<string>(map.Blocks
                .Where(static block => WorkflowProposalBuilder.OutboundBlockTypes.Contains(block.Block.Type.ToString()))
                .Select(static block => block.Id));
            var seen = new HashSet<string>(pending, StringComparer.Ordinal);

            while (pending.TryPop(out string? nodeId))
            {
                if (map.FindBlock(nodeId) is DependencyBlock block)
                {
                    AddBlock(block.Block);
                }
                else if (map.FindInput(nodeId) is DependencyInput input)
                {
                    AddInput(input);
                }

                foreach (DependencyEdge edge in map.IncomingEdges(nodeId))
                {
                    if (seen.Add(edge.SourceId))
                    {
                        pending.Push(edge.SourceId);
                    }
                }
            }
        }

        public bool Feeds(ProposedChange change)
        {
            return change.Scope switch
            {
                ProposedChangeScope.Block => _blocks.Contains(change.TargetKey ?? string.Empty)
                    && (change.Kind != ProposedChangeKind.FieldEdited || !_cosmeticFields.Contains(change.Field ?? string.Empty)),
                ProposedChangeScope.Section => change.Kind == ProposedChangeKind.Added && _sections.Contains(change.TargetKey ?? string.Empty),
                ProposedChangeScope.Variable => _variables.Contains(change.TargetKey ?? string.Empty),
                ProposedChangeScope.SignatureField => _signatureFields.Contains(change.TargetKey ?? string.Empty),
                ProposedChangeScope.DocumentClass => _documentClasses.Contains(change.TargetKey ?? string.Empty),
                _ => false,
            };
        }

        private void AddBlock(Block block)
        {
            _ = _blocks.Add(string.IsNullOrWhiteSpace(block.StableBlockId) ? ID_KEY_PREFIX + block.Id : block.StableBlockId);
            if (block.Section is Section section)
            {
                _ = _sections.Add(string.IsNullOrWhiteSpace(section.StableSectionId) ? ID_KEY_PREFIX + section.Id : section.StableSectionId);
            }

            foreach (string documentClass in block.SupportedDocumentClasses)
            {
                _ = _documentClasses.Add(documentClass);
            }
        }

        private void AddInput(DependencyInput input)
        {
            switch (input.Kind)
            {
                case DependencyInputKind.Variable:
                    _ = _variables.Add(input.Tag);
                    break;
                case DependencyInputKind.Signature:
                    _ = _signatureFields.Add(input.Tag);
                    break;
                default:
                    break;
            }
        }
    }
}
