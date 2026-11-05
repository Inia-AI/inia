using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;

namespace IniaAi.Domain.Entities.Testing;

public class WorkflowRegressionBlockResult : Entity<WorkflowRegressionBlockResult>, IOrganizationScoped, IIdentifiable
{
    public string OrganizationId { get; set; } = string.Empty;
    public string WorkflowRegressionRunId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Fact { get; set; } = string.Empty;
    public string Sources { get; set; } = string.Empty;
    public string BaselineValue { get; set; } = string.Empty;
    public string RerunValue { get; set; } = string.Empty;
    public FactMatchStatus Status { get; set; } = FactMatchStatus.Match;
    public decimal SimilarityScore { get; set; }
    public string? BaselineVariations { get; set; }
    public string? RerunVariations { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public bool DefinitionChanged { get; set; }
    public string? DefinitionDiffSummary { get; set; }
    public decimal? BaselineNumeric { get; set; }
    public decimal? RerunNumeric { get; set; }
    public decimal? Tolerance { get; set; }
    public string? SchemaFieldType { get; set; }

    public WorkflowRegressionRun WorkflowRegressionRun { get; set; } = default!;
}
