using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowReports.DeleteReportSection;

/// <summary>
/// Handles <see cref="DeleteReportSectionCommand"/> by removing a report section and its paragraphs.
/// </summary>
public class DeleteReportSectionCommandHandler : IRequestHandler<DeleteReportSectionCommand, ErrorOr<Success>>
{
    private readonly ILogger<DeleteReportSectionCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public DeleteReportSectionCommandHandler(
        ILogger<DeleteReportSectionCommandHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<Success>> Handle(DeleteReportSectionCommand request, CancellationToken cancellationToken)
    {
        var spec = new ReportSectionByIdSpec(request.SectionId, request.Id, request.OrganizationId);
        ReportSection? section = await _userDataUnitOfWork.Repository<ReportSection>()
            .GetFirstOrDefaultAsync(spec, cancellationToken);
        if (section is null)
        {
            _logger.LogWarning("ReportSection {SectionId} not found for execution {ExecutionId}", LogSanitizer.ForLog(request.SectionId), LogSanitizer.ForLog(request.Id));
            return Error.NotFound(description: "Report section not found.");
        }

        _userDataUnitOfWork.Repository<ReportParagraph>().RemoveRange(section.Paragraphs);
        _userDataUnitOfWork.Repository<ReportSection>().Remove(section);
        _ = await _userDataUnitOfWork.CompleteAsync(cancellationToken);
        return Result.Success;
    }
}
