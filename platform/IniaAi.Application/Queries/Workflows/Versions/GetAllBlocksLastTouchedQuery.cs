using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Queries.Workflows.Versions;

/// <summary>
/// Resolves, in a single pass over the workflow's version history, the saved version in which each
/// block last changed — keyed by replacement tag. The editor resolves this once for the whole
/// workflow instead of querying per block, so opening a workflow with many blocks and deep history
/// no longer fans out into one history-walking query per block.
/// </summary>
/// <remarks>
/// Deliberately not gated by WorkflowTypeOrganizationAccess: it serves the role-gated workflow
/// editor, which authors universal templates whose types may lack access rows entirely. Do not
/// expose over HTTP without adding organization-scoped gating at that mouth.
/// </remarks>
public record GetAllBlocksLastTouchedQuery(string OrganizationId, string WorkflowTypeId)
    : IRequest<ErrorOr<IReadOnlyDictionary<string, BlockLastTouchedDto>>>;

public class GetAllBlocksLastTouchedQueryHandler
    : IRequestHandler<GetAllBlocksLastTouchedQuery, ErrorOr<IReadOnlyDictionary<string, BlockLastTouchedDto>>>
{
    private const string SECTIONS = "Sections";
    private const string BLOCKS = "Blocks";
    private const string REPLACEMENT_TAG = "ReplacementTag";
    private const string STABLE_BLOCK_ID = "StableBlockId";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IUserDisplayNameResolver _nameResolver;

    public GetAllBlocksLastTouchedQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IUserDisplayNameResolver nameResolver)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _nameResolver = nameResolver;
    }

    public async Task<ErrorOr<IReadOnlyDictionary<string, BlockLastTouchedDto>>> Handle(
        GetAllBlocksLastTouchedQuery request,
        CancellationToken cancellationToken)
    {
        IList<WorkflowVersion> versions = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetAllAsync(
                new WorkflowVersionsByTypeSpec(request.WorkflowTypeId),
                cancellationToken);

        if (versions.Count == 0)
        {
            return EmptyResult.ToErrorOr();
        }

        Dictionary<string, WorkflowVersion> lastTouchedByTag = ResolveLastTouched(versions);

        IReadOnlyDictionary<string, string> names = await _nameResolver.ResolveAsync(
            [.. lastTouchedByTag.Values.Select(version => version.CreatedByUserId).Distinct()],
            cancellationToken);

        var dtos = new Dictionary<string, BlockLastTouchedDto>(StringComparer.Ordinal);
        foreach ((string replacementTag, WorkflowVersion version) in lastTouchedByTag)
        {
            string createdByName = names.GetValueOrDefault(version.CreatedByUserId, version.CreatedByUserId);
            dtos[replacementTag] = ToDto(replacementTag, version, createdByName);
        }

        IReadOnlyDictionary<string, BlockLastTouchedDto> result = dtos;
        return result.ToErrorOr();
    }

    private static IReadOnlyDictionary<string, BlockLastTouchedDto> EmptyResult =>
        new Dictionary<string, BlockLastTouchedDto>(StringComparer.Ordinal);

    private static Dictionary<string, WorkflowVersion> ResolveLastTouched(IList<WorkflowVersion> versions)
    {
        Dictionary<string, string> currentBlocks = ParseBlocks(versions[0].WorkflowJson);
        var unresolved = new HashSet<string>(currentBlocks.Keys, StringComparer.Ordinal);
        var result = new Dictionary<string, WorkflowVersion>(StringComparer.Ordinal);

        for (int i = 0; i < versions.Count - 1 && unresolved.Count > 0; i++)
        {
            WorkflowVersion older = versions[i + 1];
            if (older.PayloadTrimmedAt is not null)
            {
                foreach (string tag in unresolved)
                {
                    result[tag] = versions[i];
                }

                unresolved.Clear();
                break;
            }

            Dictionary<string, string> olderBlocks = ParseBlocks(older.WorkflowJson);
            foreach (string tag in unresolved.ToList())
            {
                if (!olderBlocks.TryGetValue(tag, out string? olderJson)
                    || !string.Equals(currentBlocks[tag], olderJson, StringComparison.Ordinal))
                {
                    result[tag] = versions[i];
                    _ = unresolved.Remove(tag);
                }
            }

            currentBlocks = olderBlocks;
        }

        foreach (string tag in unresolved)
        {
            result[tag] = versions[^1];
        }

        return result;
    }

    private static Dictionary<string, string> ParseBlocks(string workflowJson)
    {
        var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(workflowJson))
        {
            return blocks;
        }

        var workflow = JObject.Parse(workflowJson);
        if (workflow[SECTIONS] is not JArray sections)
        {
            return blocks;
        }

        foreach (JToken section in sections)
        {
            if (section[BLOCKS] is not JArray sectionBlocks)
            {
                continue;
            }

            foreach (JToken block in sectionBlocks)
            {
                if (block is JObject blockObject && (string?)blockObject[REPLACEMENT_TAG] is { Length: > 0 } tag)
                {
                    _ = blockObject.Remove(STABLE_BLOCK_ID);
                    blocks[tag] = blockObject.ToString(Formatting.None);
                }
            }
        }

        return blocks;
    }

    private static BlockLastTouchedDto ToDto(string replacementTag, WorkflowVersion version, string createdByName)
    {
        return new BlockLastTouchedDto(
            replacementTag,
            version.Id,
            WorkflowVersionCode.Short(version.ContentHash),
            version.CreatedAt,
            version.CreatedByUserId,
            createdByName,
            version.AutoNarrativeSummary);
    }
}

public class GetAllBlocksLastTouchedQueryValidator : AbstractValidator<GetAllBlocksLastTouchedQuery>
{
    public GetAllBlocksLastTouchedQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.WorkflowTypeId).NotEmpty();
    }
}
