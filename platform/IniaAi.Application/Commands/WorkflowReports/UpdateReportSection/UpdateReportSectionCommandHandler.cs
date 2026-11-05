using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowReports.UpdateReportSection;

/// <summary>
/// Handles <see cref="UpdateReportSectionCommand"/> by updating a report section's editable fields.
/// </summary>
public class UpdateReportSectionCommandHandler : IRequestHandler<UpdateReportSectionCommand, ErrorOr<Success>>
{
    private readonly ILogger<UpdateReportSectionCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public UpdateReportSectionCommandHandler(
        ILogger<UpdateReportSectionCommandHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<Success>> Handle(UpdateReportSectionCommand request, CancellationToken cancellationToken)
    {
        var spec = new ReportSectionByIdSpec(request.SectionId, request.Id, request.OrganizationId);
        ReportSection? section = await _userDataUnitOfWork.Repository<ReportSection>()
            .GetFirstOrDefaultAsync(spec, cancellationToken);
        if (section is null)
        {
            _logger.LogWarning("ReportSection {SectionId} not found for execution {ExecutionId}", LogSanitizer.ForLog(request.SectionId), LogSanitizer.ForLog(request.Id));
            return Error.NotFound(description: "Report section not found.");
        }

        if (request.Request.Title is not null)
        {
            section.Title = request.Request.Title;
        }

        _ = await _userDataUnitOfWork.CompleteAsync(cancellationToken);
        return Result.Success;
    }
}
