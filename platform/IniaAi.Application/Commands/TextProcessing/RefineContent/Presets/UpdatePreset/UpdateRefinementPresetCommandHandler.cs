using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Tools;
using IniaAi.Domain.Specifications.Common;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Models.Responses.ContentRefinement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.UpdatePreset;

public class UpdateRefinementPresetCommandHandler
    : IRequestHandler<UpdateRefinementPresetCommand, ErrorOr<RefinementPresetResponse>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateRefinementPresetCommandHandler> _logger;

    public UpdateRefinementPresetCommandHandler(
        IUserDataUnitOfWork unitOfWork,
        ILogger<UpdateRefinementPresetCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ErrorOr<RefinementPresetResponse>> Handle(
        UpdateRefinementPresetCommand request,
        CancellationToken cancellationToken)
    {
        RefinementPreset? preset = await _unitOfWork.Repository<RefinementPreset>()
            .GetAll()
            .Where(RefinementPresetExpressions.VisibleTo(request.OrganizationId, request.UserId))
            .Where(p => p.Id == request.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (preset is null)
        {
            return Error.NotFound("RefinementPreset.NotFound", "Preset not found");
        }

        if (preset.UserId != request.UserId)
        {
            return Error.Forbidden("RefinementPreset.NotOwner", "Only the preset owner can update it");
        }

        preset.Name = request.Name;
        preset.Instruction = request.Instruction;
        preset.IsShared = request.IsShared;
        preset.UpdatedAt = DateTime.UtcNow;
        preset.UpdatedByUserId = request.UserId;

        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation("Updated refinement preset '{Name}' ({Id})", LogSanitizer.ForLog(request.Name), LogSanitizer.ForLog(request.Id));

        return new RefinementPresetResponse
        {
            Id = preset.Id,
            Name = preset.Name,
            Instruction = preset.Instruction,
            IsShared = preset.IsShared,
            UserId = preset.UserId,
            CreatedAt = preset.CreatedAt,
        };
    }
}
