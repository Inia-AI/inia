using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Errors;
using IniaAi.Common.Constants;
using IniaAi.Common;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Text;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowTemplates.DuplicateWorkflowType;

public class DuplicateWorkflowTypeCommandHandler
    : IRequestHandler<DuplicateWorkflowTypeCommand, ErrorOr<DuplicateWorkflowTypeResult>>
{
    public const string ALREADY_EXISTS_CODE = WorkflowTypeErrorFactory.ALREADY_EXISTS_CODE;
    public const string NOT_FOUND_CODE = WorkflowTypeErrorFactory.NOT_FOUND_CODE;
    public const string EXISTING_WORKFLOW_TYPE_ID_METADATA_KEY = "existingWorkflowTypeId";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ICoreDbContext _coreDbContext;
    private readonly WorkflowSavePipeline _savePipeline;
    private readonly ILogger<DuplicateWorkflowTypeCommandHandler> _logger;
    private readonly ISuperAdminLookup _superAdmins;

    public DuplicateWorkflowTypeCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ICoreDbContext coreDbContext,
        WorkflowSavePipeline savePipeline,
        ILogger<DuplicateWorkflowTypeCommandHandler> logger,
        ISuperAdminLookup superAdmins)
    {
        _superAdmins = superAdmins ?? throw new ArgumentNullException(nameof(superAdmins));
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _coreDbContext = coreDbContext ?? throw new ArgumentNullException(nameof(coreDbContext));
        _savePipeline = savePipeline ?? throw new ArgumentNullException(nameof(savePipeline));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<DuplicateWorkflowTypeResult>> Handle(
        DuplicateWorkflowTypeCommand request,
        CancellationToken cancellationToken)
    {
        WorkflowType? source = await _coreUnitOfWork.WorkflowTypes
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(type => type.Id == request.SourceWorkflowTypeId, cancellationToken);
        if (source is null)
        {
            return Error.NotFound(
                NOT_FOUND_CODE,
                $"Workflow type '{request.SourceWorkflowTypeId}' does not exist.");
        }

        bool targetTaken = await _coreUnitOfWork.WorkflowTypes.ExistsAsync(request.NewWorkflowTypeId);
        if (targetTaken)
        {
            return Error.Conflict(
                ALREADY_EXISTS_CODE,
                $"Workflow type '{request.NewWorkflowTypeId}' already exists; choose a different id.",
                new Dictionary<string, object>
                {
                    [EXISTING_WORKFLOW_TYPE_ID_METADATA_KEY] = request.NewWorkflowTypeId,
                });
        }

        Workflow? sourceWorkflow = await _coreUnitOfWork.Workflows
            .GetByTypeIdAsNoTrackingAsync(request.SourceWorkflowTypeId);
        if (sourceWorkflow is null)
        {
            return Error.NotFound(
                NOT_FOUND_CODE,
                $"Workflow type '{request.SourceWorkflowTypeId}' has no template content to duplicate.");
        }

        ErrorOr<string?> category = await ResolveCategoryAsync(
            request.NewCategoryId,
            source.CategoryId,
            cancellationToken);
        if (category.IsError)
        {
            return category.Errors;
        }

        return await _coreDbContext.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                _coreDbContext.ClearChangeTracker();
                return await DuplicateWithinStrategyAsync(request, source, sourceWorkflow, category.Value, ct);
            },
            cancellationToken);
    }

    private async Task<ErrorOr<DuplicateWorkflowTypeResult>> DuplicateWithinStrategyAsync(
        DuplicateWorkflowTypeCommand request,
        WorkflowType source,
        Workflow sourceWorkflow,
        string? categoryId,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await _coreDbContext.BeginTransactionAsync(cancellationToken);

        try
        {
            var duplicate = new WorkflowType(request.NewWorkflowTypeId, request.NewName)
            {
                Description = request.NewDescription ?? source.Description,
                CategoryId = categoryId,
                DuplicatedFromWorkflowTypeId = request.SourceWorkflowTypeId,
            };
            await _coreUnitOfWork.WorkflowTypes.AddAsync(duplicate);
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

            var clone = new Workflow();
            sourceWorkflow.CloneTo(clone);

            ErrorOr<SaveWorkflowResult> saved = await _savePipeline.SaveCoreAsync(
                new SaveWorkflowCommand(
                    request.OrganizationId,
                    request.UserId,
                    request.NewWorkflowTypeId,
                    clone,
                    BaseVersionId: null,
                    EnforceOptimisticConcurrency: true),
                narrativeOverride: $"Duplicated from {request.SourceWorkflowTypeId}",
                cancellationToken);
            if (saved.IsError)
            {
                return saved.Errors;
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Duplicated workflow type {SourceWorkflowTypeId} to {NewWorkflowTypeId}",
                LogSanitizer.ForLog(request.SourceWorkflowTypeId),
                LogSanitizer.ForLog(request.NewWorkflowTypeId));

            return new DuplicateWorkflowTypeResult(
                request.NewWorkflowTypeId,
                saved.Value.VersionId,
                saved.Value.ContentHash,
                SkippedCarriers);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Concurrent write while duplicating workflow type {SourceWorkflowTypeId} to {NewWorkflowTypeId}",
                LogSanitizer.ForLog(request.SourceWorkflowTypeId),
                LogSanitizer.ForLog(request.NewWorkflowTypeId));

            return Error.Conflict(
                WorkflowSavePipeline.CONCURRENT_SAVE_CODE,
                "The duplicate could not be completed because a concurrent write won the race; retry.");
        }
    }

    private async Task<ErrorOr<string?>> ResolveCategoryAsync(
        string? requestedCategoryId,
        string? sourceCategoryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedCategoryId))
        {
            return sourceCategoryId;
        }

        bool exists = await _coreUnitOfWork.Repository<WorkflowTypeCategory>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(category => category.Id == requestedCategoryId, cancellationToken);

        return exists
            ? requestedCategoryId
            : Error.Validation(
                nameof(DuplicateWorkflowTypeCommand.NewCategoryId),
                $"Workflow type category '{requestedCategoryId}' does not exist.");
    }

    public static IReadOnlyList<DuplicateSkippedCarrier> SkippedCarriers { get; } =
        [
            new DuplicateSkippedCarrier(
                "Organization access",
                "The duplicate is invisible until an organization is granted access through Manage Tenants."),
            new DuplicateSkippedCarrier(
                "Signature override values",
                "Every signature field falls back to its staff default until a client approves a new value."),
            new DuplicateSkippedCarrier(
                CounterpartyTerms.SINGULAR + " type mappings",
                $"Mapping the duplicate to {CounterpartyTerms.SINGULAR_LOWER} types is a deliberate act, not inherited."),
            new DuplicateSkippedCarrier(
                "Execution history",
                "Baselines, regression runs and past executions stay with the source workflow type."),
        ];
}
