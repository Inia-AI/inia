using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.Commands.SharedComponents.CreateSharedComponent;

public record CreateSharedComponentCommand(
    string OrganizationId,
    string UserId,
    string Name,
    ContractsKind Kind,
    string CategoryId,
    string? Description,
    string DefinitionJson) : IRequest<ErrorOr<CreateSharedComponentResult>>;

public record CreateSharedComponentResult(
    string SharedComponentId,
    ContractsKind Kind,
    string Name,
    string HeadVersionId,
    string HeadContentHash);

public class CreateSharedComponentCommandHandler
    : IRequestHandler<CreateSharedComponentCommand, ErrorOr<CreateSharedComponentResult>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ICoreDbContext _coreDbContext;
    private readonly SharedComponentFanOutService _fanOutService;
    private readonly ILogger<CreateSharedComponentCommandHandler> _logger;

    public CreateSharedComponentCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ICoreDbContext coreDbContext,
        SharedComponentFanOutService fanOutService,
        ILogger<CreateSharedComponentCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _coreDbContext = coreDbContext ?? throw new ArgumentNullException(nameof(coreDbContext));
        _fanOutService = fanOutService ?? throw new ArgumentNullException(nameof(fanOutService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<CreateSharedComponentResult>> Handle(
        CreateSharedComponentCommand request,
        CancellationToken cancellationToken)
    {
        return await _coreDbContext.CreateExecutionStrategy().ExecuteAsync(
            ct => CreateWithinStrategyAsync(request, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<CreateSharedComponentResult>> CreateWithinStrategyAsync(
        CreateSharedComponentCommand request,
        CancellationToken cancellationToken)
    {
        _coreDbContext.ClearChangeTracker();

        ErrorOr<Success> categoryGuard = await SharedComponentCategoryGuard.EnsureExistsAsync(
            _coreUnitOfWork, request.CategoryId, cancellationToken);
        if (categoryGuard.IsError)
        {
            return categoryGuard.Errors;
        }

        var component = new SharedComponent
        {
            Kind = (SharedComponentKind)(int)request.Kind,
            Name = request.Name.Trim(),
            Description = request.Description,
            CategoryId = request.CategoryId,
            State = SharedComponentState.Active,
        };

        ErrorOr<(string DefinitionJson, string ContentHash)> normalized =
            await _fanOutService.NormalizeAndValidateForCreateAsync(
                component, request.DefinitionJson, cancellationToken);
        if (normalized.IsError)
        {
            return normalized.Errors;
        }

        var version = new SharedComponentVersion
        {
            SharedComponentId = component.Id,
            SequenceNumber = 1,
            ContentHash = normalized.Value.ContentHash,
            CreatedByUserId = request.UserId,
            ContentJson = normalized.Value.DefinitionJson,
        };

        _ = _coreDbContext.Add(component);
        _ = _coreDbContext.Add(version);
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation(
            "Created shared component {SharedComponentId} ({Kind})",
            component.Id,
            component.Kind);

        return new CreateSharedComponentResult(
            component.Id,
            request.Kind,
            component.Name,
            version.Id,
            normalized.Value.ContentHash);
    }
}

public class CreateSharedComponentCommandValidator : AbstractValidator<CreateSharedComponentCommand>
{
    public CreateSharedComponentCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.Name).NotEmpty().MaximumLength(SharedComponent.NAME_MAX_LENGTH);
        _ = RuleFor(x => x.CategoryId).NotEmpty();
        _ = RuleFor(x => x.DefinitionJson).NotEmpty();
        _ = RuleFor(x => x.Kind).IsInEnum();
    }
}
