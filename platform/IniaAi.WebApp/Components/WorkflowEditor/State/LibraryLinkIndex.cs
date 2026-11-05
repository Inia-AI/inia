using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using ContractsState = IniaAi.WebApi.Contracts.Enums.SharedComponentState;
using DomainLinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;

namespace IniaAi.WebApp.Components.WorkflowEditor.State;

/// <summary>
/// Ambient link classification for editor badges, built from one registry list snapshot. Compares
/// only the stamped link hash against the component head — never node content — so locally edited
/// copies (LocalAhead/Diverged) deliberately stay invisible here; the reconcile drawer owns
/// content-level drift. Tolerant by construction: a component id absent from the registry
/// classifies as Missing and never throws. A broken-out copy no longer follows its component, so
/// it classifies as None, like content that never came from the Library.
/// </summary>
public sealed class LibraryLinkIndex
{
    private static readonly LibraryLinkStatus _none = new(LibraryLinkIndicator.None, null);

    private readonly Dictionary<string, SharedComponentSummaryDto> _componentsById;

    public LibraryLinkIndex(IReadOnlyCollection<SharedComponentSummaryDto> components)
    {
        _componentsById = new Dictionary<string, SharedComponentSummaryDto>(StringComparer.Ordinal);
        foreach (SharedComponentSummaryDto component in components)
        {
            _componentsById[component.Id] = component;
        }
    }

    public LibraryLinkStatus For(Block block)
    {
        return Classify(block);
    }

    public LibraryLinkStatus For(Section section)
    {
        return Classify(section);
    }

    public string? HeadVersionIdFor(string componentId)
    {
        return _componentsById.GetValueOrDefault(componentId)?.HeadVersionId;
    }

    public int AttentionCount(Workflow workflow)
    {
        return workflow.Sections.Select(For)
            .Concat(workflow.GetAllBlocks().Select(For))
            .Count(s => s.Indicator
                is LibraryLinkIndicator.UpdateAvailable
                or LibraryLinkIndicator.Missing
                or LibraryLinkIndicator.Retired);
    }

    public int MissingCount(Workflow workflow)
    {
        return workflow.Sections.Select(For)
            .Concat(workflow.GetAllBlocks().Select(For))
            .Count(status => status.Indicator == LibraryLinkIndicator.Missing);
    }

    private LibraryLinkStatus Classify(ISharedComponentLinkable node)
    {
        if (string.IsNullOrWhiteSpace(node.SharedComponentId) || node.SharedComponentLinkState == DomainLinkState.BrokenOut)
        {
            return _none;
        }

        SharedComponentSummaryDto? component = _componentsById.GetValueOrDefault(node.SharedComponentId!);

        if (component is null)
        {
            return new LibraryLinkStatus(LibraryLinkIndicator.Missing, null);
        }

        if (component.State == ContractsState.Retired)
        {
            return new LibraryLinkStatus(LibraryLinkIndicator.Retired, component.Name);
        }

        bool atHead = component.HeadContentHash is not null
            && string.Equals(node.SharedComponentVersionHash, component.HeadContentHash, StringComparison.Ordinal);

        return new LibraryLinkStatus(
            atHead ? LibraryLinkIndicator.UpToDate : LibraryLinkIndicator.UpdateAvailable,
            component.Name);
    }
}

public enum LibraryLinkIndicator
{
    None,
    UpToDate,
    UpdateAvailable,
    Missing,
    Retired,
}

public sealed record LibraryLinkStatus(LibraryLinkIndicator Indicator, string? ComponentName);
