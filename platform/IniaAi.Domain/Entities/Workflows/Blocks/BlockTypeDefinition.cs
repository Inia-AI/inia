using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Entities.Workflows.Blocks;

/// <summary>
/// What the platform needs to know about one block type: its number, the name a person sees and
/// the class its settings read into. An extension's type also brings what the workflow editor
/// shows for it: a short label, a hue and the component that edits its settings.
/// </summary>
public sealed record BlockTypeDefinition
{
    private readonly Func<BlockProperties> _createDefaultProperties;

    private BlockTypeDefinition(BlockTypes type, string label, Type propertiesType, Func<BlockProperties> createDefaultProperties)
    {
        Type = type;
        Label = label;
        PropertiesType = propertiesType;
        _createDefaultProperties = createDefaultProperties;
    }

    public BlockTypes Type { get; }

    public string Label { get; }

    /// <summary>The class a stored block's settings read into.</summary>
    public Type PropertiesType { get; }

    public string? ShortLabel { get; init; }

    /// <summary>The block's colour on the editor canvas, as a CSS colour.</summary>
    public string? Hue { get; init; }

    /// <summary>
    /// The Razor component that edits the block's settings. It takes a <c>Block</c> parameter and a
    /// <c>BlockChanged</c> callback.
    /// </summary>
    public Type? EditorComponent { get; init; }

    public static BlockTypeDefinition For<TProperties>(BlockTypes type, string label)
        where TProperties : BlockProperties, new()
    {
        return new BlockTypeDefinition(type, label, typeof(TProperties), static () => new TProperties());
    }

    public BlockProperties CreateDefaultProperties()
    {
        return _createDefaultProperties();
    }
}
