namespace IniaAi.WebApp.Components.WorkflowEditor.State;

public sealed record EditorSurfaceCapabilities
{
    public bool ShowCounterpartySettings { get; init; }

    public bool CanExecute { get; init; }

    public bool CanCopyToCore { get; init; }

    public bool CanMoveAcrossSections { get; init; }

    public bool CanShareToLibrary { get; init; }

    public bool IsSingleBlockHost { get; init; }

    public bool IsComponentDefinitionHost { get; init; }

    public bool CanShowDependencyChain { get; init; }

    /// <summary>
    /// Whether a block shows its "If this block fails" setting. A Library definition leaves the
    /// setting out, because each workflow sets it for its own copy.
    /// </summary>
    public bool ShowFailureSetting { get; init; }

    public static EditorSurfaceCapabilities FullEditor { get; } = new()
    {
        ShowCounterpartySettings = true,
        CanExecute = true,
        CanCopyToCore = true,
        CanMoveAcrossSections = true,
        CanShareToLibrary = true,
        CanShowDependencyChain = true,
        ShowFailureSetting = true,
    };

    public static EditorSurfaceCapabilities Library { get; } = new();
}
