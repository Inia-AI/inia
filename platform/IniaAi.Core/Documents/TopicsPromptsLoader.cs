using IniaAi.Core.Common;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.DocumentAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Documents;

public sealed class TopicsPromptsLoader : PromptsLoader<TopicsPrompts>
{
    public TopicsPromptsLoader(ILogger logger, IServiceScopeFactory serviceScopeFactory)
        : base(logger, serviceScopeFactory)
    {
    }

    protected override async Task<TopicsPrompts?> ReadNewestRowAsync(ICoreUnitOfWork coreUnitOfWork)
    {
        return await coreUnitOfWork.Repository<TopicsPrompts>()
            .GetFirstOrDefaultAsync(new LatestTopicsPromptsSpec())
            .ConfigureAwait(false);
    }

    protected override TopicsPrompts Defaults()
    {
        return TopicsPromptsDefaults.Create();
    }
}
