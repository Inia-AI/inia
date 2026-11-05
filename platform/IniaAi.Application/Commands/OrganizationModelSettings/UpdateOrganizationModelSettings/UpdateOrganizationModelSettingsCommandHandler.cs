using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Application.Commands.OrganizationModelSettings.UpdateOrganizationModelSettings;

/// <summary>
/// Handles updates to an existing organization model settings entry.
/// </summary>
public class UpdateOrganizationModelSettingsCommandHandler : IRequestHandler<UpdateOrganizationModelSettingsCommand, ErrorOr<Updated>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IDbExceptionAnalyzer _exceptionAnalyzer;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateOrganizationModelSettingsCommandHandler"/> class.
    /// </summary>
    /// <param name="userDataUnitOfWork">The user data unit of work.</param>
    /// <param name="coreUnitOfWork">The core unit of work.</param>
    /// <param name="exceptionAnalyzer">The database exception analyzer.</param>
    public UpdateOrganizationModelSettingsCommandHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork,
        IDbExceptionAnalyzer exceptionAnalyzer)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _coreUnitOfWork = coreUnitOfWork;
        _exceptionAnalyzer = exceptionAnalyzer;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Updated>> Handle(UpdateOrganizationModelSettingsCommand request, CancellationToken cancellationToken)
    {
        Domain.Entities.TokenUsage.OrganizationModelSettings? entity = await _userDataUnitOfWork.Repository<Domain.Entities.TokenUsage.OrganizationModelSettings>()
            .SingleOrDefaultAsync(
                m => m.Id == request.Id,
                asNoTracking: false,
                cancellationToken: cancellationToken);

        if (entity is null)
        {
            return Error.NotFound("OrganizationModelSettings.NotFound", $"Organization model settings with ID '{request.Id}' was not found.");
        }

        // Verify the ModelPricing exists (from Core database)
        Domain.Entities.TokenUsage.ModelPricing? modelPricing = await _coreUnitOfWork.Repository<Domain.Entities.TokenUsage.ModelPricing>()
            .SingleOrDefaultAsync(
                m => m.Id == request.ModelPricingId,
                asNoTracking: true,
                cancellationToken: cancellationToken);

        if (modelPricing is null)
        {
            return Error.NotFound("ModelPricing.NotFound", $"Model pricing with ID '{request.ModelPricingId}' was not found.");
        }

        // Verify the Organization exists if OrganizationId is provided
        if (request.OrganizationId is not null)
        {
            Organization? organization = await _userDataUnitOfWork.Repository<Organization>()
                .SingleOrDefaultAsync(
                    o => o.Id == request.OrganizationId,
                    asNoTracking: true,
                    cancellationToken: cancellationToken);

            if (organization is null)
            {
                return Error.NotFound("Organization.NotFound", $"Organization with ID '{request.OrganizationId}' was not found.");
            }
        }

        entity.OrganizationId = request.OrganizationId;
        entity.UsageContext = request.UsageContext;
        entity.ModelPricingId = request.ModelPricingId;
        entity.IsEnabled = request.IsEnabled;
        entity.Priority = request.Priority;

        try
        {
            _ = await _userDataUnitOfWork.CompleteAsync(cancellationToken);
            return Result.Updated;
        }
        catch (Exception ex) when (_exceptionAnalyzer.IsUniqueConstraintViolation(ex))
        {
            return Error.Conflict("OrganizationModelSettings.Duplicate", $"A setting for organization '{request.OrganizationId ?? "Global"}' and usage context '{request.UsageContext}' already exists.");
        }
    }
}
