using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Strategies;

/// <summary>
/// The strategy the workflow engine runs for each block type: the platform's own, and one from each
/// <see cref="BlockStrategyRegistration"/> that a closed module or an extension adds.
/// </summary>
internal sealed class BlockStrategies
{
    private static readonly BlockStrategyRegistration[] _platform =
    [
        new(BlockTypes.AiQuery, new AiQueryStrategy()),
        new(BlockTypes.IntelligentSearch, new ExaSearchStrategy()),
        new(BlockTypes.Merge, new MergeStrategy()),
        new(BlockTypes.CalculateNumber, new CalculateFormulaStrategy()),
        new(BlockTypes.SimpleRag, new RetrieveInformationFromDocumentStrategy()),
        new(BlockTypes.BuildTopicsForAllDocuments, new BuildTopicsForAllDocumentsStrategy()),
        new(BlockTypes.ExchangeRate, new ExchangeRateStrategy()),
        new(BlockTypes.WebApiCall, new WebApiCallStrategy()),
        new(BlockTypes.DynamicChart, new DynamicChartStrategy()),
        new(BlockTypes.SwitchCase, new SwitchCaseStrategy()),
        new(BlockTypes.MergeJson, new MergeJsonStrategy()),
        new(BlockTypes.StringManipulation, new StringManipulationStrategy()),
        new(BlockTypes.Agent, new AgentBlockStrategy()),
    ];

    private readonly Dictionary<BlockTypes, IBlockStrategy> _byType = [];

    public BlockStrategies(IEnumerable<BlockStrategyRegistration> registrations)
    {
        foreach (BlockStrategyRegistration registration in _platform.Concat(registrations))
        {
            if (!_byType.TryAdd(registration.BlockType, registration.Strategy))
            {
                throw new InvalidOperationException(
                    $"Two strategies are registered for {BlockTypeLabels.Of(registration.BlockType)} blocks (type {(int)registration.BlockType}). Register one strategy for each block type.");
            }
        }
    }

    public IReadOnlyDictionary<BlockTypes, IBlockStrategy> ByType => _byType;
}
