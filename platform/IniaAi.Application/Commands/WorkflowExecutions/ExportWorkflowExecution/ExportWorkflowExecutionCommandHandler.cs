using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution.Models;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common;
using IniaAi.Common.Text;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Export.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution;

/// <summary>
/// Handles the export of workflow executions to various formats.
/// </summary>
public class ExportWorkflowExecutionCommandHandler
    : IRequestHandler<ExportWorkflowExecutionCommand, ErrorOr<WorkflowExecutionExportResult>>
{
    private const string DEFAULT_REPORT_NAME = "Report";
    private const string FALLBACK_FILE_NAME_PREFIX = "workflow-execution";
    private const string FILE_NAME_PART_SEPARATOR = " - ";

    // DownloadFileName caps a whole name at 120 characters; these caps keep the date at the end.
    private const int MAX_REPORT_NAME_LENGTH = 60;
    private const int MAX_COUNTERPARTY_NAME_LENGTH = 40;

    private readonly ILogger<ExportWorkflowExecutionCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IExportService _exportService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportWorkflowExecutionCommandHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="userDataUnitOfWork">The user data unit of work.</param>
    /// <param name="coreUnitOfWork">The core unit of work, for the workflow type name.</param>
    /// <param name="exportService">The export service.</param>
    public ExportWorkflowExecutionCommandHandler(
        ILogger<ExportWorkflowExecutionCommandHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork,
        IExportService exportService)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
        _coreUnitOfWork = coreUnitOfWork;
        _exportService = exportService;
    }

    /// <summary>
    /// Handles the export workflow execution command.
    /// </summary>
    /// <param name="request">The command request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export result or an error.</returns>
    public async Task<ErrorOr<WorkflowExecutionExportResult>> Handle(
        ExportWorkflowExecutionCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Exporting workflow execution {WorkflowExecutionId} in {Format} format for user {UserId}",
            LogSanitizer.ForLog(request.WorkflowExecutionId),
            request.Format,
            request.UserId);

        WorkflowExecution? workflowExecution = await _userDataUnitOfWork.WorkflowExecutions
            .GetByIdAsync(request.WorkflowExecutionId);

        if (workflowExecution is null || workflowExecution.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning(
                "User {UserId} asked to export workflow execution {WorkflowExecutionId}, which is not in their organization",
                request.UserId,
                LogSanitizer.ForLog(request.WorkflowExecutionId));
            return Error.NotFound(description: "Workflow execution not found.");
        }

        try
        {
            WorkflowOutputExportResult exportResult = await _exportService.ExportByIdAsync(request.WorkflowExecutionId, request.OrganizationId, request.Format);

            if (exportResult.Status is not ExportStatus.Success || exportResult.Content is null)
            {
                _logger.LogError(
                    "Export failed for workflow execution {WorkflowExecutionId}: {ErrorMessage}",
                    LogSanitizer.ForLog(request.WorkflowExecutionId),
                    LogSanitizer.ForLog(exportResult.ErrorMessage));

                return exportResult.Status switch
                {
                    ExportStatus.NotFound => Error.NotFound(description: exportResult.ErrorMessage ?? "Workflow execution not found."),
                    _ => Error.Failure(description: exportResult.ErrorMessage ?? "Export failed unexpectedly."),
                };
            }

            // Create the application-specific result
            var result = new WorkflowExecutionExportResult
            {
                Content = exportResult.Content,
                FileName = await GenerateFileNameAsync(workflowExecution, exportResult.FileExtension, cancellationToken),
                ContentType = exportResult.ContentType,
                WorkflowExecutionId = request.WorkflowExecutionId,
            };

            _logger.LogInformation(
                "Successfully exported workflow execution {WorkflowExecutionId} in {Format} format",
                LogSanitizer.ForLog(request.WorkflowExecutionId),
                request.Format);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error exporting workflow execution {WorkflowExecutionId} in {Format} format",
                LogSanitizer.ForLog(request.WorkflowExecutionId),
                request.Format);

            return Error.Failure(description: "An error occurred while exporting the workflow execution.");
        }
    }

    /// <summary>
    /// Names the exported document after the report: its title, or its workflow type when the
    /// template set no title, then the counterparty and the export date, for example
    /// <c>Credit Review - Acme Holdings - 2026-09-28.docx</c>. A browser never overwrites an
    /// earlier download of the same name; it numbers the new one.
    /// </summary>
    /// <param name="workflowExecution">The exported workflow execution.</param>
    /// <param name="extension">The file name extension of the export format, with its dot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generated filename.</returns>
    private async Task<string> GenerateFileNameAsync(
        WorkflowExecution workflowExecution,
        string extension,
        CancellationToken cancellationToken)
    {
        string reportName = string.IsNullOrWhiteSpace(workflowExecution.Title)
            ? await GetWorkflowTypeNameAsync(workflowExecution.WorkflowTypeId, cancellationToken) ?? DEFAULT_REPORT_NAME
            : workflowExecution.Title;
        string? counterpartyName = await GetCounterpartyNameAsync(workflowExecution.CounterpartyId, cancellationToken);
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        string name = string.Join(
            FILE_NAME_PART_SEPARATOR,
            new[] { reportName.Truncate(MAX_REPORT_NAME_LENGTH), counterpartyName.Truncate(MAX_COUNTERPARTY_NAME_LENGTH), date }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        return DownloadFileName.Build(name, workflowExecution.Id, FALLBACK_FILE_NAME_PREFIX, extension);
    }

    private async Task<string?> GetWorkflowTypeNameAsync(string? workflowTypeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workflowTypeId))
        {
            return null;
        }

        IList<WorkflowTypeName> names = await _coreUnitOfWork.Repository<WorkflowType>()
            .GetAllAsync(new WorkflowTypeNamesByIdsSpec([workflowTypeId]), cancellationToken);
        return names.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name.Name))?.Name;
    }

    private async Task<string?> GetCounterpartyNameAsync(string? counterpartyId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(counterpartyId))
        {
            return null;
        }

        Counterparty? counterparty = await _userDataUnitOfWork.Repository<Counterparty>()
            .GetByIdAsync(counterpartyId, cancellationToken);
        return counterparty?.Name;
    }
}
