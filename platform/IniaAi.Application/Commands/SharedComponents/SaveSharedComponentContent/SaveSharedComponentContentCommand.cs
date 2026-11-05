using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;

namespace IniaAi.Application.Commands.SharedComponents.SaveSharedComponentContent;

/// <summary>
/// Replaces a shared component's definition: the submitted JSON is re-normalized server-side,
/// appended as the new head, and immediately fanned out to every Linked consumer in one
/// transaction. A definition byte-identical to the head dedups to a no-op with no fan-out.
/// Grafts that would orphan signature overrides are refused with
/// <see cref="SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT"/> until re-sent with
/// <see cref="AcknowledgeSignatureOrphans"/>. Grafts that would discard a consumer's local edits in
/// a referenced entry slot no decision named are refused the same way with
/// <see cref="SharedComponentErrorCodes.OVERWRITES_LOCAL_ENTRIES"/> until re-sent with
/// <see cref="AcknowledgeOverwrittenEntries"/>.
/// </summary>
public record SaveSharedComponentContentCommand(
    string OrganizationId,
    string UserId,
    string SharedComponentId,
    string DefinitionJson,
    string ExpectedHeadVersionId,
    bool AcknowledgeSignatureOrphans = false,
    bool AcknowledgeOverwrittenEntries = false) : IRequest<ErrorOr<SharedComponentAppendResult>>;

public class SaveSharedComponentContentCommandHandler
    : IRequestHandler<SaveSharedComponentContentCommand, ErrorOr<SharedComponentAppendResult>>
{
    private readonly SharedComponentFanOutService _fanOutService;

    public SaveSharedComponentContentCommandHandler(SharedComponentFanOutService fanOutService)
    {
        _fanOutService = fanOutService ?? throw new ArgumentNullException(nameof(fanOutService));
    }

    public async Task<ErrorOr<SharedComponentAppendResult>> Handle(
        SaveSharedComponentContentCommand request,
        CancellationToken cancellationToken)
    {
        return await _fanOutService.AppendAndFanOutAsync(
            request.OrganizationId,
            request.UserId,
            request.SharedComponentId,
            request.DefinitionJson,
            request.ExpectedHeadVersionId,
            request.AcknowledgeSignatureOrphans,
            cancellationToken,
            acknowledgeOverwrittenEntries: request.AcknowledgeOverwrittenEntries);
    }
}

public class SaveSharedComponentContentCommandValidator : AbstractValidator<SaveSharedComponentContentCommand>
{
    public SaveSharedComponentContentCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
        _ = RuleFor(x => x.DefinitionJson).NotEmpty();
        _ = RuleFor(x => x.ExpectedHeadVersionId).NotEmpty();
    }
}
