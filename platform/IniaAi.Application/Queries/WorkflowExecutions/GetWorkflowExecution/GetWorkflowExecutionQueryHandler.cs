using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.AgentRuns.GetAgentRunActivity;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Common.Text;
using IniaAi.Core.Workflows.Activity;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Common;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.AgentActivity;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContractWorkflowState = IniaAi.WebApi.Contracts.Enums.WorkflowState;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;

/// <summary>
/// Handles <see cref="GetWorkflowExecution"/> to retrieve a workflow execution by ID.
/// </summary>
public sealed class GetWorkflowExecutionQueryHandler : IRequestHandler<GetWorkflowExecutionQuery, ErrorOr<WorkflowExecutionDto>>
{
    private readonly ILogger<GetWorkflowExecutionQueryHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IAgentRunActivityFeed? _agentRunActivityFeed;
    private readonly IBaselineReader _baselines;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetWorkflowExecutionQueryHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger for operational diagnostics.</param>
    /// <param name="userDataUnitOfWork">The unit of work for user data repository access.</param>
    /// <param name="coreUnitOfWork">The unit of work for core repository access.</param>
    /// <param name="baselines">Reads whether a starred execution's baseline is verified.</param>
    /// <param name="agentRunActivityFeed">The live agent-run activity feed; optional so hosts without it keep working.</param>
    public GetWorkflowExecutionQueryHandler(
        ILogger<GetWorkflowExecutionQueryHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork,
        IBaselineReader baselines,
        IAgentRunActivityFeed? agentRunActivityFeed = null)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
        _coreUnitOfWork = coreUnitOfWork;
        _agentRunActivityFeed = agentRunActivityFeed;
        _baselines = baselines;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<WorkflowExecutionDto>> Handle(
        GetWorkflowExecutionQuery request,
        CancellationToken cancellationToken)
    {
        ExecutionForReport? element = await _userDataUnitOfWork
            .WorkflowExecutions
            .GetAll(new WorkflowExecutionByIdAndOrganizationSpec(request.Id, request.OrganizationId))
            .Select(_toReport)
            .FirstOrDefaultAsync(cancellationToken);

        if (element is null)
        {
            _logger.LogWarning("Workflow Execution with ID: {Id} not found", LogSanitizer.ForLog(request.Id));
            return Error.NotFound(description: "Workflow Execution not found.");
        }

        WorkflowExecutionDto mapped = element.Execution;
        if (mapped.WorkflowTypeId is null)
        {
            _logger.LogWarning("WorkflowType with ID: {Id} not found", LogSanitizer.ForLog(request.Id));
            return Error.NotFound(description: "WorkflowType with ID '{id}' not found.");
        }

        WorkflowType? workflowType = await _coreUnitOfWork
            .WorkflowTypes
            .GetAll()
            .SingleOrDefaultAsync(wt => wt.Id == mapped.WorkflowTypeId, cancellationToken);

        // These must run sequentially — they share the same DbContext which is not thread-safe.
        WorkflowSnapshotForReport snapshot = await LoadWorkflowSnapshotAsync(mapped.WorkflowId, cancellationToken);
        List<DocumentInfo> documentInfos = await LoadDocumentInfosAsync(mapped.JobId);

        List<WorkflowExecutionDocumentDto> executionDocuments = [.. documentInfos
            .Where(di => !di.IsExcludedFromRun)
            .Select(di => CreateExecutionDocumentDto(di, snapshot))];
        List<WorkflowExecutionDocumentDto> excludedDocuments = [.. documentInfos
            .Where(di => di.IsExcludedFromRun)
            .Select(di => CreateExecutionDocumentDto(di, snapshot))];

        List<WorkflowExecutionVariableValueDto> variableValues =
            WorkflowExecutionVariableResolver.Resolve(snapshot.Variables, mapped.Variables);
        Dictionary<string, JsonElement>? sanitizedVariables =
            StripConfidentialValues(mapped.Variables, snapshot.Variables);

        return mapped with
        {
            WorkflowTypeName = workflowType?.Name,
            WorkflowExecutionDocuments = executionDocuments,
            ExcludedDocuments = excludedDocuments,
            Variables = sanitizedVariables,
            VariableValues = variableValues,
            BaselineVerified = await ReadBaselineStarStateAsync(mapped, request.OrganizationId, cancellationToken),
            AgentActivity = SnapshotAgentActivity(request.OrganizationId, mapped.Id),
            WorkflowActivity = await ComposeWorkflowActivityAsync(element, cancellationToken),
        };
    }

    private async Task<bool?> ReadBaselineStarStateAsync(
        WorkflowExecutionDto execution,
        string organizationId,
        CancellationToken cancellationToken)
    {
        if (!execution.IsBaseline)
        {
            return null;
        }

        IReadOnlyDictionary<string, bool> verified = await _baselines.VerifiedAsync(organizationId, [execution.Id], cancellationToken);

        return verified.TryGetValue(execution.Id, out bool isVerified) ? isVerified : null;
    }

