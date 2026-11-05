using ContractsRequirement = IniaAi.WebApi.Contracts.Enums.DocumentClassRequirement;
using DomainRequirement = IniaAi.Domain.Entities.Workflows.Enums.DocumentClassRequirement;

namespace IniaAi.Application.UnitTests.Contracts;

public class DocumentClassRequirementEnumAlignmentTests
{
    [Fact]
    public void DomainAndContractsEnums_HaveIdenticalNames()
    {
        Assert.Equal(Enum.GetNames<DomainRequirement>(), Enum.GetNames<ContractsRequirement>());
    }

    [Fact]
    public void DomainAndContractsEnums_HaveIdenticalIntegerValues()
    {
        int[] domainValues = [.. Enum.GetValues<DomainRequirement>().Select(v => (int)v)];
        int[] contractsValues = [.. Enum.GetValues<ContractsRequirement>().Select(v => (int)v)];
        Assert.Equal(domainValues, contractsValues);
    }
}
