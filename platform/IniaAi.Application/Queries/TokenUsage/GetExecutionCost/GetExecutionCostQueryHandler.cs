using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.WorkflowExecutions;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.EntityFrameworkCore;
using TokenUsageEntity = IniaAi.Domain.Entities.TokenUsage.TokenUsage;

namespace IniaAi.Application.Queries.TokenUsage.GetExecutionCost;

/// <summary>
/// Sums a run's usage rows: every row stamped with the execution, and every row of a block in
/// the run's executed workflow (a row whose execution stamp is missing still belongs to its block).
/// </summary>
public class GetExecutionCostQueryHandler : IRequestHandler<GetExecutionCostQuery, ErrorOr<ExecutionCostDto>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;

    public GetExecutionCostQueryHandler(IUserDataUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    private sealed record UsageRow(
        string? BlockId,
        UsageType UsageType,
        string ModelId,
        List<(PricingComponentType Type, int Units, decimal Cost)> Components);

    private sealed record BlockLabel(string Id, string? Name, string? ReplacementTag, string BlockType);

    public async Task<ErrorOr<ExecutionCostDto>> Handle(GetExecutionCostQuery request, CancellationToken cancellationToken)
    {
        ErrorOr<WorkflowExecution> execution = await RunEvidenceAccess.ResolveAsync(
            _unitOfWork,
            request.OrganizationId,
            request.UserId,
            request.ExecutionId,
            request.CallerIsSuperAdmin,
            cancellationToken);

        if (execution.IsError)
        {
            return execution.Errors;
        }

        string? workflowId = execution.Value.WorkflowId;
        IQueryable<Block> runBlocks = _unitOfWork.Repository<Block>()
            .GetAll()
            .AsNoTracking()
            .Where(block => workflowId != null && block.Section != null && block.Section.WorkflowId == workflowId);

        List<BlockLabel> blocks = await runBlocks
            .Select(block => new BlockLabel(block.Id, block.Name, block.ReplacementTag, block.Type.ToString()))
            .ToListAsync(cancellationToken);

        IQueryable<string> runBlockIds = runBlocks.Select(block => block.Id);

        var rows = await _unitOfWork.Repository<TokenUsageEntity>()
            .GetAll()
            .AsNoTracking()
            .Where(usage => usage.OrganizationId == request.OrganizationId
                && (usage.WorkflowExecutionId == request.ExecutionId
                    || (usage.BlockId != null && runBlockIds.Contains(usage.BlockId))))
            .Select(usage => new
            {
                usage.BlockId,
                usage.UsageType,
                usage.ModelId,
                Components = usage.Components
                    .Select(component => new { component.ComponentType, component.Units, component.Cost })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        List<UsageRow> usageRows = [.. rows.Select(row => new UsageRow(
            row.BlockId,
            row.UsageType,
            row.ModelId,
            [.. row.Components.Select(component => (component.ComponentType, component.Units, component.Cost))]))];

        Dictionary<string, BlockLabel> labels = blocks.ToDictionary(static block => block.Id, StringComparer.Ordinal);

        List<ExecutionCostLineDto> blockLines = [.. usageRows
            .Where(row => row.BlockId is not null)
            .GroupBy(row => row.BlockId!, StringComparer.Ordinal)
            .Select(group => Line(group.ToList(), labels.GetValueOrDefault(group.Key), group.Key))
            .OrderByDescending(static line => line.Cost)
            .ThenBy(static line => line.Name, StringComparer.Ordinal)];

        List<ExecutionCostLineDto> otherLines = [.. usageRows
            .Where(row => row.BlockId is null)
            .GroupBy(row => row.UsageType)
            .Select(group => Line(group.ToList(), label: null, blockId: null) with { Name = group.Key.ToString() })
            .OrderByDescending(static line => line.Cost)];

        return new ExecutionCostDto
        {
            ExecutionId = request.ExecutionId,
            TotalCost = usageRows.Sum(static row => row.Components.Sum(static component => component.Cost)),
            UsageRecordCount = usageRows.Count,
            UnpricedRecordCount = usageRows.Count(IsUnpriced),
            Blocks = blockLines,
            Other = otherLines,
        };
    }

    // An agent run's rows are never unpriced: a token row with no pricing row carries zero and its
    // spend row carries the runtime's figure for it (AgentRunBilling), and rows from before per-run billing
    // carry the whole bill on the spend row.
    private static bool IsUnpriced(UsageRow row)
    {
        return row.UsageType != UsageType.LlmAgentRun
            && row.Components.Count > 0
            && row.Components.Sum(static component => component.Cost) == 0m;
    }

    private static ExecutionCostLineDto Line(List<UsageRow> rows, BlockLabel? label, string? blockId)
    {
        List<(PricingComponentType Type, int Units, decimal Cost)> components = [.. rows.SelectMany(static row => row.Components)];

        return new ExecutionCostLineDto
        {
            BlockId = blockId,
            Name = label?.Name ?? blockId ?? string.Empty,
            ReplacementTag = label?.ReplacementTag,
            BlockType = label?.BlockType,
            Models = [.. rows
                .Select(static row => row.ModelId)
                .Where(static model => !string.IsNullOrWhiteSpace(model))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)],
            Calls = rows.Count,
            InputTokens = UnitsOf(components, PricingComponentType.Input, PricingComponentType.BatchInput),
            OutputTokens = UnitsOf(components, PricingComponentType.Output, PricingComponentType.Reasoning, PricingComponentType.BatchOutput),
            CacheReadTokens = UnitsOf(components, PricingComponentType.CacheRead, PricingComponentType.BatchCacheRead),
            CacheWriteTokens = UnitsOf(components, PricingComponentType.CacheWrite5m, PricingComponentType.CacheWrite1h),
            Cost = components.Sum(static component => component.Cost),
        };
    }

    private static long UnitsOf(
        List<(PricingComponentType Type, int Units, decimal Cost)> components,
        params PricingComponentType[] types)
    {
        return components.Where(component => types.Contains(component.Type)).Sum(static component => (long)component.Units);
    }
}
