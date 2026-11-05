using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Rewrites each agent-family block's model to its provider-neutral id on a template save, so
/// ids stored in one provider's form stop tying the template to that provider.
/// </summary>
public static class AgentBlockModelNormalizer
{
    /// <summary>
    /// Normalizes in place and returns how many blocks changed. A block inside a shared-component
    /// link is left alone: its content must keep matching the component definition it was
    /// materialized from, or the link reads as a local edit. It still runs, because a run
    /// resolves any form.
    /// </summary>
    public static int Normalize(Workflow workflow, AgentRuntimeSettings runtime)
    {
        int changed = 0;
        // Walks sections rather than GetAllBlocks: a block's Section back-reference is not set on
        // every deserialization path, and the section's link decides the skip.
        foreach (Section section in workflow.Sections.Where(static section => section.SharedComponentId is null))
        {
            foreach (Block block in section.Blocks)
            {
                if (block.SharedComponentId is not null || block.Properties is not IAgentRunProperties props)
                {
                    continue;
                }

                string? stored = AgentRuntimeModels.StoredIdFor(runtime, props.Model);
                if (!string.Equals(stored, props.Model, StringComparison.Ordinal))
                {
                    props.Model = stored;
                    changed++;
                }
            }
        }

        return changed;
    }
}
