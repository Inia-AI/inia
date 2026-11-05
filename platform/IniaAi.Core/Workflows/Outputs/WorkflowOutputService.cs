using ErrorOr;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.Runs;
using IniaAi.Core.Services.Models;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Executions;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Constants;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Workflows.Outputs;

public class WorkflowOutputService : IWorkflowOutputService
{
    private const string ERROR = "ERROR";
    private const string EXECUTION_SECTION_NAME = "Execution";
    private const string RESULT_PERSISTENCE_BLOCK_NAME = "Result persistence";
    private readonly IWorkflowOutputBuilder _outputBuilder;
    private readonly IWorkflowOutputValidator _workflowOutputValidator;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<WorkflowOutputService> _logger;
    private readonly IAgentRunTerminalCleanup? _agentRunTerminalCleanup;
    private readonly IRunSettlement _runSettlement;
    private readonly HostBootIdentity? _hostBoot;

    public WorkflowOutputService(
        IWorkflowOutputBuilder outputBuilder,
        IWorkflowOutputValidator workflowOutputValidator,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<WorkflowOutputService> logger,
        IAgentRunTerminalCleanup? agentRunTerminalCleanup = null,
        IRunSettlement? runSettlement = null,
        HostBootIdentity? hostBoot = null)
    {
        _outputBuilder = outputBuilder;
        _workflowOutputValidator = workflowOutputValidator;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _agentRunTerminalCleanup = agentRunTerminalCleanup;
        _runSettlement = runSettlement ?? new NoRunSettlement();
        _hostBoot = hostBoot;
    }

    public async Task<WorkflowExecution> CreateExecutionFromWorkflowAsync(
        Workflow workflow,
        WorkflowOutputContext? context,
        IUserDataUnitOfWork userDataUnitOfWork,
        DateTime? reviewDate = null,
        WorkflowState? workflowState = null,
        string? jobId = null,
        Dictionary<string, object>? variables = null)
    {
        var workflowExecution = new WorkflowExecution
        {
            UserId = context?.CurrentUserId,
            OrganizationId = context?.CurrentOrganizationId,
            WorkflowTypeId = workflow.WorkflowTypeId,
            Title = workflow.Title,
            ReviewDate = reviewDate,
            AnalystName = workflow.User?.UserName,
            CounterpartyId = context?.CurrentCounterparty?.Id,
            JobId = jobId,
            Variables = variables is not null ? new Dictionary<string, object>(variables) : null,
            WorkflowState = workflowState ?? WorkflowState.Undefined,
            HostBootId = _hostBoot?.Id,
        };

        // Set WorkflowId to self-reference the WorkflowExecution.Id
        // This matches the Workflow record that will be saved to UserData.Workflows
        // (see WorkflowHandler.CloneAndSaveWorkflow where clone.Id = _rootOutput?.Id)
        workflowExecution.WorkflowId = workflowExecution.Id;

        if (context?.CurrentPeerCompanies is not null && context.CurrentPeerCompanies.Count != 0)
        {
            workflowExecution.WorkflowExecutionsCounterparties = [.. context.CurrentPeerCompanies
                .Select(peer => new WorkflowExecutionCounterparty
                {
                    CounterpartyId = peer.Id
                })];
        }

        if (context is not null)
        {
            context.CurrentWorkflowOutputRoot = workflowExecution;
            context.RootElementId = workflowExecution.Id;
        }

        var report = new WorkflowReport
        {
            Id = workflowExecution.Id,
            WorkflowExecutionId = workflowExecution.Id,
            WorkflowId = workflow.Id,
            WorkflowTypeId = workflow.WorkflowTypeId,
            CounterpartyId = context?.CurrentCounterparty?.Id,
            OrganizationId = context?.CurrentOrganizationId,
            UserId = context?.CurrentUserId,
            JobId = jobId,
            AnalystName = workflow.User?.UserName,
            ReviewDate = reviewDate,
            Title = workflow.Title,
            WorkflowState = workflowState ?? WorkflowState.Undefined,
        };

        if (context is not null)
        {
            context.CurrentWorkflowReport = report;
        }

        await userDataUnitOfWork.WorkflowExecutions.AddAsync(workflowExecution);
        await userDataUnitOfWork.Repository<WorkflowReport>().AddAsync(report);
        _ = await userDataUnitOfWork.CompleteAsync();

        return workflowExecution;
    }

    public async Task<ReportSection?> CreateReportSectionAsync(
        Section section,
        WorkflowOutputContext? context,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        ReportSection? reportSection = await _outputBuilder.BuildSectionAsync(section, context);
        if (reportSection is null)
        {
            return null;
        }

        await userDataUnitOfWork.Repository<ReportSection>().AddAsync(reportSection);

        if (context is not null)
        {
            context.CurrentReportSection = reportSection;
        }

        _ = await userDataUnitOfWork.CompleteAsync();

        return reportSection;
    }

