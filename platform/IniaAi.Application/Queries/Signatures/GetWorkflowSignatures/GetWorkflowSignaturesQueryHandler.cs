using System.Text;
using System.Text.RegularExpressions;
using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Specifications.Signatures;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Signatures;
using Microsoft.EntityFrameworkCore;
using ContractsEnums = IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.Application.Queries.Signatures.GetWorkflowSignatures;

/// <summary>
/// Loads a workflow with its workflow-level signature field definitions and child
/// sections/blocks, then projects the definitions + client-authored active override values
/// (from the <see cref="SignatureFieldOverride"/> table, keyed by field name) into a
/// <see cref="WorkflowSignaturesDto"/>: each field once in <c>Fields</c> (single source of
/// truth), plus the block structure with name-only references for navigation. Active
/// overrides live in a dedicated table so that downloaded <c>.workflow</c> JSON never
/// carries client data across environments.
/// </summary>
public partial class GetWorkflowSignaturesQueryHandler
    : IRequestHandler<GetWorkflowSignaturesQuery, ErrorOr<WorkflowSignaturesDto>>
{
    private const int FALLBACK_REVIEW_THRESHOLD = 2;

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowTypeAccess _access;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public GetWorkflowSignaturesQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        WorkflowTypeAccess access,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
    }

    public async Task<ErrorOr<WorkflowSignaturesDto>> Handle(
        GetWorkflowSignaturesQuery request,
        CancellationToken cancellationToken)
    {
        bool hasAccess = await _access.CanReachAsync(request.OrganizationId, request.WorkflowTypeId, cancellationToken);

        if (!hasAccess)
        {
            return Error.NotFound(
                "Workflow.NotFound",
                $"No workflow of type '{request.WorkflowTypeId}' was found for this organisation.");
        }

        var spec = new WorkflowWithSectionsAndBlocksByTypeSpec(request.WorkflowTypeId);

        Workflow? workflow = await _coreUnitOfWork.Repository<Workflow>()
            .GetFirstOrDefaultAsync(spec, cancellationToken);

        if (workflow is null)
        {
            return Error.NotFound(
                "Workflow.NotFound",
                $"No workflow of type '{request.WorkflowTypeId}' was found for this organisation.");
        }

        Dictionary<string, PendingSignatureOverride> pendingByField =
            await LoadPendingsAsync(request.OrganizationId, request.WorkflowTypeId, cancellationToken);

        Dictionary<string, string> activeOverrides =
            await LoadActiveOverridesAsync(request.OrganizationId, request.WorkflowTypeId, cancellationToken);

        int reviewThreshold = await LoadReviewThresholdAsync(request.OrganizationId, cancellationToken);

        Dictionary<string, List<Block>> references = workflow.GetSignatureFieldReferences();

        List<SignatureFieldDto> fields = BuildFields(
            workflow, references, pendingByField, activeOverrides, request.UserId);
        List<SignatureSectionDto> sections = BuildSections(workflow, references);

        // Use the request's WorkflowTypeId — the spec matched on w.WorkflowTypeId == request.WorkflowTypeId,
        // so they're equal by construction, and the request value is guaranteed non-null by validation.
        return new WorkflowSignaturesDto(
            WorkflowTypeId: request.WorkflowTypeId,
            WorkflowName: string.IsNullOrWhiteSpace(workflow.Title) ? workflow.Name : workflow.Title,
            ReviewThreshold: reviewThreshold,
            Fields: fields,
            Sections: sections);
    }

    private async Task<int> LoadReviewThresholdAsync(string organizationId, CancellationToken cancellationToken)
    {
        Organization? organization = await _userDataUnitOfWork.Repository<Organization>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

        if (organization is null || organization.SignatureReviewThreshold < FALLBACK_REVIEW_THRESHOLD)
        {
            return FALLBACK_REVIEW_THRESHOLD;
        }

        return organization.SignatureReviewThreshold;
    }

    /// <summary>
    /// Batches a single DB round-trip for the active override rows scoped to the caller's
    /// organisation and the workflow type, keyed by field name. <c>AsNoTracking</c> —
    /// pure read path.
    /// </summary>
    private async Task<Dictionary<string, string>> LoadActiveOverridesAsync(
        string organizationId,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        IRepository<SignatureFieldOverride> repo =
            _coreUnitOfWork.Repository<SignatureFieldOverride>();

        List<SignatureFieldOverride> rows = await repo
            .GetAll()
            .AsNoTracking()
            .Where(o => o.OrganizationId == organizationId
                && o.WorkflowTypeId == workflowTypeId)
            .ToListAsync(cancellationToken);

        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (SignatureFieldOverride row in rows)
        {
            result[row.PlaceholderName] = row.Value;
        }

        return result;
    }

    /// <summary>
    /// Loads every pending override (+ votes) scoped to the caller's organisation and
    /// workflow type, keyed by field name. <c>AsNoTracking</c> — this is a pure read path.
    /// </summary>
    private async Task<Dictionary<string, PendingSignatureOverride>> LoadPendingsAsync(
        string organizationId,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        IRepository<PendingSignatureOverride> repo =
            _coreUnitOfWork.Repository<PendingSignatureOverride>();

        List<PendingSignatureOverride> pendings = await repo
            .GetAll()
            .AsNoTracking()
            .Where(p => p.OrganizationId == organizationId
                && p.WorkflowTypeId == workflowTypeId)
            .Include(p => p.Votes)
            .ToListAsync(cancellationToken);

        Dictionary<string, PendingSignatureOverride> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (PendingSignatureOverride pending in pendings)
        {
            result[pending.PlaceholderName] = pending;
        }

        return result;
    }

    /// <summary>
    /// Projects the workflow-level definitions into the canonical <c>Fields</c> list, each
    /// merged with the caller-org override/pending state and the referencing block ids.
    /// Ordered by first template appearance walking blocks in (section, block) order, with
    /// unreferenced definitions last, sorted by name.
    /// </summary>
    private static List<SignatureFieldDto> BuildFields(
        Workflow workflow,
        IReadOnlyDictionary<string, List<Block>> references,
        IReadOnlyDictionary<string, PendingSignatureOverride> pendingByField,
        IReadOnlyDictionary<string, string> activeOverrides,
        string requesterUserId)
    {
        List<WorkflowSignatureField> orderedDefinitions = OrderDefinitions(workflow, references);

        List<SignatureFieldDto> fields = [];
        foreach (WorkflowSignatureField definition in orderedDefinitions)
        {
            string resolvedValue = definition.DefaultValue;
            bool isOverridden = false;
            if (activeOverrides.TryGetValue(definition.Name, out string? overrideValue))
            {
                resolvedValue = overrideValue;
                isOverridden = true;
            }

            PendingSignatureOverrideDto? pendingDto = null;
            if (pendingByField.TryGetValue(definition.Name, out PendingSignatureOverride? pending))
            {
                pendingDto = ProjectPending(pending, requesterUserId);
            }

            List<string> usedByBlockIds = references.TryGetValue(definition.Name, out List<Block>? binders)
                ? [.. binders.Where(b => !string.IsNullOrEmpty(b.StableBlockId)).Select(b => b.StableBlockId!)]
                : [];

            fields.Add(new SignatureFieldDto(
                Name: definition.Name,
                DisplayName: definition.DisplayName,
                Description: definition.Description,
                DefaultValue: definition.DefaultValue,
                Value: resolvedValue,
                IsOverridden: isOverridden,
                UsedByBlockIds: usedByBlockIds,
                Pending: pendingDto));
        }

        return fields;
    }

    /// <summary>
    /// Orders definitions by where their token first appears, walking bearing blocks in
    /// (section, block) order and each block's templates in editor order. Definitions
    /// referenced by no block sort last, by name.
    /// </summary>
    private static List<WorkflowSignatureField> OrderDefinitions(
        Workflow workflow,
        IReadOnlyDictionary<string, List<Block>> references)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int next = 0;

        foreach (Block block in workflow.GetSignatureBearingBlocks())
        {
            foreach (string name in OrderedFieldNamesForBlock(block, workflow, references))
            {
                if (!rank.ContainsKey(name))
                {
                    rank[name] = next++;
                }
            }
        }

        return [.. workflow.SignatureFields
            .OrderBy(f => rank.TryGetValue(f.Name, out int r) ? r : int.MaxValue)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The workflow-level field names this block's templates reference, ordered by first
    /// appearance in the block's editor-ordered templates
    /// (<see cref="IHasSignatureFields.GetSignatureOrderingTemplates"/>); names referenced
    /// only outside the ordering templates sort last, by name.
    /// </summary>
    private static List<string> OrderedFieldNamesForBlock(
        Block block,
        Workflow workflow,
        IReadOnlyDictionary<string, List<Block>> references)
    {
        if (block.Properties is not IHasSignatureFields bearer)
        {
            return [];
        }

        List<string> referencedNames = [.. references
            .Where(entry => entry.Value.Any(b => ReferenceEquals(b, block)))
            .Select(entry => entry.Key)];

        if (referencedNames.Count == 0)
        {
            return [];
        }

        List<string?> orderingTemplates = [.. bearer.GetSignatureOrderingTemplates()];

        return [.. referencedNames
            .OrderBy(name => FindFirstAppearance(orderingTemplates, name))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    private static long FindFirstAppearance(IReadOnlyList<string?> orderingTemplates, string name)
    {
        string token = "{" + name + "}";
        for (int templateIndex = 0; templateIndex < orderingTemplates.Count; templateIndex++)
        {
            string? template = orderingTemplates[templateIndex];
            if (string.IsNullOrEmpty(template))
            {
                continue;
            }

            int position = template.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (position >= 0)
            {
                return ((long)templateIndex << 32) + position;
            }
        }

        return long.MaxValue;
    }

    /// <summary>
    /// Projects a workflow's child sections into <see cref="SignatureSectionDto"/> rows.
    /// AI Query blocks always live inside a child section in our domain; root-level
    /// blocks are not a supported shape for this surface. Blocks referencing zero fields
    /// and sections with zero qualifying blocks are omitted so the FE doesn't need to
    /// filter.
    /// </summary>
    private static List<SignatureSectionDto> BuildSections(
        Workflow workflow,
        IReadOnlyDictionary<string, List<Block>> references)
    {
        List<SignatureSectionDto> result = [];

        foreach (Section section in workflow.Sections.OrderBy(s => s.Order))
        {
            List<SignatureBlockDto> blocks = [];
            foreach (Block block in section.Blocks.OrderBy(b => b.Order))
            {
                if (block.Properties is not IHasSignatureFields signatureBearer
                    || string.IsNullOrEmpty(block.StableBlockId))
                {
                    continue;
                }

                List<string> fieldNames = OrderedFieldNamesForBlock(block, workflow, references);
                if (fieldNames.Count == 0)
                {
                    continue;
                }

                blocks.Add(new SignatureBlockDto(
                    BlockId: block.StableBlockId,
                    BlockName: block.Name,
                    BlockType: block.Type.ToString(),
                    ReplacementTag: block.ReplacementTag,
                    DependsOn: ExtractBlockReferences(signatureBearer),
                    FieldNames: fieldNames));
            }

            if (blocks.Count == 0)
            {
                continue;
            }

            result.Add(new SignatureSectionDto(
                SectionId: section.Id,
                SectionTitle: string.IsNullOrWhiteSpace(section.Title) ? section.Name : section.Title,
                SectionOrder: section.Order,
                Blocks: blocks));
        }

        return result;
    }

    /// <summary>
    /// Projects a <see cref="PendingSignatureOverride"/> (with eagerly-loaded votes)
    /// into its <see cref="PendingSignatureOverrideDto"/>. Counts are computed in-memory
    /// from the loaded votes; comments and suggestions are filtered to votes whose
    /// respective fields are non-null and ordered ascending by <c>CreatedAt</c>.
    /// </summary>
    private static PendingSignatureOverrideDto ProjectPending(
        PendingSignatureOverride pending,
        string requesterUserId)
    {
        int approveCount = 0;
        int rejectCount = 0;
        ContractsEnums.SignatureVoteKind? myVote = null;

        foreach (SignatureOverrideVote vote in pending.Votes)
        {
            if (vote.VoteKind is SignatureVoteKind.Approve)
            {
                approveCount++;
            }
            else
            {
                rejectCount++;
            }

            if (vote.VoterUserId == requesterUserId)
            {
                myVote = MapVoteKind(vote.VoteKind);
            }
        }

        List<VoteCommentDto> comments = [.. pending.Votes
            .Where(v => v.Comment is not null)
            .OrderBy(v => v.CreatedAt)
            .Select(v => new VoteCommentDto(
                VoteId: v.Id,
                VoterUserId: v.VoterUserId,
                VoteKind: MapVoteKind(v.VoteKind),
                Comment: v.Comment!,
                CreatedAt: v.CreatedAt))];

        List<SuggestionDto> suggestions = [.. pending.Votes
            .Where(v => v.SuggestedValue is not null)
            .OrderBy(v => v.CreatedAt)
            .Select(v => new SuggestionDto(
                VoteId: v.Id,
                VoterUserId: v.VoterUserId,
                SuggestedValue: v.SuggestedValue!,
                Comment: v.Comment,
                CreatedAt: v.CreatedAt))];

        return new PendingSignatureOverrideDto(
            Id: pending.Id,
            ProposerUserId: pending.ProposerUserId,
            ProposedValue: pending.ProposedValue,
            CreatedAt: pending.CreatedAt,
            ApproveCount: approveCount,
            RejectCount: rejectCount,
            MyVote: myVote,
            Comments: comments,
            Suggestions: suggestions);
    }

    private static ContractsEnums.SignatureVoteKind MapVoteKind(SignatureVoteKind kind) => kind switch
    {
        SignatureVoteKind.Approve => ContractsEnums.SignatureVoteKind.Approve,
        SignatureVoteKind.Reject => ContractsEnums.SignatureVoteKind.Reject,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled SignatureVoteKind"),
    };

    /// <summary>
    /// Scans every templated string the block exposes via
    /// <see cref="IHasSignatureFields.GetTemplateSources"/> for <c>{identifier...}</c>
    /// references and returns the unique identifiers. Each block type contributes its
    /// own template list so this method is closed to extension on new block types.
    ///
    /// <para>
    /// We deliberately do <b>not</b> call <see cref="Block.GetDependencyReplacementTags"/>
    /// here. That method runs post-substitution — by the time the execution engine calls
    /// it, parameterised loop sections have already been expanded
    /// (<c>WorkflowHandler.ExpandParameterizedSectionAsync</c>) so templates are flat and
    /// the regex in <c>GET_PLACEHOLDERS</c> works fine. The signatures UI reads the
    /// <i>raw</i> saved template straight from the DB — pre-substitution — so it sees
    /// nested forms like <c>{PL_Revenue_{PERIOD_ID}.reasoning}</c> that the domain regex
    /// cannot match. This scanner walks the string manually so those outer references
    /// show up in the dependency surface.
    /// </para>
    /// </summary>
    private static List<string> ExtractBlockReferences(IHasSignatureFields signatureBearer)
    {
        HashSet<string> seen = [];
        foreach (string? template in signatureBearer.GetTemplateSources())
        {
            ScanInto(template, seen);
        }

        List<string> ordered = [.. seen];
        ordered.Sort(static (left, right) => string.CompareOrdinal(left, right));
        return ordered;
    }

    // Grammar mirrors IniaAi.Common/StringExtensions.GET_PLACEHOLDERS so the
    // signatures scan doesn't miss refs the runtime accepts (e.g. hyphens).
    private static readonly Regex _doubleBraceRegex = GenerateDoubleBraceRegex();

    private static void ScanInto(string? text, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string cleaned = _doubleBraceRegex.Replace(text, match =>
        {
            string doubleIdent = match.Groups[1].Value.Trim('_');
            if (doubleIdent.Length > 0)
            {
                _ = seen.Add(doubleIdent);
            }

            return new string(' ', match.Length);
        });

        int depth = 0;
        StringBuilder buffer = new();
        bool collectingStopped = false;

        for (int i = 0; i < cleaned.Length; i++)
        {
            char c = cleaned[i];

            if (c == '{')
            {
                if (depth == 0)
                {
                    _ = buffer.Clear();
                    collectingStopped = false;
                }

                depth++;
                continue;
            }

            if (c == '}')
            {
                if (depth == 1)
                {
                    string ident = buffer.ToString().Trim('_');
                    if (ident.Length > 0)
                    {
                        _ = seen.Add(ident);
                    }
                }

                if (depth > 0)
                {
                    depth--;
                }

                continue;
            }

            if (depth != 1 || collectingStopped)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c) || c == '_')
            {
                _ = buffer.Append(c);
            }
            else if (c == '.')
            {
                collectingStopped = true;
            }
            else
            {
                // Drop the buffer on any unrecognized char so `{test-tag}`
                // doesn't silently surface as `test` or `testtag` in DependsOn.
                _ = buffer.Clear();
                collectingStopped = true;
            }
        }
    }

    [GeneratedRegex(@"\{\{([^{}]+)\}\}", RegexOptions.Compiled)]
    private static partial Regex GenerateDoubleBraceRegex();
}
