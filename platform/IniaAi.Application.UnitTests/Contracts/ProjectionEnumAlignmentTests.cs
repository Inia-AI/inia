using ContractJobType = IniaAi.WebApi.Contracts.Enums.BackgroundJobType;
using ContractPricingComponentType = IniaAi.WebApi.Contracts.Enums.PricingComponentType;
using ContractPricingType = IniaAi.WebApi.Contracts.Enums.PricingType;
using ContractPricingUnit = IniaAi.WebApi.Contracts.Enums.PricingUnit;
using ContractSignatureVoteKind = IniaAi.WebApi.Contracts.Enums.SignatureVoteKind;
using ContractUsageContext = IniaAi.WebApi.Contracts.Enums.UsageContext;
using DomainJobType = IniaAi.Services.Models.BackgroundJobs.Enums.BackgroundJobType;
using DomainPricingComponentType = IniaAi.Domain.Entities.TokenUsage.Enums.PricingComponentType;
using DomainPricingType = IniaAi.Domain.Entities.TokenUsage.Enums.PricingType;
using DomainPricingUnit = IniaAi.Domain.Entities.TokenUsage.Enums.PricingUnit;
using DomainSignatureVoteKind = IniaAi.Domain.Entities.Signatures.SignatureVoteKind;
using DomainUsageContext = IniaAi.Domain.Entities.TokenUsage.Enums.UsageContext;

namespace IniaAi.Application.UnitTests.Contracts;

/// <summary>
/// The list and read projections and the controllers cast each of these enums to its twin by integer
/// value, so a member added to one side alone, or a reordered member, returns the wrong value
/// without an error.
/// </summary>
public class ProjectionEnumAlignmentTests
{
    [Fact]
    public void PricingTypeEnums_StayAligned()
    {
        AssertAligned<DomainPricingType, ContractPricingType>();
    }

    [Fact]
    public void PricingComponentTypeEnums_StayAligned()
    {
        AssertAligned<DomainPricingComponentType, ContractPricingComponentType>();
    }

    [Fact]
    public void PricingUnitEnums_StayAligned()
    {
        AssertAligned<DomainPricingUnit, ContractPricingUnit>();
    }

    [Fact]
    public void UsageContextEnums_StayAligned()
    {
        AssertAligned<DomainUsageContext, ContractUsageContext>();
    }

    [Fact]
    public void BackgroundJobTypeEnums_StayAligned()
    {
        AssertAligned<DomainJobType, ContractJobType>();
    }

    [Fact]
    public void SignatureVoteKindEnums_StayAligned()
    {
        AssertAligned<DomainSignatureVoteKind, ContractSignatureVoteKind>();
    }

    internal static void AssertAligned<TDomain, TContract>()
        where TDomain : struct, Enum
        where TContract : struct, Enum
    {
        Assert.Equal(Enum.GetNames<TDomain>(), Enum.GetNames<TContract>());

        int[] domainValues = [.. Enum.GetValues<TDomain>().Select(value => Convert.ToInt32(value))];
        int[] contractValues = [.. Enum.GetValues<TContract>().Select(value => Convert.ToInt32(value))];
        Assert.Equal(domainValues, contractValues);
    }
}
