using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.Common.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration.Storage;
using IniaAi.Core.AugmentedSearch;
using IniaAi.Core.Documents;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Reruns;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Unlock;
using IniaAi.Services.Documents;
using IniaAi.Services.Models.Jobs;
using IniaAi.Services.Models.Jobs.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Document = IniaAi.Domain.Entities.Documents.Document;
using DocumentInfo = IniaAi.Domain.Entities.Documents.DocumentInfo;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Services.Jobs;

public class JobExecutor : IJobExecutor
{
    private const string ANONYMISATION_UNAVAILABLE =
        "This build does not anonymise documents, so it cannot run a confidential job.";

    #region Private fields

    private readonly ILogger _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly DocumentProcessingService _documentProcessing;
    private readonly IWorkflowOutputService _workflowOutputService;
    private readonly EmailIntakeJobStage _emailIntakeJobStage;
    private readonly IRunInputStage _runInputStage;
    private readonly IRunContentCheck _runContentCheck;
    private readonly IConfidentialDocumentAnonymiser? _anonymiser;

    private IWorkflowHandler _workflowHandler = default!;

    #endregion

    #region Constructors

    public JobExecutor(
        ILogger<JobExecutor> logger,
        IServiceScopeFactory serviceScopeFactory,
        DocumentProcessingService documentProcessing,
        IWorkflowOutputService workflowOutputService,
        EmailIntakeJobStage emailIntakeJobStage,
        IRunInputStage runInputStage,
        IRunContentCheck runContentCheck,
        IConfidentialDocumentAnonymiser? anonymiser = null)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _documentProcessing = documentProcessing;
        _workflowOutputService = workflowOutputService;
        _emailIntakeJobStage = emailIntakeJobStage;
        _runInputStage = runInputStage;
        _runContentCheck = runContentCheck;
        _anonymiser = anonymiser;
    }

    #endregion

    #region Public methods

    public void Execute(Job job, Action<Job> onFinish, CancellationToken cancellationToken)
    {
        if (job is DelegatedJob delegatedJob)
        {
            ExecuteDelegated(delegatedJob, onFinish, cancellationToken);
            return;
        }

        ArgumentNullException.ThrowIfNull(job.Workflow, nameof(job.Workflow));

        _logger.LogInformation("Starting job");

        // Preserve source entity Ids on this in-memory clone. It's not persisted — it's handed
        // to ExecuteWorkflowAsync which makes its own fresh-Id clone for the UserData snapshot.
        // SignatureFieldOverride resolution is keyed by StableBlockId, which every clone
        // preserves, so the id preservation here is no longer load-bearing — just harmless.
        var clone = new Workflow();
        job.Workflow.CloneToPreservingIds(clone);

        SetRootOrganizationId(clone, job.OrganizationId);

        job.Workflow = clone;

        _ = Task.Factory.StartNew(async () =>
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            IUserDataUnitOfWork userDataUnitOfWork = scope.ServiceProvider
                .GetRequiredService<IUserDataUnitOfWork>();

            try
            {
                await ExecuteAsync(job, userDataUnitOfWork, scope.ServiceProvider, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                job.Cancel();
                _logger.LogInformation("Job {JobId} was cancelled", job.Id);

                await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(ResolveExecutionId(job), job.OrganizationId);
            }
            catch (Exception ex)
            {
                await HandleJobError(job, ex);
            }
            finally
            {
                onFinish(job);
            }
        }, TaskCreationOptions.LongRunning);
    }

    #endregion

    #region Private methods

    /// <summary>
    /// Runs non-workflow work. The work owns its own records, so this only keeps the job's state
    /// honest and removes the finished job from the job list, where it would otherwise sit beside
    /// workflow runs.
    /// </summary>
    private void ExecuteDelegated(DelegatedJob job, Action<Job> onFinish, CancellationToken cancellationToken)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();

            try
            {
                job.SetState(JobState.Processing);
                await job.Work(scope.ServiceProvider, cancellationToken);
                if (!job.IsFinished)
                {
                    job.SetState(JobState.Done);
                }
            }
            catch (OperationCanceledException)
            {
                if (!job.IsFinished)
                {
                    job.Cancel();
                }

                _logger.LogInformation("Delegated job {JobId} was cancelled", job.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delegated job {JobId} failed: {Description}", job.Id, job.Description);
                if (!job.IsFinished)
                {
                    job.SetState(JobState.Error);
                }

                job.SetError(ex.Message);
            }
            finally
            {
                onFinish(job);
                job.Delete();
            }
        }, TaskCreationOptions.LongRunning);
    }

    private async Task ExecuteAsync(
        Job job,
        IUserDataUnitOfWork userDataUnitOfWork,
        IServiceProvider scopeProvider,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Job started");

        using IDisposable rerunWrites = RerunWrites.Scope(job.IsRerun);

        try
        {
            // Resolve from the per-job scope so the handler shares the scope's scoped LevelScheduler
            // / IBlockConcurrencyCap / UoW; the root provider would hand out a different scope.
            _workflowHandler = scopeProvider.GetKeyedService<IWorkflowHandler>(job.Workflow?.WorkflowTypeId ?? WorkflowType.GENERIC)
                ?? scopeProvider.GetRequiredKeyedService<IWorkflowHandler>(WorkflowType.GENERIC);

            await InitiateWorkflowOutputAsync(job, userDataUnitOfWork);

            await ProcessJobStatesAsync(
                job,
                userDataUnitOfWork,
                cancellationToken);
        }
        catch (WorkflowExecutionFencedException fenced)
        {
            // Ahead of the generic handler on purpose: HandleJobError writes a Failed job
            // state, and every write is exactly what a fenced run must not do.
            StopFencedJob(job, fenced);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await HandleJobError(job, ex);
        }
    }

    private async Task InitiateWorkflowOutputAsync(
        Job job,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        ArgumentNullException.ThrowIfNull(job.Workflow, nameof(job.Workflow));

        job.Workflow.Name = string.IsNullOrEmpty(job.Name)
            ? $"{job.Workflow.Name} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            : $"{job.Name} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

        _workflowHandler.CounterpartyId = job.CounterpartyId;
        _workflowHandler.PeerCompanyIds = job.PeerCompanyIds;

        await _workflowHandler.CreateRootOutputAsync(
            userDataUnitOfWork,
            job.Id,
            job.Workflow,
            job.OrganizationId,
            job.UserId,
            job.InputsObject,
            job.WorkflowExecutionId);
    }

    private async Task ProcessJobStatesAsync(
        Job job,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        while (!job.IsFinished)
        {
            switch (job.State)
            {
                case JobState.None:
                    HandleNoneState(job);
                    break;

                case JobState.Preparing:
                    await HandlePrepareStateAsync(
                        job,
                        userDataUnitOfWork,
                        cancellationToken);
                    break;

                case JobState.Processing:
                    await HandleProcessStateAsync(
                        job,
                        userDataUnitOfWork,
                        cancellationToken);
                    break;
            }
        }
    }

    private void HandleNoneState(Job job)
    {
        job.SetState(JobState.Preparing);

        _logger.LogInformation("Job state set to 'Prepare'");
    }

    private async Task HandlePrepareStateAsync(
        Job job,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        await _workflowHandler.UpdateWorkflowStateAsync(
            WorkflowState.ProcessingDocuments,
            userDataUnitOfWork);
        await PrepareDocumentsAsync(job, userDataUnitOfWork, cancellationToken);

        if (!job.IsFinished)
        {
            job.SetState(JobState.Processing);
        }
    }

    /// <summary>
    /// The execution reached a terminal state under another writer, so this job lost the row.
    /// Finishing the job is the whole reaction: writing anything would contradict the state the
    /// winner published.
    /// </summary>
    private void StopFencedJob(Job job, WorkflowExecutionFencedException fenced)
    {
        _logger.LogWarning(
            fenced,
            "Job {JobId} was fenced; stopping without further writes.",
            job.Id);

        if (!job.IsFinished)
        {
            job.Cancel();
        }
    }

    private async Task PrepareDocumentsAsync(
        Job job,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        if (job.IsConfidential)
        {
            if (_anonymiser is null)
            {
                throw new InvalidOperationException(ANONYMISATION_UNAVAILABLE);
            }

            job.SetState(JobState.Anonymizing);
            await _anonymiser.AnonymiseAsync(job, userDataUnitOfWork, cancellationToken);
        }

        _logger.LogInformation("Documents prepared for job");
    }

    private async Task HandleProcessStateAsync(
        Job job,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        await ProcessJobTypeAsync(job, userDataUnitOfWork, cancellationToken);

        if (!job.IsFinished)
        {
            await _workflowHandler.UpdateWorkflowStateAsync(
                WorkflowState.Draft,
                userDataUnitOfWork);

            // A cancel can land while the Draft write is in flight, and SetState(Done) on a
            // finished job throws — re-check before concluding.
            if (!job.IsFinished)
            {
                job.SetState(JobState.Done);

                _logger.LogInformation("Job state set to 'Done'");
            }
        }
    }

    private async Task ProcessJobTypeAsync(
        Job job,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DocumentInfo> documentInfos = await ProcessAndLoadDocumentsAsync(job, userDataUnitOfWork, cancellationToken);

        try
        {
            if (await _runContentCheck.CheckAsync(job, documentInfos, cancellationToken) is { } refusal)
            {
                throw new RunContentRefusedException(refusal);
            }

            await ExecuteWorkflowAndSetOutputAsync(job, userDataUnitOfWork, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            job.Cancel();
            _logger.LogInformation("Job {JobId} was cancelled during workflow execution", job.Id);

            await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(ResolveExecutionId(job), job.OrganizationId);

            return;
        }
        catch (WorkflowExecutionFencedException)
        {
            // Not HandleJobError's to swallow: the dedicated catch in ExecuteAsync stops the job
            // without the Error state a fenced run must not write.
            throw;
        }
        catch (Exception e)
        {
            await HandleJobError(job, e);
        }
    }

    private async Task ExecuteWorkflowAndSetOutputAsync(
        Job job,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job.Workflow, nameof(job.Workflow));

        _logger.LogInformation("JobExecutor: Starting workflow execution for JobId '{JobId}' with OrganizationId '{OrganizationId}'",
            job.Id, job.OrganizationId);

        await _workflowHandler.UpdateWorkflowStateAsync(
            WorkflowState.Running,
            userDataUnitOfWork);
        _ = await _workflowHandler.ExecuteWorkflowAsync(
            job.Workflow,
            job.AnonymisationMap,
            job.OrganizationId,
            job.Id,
            userDataUnitOfWork,
            job.UserId,
            inputs: job.Inputs,
            replayOutputs: job.ReplayOutputs,
            replayUncoveredBlockPolicy: job.ReplayUncoveredBlockPolicy,
            cancellationToken: cancellationToken);

        job.Output = GetJobOutput(job);
    }

    internal async Task HandleJobError(
        Job job,
        Exception e)
    {
        _logger.LogError(e, "Error processing job: {Message}", e.Message);

        // A job that already finished (e.g. cancelled mid-failure) keeps its state; SetState
        // would throw and take the execution's Failed mark down with it.
        if (!job.IsFinished)
        {
            job.SetState(JobState.Error);
        }

        job.SetError(e is CriticalBlockFailureException critical ? critical.FailureDetail : BlockFailureException.ReasonOf(e));

        string? executionId = ResolveExecutionId(job);

        var classifiedFailure = e as IWorkflowExecutionFailure;

        // Persist Failed through a fresh scope, never through this job's unit of work:
        // the error that brought us here may have poisoned its change tracker (e.g. an
        // entity PostgreSQL rejects), and flushing it again would fail the same way,
        // leaving the execution stuck "in progress" forever.
        await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(
            executionId,
            job.OrganizationId,
            classifiedFailure?.FailureKind,
            classifiedFailure?.FailureDetail);

        if (e is DocumentPasswordProtectedException passwordProtected)
        {
            await _workflowOutputService.AppendDocumentIntakeErrorLogAsync(
                executionId,
                passwordProtected.DocumentName,
                passwordProtected.Message);
        }
    }

    /// <summary>
    /// Names each page the model refused to read on the run, in the list where the reader looks
    /// for a failed block. A page refused when its document was first read, in an earlier run or
    /// at upload, is named too: the run reads it as an image with no text.
    /// </summary>
    internal async Task RecordRefusedPagesAsync(string? executionId, IReadOnlyList<DocumentInfo> documentInfos)
    {
        foreach (DocumentInfo documentInfo in documentInfos)
        {
            if (documentInfo.Document is not Document document)
            {
                continue;
            }

            foreach (Page page in document.Pages.OrderBy(static page => page.PageNumber))
            {
                if (page.RefusalCategory is string refusalCategory)
                {
                    await _workflowOutputService.AppendPageRefusalLogAsync(
                        executionId,
                        document.FileName,
                        page.PageNumber,
                        refusalCategory,
                        wholePage: page.Status is PageStatus.OCRNeedsReview);
                }
            }
        }
    }

    /// <summary>
    /// The job carries the execution id when the orchestrator pre-created the row;
    /// executions created mid-run by the workflow handler are only known to the handler.
    /// </summary>
    private string? ResolveExecutionId(Job job)
    {
        return job.WorkflowExecutionId ?? _workflowHandler?.WorkflowExecutionId;
    }

    private async Task<IReadOnlyList<DocumentInfo>> ProcessAndLoadDocumentsAsync(
        Job job,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job.Workflow, nameof(job.Workflow));

        await _emailIntakeJobStage.ProcessAsync(
            job,
            ResolveExecutionId(job),
            userDataUnitOfWork,
            cancellationToken);

        IReadOnlyList<DocumentInfo> documentInfos = await LoadRunInputAsync(job, userDataUnitOfWork, cancellationToken);

        // Update workflow state to ProcessingDocuments before starting document processing
        await _workflowHandler.UpdateWorkflowStateAsync(
            WorkflowState.ProcessingDocuments,
            userDataUnitOfWork);

        await _documentProcessing.ProcessDocumentsAsync(
            [.. documentInfos],
            userDataUnitOfWork,
            job.Workflow,
            workflowTypeId: job.Workflow.WorkflowTypeId,
            workflowExecutionId: job.WorkflowExecutionId,
            userId: job.UserId,
            cancellationToken: cancellationToken);

        await _documentProcessing.LoadContextToWorkflowAsync(
            [.. documentInfos],
            job.Workflow,
            _workflowHandler.AllPages,
            onDocumentProcessed: async documentId =>
            {
                _logger.LogInformation("Saving document {DocumentId} after loading to workflow", documentId);
                _ = await userDataUnitOfWork.CompleteAsync();
            },
            cancellationToken: cancellationToken);

        await RecordRefusedPagesAsync(ResolveExecutionId(job), documentInfos);

        _ = await userDataUnitOfWork.CompleteAsync(cancellationToken);
        return documentInfos;
    }

    /// <summary>
    /// The documents the run processes: every document linked to the job or to its workflow type,
    /// less what the run-input stage took out of the run.
    /// A run that was sent a document deleted since stops here, before anything is read.
    /// </summary>
    internal async Task<IReadOnlyList<DocumentInfo>> LoadRunInputAsync(
        Job job,
        IUserDataUnitOfWork userDataUnitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job.Workflow, nameof(job.Workflow));

        List<string> deleted = await userDataUnitOfWork.Documents.GetDeletedRunInputFileNamesAsync(job.Id, cancellationToken);
        if (deleted.Count > 0)
        {
            throw new RunInputDeletedException(deleted);
        }

        IReadOnlyList<DocumentInfo> documentInfos = [.. await userDataUnitOfWork
            .Documents
            .GetDocumentInfosByJobIdOrWorkTypeIdAsync(
            jobId: job.Id,
            organizationId: job.OrganizationId,
            workflowTypeId: job.Workflow.WorkflowTypeId)];

        await _runInputStage.RunAsync(job, ResolveExecutionId(job), documentInfos, userDataUnitOfWork, cancellationToken);

        return [.. documentInfos.Where(documentInfo => !documentInfo.IsExcludedFromRun)];
    }

    private static string GetJobOutput(Job job)
    {
        Block? block = job.Workflow?
            .Sections
            .Where(s => !s.IsSeparator)
            .OrderBy(s => s.Order)
            .LastOrDefault()?
            .Blocks
            .OrderBy(b => b.Order)
            .LastOrDefault();
        string subtitle = string.IsNullOrWhiteSpace(block?.ExecutionState?.OutputTitle)
            ? ""
            : block.ExecutionState.OutputTitle;
        return (subtitle + block?.BlockResponse?.RawResponse) ?? "";
    }

    private static void SetRootOrganizationId(Workflow workflow, string? organizationId)
    {
        if (string.IsNullOrEmpty(organizationId))
        {
            return;
        }

        workflow.OrganizationId = organizationId;
    }

    #endregion
}
