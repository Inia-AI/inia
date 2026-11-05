using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Agent;

namespace IniaAi.WebApp.Components.WorkflowEditor.State;

public sealed class EditorSurfaceState
{
    private readonly Dictionary<string, HashSet<string>> _agentChanges = new(StringComparer.Ordinal);
    private readonly HashSet<string> _agentProposed = new(StringComparer.Ordinal);

    public EditorSurfaceCapabilities Capabilities { get; }

    public event Action<Section>? SectionChanged;

    public event Action<Block>? BlockChanged;

    public event Action? DocumentClassesChanged;

    /// <summary>
    /// Raised when a block-panel component asks to open the workflow-level Signatures tab
    /// (e.g. the "Manage" affordance on a referenced field). WorkflowInteraction subscribes
    /// and switches the workflow surface to that tab.
    /// </summary>
    public event Action? SignaturesTabRequested;

    /// <summary>Raised by a block's "Dependency chain" button; the dependency slide-over opens on the block.</summary>
    public event Action<Block>? DependencyChainRequested;

    /// <summary>
    /// Raised by the slide-over's "See on the full graph" link with a block id. WorkflowInteraction
    /// opens the Graph tab with the block shown on the map.
    /// </summary>
    public event Action<string>? GraphTabRequested;

    /// <summary>
    /// Raised when a block-panel component asks to bring another block into view (e.g. a block
    /// name in the Citation trace panel). WorkflowInteraction subscribes and scrolls to it.
    /// </summary>
    public event Action<Block>? BlockOpenRequested;

    public EditorSurfaceState(EditorSurfaceCapabilities capabilities)
    {
        Capabilities = capabilities;
    }

    public Block AddBlock(Section section, int index)
    {
        var block = new Block(BlockTypes.AiQuery, section, index);
        block.SupportedDocumentClasses.Add(DocumentClass.KEY_ANY);

        SectionChanged?.Invoke(section);

        return block;
    }

    public void RemoveBlock(Block block)
    {
        Section? section = block.Section;
        if (section is null)
        {
            return;
        }

        _ = section.RemoveBlockAt(block.Order);

        DocumentClassesChanged?.Invoke();
        SectionChanged?.Invoke(section);
    }

    public Block? DuplicateBlock(Block block)
    {
        Section? section = block.Section;
        if (section is null)
        {
            return null;
        }

        Block copy = block.DuplicateBlockToSection(section);

        SectionChanged?.Invoke(section);

        return copy;
    }

    public void MoveBlock(Block block, Section destination, int newIndex)
    {
        Section? source = block.Section;
        if (source is null)
        {
            return;
        }

        bool isCrossSection = destination.Id != block.SectionId;

        if (isCrossSection && !Capabilities.CanMoveAcrossSections)
        {
            throw new InvalidOperationException(
                "Moving a block to a different section is not permitted on this surface.");
        }

        block.MoveToSection(destination, newIndex);

        SectionChanged?.Invoke(source);

        if (isCrossSection)
        {
            SectionChanged?.Invoke(destination);
        }
    }

    public bool ToggleSectionCitations(Section section)
    {
        bool enable = !section.Blocks.Any(
            block => block.Properties is AiQueryProperties props && props.ShouldGetCitationsFromModel);

        ApplySectionCitations(section, enable);

        SectionChanged?.Invoke(section);

        return enable;
    }

    public void ApplySectionCitations(Section section, bool enable)
    {
        foreach (Block block in section.Blocks.Where(block => block.Type is BlockTypes.AiQuery))
        {
            if (enable)
            {
                block.Properties ??= new AiQueryProperties();
            }

            if (block.Properties is AiQueryProperties props)
            {
                props.ShouldGetCitationsFromModel = enable;
            }
        }
    }

    public void NotifySectionChanged(Section section)
    {
        SectionChanged?.Invoke(section);
    }

    public void NotifyBlockChanged(Block block)
    {
        BlockChanged?.Invoke(block);
    }

    public void NotifyDocumentClassesChanged()
    {
        DocumentClassesChanged?.Invoke();
    }

    public void RequestSignaturesTab()
    {
        SignaturesTabRequested?.Invoke();
    }

