using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Tools;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.DeletePreset;

public class DeleteRefinementPresetCommandHandler
    : IRequestHandler<DeleteRefinementPresetCommand, ErrorOr<Success>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteRefinementPresetCommandHandler> _logger;

    public DeleteRefinementPresetCommandHandler(
        IUserDataUnitOfWork unitOfWork,
        ILogger<DeleteRefinementPresetCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        DeleteRefinementPresetCommand request,
        CancellationToken cancellationToken)
    {
        RefinementPreset? preset = await _unitOfWork.Repository<RefinementPreset>()
            .GetAll()
            .Where(p => p.Id == request.Id && p.OrganizationId == request.OrganizationId && !p.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (preset is null)
        {
            return Error.NotFound("RefinementPreset.NotFound", "Preset not found");
        }

        if (preset.UserId != request.UserId)
        {
            return Error.Forbidden("RefinementPreset.NotOwner", "Only the preset owner can delete it");
        }

        preset.IsDeleted = true;
        preset.DeletedAt = DateTime.UtcNow;
        preset.DeletedByUserId = request.UserId;

        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation("Deleted refinement preset '{Name}' ({Id})", preset.Name, LogSanitizer.ForLog(request.Id));

        return Result.Success;
    }
}
