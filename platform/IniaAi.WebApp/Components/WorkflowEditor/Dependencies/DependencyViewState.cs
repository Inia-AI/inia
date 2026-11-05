namespace IniaAi.WebApp.Components.WorkflowEditor.Dependencies;

/// <summary>
/// What the reader has asked the dependency graph to show: the mode, the focused block and the trail
/// back to earlier ones, and every count or stub they opened. Focusing another block starts it from
/// one step each way again. The Graph tab and the slide-over each keep their own.
/// </summary>
public sealed class DependencyViewState
{
    public const int MAX_TRAIL = 8;

    private const string STUB_UPSTREAM_PREFIX = "stub:up:";
    private const string STUB_DOWNSTREAM_PREFIX = "stub:down:";

    private readonly List<string> _trail = [];

    public string Mode { get; private set; } = DependencyViewModes.MAP;

    public string? FocusBlockId { get; private set; }

    /// <summary>Earlier focused blocks, oldest first, ending with the current one.</summary>
    public IReadOnlyList<string> Trail => _trail;

    public bool ShowInputs { get; set; }

    public bool ShowGates { get; set; }

    public HashSet<string> ExpandedUpstream { get; } = new(StringComparer.Ordinal);

    public HashSet<string> ExpandedDownstream { get; } = new(StringComparer.Ordinal);

    public HashSet<string> InputsOpenFor { get; } = new(StringComparer.Ordinal);

    public HashSet<string> GatesOpenFor { get; } = new(StringComparer.Ordinal);

    public HashSet<string> OpenSections { get; } = new(StringComparer.Ordinal);

    /// <summary>A block another surface asked the map to show, applied when the map is next drawn.</summary>
    public string? PendingMapFocusId { get; private set; }

    public static string UpstreamStubId(string nodeId)
    {
        return STUB_UPSTREAM_PREFIX + nodeId;
    }

    public static string DownstreamStubId(string nodeId)
    {
        return STUB_DOWNSTREAM_PREFIX + nodeId;
    }

    /// <summary>Centres the chain on a block. Returns false when it was already the focus in chain mode.</summary>
    public bool Focus(string blockId)
    {
        bool unchanged = Mode == DependencyViewModes.CHAIN
            && string.Equals(FocusBlockId, blockId, StringComparison.Ordinal);
        Mode = DependencyViewModes.CHAIN;
        if (unchanged)
        {
            return false;
        }

        FocusBlockId = blockId;
        ResetExpansion();

        _ = _trail.Remove(blockId);
        _trail.Add(blockId);
        if (_trail.Count > MAX_TRAIL)
        {
            _trail.RemoveAt(0);
        }

        return true;
    }

    /// <summary>A fresh state for a new look at the graph that keeps the reader's input and gate switches.</summary>
    public DependencyViewState WithSwitchesOnly()
    {
        return new DependencyViewState
        {
            ShowInputs = ShowInputs,
            ShowGates = ShowGates,
        };
    }

    public void RequestMapFocus(string blockId)
    {
        PendingMapFocusId = blockId;
    }

    /// <summary>
    /// Shows the requested block on the map: it becomes the focus, its section opens into its blocks and
    /// the map stays in map mode. Returns false when nothing was requested or the block is gone.
    /// </summary>
    /// <param name="sectionKeyOf">The map's key for the block's section, or null when the block is not on it.</param>
    public bool ApplyPendingMapFocus(Func<string, string?> sectionKeyOf)
    {
        if (PendingMapFocusId is not { } blockId)
        {
            return false;
        }

        PendingMapFocusId = null;
        if (sectionKeyOf(blockId) is not { } sectionKey)
        {
            return false;
        }

        _ = Focus(blockId);
        Mode = DependencyViewModes.MAP;
        _ = OpenSections.Add(sectionKey);

        return true;
    }

    public void ShowMap()
    {
        Mode = DependencyViewModes.MAP;
    }

    public void ShowChain()
    {
        if (FocusBlockId is not null)
        {
            Mode = DependencyViewModes.CHAIN;
        }
    }

    /// <summary>Drops blocks the workflow no longer has, after an edit removed them.</summary>
    public void Forget(Func<string, bool> blockExists)
    {
        _ = _trail.RemoveAll(id => !blockExists(id));
        if (FocusBlockId is not null && !blockExists(FocusBlockId))
        {
            FocusBlockId = _trail.Count > 0 ? _trail[^1] : null;
            ResetExpansion();
            if (FocusBlockId is null)
            {
                Mode = DependencyViewModes.MAP;
            }
        }
    }

    public bool InputsShownFor(string blockId)
    {
        return ShowInputs || InputsOpenFor.Contains(blockId);
    }

    public bool GatesShownFor(string blockId)
    {
        return ShowGates || GatesOpenFor.Contains(blockId);
    }

    /// <summary>Opens one more step beyond the node a stub hangs from.</summary>
    public bool ExpandStub(string stubId)
    {
        if (stubId.StartsWith(STUB_UPSTREAM_PREFIX, StringComparison.Ordinal))
        {
            return ExpandedUpstream.Add(stubId[STUB_UPSTREAM_PREFIX.Length..]);
        }

        if (stubId.StartsWith(STUB_DOWNSTREAM_PREFIX, StringComparison.Ordinal))
        {
            return ExpandedDownstream.Add(stubId[STUB_DOWNSTREAM_PREFIX.Length..]);
        }

        return false;
    }

    /// <summary>Shows or hides a block's inputs. From the map, which draws no inputs, it opens the block's chain with them shown.</summary>
    public void ToggleInputs(string nodeId)
    {
        FocusFromMap(nodeId);
        Toggle(InputsOpenFor, nodeId);
    }

    /// <summary>Shows or hides a block's section gates. From the map it opens the block's chain with them shown.</summary>
    public void ToggleGates(string nodeId)
    {
        FocusFromMap(nodeId);
        Toggle(GatesOpenFor, nodeId);
    }

    public void ToggleSection(string sectionId)
    {
        Toggle(OpenSections, sectionId);
    }

    private void FocusFromMap(string nodeId)
    {
        if (Mode == DependencyViewModes.MAP)
        {
            _ = Focus(nodeId);
        }
    }

    private void ResetExpansion()
    {
        ExpandedUpstream.Clear();
        ExpandedDownstream.Clear();
        InputsOpenFor.Clear();
        GatesOpenFor.Clear();
    }

    private static void Toggle(HashSet<string> set, string id)
    {
        if (!set.Remove(id))
        {
            _ = set.Add(id);
        }
    }
}
