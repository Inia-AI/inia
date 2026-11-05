using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace IniaAi.Domain.Entities.Workflows.Blocks;

/// <summary>
/// Every block type this process knows: the platform's own, and each type an extension registers
/// at startup. A stored block of a type the catalog lacks keeps its settings as they were written
/// (<see cref="UnavailableBlockProperties"/>); the editor shows it as unavailable and a run
/// refuses it.
/// </summary>
public static class BlockTypeCatalog
{
    /// <summary>
    /// The first number an extension's block type may take. The platform's own types stay below
    /// it, so a block an extension stored never takes the number of a later platform type.
    /// </summary>
    public const int FIRST_EXTENSION_TYPE = 1000;

    private const string EDITOR_BLOCK_PARAMETER = "Block";
    private const string EDITOR_CHANGED_PARAMETER = "BlockChanged";

    private static readonly Lock _registering = new();

    private static readonly ConcurrentDictionary<BlockTypes, BlockTypeDefinition> _definitions =
        new(PlatformTypes().ToDictionary(static definition => definition.Type));

    private static ReadOnlyCollection<BlockTypeDefinition> _all = ByNumber();

    public static IReadOnlyList<BlockTypeDefinition> All => _all;

    public static BlockTypeDefinition? Find(BlockTypes type)
    {
        return _definitions.GetValueOrDefault(type);
    }

    public static bool IsExtensionType(BlockTypes type)
    {
        return (int)type >= FIRST_EXTENSION_TYPE;
    }

    /// <summary>
    /// The class a block's settings read into, from their <c>type</c> value: a type name in any
    /// case, or a number. Null when the catalog has no such type: the settings then read as
    /// <see cref="UnavailableBlockProperties"/> of <paramref name="type"/>.
    /// </summary>
    public static Type? PropertiesTypeOf(string discriminator, out BlockTypes type)
    {
        if (!Enum.TryParse(discriminator, ignoreCase: true, out type))
        {
            type = UnavailableBlockProperties.UNNAMED_TYPE;
            return null;
        }

        return Find(type)?.PropertiesType;
    }

    /// <summary>
    /// Adds an extension's block type. Registering the same definition again does nothing, so two
    /// hosts in one process can each register it.
    /// </summary>
    public static void Register(BlockTypeDefinition definition)
    {
        if (!IsExtensionType(definition.Type))
        {
            throw new ArgumentException(
                $"An extension's block type takes a number from {FIRST_EXTENSION_TYPE} up, so that it never collides with a platform type; {definition.Label} has {(int)definition.Type}.",
                nameof(definition));
        }

        if (definition.EditorComponent is { } editor
            && (editor.GetProperty(EDITOR_BLOCK_PARAMETER) is null || editor.GetProperty(EDITOR_CHANGED_PARAMETER) is null))
        {
            throw new ArgumentException(
                $"The editor component of {definition.Label} must take a {EDITOR_BLOCK_PARAMETER} parameter and a {EDITOR_CHANGED_PARAMETER} callback.",
                nameof(definition));
        }

        if (definition.CreateDefaultProperties().Type != definition.Type)
        {
            throw new ArgumentException(
                $"The settings class of {definition.Label} must set its Type to {(int)definition.Type} in its constructor.",
                nameof(definition));
        }

        lock (_registering)
        {
            BlockTypeDefinition stored = _definitions.GetOrAdd(definition.Type, definition);
            if (stored != definition)
            {
                throw new InvalidOperationException(
                    $"Block type {(int)definition.Type} is already registered as {stored.Label}, so {definition.Label} needs another number.");
            }

            _all = ByNumber();
        }
    }

    private static ReadOnlyCollection<BlockTypeDefinition> ByNumber()
    {
        return _definitions.Values.OrderBy(static definition => (int)definition.Type).ToList().AsReadOnly();
    }

    private static IEnumerable<BlockTypeDefinition> PlatformTypes()
    {
        yield return BlockTypeDefinition.For<AiQueryProperties>(BlockTypes.AiQuery, "AI Query");
        yield return BlockTypeDefinition.For<MergeProperties>(BlockTypes.Merge, "Apply Template To Data") with { ShortLabel = "Template" };
        yield return BlockTypeDefinition.For<CalculateFormulaProperties>(BlockTypes.CalculateNumber, "Calculate Formula") with { ShortLabel = "Formula" };
        yield return BlockTypeDefinition.For<RetrieveDataFromDocumentsProperties>(BlockTypes.SimpleRag, "Retrieve Data From Documents") with { ShortLabel = "Retrieve" };
        yield return BlockTypeDefinition.For<LoopStartProperties>(BlockTypes.LoopStart, "Loop Start");
        yield return BlockTypeDefinition.For<MergeProperties>(BlockTypes.LoopEnd, "Loop End");
        yield return BlockTypeDefinition.For<BuildTopicsProperties>(BlockTypes.BuildTopicsForAllDocuments, "Build Topics For All Documents") with { ShortLabel = "Topics" };
        yield return BlockTypeDefinition.For<IntelligentSearchProperties>(BlockTypes.IntelligentSearch, "Intelligent Search") with { ShortLabel = "Search" };
        yield return BlockTypeDefinition.For<ExchangeRateProperties>(BlockTypes.ExchangeRate, "Exchange Rate") with { ShortLabel = "FX" };
        yield return BlockTypeDefinition.For<WebApiCallProperties>(BlockTypes.WebApiCall, "Web API Call") with { ShortLabel = "Web API" };
        yield return BlockTypeDefinition.For<DynamicChartProperties>(BlockTypes.DynamicChart, "Dynamic Chart") with { ShortLabel = "Chart" };
        yield return BlockTypeDefinition.For<Json2XlsxProperties>(BlockTypes.Json2Xlsx, "JSON to XLSX") with { ShortLabel = "XLSX" };
        yield return BlockTypeDefinition.For<SwitchCaseProperties>(BlockTypes.SwitchCase, "Switch Case") with { ShortLabel = "Switch" };
        yield return BlockTypeDefinition.For<MergeJsonProperties>(BlockTypes.MergeJson, "Merge JSON");
        yield return BlockTypeDefinition.For<StringManipulationProperties>(BlockTypes.StringManipulation, "String Manipulation") with { ShortLabel = "Text" };
        yield return BlockTypeDefinition.For<AgentProperties>(BlockTypes.Agent, "AI Agent");
        yield return BlockTypeDefinition.For<PeerFinancialsProperties>(BlockTypes.PeerFinancials, "Peer Financials") with { ShortLabel = "Peers" };
        yield return BlockTypeDefinition.For<VerificationProperties>(BlockTypes.Verification, "Verification Group") with { ShortLabel = "Verify" };
    }
}