    public void RequestDependencyChain(Block block)
    {
        DependencyChainRequested?.Invoke(block);
    }

    public void RequestGraphTab(string blockId)
    {
        GraphTabRequested?.Invoke(blockId);
    }

    public void RequestOpenBlock(Block block)
    {
        BlockOpenRequested?.Invoke(block);
    }

    /// <summary>
    /// Records the blocks and sections, by stable id, that the agent's accepted changes touched for
    /// one proposal. They show as changed by the agent until the next save or until that proposal is undone.
    /// </summary>
    public void MarkChangedByAgent(string proposalKey, IEnumerable<string> stableIds)
    {
        if (!_agentChanges.TryGetValue(proposalKey, out HashSet<string>? marked))
        {
            marked = new HashSet<string>(StringComparer.Ordinal);
            _agentChanges[proposalKey] = marked;
        }

        marked.UnionWith(stableIds);
    }

    public void UnmarkChangedByAgent(string proposalKey)
    {
        _ = _agentChanges.Remove(proposalKey);
    }

    /// <summary>Takes the mark off some of one proposal's blocks and sections, after part of it was undone.</summary>
    public void UnmarkChangedByAgent(string proposalKey, IEnumerable<string> stableIds)
    {
        if (_agentChanges.TryGetValue(proposalKey, out HashSet<string>? marked))
        {
            marked.ExceptWith(stableIds);
        }
    }

    public void ClearChangedByAgent()
    {
        _agentChanges.Clear();
    }

    public bool IsChangedByAgent(Block block)
    {
        return block.StableBlockId is string id && _agentChanges.Values.Any(marked => marked.Contains(id));
    }

    /// <summary>True when the section itself or any block in it was changed by the agent.</summary>
    public bool IsChangedByAgent(Section section)
    {
        return (section.StableSectionId is string id && _agentChanges.Values.Any(marked => marked.Contains(id)))
            || section.Blocks.Any(IsChangedByAgent);
    }

    /// <summary>The workflow with the agent's undecided changes applied, for the graph; null when there are none.</summary>
    public EditorAgentPreview? AgentPreview { get; private set; }

    public void SetAgentPreview(EditorAgentPreview? preview)
    {
        AgentPreview = preview;
    }

    /// <summary>The blocks and sections, by stable id, the agent proposes to change and nobody has decided yet.</summary>
    public void SetProposedByAgent(IEnumerable<string> stableIds)
    {
        _agentProposed.Clear();
        _agentProposed.UnionWith(stableIds);
    }

    public bool IsProposedByAgent(Block block)
    {
        return block.StableBlockId is string id && _agentProposed.Contains(id);
    }

    /// <summary>True when the section itself or any block in it has a change the agent proposes.</summary>
    public bool IsProposedByAgent(Section section)
    {
        return (section.StableSectionId is string id && _agentProposed.Contains(id)) || section.Blocks.Any(IsProposedByAgent);
    }

    /// <summary>The class marking what the agent did or proposes here; empty when neither.</summary>
    public string AgentMarkClass(Block block)
    {
        return IsChangedByAgent(block) ? " " + EditorAgentMarks.CHANGED_CLASS
            : IsProposedByAgent(block) ? " " + EditorAgentMarks.PROPOSED_CLASS
            : string.Empty;
    }

    public string AgentMarkClass(Section section)
    {
        return IsChangedByAgent(section) ? " " + EditorAgentMarks.CHANGED_CLASS
            : IsProposedByAgent(section) ? " " + EditorAgentMarks.PROPOSED_CLASS
            : string.Empty;
    }

    public string? AgentMarkLabel(Block block)
    {
        return IsChangedByAgent(block) ? EditorAgentMarks.CHANGED_LABEL
            : IsProposedByAgent(block) ? EditorAgentMarks.PROPOSED_LABEL
            : null;
    }

    public string? AgentMarkLabel(Section section)
    {
        return IsChangedByAgent(section) ? EditorAgentMarks.CHANGED_LABEL
            : IsProposedByAgent(section) ? EditorAgentMarks.PROPOSED_LABEL
            : null;
    }
}
