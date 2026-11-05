using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Common;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Application.Queries.Workflows.Versions;

/// <summary>
/// Builds this deployment's currency card: the current saved version of every workflow in this
/// deployment, labelled with the deployment's instance name. Downloaded as a file and compared
/// against other deployments' cards.
/// </summary>
/// <remarks>
/// Deliberately not gated by WorkflowTypeOrganizationAccess: it serves the role-gated cross-instance
/// compare tool, where the point is to reconcile every template across deployments regardless of
/// which orgs consume it. Do not expose over HTTP without adding organization-scoped gating.
/// </remarks>
public record GenerateCurrencyCardQuery(string OrganizationId) : IRequest<ErrorOr<CurrencyCardDto>>;

public class GenerateCurrencyCardQueryHandler
    : IRequestHandler<GenerateCurrencyCardQuery, ErrorOr<CurrencyCardDto>>
{
    private const string INSTANCE_NAME_KEY = "PlatformSettings:InstanceName";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IConfiguration _configuration;
    private readonly IUserDisplayNameResolver _nameResolver;

    public GenerateCurrencyCardQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IConfiguration configuration,
        IUserDisplayNameResolver nameResolver)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _configuration = configuration;
        _nameResolver = nameResolver;
    }

    public async Task<ErrorOr<CurrencyCardDto>> Handle(
        GenerateCurrencyCardQuery request,
        CancellationToken cancellationToken)
    {
        List<VersionMetadata> versions = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetAll()
            .Select(version => new VersionMetadata(
                version.Id,
                version.WorkflowTypeId,
                version.ContentHash,
                version.CreatedAt,
                version.CreatedByUserId))
            .ToListAsync(cancellationToken);

        List<(VersionMetadata Head, List<string> Lineage)> lineagesPerType = [.. versions
            .GroupBy(version => version.WorkflowTypeId)
            .Select(BuildLineage)];

        IReadOnlyDictionary<string, string> names = await _nameResolver.ResolveAsync(
            [.. lineagesPerType.Select(lineage => lineage.Head.CreatedByUserId)], cancellationToken);

        IList<WorkflowTypeName> typeNames = await _coreUnitOfWork.Repository<WorkflowType>().GetAllAsync(
            new WorkflowTypeNamesByIdsSpec([.. lineagesPerType.Select(lineage => lineage.Head.WorkflowTypeId)]),
            cancellationToken);
        Dictionary<string, string> workflowTypeNames = typeNames.ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);

        IReadOnlyList<CurrencyCardEntryDto> entries = [.. lineagesPerType
            .Select(lineage => ToEntry(lineage.Head, lineage.Lineage, names, workflowTypeNames))
            .OrderBy(entry => entry.WorkflowTypeId, StringComparer.Ordinal)];

        string instanceName = InstanceIdentity.Resolve(_configuration[INSTANCE_NAME_KEY], ambientFallback: null);

        return new CurrencyCardDto(instanceName, DateTime.UtcNow, entries);
    }

    private static (VersionMetadata Head, List<string> Lineage) BuildLineage(IGrouping<string, VersionMetadata> typeGroup)
    {
        List<VersionMetadata> oldestFirst = [.. typeGroup
            .OrderBy(version => version.CreatedAt)
            .ThenBy(version => version.ContentHash, StringComparer.Ordinal)
            .ThenBy(version => version.Id, StringComparer.Ordinal)];

        List<string> lineage = [.. oldestFirst.Select(version => version.ContentHash)];

        return (oldestFirst[^1], lineage);
    }

    private static CurrencyCardEntryDto ToEntry(
        VersionMetadata head,
        IReadOnlyList<string> lineage,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, string> workflowTypeNames)
    {
        return new CurrencyCardEntryDto(
            head.WorkflowTypeId,
            head.Id,
            head.ContentHash,
            WorkflowVersionCode.Short(head.ContentHash),
            head.CreatedAt,
            head.CreatedByUserId,
            names.GetValueOrDefault(head.CreatedByUserId, head.CreatedByUserId),
            lineage,
            workflowTypeNames.GetValueOrDefault(head.WorkflowTypeId));
    }

    private sealed record VersionMetadata(
        string Id,
        string WorkflowTypeId,
        string ContentHash,
        DateTime CreatedAt,
        string CreatedByUserId);
}

public class GenerateCurrencyCardQueryValidator : AbstractValidator<GenerateCurrencyCardQuery>
{
    public GenerateCurrencyCardQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
    }
}
