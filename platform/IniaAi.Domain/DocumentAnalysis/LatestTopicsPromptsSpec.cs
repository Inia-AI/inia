using Ardalis.Specification;
using IniaAi.Domain.Entities.DocumentAnalysis;

namespace IniaAi.Domain.DocumentAnalysis;

public sealed class LatestTopicsPromptsSpec
    : Specification<TopicsPrompts>, ISingleResultSpecification<TopicsPrompts>
{
    public LatestTopicsPromptsSpec(bool tracked = false)
    {
        _ = Query.OrderByDescending(prompts => prompts.CreatedAt);

        if (!tracked)
        {
            _ = Query.AsNoTracking();
        }
    }
}
