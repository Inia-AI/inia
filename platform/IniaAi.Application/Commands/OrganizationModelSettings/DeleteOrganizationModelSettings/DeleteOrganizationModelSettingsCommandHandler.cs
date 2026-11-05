using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Application.Commands.OrganizationModelSettings.DeleteOrganizationModelSettings;

/// <summary>
/// Handles deletion of an organization model settings entry.
/// </summary>
public class DeleteOrganizationModelSettingsCommandHandler : IRequestHandler<DeleteOrganizationModelSettingsCommand, ErrorOr<Deleted>>
{
    private readonly IUserDataUnitOfWork _userDataDbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteOrganizationModelSettingsCommandHandler"/> class.
    /// </summary>
    /// <param name="context">Unit of work.</param>
    public DeleteOrganizationModelSettingsCommandHandler(IUserDataUnitOfWork context)
    {
        _userDataDbContext = context;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<Deleted>> Handle(DeleteOrganizationModelSettingsCommand request, CancellationToken cancellationToken)
    {
        Domain.Entities.TokenUsage.OrganizationModelSettings? entity = await _userDataDbContext.Repository<Domain.Entities.TokenUsage.OrganizationModelSettings>()
            .SingleOrDefaultAsync(
                m => m.Id == request.Id,
                asNoTracking: false,
                cancellationToken: cancellationToken);

        if (entity is null)
        {
            return Error.NotFound("OrganizationModelSettings.NotFound", $"Organization model settings with ID '{request.Id}' was not found.");
        }

        _userDataDbContext.Repository<Domain.Entities.TokenUsage.OrganizationModelSettings>().Remove(entity);
        _ = await _userDataDbContext.CompleteAsync();

        return Result.Deleted;
    }
}
