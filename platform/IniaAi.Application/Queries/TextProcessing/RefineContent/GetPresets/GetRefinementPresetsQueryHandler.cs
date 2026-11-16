using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.Tools;
using IniaAi.Domain.Specifications.Common;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Models.Responses.ContentRefinement;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.TextProcessing.RefineContent.GetPresets;

public class GetRefinementPresetsQueryHandler
    : IRequestHandler<GetRefinementPresetsQuery, ErrorOr<List<RefinementPresetResponse>>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;

    public GetRefinementPresetsQueryHandler(IUserDataUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<List<RefinementPresetResponse>>> Handle(
        GetRefinementPresetsQuery request,
        CancellationToken cancellationToken)
    {
        // Get user's own presets + shared presets from the same org
        List<RefinementPreset> presets = await _unitOfWork.Repository<RefinementPreset>()
            .GetAll()
            .AsNoTracking()
            .Where(RefinementPresetExpressions.VisibleTo(request.OrganizationId, request.UserId))
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return presets.Select(p => new RefinementPresetResponse
        {
            Id = p.Id,
            Name = p.Name,
            Instruction = p.Instruction,
            IsShared = p.IsShared,
            UserId = p.UserId,
            CreatedAt = p.CreatedAt,
        }).ToList();
    }
}
