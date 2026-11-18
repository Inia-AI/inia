using IniaAi.Core.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.WorkflowExecutions;
using IniaAi.Application.Workflows.Interfaces;
using IniaAi.Application.Workflows.Services;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Common.Constants;
using IniaAi.Common.Interfaces.Services.Runs;
using IniaAi.Common.Text;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Core.Services.DocumentClassValidation;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Activity;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.InputRules;
using IniaAi.Domain.Replay;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.Models.Jobs;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// Orchestrates the shared logic for starting a workflow execution.
/// Used by both <c>StartWorkflowExecutionCommandHandler</c> and <c>SubmitWorkflowExecutionCommandHandler</c>.
/// </summary>
public class WorkflowExecutionOrchestrator : IWorkflowExecutionOrchestrator
{
    public const string AGENT_RUNTIME_DISABLED_CODE = "WorkflowExecution.AgentRuntimeDisabled";
    public const string AGENT_MODEL_NOT_ALLOWED_CODE = "WorkflowExecution.AgentModelNotAllowed";
    public const string UNRESOLVABLE_INPUT_DOCUMENTS_CODE = "WorkflowExecution.UnresolvableInputDocuments";
    public const string UNRESOLVABLE_COUNTERPARTIES_CODE = "WorkflowExecution.UnresolvableCounterparties";
    public const string ANONYMISATION_UNAVAILABLE_CODE = "WorkflowExecution.AnonymisationUnavailable";
    public const string ANONYMISATION_UNAVAILABLE_MESSAGE =
        $"The platform cannot anonymise documents, so it cannot start a confidential run. Start the run with isConfidential set to false. {ProductEditions.FULL_EDITION} anonymises documents before any model reads them.";

    private readonly ILogger<WorkflowExecutionOrchestrator> _logger;
    private readonly IJobsService _jobsService;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowTypeAccess _access;
    private readonly WorkflowCanonicalizer _canonicalizer;
    private readonly IWorkflowVariableValidationService _variableValidationService;
    private readonly IDocumentClassValidationService _documentClassValidationService;
    private readonly IWorkflowOutputService _workflowOutputService;
    private readonly IExecutionFreezeSourceResolver _freezeSourceResolver;
    private readonly ICaseFreezeSources _caseFreezeSources;
    private readonly IWorkflowActivityScriptEnsurer? _activityScriptEnsurer;
    private readonly IRunAdmission _runAdmission;
    private readonly RunIntake _runIntake;
    private readonly AgentRuntimeSettings _agentRuntime;
    private readonly ModelRoutingSettings? _modelRouting;
    private readonly bool _anonymises;

    public WorkflowExecutionOrchestrator(
        ILogger<WorkflowExecutionOrchestrator> logger,
        IJobsService jobsService,
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork,
        WorkflowCanonicalizer canonicalizer,
        IWorkflowVariableValidationService variableValidationService,
        IDocumentClassValidationService documentClassValidationService,
        IWorkflowOutputService workflowOutputService,
        IExecutionFreezeSourceResolver freezeSourceResolver,
        ICaseFreezeSources caseFreezeSources,
        IOptions<AgentRuntimeSettings>? agentRuntimeSettings = null,
        IWorkflowActivityScriptEnsurer? activityScriptEnsurer = null,
        IOptions<PlatformSettings>? platformSettings = null,
        IServiceProviderIsService? registrations = null,
        IRunAdmission? runAdmission = null,
        RunIntake? runIntake = null,
        WorkflowTypeAccess? access = null)
    {
        _logger = logger;
        _jobsService = jobsService;
        _userDataUnitOfWork = userDataUnitOfWork;
        _coreUnitOfWork = coreUnitOfWork;
        _canonicalizer = canonicalizer;
        _variableValidationService = variableValidationService;
        _documentClassValidationService = documentClassValidationService;
        _workflowOutputService = workflowOutputService;
        _freezeSourceResolver = freezeSourceResolver;
        _caseFreezeSources = caseFreezeSources;
        _activityScriptEnsurer = activityScriptEnsurer;
        _agentRuntime = agentRuntimeSettings?.Value ?? new AgentRuntimeSettings();
        _modelRouting = platformSettings?.Value.ModelRouting;
        _anonymises = registrations?.IsService(typeof(IConfidentialDocumentAnonymiser)) == true;
        _runAdmission = runAdmission ?? new AdmitEveryRun();
        _runIntake = runIntake ?? new RunIntake();
        _access = access ?? new WorkflowTypeAccess(coreUnitOfWork, new AccessRowsDecideReach());
    }

