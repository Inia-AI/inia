using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

/// <summary>One cell in the cross-deployment compare grid: one workflow on one deployment.</summary>
public record CurrencyComparisonCellDto(
    string InstanceName,
    string? ShortCode,
    CurrencyCellStatus Status);

/// <summary>
/// One row in the compare grid: a single workflow across every compared deployment.
/// <see cref="WorkflowTypeName"/> is the workflow type's name from the newest card that carries one,
/// or the unaltered <see cref="WorkflowTypeId"/> when no card does.
/// </summary>
public record CurrencyComparisonRowDto(
    string WorkflowTypeId,
    string WorkflowTypeName,
    IReadOnlyList<CurrencyComparisonCellDto> Cells);

/// <summary>The full cross-deployment compare grid, deployments as columns and workflows as rows.</summary>
public record CurrencyComparisonDto(
    IReadOnlyList<string> InstanceNames,
    IReadOnlyList<CurrencyComparisonRowDto> Rows);
