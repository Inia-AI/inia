using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Workflows.Strategies;

public record BlockStrategyOutput
{
    public List<DocumentInfo> Documents { get; } = [];
}