    /// <inheritdoc />
    public async Task<ErrorOr<WorkflowExecutionDto>> ExecuteWorkflowAsync(
        Workflow workflow,
        List<PostDocumentInfoRequest>? files,
        Dictionary<string, JsonElement>? variables,
        string organizationId,
        string userId,
        string? username,
        string? email,
        string? counterpartyId,
        List<string>? peerCompanyIds,
        bool isConfidential = false,
        string? idempotencyKey = null,
        CancellationToken ct = default,
        FreezeSource? freezeSource = null,
        string? triggerEmailIdentifier = null,
        IReadOnlyList<SkippedProtectedFile>? intakeSkippedFiles = null,
        string? emailIntakeMode = null,
        string? emailAttachmentsClass = null,
        bool isRerun = false)
    {
        ErrorOr<Workflow> runnable = await RunnableWorkflowGuard.RunnableAsync(workflow, organizationId, _access, _logger, ct);
        if (runnable.IsError)
        {
            return runnable.Errors;
        }

        // Check idempotency key for duplicate submissions
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            WorkflowExecution? existing = await _userDataUnitOfWork
                .Repository<WorkflowExecution>()
                .SingleOrDefaultAsync(
                    e => e.IdempotencyKey == idempotencyKey && e.OrganizationId == organizationId,
                    asNoTracking: true);

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Duplicate submission detected for idempotency key {IdempotencyKey}, returning existing execution {ExecutionId}",
                    LogSanitizer.ForLog(idempotencyKey),
                    existing.Id);
                return ReadableDtoOf(existing, workflow);
            }
        }

        if (_runIntake.IsPaused)
        {
            _logger.LogInformation("Refused a run of {WorkflowTypeId} for {OrganizationId}: run intake is paused for a deploy", workflow.WorkflowTypeId, organizationId);
            return RunIntake.Refusal();
        }

        // A confidential run that nothing anonymises would read the original documents.
        if (isConfidential && !_anonymises)
        {
            return Error.Validation(ANONYMISATION_UNAVAILABLE_CODE, ANONYMISATION_UNAVAILABLE_MESSAGE);
        }

        // Refuse before any execution row exists: the Agent block is critical, so letting the
        // submission through only to fail at the block would void every upstream block's spend.
        if (AgentBlockGuard.DescribeSubmitRefusal(
                workflow, _agentRuntime, AgentModelTiers.For(_agentRuntime, _modelRouting, organizationId))
            is { } agentRefusal)
        {
            _logger.LogWarning(
                "Refused submission of workflow {WorkflowTypeId} before any spend: {RefusalKind}",
                workflow.WorkflowTypeId,
                agentRefusal.Kind);
            string code = agentRefusal.Kind == AgentBlockGuard.AgentViolationKind.RuntimeDisabled
                ? AGENT_RUNTIME_DISABLED_CODE
                : AGENT_MODEL_NOT_ALLOWED_CODE;
            return Error.Validation(code, agentRefusal.Message);
        }

        // Validate workflow variables
        List<Error> validationErrors = _variableValidationService.ValidateVariables(
            workflow,
            variables);

        if (validationErrors.Count != 0)
        {
            return validationErrors;
        }

        List<string> unresolvableCounterpartyIds = await FindCounterpartiesOutsideTheOrganizationAsync(
            organizationId, counterpartyId, peerCompanyIds, ct);

        if (unresolvableCounterpartyIds.Count > 0)
        {
            _logger.LogWarning(
                "Refused submission of workflow {WorkflowTypeId}: it names counterparties that do not exist in organization {OrganizationId}: {UnresolvableIds}",
                workflow.WorkflowTypeId,
                organizationId,
                LogSanitizer.ForLog(string.Join(", ", unresolvableCounterpartyIds)));

            return Error.Validation(
                code: UNRESOLVABLE_COUNTERPARTIES_CODE,
                description: $"The submission names {CounterpartyTerms.PLURAL_LOWER} that do not exist in this organization: "
                    + string.Join(", ", unresolvableCounterpartyIds.Select(id => $"'{id}'"))
                    + $". Choose the {CounterpartyTerms.SINGULAR_LOWER} and the peer {CounterpartyTerms.PLURAL_LOWER} from this organization's list, then resubmit.");
        }

        List<string> blocksMissingDocumentClass = FindOutputBlocksMissingDocumentClass(workflow);

        if (blocksMissingDocumentClass.Count != 0)
        {
            return Error.Validation(
                description: "These output-document blocks need a document class selected: "
                    + string.Join(", ", blocksMissingDocumentClass));
        }

        // Validate document classes
        List<Document> documentsWithClasses = [];

        if (files is not null && files.Count > 0)
        {
            var fileIds = files.Select(f => f.FileId).ToList();
            IEnumerable<(string FileId, string DocumentClass, bool ShouldUseOcr, bool ShouldBeDisplayedInOutput)> fileInfos =
                files.Select(f => (f.FileId, f.DocumentClass, f.ShouldUseOcr, f.ShouldBeDisplayedInOutput));

            List<Document> documents = await _userDataUnitOfWork.Documents
                .GetDocumentsWithInfoAsync(fileIds, organizationId, fileInfos);

            // Reject before any spend: running with fewer inputs than the request declared
            // produces a report that reads as if the input never existed.
            List<string> unresolvableIds = [.. fileIds.Except(documents.Select(doc => doc.Id))];

            if (unresolvableIds.Count > 0)
            {
                _logger.LogWarning(
                    "Refused submission of workflow {WorkflowTypeId}: it names input documents that do not exist in organization {OrganizationId}: {UnresolvableIds}",
                    workflow.WorkflowTypeId,
                    organizationId,
                    LogSanitizer.ForLog(string.Join(", ", unresolvableIds)));

                return Error.Validation(
                    code: UNRESOLVABLE_INPUT_DOCUMENTS_CODE,
                    description: "The submission names input documents that do not exist in this organization: "
                        + string.Join(", ", unresolvableIds.Select(fileId => $"'{fileId}'"))
                        + ". Remove these documents from the submission, or upload them again, then resubmit.");
            }

            documentsWithClasses = [.. documents.Select(doc =>
            {
                doc.Class = doc.CurrentDocumentInfo?.DocumentClass ?? string.Empty;
                return doc;
            })];
        }

        DocumentClassValidationResult documentValidation = _documentClassValidationService
            .ValidateDocumentClasses(workflow, documentsWithClasses);

        bool logsMissingSuggested = documentValidation.HasWarnings;
        List<string> documentClassErrors = [.. SubmissionDocumentClassRules
            .Refusing(workflow, documentValidation)
            .Select(status => status.ValidationMessage!)];

        if (documentClassErrors.Count > 0)
        {
            string errorMessage = string.Join("; ", documentClassErrors);

            _logger.LogWarning(
                "Document class validation failed for workflow {WorkflowId}: {ErrorMessage}",
                workflow.Id,
                errorMessage);

            return Error.Validation(
                code: "DocumentClasses",
                description: errorMessage);
        }

        // Log warnings for missing suggested documents
        if (logsMissingSuggested)
        {
            var missingSuggested = documentValidation.DocumentClassStatuses
                .Where(s => s.Requirement == "Suggested" && s.UploadedCount == 0)
                .Select(s => s.DisplayName)
                .ToList();

            if (missingSuggested.Count > 0)
            {
                _logger.LogInformation(
                    "Suggested documents missing for workflow {WorkflowId}: {MissingSuggested}",
                    workflow.Id,
                    string.Join(", ", missingSuggested));
            }
        }

        InputRuleOutcome inputRules = VariableInputRuleCheck.Check(
            workflow,
            variables,
            documentsWithClasses.Select(document => (string?)document.Class));

        if (inputRules.Violations.Count > 0)
        {
            _logger.LogInformation(
                "Refused submission of workflow {WorkflowTypeId}: {Count} variable input rules not met",
                workflow.WorkflowTypeId,
                inputRules.Violations.Count);
            return VariableInputRuleErrors.Of(inputRules);
        }

        if (!string.IsNullOrEmpty(emailAttachmentsClass)
            && workflow.DocumentClasses.Any(dc => dc.IsOutputDocument
                && string.Equals(dc.Key, emailAttachmentsClass, StringComparison.Ordinal)))
        {
            return Error.Validation(
                code: "DocumentClasses",
                description: $"Output document class '{emailAttachmentsClass}' should not be assigned to email attachments");
        }

        ErrorOr<ResolvedFreeze> freeze = await ResolveFreezeAsync(
            freezeSource,
            organizationId,
            workflow,
            counterpartyId,
            variables,
            ct);

        if (freeze.IsError)
        {
            return freeze.Errors;
        }

        // The caller's pending changes are saved before the transaction, as the submission's first
        // save always did, so a refused or retried attempt detaches only what the attempt added.
        _ = await _userDataUnitOfWork.CompleteAsync(ct);

        ErrorOr<(Job Job, WorkflowExecution Execution)> admitted = await _userDataUnitOfWork.InTransactionAsync<ErrorOr<(Job, WorkflowExecution)>>(
            async token =>
            {
                Job job = await CreateWorkflowJobAsync(
                    organizationId,
                    userId,
                    username,
                    email,
                    files,
                    variables,
                    counterpartyId,
                    peerCompanyIds,
                    isConfidential,
                    workflow,
                    triggerEmailIdentifier,
                    intakeSkippedFiles,
                    emailIntakeMode,
                    emailAttachmentsClass);

                job.ReplayOutputs = freeze.Value.Outputs;
                job.ReplayUncoveredBlockPolicy = freeze.Value.UncoveredPolicy;
                job.IsRerun = isRerun || freezeSource is not null;

                WorkflowExecution workflowExecution = await CreateRootOutputAsync(
                    job.Id,
                    job.Workflow
                        ?? throw new InvalidOperationException("Job workflow cannot be null"),
                    job.OrganizationId,
                    job.UserId,
                    job.CounterpartyId,
                    job.PeerCompanyIds,
                    job.InputsObject);

                // Stamp the template version/hash this execution ran against (cross-context pointer).
                await StampTemplateVersionAsync(workflowExecution, workflow, token);

                workflowExecution.FreezeSourceExecutionId = freeze.Value.ExecutionId;
                workflowExecution.FreezeSourceCaseId = freeze.Value.CaseId;

                // The unique index on (IdempotencyKey, OrganizationId) rolls back the second of two
                // duplicates that race past the check above.
                if (!string.IsNullOrEmpty(idempotencyKey))
                {
                    workflowExecution.IdempotencyKey = idempotencyKey;
                }

                job.WorkflowExecutionId = workflowExecution.Id;

                foreach (WorkflowExecutionLog skippedRule in VariableInputRuleErrors.LogRowsOf(inputRules, workflowExecution.Id))
                {
                    await _userDataUnitOfWork.Repository<WorkflowExecutionLog>().AddAsync(skippedRule);
                }

                _ = await _userDataUnitOfWork.CompleteAsync(token);

                ErrorOr<Success> admission = await _runAdmission.AdmitAsync(
                    new RunAdmissionRequest(
                        workflowExecution.Id,
                        organizationId,
                        userId,
                        workflow,
                        [.. files?.Select(file => file.FileId) ?? []],
                        emailIntakeMode),
                    token);

                return admission.IsError ? admission.Errors : (job, workflowExecution);
            },
            result => !result.IsError,
            ct);

        if (admitted.IsError)
        {
            _logger.LogInformation(
                "Refused submission of workflow {WorkflowTypeId} at admission: {Codes}",
                workflow.WorkflowTypeId,
                string.Join(", ", admitted.Errors.Select(error => error.Code)));
            return admitted.Errors;
        }

        // Only a committed run is queued or warms its activity script: inside a retried
        // transaction either could happen twice, or for a run that was rolled back.
        (Job queued, WorkflowExecution execution) = admitted.Value;
        if (execution.TemplateVersionId is string templateVersionId
            && workflow.WorkflowTypeId is string stampedWorkflowTypeId)
        {
            _activityScriptEnsurer?.EnsureScriptInBackground(templateVersionId, stampedWorkflowTypeId);
        }

        _jobsService.Execute(queued, shouldUseQueue: true);

        return ReadableDtoOf(execution, workflow);
    }

    private static WorkflowExecutionDto ReadableDtoOf(WorkflowExecution execution, Workflow workflow)
    {
        WorkflowExecutionDto dto = WorkflowExecutionProjections.DtoOf(execution);
        return dto with { Variables = UnreadableVariables.Without(dto.Variables, workflow.Variables) };
    }

    /// <summary>
    /// The ids of the run's counterparty and peer companies that name no counterparty of the
    /// organization. Another organization's counterparty counts as one that does not exist.
    /// </summary>
    private async Task<List<string>> FindCounterpartiesOutsideTheOrganizationAsync(
        string organizationId,
        string? counterpartyId,
        List<string>? peerCompanyIds,
        CancellationToken cancellationToken)
    {
        List<string> named = [.. (peerCompanyIds ?? []).Prepend(counterpartyId).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).Distinct(StringComparer.Ordinal)];
        if (named.Count == 0)
        {
            return [];
        }

        List<string> owned = await _userDataUnitOfWork.Repository<Counterparty>()
            .GetAll()
            .AsNoTracking()
            .Where(counterparty => counterparty.OrganizationId == organizationId && named.Contains(counterparty.Id))
            .Select(counterparty => counterparty.Id)
            .ToListAsync(cancellationToken);

        return [.. named.Except(owned, StringComparer.Ordinal)];
    }

    private async Task<WorkflowExecution> CreateRootOutputAsync(
       string? jobId,
       Workflow rootWorkflow,
       string organizationId,
       string? userId,
       string? counterpartyId,
       List<string>? peerCompanyIds = null,
       Dictionary<string, object>? variables = null)
    {
        DateTime? reviewDateOutput = DateTime.UtcNow;

        List<Counterparty>? currentPeerCompanies = null;

        if (peerCompanyIds is not null && peerCompanyIds.Count != 0)
        {
            currentPeerCompanies = [];
            peerCompanyIds.ForEach(x =>
            {
                currentPeerCompanies.Add(new Counterparty { Id = x });
            });
        }

        WorkflowExecution root = await _workflowOutputService
            .CreateExecutionFromWorkflowAsync(
                rootWorkflow,
                new WorkflowOutputContext()
                {
                    CurrentUserId = userId,
                    CurrentCounterparty = counterpartyId is not null
                        ? new Counterparty { Id = counterpartyId }
                        : null,
                    CurrentOrganizationId = organizationId,
                    CurrentPeerCompanies = currentPeerCompanies,
                },
                _userDataUnitOfWork,
                reviewDate: reviewDateOutput,
                workflowState: WorkflowState.Queued,
                jobId: jobId,
                variables: variables);

        return root;
    }

    /// <summary>
    /// Turns the submission's one freeze source into a <see cref="ResolvedFreeze"/>. The execution
    /// option resolves through <see cref="IExecutionFreezeSourceResolver"/> and runs uncovered
    /// blocks live; the dormant regression path hands over its captures already collected and
    /// keeps the old skip-as-inconclusive semantics; a case resolves through
    /// <see cref="ICaseFreezeSources"/>.
    /// </summary>
    private async Task<ErrorOr<ResolvedFreeze>> ResolveFreezeAsync(
        FreezeSource? freezeSource,
        string organizationId,
        Workflow workflow,
        string? counterpartyId,
        Dictionary<string, JsonElement>? variables,
        CancellationToken ct)
    {
        switch (freezeSource)
        {
            case null:
                return new ResolvedFreeze(null, null, null, ReplayUncoveredBlockPolicy.SkipAsInconclusive);

            case FreezeSource.ReplaySet replaySet:
                return new ResolvedFreeze(replaySet.Outputs, null, null, ReplayUncoveredBlockPolicy.SkipAsInconclusive);

            case FreezeSource.Execution fromExecution:
                ErrorOr<IReadOnlyList<ReplayOutput>> fromExecutionResult = await _freezeSourceResolver.ResolveAsync(
                    organizationId,
                    fromExecution.ExecutionId,
                    workflow,
                    counterpartyId,
                    variables,
                    ct);

                return fromExecutionResult.IsError
                    ? fromExecutionResult.Errors
                    : new ResolvedFreeze(
                        fromExecutionResult.Value,
                        fromExecution.ExecutionId,
                        null,
                        ReplayUncoveredBlockPolicy.RunLive);

            case FreezeSource.Case fromCase:
                return await _caseFreezeSources.ResolveAsync(fromCase, organizationId, workflow, counterpartyId, variables, ct);

            default:
                throw new InvalidOperationException($"Unhandled freeze source {freezeSource.GetType().Name}.");
        }
    }

    /// <summary>
    /// Stamps the execution with the template version + content hash it ran against. Reads the live
    /// head version for the workflow type from Core; when none exists, computes the hash from the live
    /// template so baseline staleness can still be derived. Best-effort: a lookup failure must not
    /// fail the run.
    /// </summary>
    private async Task StampTemplateVersionAsync(
        WorkflowExecution execution,
        Workflow workflow,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workflow.WorkflowTypeId))
        {
            return;
        }

        try
        {
            WorkflowVersion? headVersion = await _coreUnitOfWork.Repository<WorkflowVersion>()
                .GetFirstOrDefaultAsync(
                    new LatestWorkflowVersionByTypeSpec(workflow.WorkflowTypeId),
                    ct);

            if (headVersion is not null)
            {
                execution.TemplateVersionId = headVersion.Id;
                execution.TemplateContentHash = headVersion.ContentHash;
                return;
            }

            execution.TemplateContentHash = _canonicalizer.ComputeContentHash(workflow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to stamp template version for execution {ExecutionId} (type {WorkflowTypeId})",
                execution.Id,
                workflow.WorkflowTypeId);
        }
    }

    private async Task<Job> CreateWorkflowJobAsync(
        string organizationId,
        string userId,
        string? username,
        string? email,
        List<PostDocumentInfoRequest>? files,
        Dictionary<string, JsonElement>? variables,
        string? counterpartyId,
        List<string>? peerCompanyIds,
        bool isConfidential,
        Workflow baseWorkflow,
        string? triggerEmailIdentifier,
        IReadOnlyList<SkippedProtectedFile>? intakeSkippedFiles,
        string? emailIntakeMode,
        string? emailAttachmentsClass)
    {
        Job job = _jobsService.CreateUserJob(
            organizationId,
            userId,
            baseWorkflow.WorkflowTypeId);

        List<string> documentInfoIds = await PersistDocumentMetadataAsync(job.Id, files);
        _ = await _userDataUnitOfWork.CompleteAsync();

        job.CounterpartyId = counterpartyId;
        job.PeerCompanyIds = peerCompanyIds;
        job.IsConfidential = isConfidential;

        // Preserve source entity Ids on this in-memory clone. It's only the initial
        // handoff to the background job runner; the handler's BuildExecutedWorkflowAsync
        // will still take its own fresh-Id copy for the UserData snapshot.
        // SignatureFieldOverride lookups are keyed by StableBlockId, which every clone
        // preserves, so the id preservation here is no longer load-bearing — just harmless.
        Workflow workflowClone = new();
        baseWorkflow.CloneToPreservingIds(workflowClone);
        workflowClone.User = new User
        {
            Id = userId,
            UserName = username,
        };
        workflowClone.UserId = userId;

        SetRootOrganizationId(workflowClone, organizationId);

        job.Workflow = workflowClone;
        job.DocumentInfos.AddRange(documentInfoIds);

        string? counterpartyName = await GetCounterpartyNameFromIdAsync(counterpartyId);
        job.Name = Services.Models.BackgroundJobs.BackgroundJob.GetJobName(
            workflowClone,
            [
                counterpartyName,
                username ?? email,
            ]);

        // Add workflow variables to job inputs
        Dictionary<string, string> variableInputs = _variableValidationService
            .ConvertVariablesToStrings(variables);

        foreach ((string key, string value) in variableInputs)
        {
            _ = job.Inputs.TryAdd(key, value);
        }

        Dictionary<string, object> variableInputsObject = _variableValidationService
            .ConvertVariablesToObjects(variables);

        foreach ((string key, object value) in variableInputsObject)
        {
            _ = job.InputsObject.TryAdd(key, value);
        }

        if (triggerEmailIdentifier is not null)
        {
            job.Inputs[SharedInputKeys.TRIGGER_EMAIL_IDENTIFIER] = triggerEmailIdentifier;
        }

        if (intakeSkippedFiles is not null && intakeSkippedFiles.Count > 0)
        {
            job.Inputs[IntakeInputKeys.SKIPPED_PROTECTED_FILES] =
                SkippedProtectedFile.Serialize(intakeSkippedFiles);
        }

        if (emailIntakeMode is not null)
        {
            EmailIntakeModes.WriteJobInputs(job.Inputs, emailIntakeMode, emailAttachmentsClass);
        }

        return job;
    }

    private async Task<List<string>> PersistDocumentMetadataAsync(string jobId, List<PostDocumentInfoRequest>? filesMetaData)
    {
        if (filesMetaData is null || filesMetaData.Count == 0)
        {
            return [];
        }

        var documentInfoIds = new List<string>();

        foreach (PostDocumentInfoRequest item in filesMetaData)
        {
            var documentInfo = new DocumentInfo(item.FileId)
            {
                DocumentClass = item.DocumentClass ?? string.Empty,
                ShouldUseOcr = item.ShouldUseOcr,
                ShouldBeDisplayedInOutput = item.ShouldBeDisplayedInOutput,
                JobId = jobId,
            };

            await _userDataUnitOfWork.Documents.AddDocumentInfoAsync(documentInfo);
            documentInfoIds.Add(documentInfo.Id);
        }

        return documentInfoIds;
    }

    private async Task<string?> GetCounterpartyNameFromIdAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        Counterparty? counterparty = await _userDataUnitOfWork
            .Repository<Counterparty>()
            .GetByIdAsync(id);

        return counterparty?.Name;
    }

    private static void SetRootOrganizationId(Workflow workflow, string organizationId)
    {
        workflow.OrganizationId = organizationId;
    }

    /// <summary>
    /// Names of output-document blocks that require a document class but haven't declared one.
    /// Whether a block requires one is the block's own policy, exposed via
    /// <see cref="IProducesDocumentBlockProperties.RequiresDocumentClass"/>.
    /// </summary>
    internal static List<string> FindOutputBlocksMissingDocumentClass(Workflow workflow)
    {
        return [.. workflow.Sections
            .SelectMany(s => s.Blocks)
            .Where(b => b.Properties is IProducesDocumentBlockProperties docProps
                && docProps.RequiresDocumentClass
                && string.IsNullOrWhiteSpace(docProps.CreatesDocumentClass))
            .Select(b => b.Name)];
    }
}
