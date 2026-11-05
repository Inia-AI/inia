using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;
using ContractsState = IniaAi.WebApi.Contracts.Enums.SharedComponentState;
using DomainLinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

internal static class LibraryLinkTestData
{
    internal static Block NewBlock(
        string? componentId = null,
        DomainLinkState? linkState = null,
        string? versionHash = null)
    {
        Block block = NewBlockIn(NewSection(), "tag");
        if (componentId is not null)
        {
            StampBlock(block, componentId, linkState, versionHash);
        }

        return block;
    }

    internal static Block NewBlockIn(Section section, string tag)
    {
        return new Block(BlockTypes.AiQuery, section, tag) { Name = $"Block {tag}" };
    }

    internal static void StampBlock(
        Block block,
        string componentId,
        DomainLinkState? linkState,
        string? versionHash)
    {
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = linkState;
        block.SharedComponentVersionHash = versionHash;
    }

    internal static Section NewSection(
        string? componentId = null,
        DomainLinkState? linkState = null,
        string? versionHash = null)
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        var section = new Section("s", workflow) { Title = "s" };
        if (componentId is not null)
        {
            section.SharedComponentId = componentId;
            section.SharedComponentLinkState = linkState;
            section.SharedComponentVersionHash = versionHash;
        }

        return section;
    }

    internal static SharedComponentSummaryDto Component(
        string id,
        string name,
        string? headContentHash,
        ContractsState state = ContractsState.Active)
    {
        return new SharedComponentSummaryDto(
            id, ContractsKind.Block, name, null,
            CategoryId: null,
            CategoryName: null,
            State: state,
            HeadVersionId: headContentHash is null ? null : "v1",
            HeadContentHash: headContentHash,
            HeadCreatedAtUtc: headContentHash is null ? null : DateTime.UtcNow,
            ConsumerCount: 1,
            ConsumingWorkflowTypes: []);
    }
}
