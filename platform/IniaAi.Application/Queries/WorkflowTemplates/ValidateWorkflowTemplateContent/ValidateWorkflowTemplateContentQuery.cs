using ErrorOr;
using FluentValidation;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Queries.WorkflowTemplates.ValidateWorkflowTemplateContent;

/// <summary>
/// Runs a template save's read-only checks against a candidate payload and writes nothing: the same
/// structural validation, shared-component reference resolution, workflow-type upsert decision,
/// category resolution, and optimistic-concurrency check the save performs before its first write.
/// </summary>
/// <remarks>
/// Deliberately narrower than the save. It cannot report whether the save would dedupe, nor whether
/// signature-field deletions would need confirming — both are decided after the graph is replaced,
/// and predicting them would mean reimplementing pipeline internals that could then drift and lie.
/// Both remaining outcomes are safe to meet on the real save: a dedupe is a no-op, and an
/// unconfirmed signature deletion is refused with nothing written. Like every preview here, the
/// answer is advisory — the save re-runs all of it inside its own transaction.
/// </remarks>
/// <param name="WorkflowJson">The candidate template JSON, exactly as it would be sent to the save.</param>
/// <param name="BaseVersionId">The head version the caller based its edit on; null only for a first save.</param>
/// <param name="NewTypeName">Display name the save would use when creating the workflow type.</param>
/// <param name="NewTypeCategoryId">Category to resolve when the workflow type does not exist yet.</param>
public record ValidateWorkflowTemplateContentQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string WorkflowJson,
    string? BaseVersionId,
    string? NewTypeName = null,
    string? NewTypeCategoryId = null) : IRequest<ErrorOr<ValidateWorkflowTemplateContentResult>>;

/// <summary>
/// What the save would do with this payload, had it been sent.
/// </summary>
/// <param name="WouldCreateWorkflowType">
/// True when no workflow type carries this id yet, so the save would create it. It is the signal
/// that matters on a save with a null base version, where the upsert turns a mistyped id into a
/// second workflow type; a save that carries a base version already conflicts on its own.
/// </param>
/// <param name="HeadVersionId">The current head version id, or null when the template has none yet.</param>
/// <param name="Warnings">Non-blocking structural issues the save would report alongside a success.</param>
public record ValidateWorkflowTemplateContentResult(
    bool WouldCreateWorkflowType,
    string? HeadVersionId,
    IReadOnlyList<WorkflowStructuralValidationWarning> Warnings);

/// <summary>
/// Mirrors <c>SaveWorkflowTemplateContentCommandValidator</c> rule for rule. The rehearsal promises
/// the save's own verdicts, so a payload this accepts must be one the save's validator also accepts;
/// a rule added to either validator needs the same rule in the other.
/// </summary>
public class ValidateWorkflowTemplateContentQueryValidator
    : AbstractValidator<ValidateWorkflowTemplateContentQuery>
{
    public ValidateWorkflowTemplateContentQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId)
            .NotEmpty()
            .MaximumLength(WorkflowType.ID_MAX_LENGTH);
        _ = RuleFor(x => x.WorkflowJson).NotEmpty();
        _ = RuleFor(x => x.NewTypeName)
            .MaximumLength(WorkflowType.NAME_MAX_LENGTH)
            .When(x => !string.IsNullOrEmpty(x.NewTypeName));
    }
}

public class ValidateWorkflowTemplateContentQueryHandler
    : IRequestHandler<ValidateWorkflowTemplateContentQuery, ErrorOr<ValidateWorkflowTemplateContentResult>>
{
    private readonly IWorkflowTemplateStructuralValidator _structuralValidator;
    private readonly WorkflowTemplateSavePreflight _preflight;

    public ValidateWorkflowTemplateContentQueryHandler(
        IWorkflowTemplateStructuralValidator structuralValidator,
        WorkflowTemplateSavePreflight preflight)
    {
        _structuralValidator = structuralValidator ?? throw new ArgumentNullException(nameof(structuralValidator));
        _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
    }

    public async Task<ErrorOr<ValidateWorkflowTemplateContentResult>> Handle(
        ValidateWorkflowTemplateContentQuery request,
        CancellationToken cancellationToken)
    {
        WorkflowStructuralValidationResult validation = _structuralValidator.Validate(request.WorkflowJson);
        if (!validation.IsValid)
        {
            return validation.Errors;
        }

        List<Error> referenceErrors = await _preflight.ValidateSharedComponentReferencesAsync(
            validation.Workflow!,
            cancellationToken);
        if (referenceErrors.Count > 0)
        {
            return referenceErrors;
        }

        WorkflowTypePresence presence = await _preflight.ReadPresenceAsync(request.WorkflowTypeId, cancellationToken);
        if (presence == WorkflowTypePresence.Retired)
        {
            return WorkflowTypeErrorFactory.Deleted(request.WorkflowTypeId);
        }

        if (presence == WorkflowTypePresence.Missing)
        {
            ErrorOr<string?> categoryResult = await _preflight.ResolveCategoryAsync(
                request.NewTypeCategoryId,
                cancellationToken);
            if (categoryResult.IsError)
            {
                return categoryResult.Errors;
            }
        }

        WorkflowVersion? head = await _preflight.ReadHeadAsync(request.WorkflowTypeId, cancellationToken);
        Error? conflict = WorkflowSavePipeline.CheckBaseVersion(request.BaseVersionId, head);
        if (conflict is not null)
        {
            return conflict.Value;
        }

        return new ValidateWorkflowTemplateContentResult(
            presence == WorkflowTypePresence.Missing,
            head?.Id,
            validation.Warnings);
    }
}
