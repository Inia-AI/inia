using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using SharedComponentKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;
using SharedComponentLinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// Builds a throwaway, fully wired section graph from a stored shared-component definition so the
/// Library can mount it on the editing surface as a preview. Nothing built here is ever saved; the
/// scratch workflow exists only to satisfy the surface's parent wiring.
/// </summary>
public static class SharedComponentDefinitionMount
{
    private const string SCRATCH_WORKFLOW_NAME = "component-library-scratch";
    private const string SCRATCH_WORKFLOW_TITLE = "Component Library";
    private const string BLOCK_PREVIEW_TAG = "preview";

    public static Workflow CreateScratchWorkflow()
    {
        return new Workflow(SCRATCH_WORKFLOW_NAME) { Title = SCRATCH_WORKFLOW_TITLE };
    }

    public static Section BuildNewSectionHost(string componentName)
    {
        Workflow scratch = CreateScratchWorkflow();
        return new Section(componentName, scratch) { Title = componentName };
    }

    public static Section BuildNewBlockHost(string componentName)
    {
        Workflow scratch = CreateScratchWorkflow();
        var host = new Section(componentName, scratch) { Title = componentName };
        var block = new Block(BlockTypes.AiQuery, host, 0, BLOCK_PREVIEW_TAG);
        block.SupportedDocumentClasses.Add(DocumentClass.KEY_ANY);
        return host;
    }

    public static Section BuildViewSection(
        SharedComponentKind kind,
        string componentName,
        string definitionJson)
    {
        Workflow scratch = CreateScratchWorkflow();

        if (kind == SharedComponentKind.Section)
        {
            Section? payloadSection = Section.FromJson(definitionJson);
            if (payloadSection is null)
            {
                throw new InvalidOperationException("The stored definition could not be read.");
            }

            return payloadSection.CloneToWorkflow(scratch, 0);
        }

        Block? payloadBlock = Block.FromJson(definitionJson);
        if (payloadBlock is null)
        {
            throw new InvalidOperationException("The stored definition could not be read.");
        }

        var host = new Section(componentName, scratch) { Title = componentName };
        _ = payloadBlock.CopyToSection(host, 0, BLOCK_PREVIEW_TAG);

        return host;
    }

    public static Section BuildEditSection(
        SharedComponentKind kind,
        string componentName,
        string definitionJson)
    {
        if (kind == SharedComponentKind.Block)
        {
            return BuildViewSection(kind, componentName, definitionJson);
        }

        Workflow scratch = CreateScratchWorkflow();
        Section? payloadSection = Section.FromJson(definitionJson);
        if (payloadSection is null)
        {
            throw new InvalidOperationException("The stored definition could not be read.");
        }

        Section host = payloadSection.CloneToWorkflow(scratch, 0);
        host.Blocks.Clear();

        Dictionary<string, Block> inlineByTag = new(StringComparer.Ordinal);
        foreach (Block inline in payloadSection.Blocks)
        {
            _ = inlineByTag.TryAdd(inline.ReplacementTag, inline);
        }

        int order = 0;
        foreach (SectionEntry entry in payloadSection.EffectiveEntries())
        {
            if (entry.Kind == SectionEntryKind.ComponentRef)
            {
                _ = new Block(BlockTypes.AiQuery, host, order, entry.SlotTag)
                {
                    SharedComponentId = entry.SharedComponentId,
                    SharedComponentLinkState = SharedComponentLinkState.Linked,
                    SharedComponentBindings = entry.Bindings is { Count: > 0 }
                        ? new Dictionary<string, string>(entry.Bindings, StringComparer.Ordinal)
                        : null,
                };
            }
            else if (inlineByTag.TryGetValue(entry.SlotTag, out Block? inline))
            {
                _ = inline.CopyToSection(host, order, entry.SlotTag);
            }
            else
            {
                continue;
            }

            order++;
        }

        host.Entries = null;

        return host;
    }

    public static string ExtractDefinitionJson(SharedComponentKind kind, Section mounted)
    {
        var normalizer = new SharedComponentContentNormalizer(new WorkflowCanonicalizer());

        if (kind == SharedComponentKind.Section)
        {
            return normalizer.ToDefinitionJson(mounted);
        }

        if (mounted.Blocks.Count != 1)
        {
            throw new InvalidOperationException(
                "A block-component host must carry exactly one block to capture its definition.");
        }

        return normalizer.ToDefinitionJson(mounted.Blocks.First());
    }
}
