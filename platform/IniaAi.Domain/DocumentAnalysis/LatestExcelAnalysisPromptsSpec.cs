using Ardalis.Specification;
using IniaAi.Domain.Entities.DocumentAnalysis;

namespace IniaAi.Domain.DocumentAnalysis;

public sealed class LatestExcelAnalysisPromptsSpec
    : Specification<ExcelAnalysisPrompts>, ISingleResultSpecification<ExcelAnalysisPrompts>
{
    public LatestExcelAnalysisPromptsSpec(bool tracked = false)
    {
        _ = Query.OrderByDescending(prompts => prompts.CreatedAt);

        if (!tracked)
        {
            _ = Query.AsNoTracking();
        }
    }
}
