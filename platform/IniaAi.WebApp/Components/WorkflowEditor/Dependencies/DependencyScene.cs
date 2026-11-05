namespace IniaAi.WebApp.Components.WorkflowEditor.Dependencies;

public static class DependencyViewModes
{
    public const string CHAIN = "chain";
    public const string MAP = "map";
}

public static class DependencyCardKinds
{
    public const string BLOCK = "block";
    public const string INPUT = "input";
    public const string STUB = "stub";
    public const string SECTION = "section";
}

public static class DependencyRoles
{
    public const string FOCUS = "focus";
    public const string UPSTREAM = "up";
    public const string DOWNSTREAM = "down";
    public const string NONE = "none";
}

public static class DependencyLinkStyles
{
    public const string DATA = "data";
    public const string DOCUMENT = "document";
    public const string COUNTERPARTY = "counterparty";
    public const string GATE = "gate";
    public const string INPUT = "input";
    public const string STUB = "stub";
}

public static class DependencyGroupKinds
{
    public const string VERIFICATION = "verification";
    public const string SECTION = "section";
}

/// <summary>
/// What the dependency graph draws: the cards, links and groups the JS module lays out and renders.
/// It is plain data so the shaping can be tested without a browser.
/// </summary>
public sealed record DependencyScene(
    string Mode,
    string? FocusId,
    IReadOnlyList<DependencyCard> Cards,
    IReadOnlyList<DependencyLink> Links,
    IReadOnlyList<DependencyGroup> Groups,
    string? EmptyMessage);

public sealed record DependencyCard
{
    public required string Id { get; init; }

    public required string Kind { get; init; }

    public required string Label { get; init; }

    /// <summary>The block's section, the input's kind, or the stub's "in K sections".</summary>
    public string? Detail { get; init; }

    public string? TypeLabel { get; init; }

    public string? Hue { get; init; }

    public string? Tag { get; init; }

    public string Role { get; init; } = DependencyRoles.NONE;

    /// <summary>Workflow inputs (variables, signature fields, system and loop values) this block reads.</summary>
    public int InputCount { get; init; }

    /// <summary>Section-gate sources deciding whether this block runs.</summary>
    public int GateCount { get; init; }

    public bool InputsOpen { get; init; }

    public bool GatesOpen { get; init; }

    public bool InCycle { get; init; }

    public bool IsUnresolved { get; init; }

    /// <summary>What the agent's undecided proposal does to this block or section: "added", "changed", or null.</summary>
    public string? AgentMark { get; init; }

    public int VariantCount { get; init; }

    public string? GroupId { get; init; }

    public int BlockCount { get; init; }

    /// <summary>Section cards: block-type counts. Stubs: the unopened blocks per section.</summary>
    public IReadOnlyList<string> Summary { get; init; } = [];
}

public sealed record DependencyLink
{
    public required string Id { get; init; }

    public required string SourceId { get; init; }

    public required string TargetId { get; init; }

    public required string Style { get; init; }

    public string Role { get; init; } = DependencyRoles.NONE;

    /// <summary>How many block-to-block links a map edge stands for.</summary>
    public int Weight { get; init; } = 1;

    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <param name="Id">Cards join a group through <see cref="DependencyCard.GroupId"/>.</param>
public sealed record DependencyGroup(string Id, string Kind, string Label);
