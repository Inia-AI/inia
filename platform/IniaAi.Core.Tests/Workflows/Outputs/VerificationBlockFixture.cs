using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;

namespace IniaAi.Core.Tests.Workflows.Outputs;

internal static class VerificationBlockFixture
{
    internal static Block WithMarker(VerificationReleaseMarker? marker)
    {
        var section = new Section("workflow", new Workflow());
        var properties = new VerificationProperties();
        var block = new Block(BlockTypes.Verification, section)
        {
            Name = "Verifier",
            Properties = properties,
        };
        properties.ExecutionState = new VerificationPropertiesExecutionState(properties)
        {
            ReleaseMarker = marker,
        };
        _ = new BlockResponse(block)
        {
            RawResponse = "{ \"judgment\": \"machine record, never analyst-facing\" }",
        };

        return block;
    }
}
