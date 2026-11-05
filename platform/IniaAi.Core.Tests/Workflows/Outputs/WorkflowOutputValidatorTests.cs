using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;

namespace IniaAi.Core.Tests.Workflows.Outputs;

public class WorkflowOutputValidatorTests
{
    private readonly WorkflowOutputValidator _validator = new();

    private static Block CreateBlock(BlockTypes type)
    {
        return new Block(type, new Section("s", new Workflow("w")));
    }

    [Fact]
    public void AnOutputSourceBlockWithAResponse_IsValid()
    {
        Block block = CreateBlock(BlockTypes.AiQuery);
        block.IsOutputSource = true;
        _ = new BlockResponse(block) { RawResponse = "content" };

        Assert.True(_validator.IsValidBlockForOutput(block));
    }

    [Fact]
    public void ABlockThatIsNotAnOutputSource_IsNotValid()
    {
        Block block = CreateBlock(BlockTypes.AiQuery);
        _ = new BlockResponse(block) { RawResponse = "content" };

        Assert.False(_validator.IsValidBlockForOutput(block));
    }

    [Fact]
    public void AVerificationBlockWithAMarkerWorthShowing_IsValidWithoutBeingAnOutputSource()
    {
        Block block = VerificationBlockFixture.WithMarker(new VerificationReleaseMarker
        {
            Provenance = VerificationProvenance.UNVERIFIED_UNCHECKED,
            UncheckedReason = "capacity overflow",
        });

        Assert.False(block.IsOutputSource);
        Assert.True(_validator.IsValidBlockForOutput(block));
    }

    [Fact]
    public void AVerificationBlockWithACleanVerifiedMarker_IsNotValid()
    {
        Block block = VerificationBlockFixture.WithMarker(new VerificationReleaseMarker
        {
            Provenance = VerificationProvenance.VERIFIED,
        });

        Assert.False(_validator.IsValidBlockForOutput(block));
    }

    [Fact]
    public void AVerifiedMarkerWithAMixedRoundStatement_IsValid_WithNoAbstentions()
    {
        Block block = VerificationBlockFixture.WithMarker(new VerificationReleaseMarker
        {
            Provenance = VerificationProvenance.VERIFIED,
            MixedRoundStatement = "The released work mixes attempts from different review rounds.",
        });

        Assert.True(_validator.IsValidBlockForOutput(block));
    }

    [Fact]
    public void AVerificationBlockWithoutAMarker_IsNotValidEvenAsAnOutputSource()
    {
        Block block = CreateBlock(BlockTypes.Verification);
        block.IsOutputSource = true;
        _ = new BlockResponse(block) { RawResponse = "{ \"judgment\": true }" };

        Assert.False(_validator.IsValidBlockForOutput(block));
    }
}