    public async Task<(ReportParagraph? reportParagraph, string? reason, bool isValidForOutput)> BuildReportParagraphFromBlock(
        Block block,
        WorkflowOutputContext? context,
        BlockAnnotations? blockAnnotations,
        WorkflowAnnotationsContext? annotationsContext = null)
    {
        if (StopMarker.IsInInputsOf(block))
        {
            return (null, StopMarker.SKIP_REASON, false);
        }

        if (!_workflowOutputValidator.IsValidBlockForOutput(block))
        {
            return (null, null, false);
        }

        ReportParagraph? reportParagraph = await _outputBuilder.BuildParagraphAsync(
            block,
            context,
            blockAnnotations,
            annotationsContext);

        return (reportParagraph, null, true);
    }

    public ErrorOr<ReportDataOutput>? BuildDataOutputFromBlock(
        Block block,
        WorkflowOutputContext? context,
        BlockAnnotations? blockAnnotations,
        WorkflowAnnotationsContext? annotationsContext = null)
    {
        if (!block.IsDataOutput
            || StopMarker.IsInInputsOf(block)
            || block.ExecutionState is { HasFailed: true } or { IsSkipped: true })
        {
            return null;
        }

        return _outputBuilder.BuildDataOutput(block, context, blockAnnotations, annotationsContext);
    }

    public async Task<ReportParagraph> CreateReportParagraphAsync(
        ReportParagraph reportParagraph,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        await userDataUnitOfWork.Repository<ReportParagraph>().AddAsync(reportParagraph);
        _ = await userDataUnitOfWork.CompleteAsync();

        return reportParagraph;
    }

    public async Task CreateErrorOutputAsync(
        string workflowExecutionId,
        string errorMessage,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        var reportSection = new ReportSection
        {
            WorkflowReportId = workflowExecutionId,
            Order = 0,
            Title = ERROR,
        };

        var reportParagraph = new ReportParagraph
        {
            SectionId = reportSection.Id,
            Kind = ReportParagraphKind.Paragraph,
            Order = 0,
            Title = ERROR,
            Content = errorMessage,
        };

        await userDataUnitOfWork.Repository<ReportSection>().AddAsync(reportSection);
        await userDataUnitOfWork.Repository<ReportParagraph>().AddAsync(reportParagraph);

        _ = await userDataUnitOfWork.CompleteAsync();
    }

    public Task<bool> MarkExecutionAsFailedIfActiveAsync(
        string? workflowExecutionId,
        string? organizationId,
        string? failureKind = null,
        string? failureDetail = null)
    {
        return MarkExecutionAsTerminalIfActiveAsync(
            workflowExecutionId,
            organizationId,
            WorkflowState.Failed,
            failureKind,
            failureDetail);
    }

    public Task<bool> MarkExecutionAsCancelledIfActiveAsync(string? workflowExecutionId, string? organizationId)
    {
        return MarkExecutionAsTerminalIfActiveAsync(workflowExecutionId, organizationId, WorkflowState.Cancelled);
    }

    public async Task AppendDocumentIntakeErrorLogAsync(string? workflowExecutionId, string documentName, string error)
    {
        if (string.IsNullOrEmpty(workflowExecutionId))
        {
            _logger.LogWarning("Cannot record a document-intake error: execution id is missing");
            return;
        }

        try
        {
            // The job error path can handle the same exception twice (inner catch rethrows to the
            // outer one) — an identical row already on the execution means this is that second pass.
            if (!await TryAppendFailedLogRowAsync(
                    workflowExecutionId,
                    WorkflowHandler.DOCUMENT_INTAKE_SECTION_NAME,
                    documentName,
                    error,
                    dedupe: true))
            {
                return;
            }

            _logger.LogInformation(
                "Recorded a document-intake error on execution {ExecutionId}; the failed document is named on the row",
                workflowExecutionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record a document-intake error on execution {ExecutionId}",
                workflowExecutionId);
        }
    }

