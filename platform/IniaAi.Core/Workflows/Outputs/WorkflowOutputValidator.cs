using IniaAi.Core.Common.Helpers;
using IniaAi.Core.Services.Models;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Outputs;

public class WorkflowOutputValidator : IWorkflowOutputValidator
{
    public bool IsValidBlockForOutput(Block block)
    {
        if (block.Type is BlockTypes.Verification)
        {
            // The release marker is the analyst's protection, so it must not depend on the
            // author having ticked the block as an output source. A marker with nothing to
            // tell the analyst renders nothing, so it skips the output pipeline entirely.
            return block.TryGetProperties<VerificationProperties>()?.ExecutionState?.ReleaseMarker
                ?.HasAnalystNote() is true;
        }

        return block.IsOutputSource
            && !string.IsNullOrEmpty(block.BlockResponse?.RawResponse);
    }
}
