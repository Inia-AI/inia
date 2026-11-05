using IniaAi.Core.Dto.Exa.Search;

namespace IniaAi.Core.AugmentedSearch.Exa.Interfaces;

public interface IExaSearchService
{
    Task<ExaSearchResponse?> SearchAsync(ExaSearchRequest request, CancellationToken cancellationToken = default);
}
