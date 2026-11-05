using IniaAi.Domain.Entities.SharedComponents.Enums;

namespace IniaAi.Domain.Abstractions.Entities;

/// <summary>
/// A workflow node (block or section) carrying the shared-component link record: the component id,
/// link state, version hash, and bindings quartet stamped by promote/materialize.
/// </summary>
public interface ISharedComponentLinkable
{
    string? SharedComponentId { get; set; }

    SharedComponentLinkState? SharedComponentLinkState { get; set; }

    string? SharedComponentVersionHash { get; set; }

    Dictionary<string, string>? SharedComponentBindings { get; set; }
}

public static class SharedComponentLinkableExtensions
{
    public static bool IsActivelyLinked(this ISharedComponentLinkable node)
    {
        return node.SharedComponentLinkState == SharedComponentLinkState.Linked
            && !string.IsNullOrEmpty(node.SharedComponentId);
    }
}
