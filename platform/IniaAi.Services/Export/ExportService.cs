using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Content;
using IniaAi.Common.Interfaces;
using IniaAi.Common.Interfaces.Services.Reports;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Word;
using IniaAi.Services.Export.Models;
using Microsoft.Extensions.Logging;

namespace IniaAi.Services.Export;

public class ExportService : IExportService
{
    private readonly ILogger<ExportService> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IUserDisplayNameResolver _userDisplayNameResolver;
    private readonly IReadOnlyDictionary<ExportFormat, IReportFileWriter> _writers;
    private readonly IChartImageSessions _chartSessions;
    private readonly IReportNotice _notice;

    public ExportService(
        ILogger<ExportService> logger,
        IUserDataUnitOfWork userDataUnitOfWork,
        IUserDisplayNameResolver userDisplayNameResolver,
        IEnumerable<IReportFileWriter> writers,
        IChartImageSessions chartSessions,
        IReportNotice notice)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
        _userDisplayNameResolver = userDisplayNameResolver;
        _writers = ReportFileFormats.ByFormat(writers);
        _chartSessions = chartSessions;
        _notice = notice;
    }

    public async Task<BinaryContent> ExportAsync(WorkflowReport report, ExportFormat format)
    {
        return await ExportAsync(report, Writer(format), reportLayout: null, report.OrganizationId);
    }

    public async Task<WorkflowOutputExportResult> ExportByIdAsync(string workflowExecutionId, string organizationId, ExportFormat format)
    {
        if (!_writers.TryGetValue(format, out IReportFileWriter? writer))
        {
            return WorkflowOutputExportResult.Error(ReportFileFormats.Unavailable(format));
        }

        WorkflowReport? report = await _userDataUnitOfWork
            .Repository<WorkflowReport>()
            .GetFirstOrDefaultAsync(new WorkflowReportForExportSpec(workflowExecutionId));
        if (report is null)
        {
            return WorkflowOutputExportResult.NotFound($"WorkflowReport for execution {workflowExecutionId} not found");
        }

        ReportLayout? reportLayout = null;
        if (!string.IsNullOrWhiteSpace(report.WorkflowId))
        {
            Workflow? workflow = await _userDataUnitOfWork.Workflows.GetFirstOrDefaultAsync(new WorkflowConfigurationByIdSpec(report.WorkflowId));
            reportLayout = workflow?.Configuration?.ReportLayout;
        }

        try
        {
            BinaryContent content = await ExportAsync(report, writer, reportLayout, organizationId);
            return WorkflowOutputExportResult.FromContent(content, writer.Extension, writer.ContentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting workflow output {Id} to {Format}", LogSanitizer.ForLog(workflowExecutionId), format);
            return WorkflowOutputExportResult.Error($"Error exporting workflow output: {ex.Message}");
        }
    }

    private IReportFileWriter Writer(ExportFormat format)
    {
        return _writers.TryGetValue(format, out IReportFileWriter? writer)
            ? writer
            : throw new NotSupportedException(ReportFileFormats.Unavailable(format));
    }

    private async Task<BinaryContent> ExportAsync(WorkflowReport report, IReportFileWriter writer, ReportLayout? reportLayout, string? organizationId)
    {
        _logger.LogInformation("Converting WorkflowReport with ID: {Id} to {Format}", report.Id, writer.Format);

        string? footerNotice = string.IsNullOrWhiteSpace(organizationId)
            ? null
            : await _notice.FooterAsync(organizationId, CancellationToken.None);

        string? analystDisplayName = await ResolveAnalystDisplayNameAsync(report);
        DocumentElement document = WorkflowDocumentAdapter.ToDocumentElement(report, analystDisplayName);
        string reportContext = !string.IsNullOrWhiteSpace(report.Title) ? report.Title : report.Id;

        await using IChartImageSession charts = _chartSessions.Open();
        return await writer.WriteAsync(document, new ReportFileOptions(charts, reportLayout, reportContext, footerNotice));
    }

    private async Task<string?> ResolveAnalystDisplayNameAsync(WorkflowReport report)
    {
        if (string.IsNullOrWhiteSpace(report.UserId))
        {
            return null;
        }

        IReadOnlyDictionary<string, string> displayNames = await _userDisplayNameResolver.ResolveAsync([report.UserId]);
        return displayNames.TryGetValue(report.UserId, out string? displayName) ? displayName : null;
    }
}