    /// <summary>
    /// Resolves a running execution's place in its version's cached activity script from the
    /// per-block rows the execution log already persists — completed, failed, and skipped rows
    /// all mean the block is no longer pending. Best-effort like the agent feed: any failure
    /// answers null and the report keeps its progress-only surface.
    /// </summary>
    private async Task<WorkflowActivityDto?> ComposeWorkflowActivityAsync(
        ExecutionForReport element,
        CancellationToken cancellationToken)
    {
        if (element.Execution.WorkflowState is not ContractWorkflowState.Running
            || string.IsNullOrEmpty(element.TemplateVersionId))
        {
            return null;
        }

        try
        {
            string? scriptJson = await _coreUnitOfWork
                .Repository<WorkflowVersion>()
                .GetAll()
                .Where(version => version.Id == element.TemplateVersionId)
                .Select(version => version.ActivityScriptJson)
                .SingleOrDefaultAsync(cancellationToken);

            WorkflowActivityScript? script = WorkflowActivityScriptSerializer.TryDeserialize(scriptJson);
            if (script is null)
            {
                return null;
            }

            List<string> completedBlockIds = await _userDataUnitOfWork
                .Repository<WorkflowExecutionLog>()
                .GetAll()
                .Where(log => log.WorkflowExecutionId == element.Execution.Id
                    && log.Block != null
                    && log.Block.StableBlockId != null)
                .Select(log => log.Block!.StableBlockId!)
                .Distinct()
                .ToListAsync(cancellationToken);

            WorkflowActivityProgress progress = script.ResolveProgress(completedBlockIds);
            if (progress.Steps.Count == 0)
            {
                return null;
            }

            return new WorkflowActivityDto
            {
                SchemaVersion = WORKFLOW_ACTIVITY_SCHEMA_VERSION,
                CurrentStepIndex = progress.CurrentStepIndex,
                Steps = [.. progress.Steps.Select(step => new WorkflowActivityStepDto
                {
                    Label = step.Label,
                    Status = step.State switch
                    {
                        WorkflowActivityStepState.Done => WorkflowActivityStepStatus.DONE,
                        WorkflowActivityStepState.Active => WorkflowActivityStepStatus.ACTIVE,
                        _ => WorkflowActivityStepStatus.PENDING,
                    },
                })],
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not compose workflow activity for execution {ExecutionId}",
                element.Execution.Id);
            return null;
        }
    }

    private const int WORKFLOW_ACTIVITY_SCHEMA_VERSION = 1;

    private List<AgentBlockActivityDto>? SnapshotAgentActivity(string organizationId, string executionId)
    {
        IReadOnlyList<AgentRunActivityEntry> entries =
            _agentRunActivityFeed?.SnapshotForExecution(organizationId, executionId) ?? [];
        if (entries.Count == 0)
        {
            return null;
        }

        // The registration carries the run id's role LETTER; the contract carries the named
        // value. AgentRunIds owns the one letter-to-name mapping, and an unallocated
        // letter passes through verbatim so a consumer can show it as unrecognised instead of
        // silently drawing it as a worker.
        return [.. entries.Select(static entry => new AgentBlockActivityDto
        {
            BlockId = entry.Registration.BlockId ?? string.Empty,
            BlockName = entry.Registration.BlockName ?? string.Empty,
            RunExecutionId = entry.Registration.RunExecutionId,
            Round = entry.Registration.Round,
            Role = AgentRunIds.RoleNameOf(entry.Registration.Role),
            Settled = entry.Settled,
            Activity = entry.Digest is null ? null : AgentActivityDtoMapper.ToDto(entry.Digest),
        })];
    }

