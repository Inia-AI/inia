using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Models.Responses.ContentRefinement;

namespace IniaAi.Application.Queries.TextProcessing.RefineContent.GetPresets;

public record GetRefinementPresetsQuery(
    string OrganizationId,
    string UserId) : IRequest<ErrorOr<List<RefinementPresetResponse>>>;
