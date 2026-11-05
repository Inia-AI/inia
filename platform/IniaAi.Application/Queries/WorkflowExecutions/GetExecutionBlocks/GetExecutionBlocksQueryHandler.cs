using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Signatures;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetExecutionBlocks;

/// <summary>
/// Loads the persisted execution-snapshot <see cref="Workflow"/> and projects every AI Query
/// block plus its captured output + resolved signature values into <see cref="ExecutionBlocksDto"/>.
/// The execution snapshot is stored in <c>UserData.Workflows</c> keyed by the
/// <see cref="Domain.Entities.WorkflowExecutions.WorkflowExecution.Id"/>, so a single GET by id
/// with blocks + children + block responses covers the full tree.
/// Deliberately omits every prompt string — those are Inia IP and must never leave the BE.
/// </summary>
public sealed class GetExecutionBlocksQueryHandler
    : IRequestHandler<GetExecutionBlocksQuery, ErrorOr<ExecutionBlocksDto>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public GetExecutionBlocksQueryHandler(IUserDataUnitOfWork userDataUnitOfWork)
    {
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
    }

    public async Task<ErrorOr<ExecutionBlocksDto>> Handle(
        GetExecutionBlocksQuery request,
        CancellationToken cancellationToken)
    {
        Workflow? snapshot = await _userDataUnitOfWork.Workflows
            .GetAll()
            .AsNoTracking()
            .Where(w => w.Id == request.WorkflowExecutionId
                && w.OrganizationId == request.OrganizationId)
            .Include(w => w.SignatureFields)
            .Include(w => w.Sections)
                .ThenInclude(s => s.Blocks)
                .ThenInclude(b => b.BlockResponse)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            return Error.NotFound(
                "WorkflowExecution.NotFound",
                $"No workflow execution '{request.WorkflowExecutionId}' was found for this organisation.");
        }

        List<ExecutionBlockDto> blocks = [];
        foreach (Section section in snapshot.Sections.OrderBy(s => s.Order))
        {
            // Section title doubles as the disambiguator when the same block
            // (same ReplacementTag) shows up in multiple parameterised-section
            // variants — the only way a user can tell them apart in the picker.
            string? sectionTitle = string.IsNullOrWhiteSpace(section.Title)
                ? section.Name
                : section.Title;
            CollectAiQueryBlocks(section.Blocks, blocks, sectionTitle, snapshot);
        }

        return new ExecutionBlocksDto(
            WorkflowExecutionId: snapshot.Id,
            WorkflowTypeId: snapshot.WorkflowTypeId,
            Blocks: blocks);
    }

    private static void CollectAiQueryBlocks(
        IEnumerable<Block> source,
        List<ExecutionBlockDto> sink,
        string? sectionTitle,
        Workflow snapshot)
    {
        foreach (Block block in source.OrderBy(b => b.Order))
        {
            if (block.Type is not BlockTypes.AiQuery)
            {
                continue;
            }

            if (block.Properties is not AiQueryProperties aiProps)
            {
                continue;
            }

            // Snapshots persisted before the workflow-level hoist still carry the inline
            // per-block dict; CollectBlockDefinitions falls back to it for those.
            List<(string Name, SignatureFieldDefinition Definition)> definitions =
                CollectBlockDefinitions(aiProps, snapshot);
            if (definitions.Count == 0)
            {
                continue;
            }

            AiQueryPropertiesExecutionState? state = aiProps.ExecutionState;

            List<ReplayableSignatureFieldDto> fields = [];
            foreach ((string name, SignatureFieldDefinition def) in definitions)
            {
                string? captured = null;
                if (state?.ResolvedSignatureFieldValues is not null
                    && state.ResolvedSignatureFieldValues.TryGetValue(name, out string? value))
                {
                    captured = value;
                }

                fields.Add(new ReplayableSignatureFieldDto(
                    PlaceholderName: name,
                    DisplayName: def.DisplayName ?? string.Empty,
                    Description: def.Description ?? string.Empty,
                    DefaultValue: def.DefaultValue ?? string.Empty,
                    CapturedResolvedValue: captured,
                    CanReplay: captured is not null));
            }

            sink.Add(new ExecutionBlockDto(
                BlockId: block.Id,
                BlockName: block.Name ?? string.Empty,
                SectionTitle: sectionTitle,
                ReplacementTag: block.ReplacementTag,
                RawResponse: block.BlockResponse?.RawResponse,
                SignatureFields: fields));
        }
    }

    private static List<(string Name, SignatureFieldDefinition Definition)> CollectBlockDefinitions(
        AiQueryProperties aiProps,
        Workflow snapshot)
    {
        if (aiProps.SignatureFields is { Count: > 0 })
        {
            return [.. aiProps.SignatureFields.Select(kv => (kv.Key, kv.Value))];
        }

        List<(string, SignatureFieldDefinition)> result = [];
        foreach (WorkflowSignatureField field in snapshot.SignatureFields)
        {
            if (aiProps.HasPlaceholder(
                    Workflow.SignatureFieldToken(field.Name), StringComparison.OrdinalIgnoreCase))
            {
                result.Add((field.Name, new SignatureFieldDefinition(
                    field.DisplayName, field.Description, field.DefaultValue)));
            }
        }

        return result;
    }
}