    private static Dictionary<string, JsonElement>? StripConfidentialValues(
        Dictionary<string, JsonElement>? rawValues,
        IReadOnlyList<WorkflowVariable> definitions)
    {
        if (rawValues is null || rawValues.Count == 0)
        {
            return rawValues;
        }

        // Match the persisted camelCase value keys against the PascalCase
        // ReplacementTags case-insensitively, else a confidential variable's raw
        // value would not be stripped and would leak in the response payload.
        var confidentialTags = new HashSet<string>(
            definitions
                .Where(v => v.IsConfidential)
                .Select(v => v.ReplacementTag),
            StringComparer.OrdinalIgnoreCase);

        if (confidentialTags.Count == 0)
        {
            return rawValues;
        }

        return rawValues
            .Where(kv => !confidentialTags.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Loads the snapshotted workflow's document classes and variables, one query each, without
    /// loading the workflow row and its run state.
    /// Returns empty collections when the snapshot can't be loaded so callers can render without nulls.
    /// </summary>
    private async Task<WorkflowSnapshotForReport> LoadWorkflowSnapshotAsync(
        string? workflowId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return WorkflowSnapshotForReport.CreateEmpty();
        }

        try
        {
            IQueryable<Workflow> workflow = _userDataUnitOfWork.Workflows
                .GetAll()
                .AsNoTracking()
                .Where(w => w.Id == workflowId);
            List<DocumentClass> documentClasses = await workflow.SelectMany(w => w.DocumentClasses).ToListAsync(cancellationToken);
            List<WorkflowVariable> variables = await workflow.SelectMany(w => w.Variables).ToListAsync(cancellationToken);

            // GroupBy + First tolerates legacy data with duplicate Keys (no DB unique
            // index exists on (WorkflowId, Key)); .ToDictionary would throw and the
            // catch below would silently swallow the panel.
            var documentClassesByKey = documentClasses
                .GroupBy(dc => dc.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            // EF returns Variables in non-deterministic (PK) order; sort by a stable
            // natural-order key so the frontend can trust the payload's order.
            List<WorkflowVariable> orderedVariables = [.. variables
                .OrderBy(v => NaturalOrderKey(string.IsNullOrEmpty(v.DisplayName) ? v.ReplacementTag : v.DisplayName), StringComparer.Ordinal)];

            return new WorkflowSnapshotForReport(documentClassesByKey, orderedVariables);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load workflow snapshot for WorkflowId: {WorkflowId}", workflowId);
            return WorkflowSnapshotForReport.CreateEmpty();
        }
    }

    /// <summary>
    /// Loads document infos for the given job ID with error handling for missing data.
    /// </summary>
    private async Task<List<DocumentInfo>> LoadDocumentInfosAsync(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return [];
        }

        try
        {
            return await _userDataUnitOfWork
                .Documents
                .GetByJobIdWithMetadataAsync(jobId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading execution documents for JobId: {JobId}", jobId);
            return [];
        }
    }

    /// <summary>
    /// Creates an ExecutionDocumentDto from DocumentInfo with error handling for missing relationships.
    /// </summary>
    private static WorkflowExecutionDocumentDto CreateExecutionDocumentDto(
        DocumentInfo documentInfo,
        WorkflowSnapshotForReport snapshot)
    {
        DocumentClass? documentClass = snapshot.DocumentClassesByKey.GetValueOrDefault(documentInfo.DocumentClass);
        bool isOutput = documentClass?.IsOutputDocument ?? false;
        string displayName = string.IsNullOrEmpty(documentClass?.DisplayName)
            ? documentInfo.DocumentClass
            : documentClass.DisplayName;

        Document? document = documentInfo.Document;
        string name = document?.FileMetadata?.Name ?? document?.Name ?? "[ERROR: Document not found]";
        string extension = document?.FileMetadata?.Extension
            ?? (document is null ? "[ERROR: Document not found]" : "[ERROR: File metadata not found]");
        long sizeBytes = document?.FileMetadata?.SizeBytes ?? 0;
        List<string> sources = document?.Sources is { } s ? [.. s] : [];

        return new WorkflowExecutionDocumentDto
        {
            FileId = documentInfo.DocumentId,
            Name = name,
            Extension = extension,
            SizeBytes = sizeBytes,
            DocumentClass = documentInfo.DocumentClass,
            DocumentClassDisplayName = displayName,
            ShouldUseOcr = documentInfo.ShouldUseOcr,
            ShouldBeDisplayedInOutput = documentInfo.ShouldBeDisplayedInOutput,
            IsOutputDocument = isOutput,
            Sources = sources,
            ExcludedAt = documentInfo.ExcludedAt,
            ExclusionReason = documentInfo.ExclusionReason,
        };
    }

    private static readonly Expression<Func<WorkflowExecution, ExecutionForReport>> _toReport = ProjectionExpressions.Of<WorkflowExecution, ExecutionForReport>(execution =>
        new ExecutionForReport(WorkflowExecutionProjections.ToDto.Inline(execution), execution.TemplateVersionId));

    /// <summary>The report's execution, with the template version its activity step reads beside it.</summary>
    private sealed record ExecutionForReport(WorkflowExecutionDto Execution, string? TemplateVersionId);

    private sealed record WorkflowSnapshotForReport(
        Dictionary<string, DocumentClass> DocumentClassesByKey,
        IReadOnlyList<WorkflowVariable> Variables)
    {
        public static WorkflowSnapshotForReport CreateEmpty()
        {
            return new WorkflowSnapshotForReport(
                new Dictionary<string, DocumentClass>(StringComparer.Ordinal),
                []);
        }
    }

    // Pads embedded digit runs so lexical OrderBy yields natural numeric order
    // (e.g. "Var 10" sorts after "Var 9", not after "Var 1").
    private static string NaturalOrderKey(string input)
    {
        return _digitRun.Replace(input, m => m.Value.PadLeft(20, '0'));
    }

    private static readonly Regex _digitRun = new(@"\d+", RegexOptions.Compiled);
}
