using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Application.Errors;
using IniaAi.Application.Mappers;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Specifications.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Counterparty;

namespace IniaAi.Application.Commands.Counterparties.CreateCounterparty;

/// <summary>
/// Handles creation of a new counterparty. When the requested ID is held by a soft-deleted
/// counterparty of the same organization, the deleted record is resurrected with the new
/// values instead of failing on the retained primary key.
/// </summary>
public class CreateCounterpartyCommandHandler : IRequestHandler<CreateCounterpartyCommand, ErrorOr<CounterpartyDto>>
{
    private readonly IUserDataUnitOfWork _userDataDbContext;
    private readonly IDbExceptionAnalyzer _exceptionAnalyzer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateCounterpartyCommandHandler"/> class.
    /// </summary>
    /// <param name="context">The unit of Work.</param>
    /// <param name="exceptionAnalyzer">The database exception analyzer.</param>
    public CreateCounterpartyCommandHandler(IUserDataUnitOfWork context, IDbExceptionAnalyzer exceptionAnalyzer)
    {
        _userDataDbContext = context;
        _exceptionAnalyzer = exceptionAnalyzer;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<CounterpartyDto>> Handle(CreateCounterpartyCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrEmpty(request.ParentId)
                && await _userDataDbContext.Repository<Counterparty>().SingleOrDefaultAsync(
                    parent => parent.Id == request.ParentId && parent.OrganizationId == request.OrganizationId,
                    asNoTracking: true,
                    cancellationToken: cancellationToken) is null)
            {
                return CounterpartyErrorFactory.ParentNotFound(request.ParentId);
            }

            if (!string.IsNullOrWhiteSpace(request.Id))
            {
                var spec = new CounterpartyByIdIncludingDeletedSpec(request.Id);
                Counterparty? existing = await _userDataDbContext.Repository<Counterparty>()
                    .GetFirstOrDefaultAsync(spec, cancellationToken);

                if (existing is not null)
                {
                    if (existing.OrganizationId != request.OrganizationId)
                    {
                        return CounterpartyErrorFactory.DuplicateId(request.Id);
                    }

                    if (!existing.IsDeleted)
                    {
                        return CounterpartyErrorFactory.DuplicateIdInOrganization(request.Id);
                    }

                    return await ResurrectAsync(existing, request, cancellationToken);
                }
            }

            var entity = new Counterparty
            {
                Id = request.Id ?? Guid.NewGuid().ToString(),
                OrganizationId = request.OrganizationId!,
                ParentId = request.ParentId,
            };
            CounterpartyFields.CopyText(request, entity);

            await ApplyCounterpartyTypesAsync(entity, request.CounterpartyTypeIds);

            CounterpartyPropertiesWriter.Apply(entity, request.Properties);

            await _userDataDbContext.Repository<Counterparty>().AddAsync(entity);
            _ = await _userDataDbContext.CompleteAsync(cancellationToken);

            return CounterpartyDtoMapper.ToDto(entity);
        }
        catch (Exception ex) when (_exceptionAnalyzer.IsCounterpartyNameDuplicate(ex))
        {
            return CounterpartyErrorFactory.DuplicateName(
                request.Name,
                request.OrganizationId ?? "unknown");
        }
        catch (Exception ex) when (_exceptionAnalyzer.IsUniqueConstraintViolation(ex))
        {
            return CounterpartyErrorFactory.DuplicateId(request.Id ?? "unknown");
        }
        catch (Exception ex) when (_exceptionAnalyzer.IsConcurrencyConflict(ex))
        {
            // Two concurrent creates raced to resurrect the same soft-deleted record.
            return CounterpartyErrorFactory.DuplicateIdInOrganization(request.Id ?? "unknown");
        }
    }

    /// <summary>
    /// Reactivates a soft-deleted counterparty that still holds the requested ID, replacing its
    /// scalar fields and types with the request values. The record's existing relationships
    /// (documents, users, children, workflow execution history) deliberately reconnect.
    /// </summary>
    private async Task<ErrorOr<CounterpartyDto>> ResurrectAsync(
        Counterparty existing,
        CreateCounterpartyCommand request,
        CancellationToken cancellationToken)
    {
        existing.ParentId = request.ParentId;
        CounterpartyFields.CopyText(request, existing);

        existing.IsDeleted = false;
        existing.DeletedAt = null;
        existing.DeletedByUserId = null;

        existing.CounterpartyTypes.Clear();
        await ApplyCounterpartyTypesAsync(existing, request.CounterpartyTypeIds);

        CounterpartyPropertiesWriter.Apply(existing, request.Properties);

        _ = await _userDataDbContext.CompleteAsync(cancellationToken);

        return CounterpartyDtoMapper.ToDto(existing);
    }

    private async Task ApplyCounterpartyTypesAsync(Counterparty entity, List<string>? counterpartyTypeIds)
    {
        if (counterpartyTypeIds is not { Count: > 0 })
        {
            return;
        }

        IEnumerable<CounterpartyType> types = await _userDataDbContext.Repository<CounterpartyType>()
            .FindAsync(t => counterpartyTypeIds.Contains(t.Id));

        foreach (CounterpartyType type in types)
        {
            entity.CounterpartyTypes.Add(type);
        }
    }
}
