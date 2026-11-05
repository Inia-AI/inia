using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.TenantModelSettings;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.OrganizationModelSettings.CreateOrganizationModelSettings;

/// <summary>
/// Handles creation of a new organization model settings entry.
/// </summary>
public class CreateOrganizationModelSettingsCommandHandler : IRequestHandler<CreateOrganizationModelSettingsCommand, ErrorOr<TenantModelSettingsDto>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IDbExceptionAnalyzer _exceptionAnalyzer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateOrganizationModelSettingsCommandHandler"/> class.
    /// </summary>
    /// <param name="userDataUnitOfWork">The user data unit of work.</param>
    /// <param name="coreUnitOfWork">The core unit of work.</param>
    /// <param name="exceptionAnalyzer">The database exception analyzer.</param>
    public CreateOrganizationModelSettingsCommandHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork,
        IDbExceptionAnalyzer exceptionAnalyzer)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _coreUnitOfWork = coreUnitOfWork;
        _exceptionAnalyzer = exceptionAnalyzer;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<TenantModelSettingsDto>> Handle(CreateOrganizationModelSettingsCommand request, CancellationToken cancellationToken)
    {
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

        try
        {
            var entity = new Domain.Entities.TokenUsage.OrganizationModelSettings
            {
                Id = Guid.NewGuid().ToString(),
                OrganizationId = request.OrganizationId,
                UsageContext = request.UsageContext,
                ModelPricingId = request.ModelPricingId,
                IsEnabled = request.IsEnabled,
                Priority = request.Priority,
            };

            await _userDataUnitOfWork.Repository<Domain.Entities.TokenUsage.OrganizationModelSettings>().AddAsync(entity);
            _ = await _userDataUnitOfWork.CompleteAsync(cancellationToken);

            // Reload with Organization navigation property
            Domain.Entities.TokenUsage.OrganizationModelSettings? reloaded = await _userDataUnitOfWork.Repository<Domain.Entities.TokenUsage.OrganizationModelSettings>()
                .GetAll()
                .Include(e => e.Organization)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == entity.Id, cancellationToken);

            // Build DTO manually since ModelPricing is in Core database
            var dto = new TenantModelSettingsDto
            {
                Id = reloaded!.Id,
                TenantId = reloaded.OrganizationId,
                TenantName = reloaded.Organization?.Name,
                UsageContext = (WebApi.Contracts.Enums.UsageContext)reloaded.UsageContext,
                ModelPricingId = reloaded.ModelPricingId,
                ModelDisplayName = modelPricing.DisplayName,
                IsEnabled = reloaded.IsEnabled,
                Priority = reloaded.Priority,
                CreatedAt = reloaded.CreatedAt,
            };

            return dto;
        }
        catch (Exception ex) when (_exceptionAnalyzer.IsUniqueConstraintViolation(ex))
        {
            return Error.Conflict("OrganizationModelSettings.Duplicate", $"A setting for organization '{request.OrganizationId ?? "Global"}' and usage context '{request.UsageContext}' already exists.");
        }
    }
}
