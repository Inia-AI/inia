using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Replay;

namespace IniaAi.Domain.Tests.Replay;

public class FreezePolicyTests
{
    [Theory]
    [InlineData(BlockTypes.IntelligentSearch)]
    [InlineData(BlockTypes.ExchangeRate)]
    [InlineData(BlockTypes.WebApiCall)]
    public void IsFreezeEligible_ExternalFetchTypes_ReturnsTrue(BlockTypes type)
    {
        var block = new Block(type);

        Assert.True(FreezePolicy.IsFreezeEligible(block));
    }

    [Theory]
    [InlineData(BlockTypes.AiQuery)]
    [InlineData(BlockTypes.Merge)]
    [InlineData(BlockTypes.Json2Xlsx)]
    [InlineData(BlockTypes.CalculateNumber)]
    [InlineData(BlockTypes.Agent)]
    [InlineData(BlockTypes.Verification)]
    public void IsFreezeEligible_NonExternalTypes_ReturnsFalse(BlockTypes type)
    {
        var block = new Block(type);

        Assert.False(FreezePolicy.IsFreezeEligible(block));
    }

    [Fact]
    public void FreezeBlockTypes_IsExactlyTheThreeExternalFetchTypes()
    {
        Assert.Equal(
            [BlockTypes.IntelligentSearch, BlockTypes.ExchangeRate, BlockTypes.WebApiCall],
            FreezePolicy.FreezeBlockTypes.Order());
    }
}