    public async Task AppendPageRefusalLogAsync(
        string? workflowExecutionId,
        string documentName,
        int pageNumber,
        string refusalCategory,
        bool wholePage)
    {
        if (string.IsNullOrEmpty(workflowExecutionId))
        {
            return;
        }

        try
        {
            _ = await TryAppendFailedLogRowAsync(
                workflowExecutionId,
                WorkflowHandler.DOCUMENT_INTAKE_SECTION_NAME,
                $"{documentName}, page {pageNumber}",
                DescribePageRefusal(refusalCategory, wholePage),
                dedupe: true,
                refusalCategory: refusalCategory);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record a page refusal on execution {ExecutionId}",
                workflowExecutionId);
        }
    }

    /// <summary>What the run shows for a page the model refused to read.</summary>
    /// <param name="wholePage">True when the page lost its text; false when the model refused
    /// one image on a page whose own text was kept (a Word document).</param>
    public static string DescribePageRefusal(string refusalCategory, bool wholePage)
    {
        string reason = refusalCategory is ModelRefusal.WRITTEN_CATEGORY
            ? "it wrote a refusal in place of the text"
            : $"refusal category: {refusalCategory}";

        return wholePage
            ? $"The model refused to read this page ({reason}). The page has no text; blocks get it as an image."
            : $"The model refused to read an image on this page ({reason}). The image's content is not in the page's text.";
    }

    public async Task AppendExecutionErrorLogAsync(string? workflowExecutionId, string error)
    {
        if (string.IsNullOrEmpty(workflowExecutionId))
        {
            _logger.LogWarning("Cannot record execution error log: execution id is missing");
            return;
        }

        try
        {
            _ = await TryAppendFailedLogRowAsync(
                workflowExecutionId,
                EXECUTION_SECTION_NAME,
                RESULT_PERSISTENCE_BLOCK_NAME,
                error,
                dedupe: false);

            _logger.LogInformation(
                "Recorded execution error log on execution {ExecutionId}",
                workflowExecutionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record execution error log on execution {ExecutionId}",
                workflowExecutionId);
        }
    }

    /// <summary>
    /// Writes one Failed <see cref="WorkflowExecutionLog"/> row through a fresh scope (the caller's
    /// context may be the thing that failed). Returns false when <paramref name="dedupe"/> found an
    /// identical row already on the execution; throws on write failure so the caller owns the
    /// best-effort catch and its logging.
    /// </summary>
    private async Task<bool> TryAppendFailedLogRowAsync(
        string workflowExecutionId,
        string sectionName,
        string blockName,
        string error,
        bool dedupe,
        string? refusalCategory = null)
    {
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        if (dedupe)
        {
            WorkflowExecutionLog? existing = await unitOfWork.Repository<WorkflowExecutionLog>()
                .SingleOrDefaultAsync(
                    log => log.WorkflowExecutionId == workflowExecutionId
                        && log.SectionName == sectionName
                        && log.BlockName == blockName
                        && log.Error == error,
                    asNoTracking: true);

            if (existing is not null)
            {
                return false;
            }
        }

        await unitOfWork.Repository<WorkflowExecutionLog>().AddAsync(new WorkflowExecutionLog
        {
            SectionName = sectionName,
            BlockName = blockName,
            Status = BlockExecutionStatus.Failed,
            Error = error,
            RefusalCategory = refusalCategory,
            Duration = TimeSpan.Zero,
            WorkflowExecutionId = workflowExecutionId,
        });
        _ = await unitOfWork.CompleteAsync();

        return true;
    }

    private async Task<bool> MarkExecutionAsTerminalIfActiveAsync(
        string? workflowExecutionId,
        string? organizationId,
        WorkflowState terminalState,
        string? failureKind = null,
        string? failureDetail = null)
    {
        if (string.IsNullOrEmpty(workflowExecutionId) || string.IsNullOrEmpty(organizationId))
        {
            _logger.LogWarning(
                "Cannot mark execution as {State}: execution id or organization id is missing",
                terminalState);
            return false;
        }

        bool updated;
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

            updated = await unitOfWork.WorkflowExecutions.TryMarkTerminalIfActiveAsync(
                workflowExecutionId,
                organizationId,
                terminalState,
                failureKind,
                failureDetail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to mark workflow execution {ExecutionId} as {State}",
                workflowExecutionId,
                terminalState);
            return false;
        }

        if (updated)
        {
            _logger.LogInformation(
                "Marked workflow execution {ExecutionId} as {State} via isolated scope",
                workflowExecutionId,
                terminalState);

            if (_agentRunTerminalCleanup is not null)
            {
                try
                {
                    await _agentRunTerminalCleanup.CleanUpAsync(
                        workflowExecutionId, organizationId, terminalState);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Agent-run cleanup broke its never-throw contract for execution {ExecutionId}",
                        workflowExecutionId);
                }
            }

            try
            {
                await _runSettlement.SettleAsync(workflowExecutionId, organizationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Run settlement broke its never-throw contract for execution {ExecutionId}",
                    workflowExecutionId);
            }
        }

        return updated;
    }

    public async Task<WorkflowExecution?> GetWorkflowExecution(
        string workflowExecutionId,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        WorkflowExecution? result = await userDataUnitOfWork.WorkflowExecutions
            .SingleOrDefaultAsync(predicate: x => x.Id == workflowExecutionId);
        return result;
    }
}
