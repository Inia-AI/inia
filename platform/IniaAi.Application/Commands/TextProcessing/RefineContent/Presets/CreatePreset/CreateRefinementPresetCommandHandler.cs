using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Tools;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Models.Responses.ContentRefinement;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.TextProcessing.RefineContent.Presets.CreatePreset;

public class CreateRefinementPresetCommandHandler
    : IRequestHandler<CreateRefinementPresetCommand, ErrorOr<RefinementPresetResponse>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<CreateRefinementPresetCommandHandler> _logger;

    public CreateRefinementPresetCommandHandler(
        IUserDataUnitOfWork unitOfWork,
        ILogger<CreateRefinementPresetCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ErrorOr<RefinementPresetResponse>> Handle(
        CreateRefinementPresetCommand request,
        CancellationToken cancellationToken)
    {
        var preset = new RefinementPreset
        {
            OrganizationId = request.OrganizationId,
            UserId = request.UserId,
            Name = request.Name,
            Instruction = request.Instruction,
            IsShared = request.IsShared,
        };

        await _unitOfWork.Repository<RefinementPreset>().AddAsync(preset);
        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation(
            "Created refinement preset '{Name}' for user {UserId} in org {OrganizationId}",
            LogSanitizer.ForLog(request.Name), request.UserId, request.OrganizationId);

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
