using DomainState = IniaAi.Domain.Enums.DocumentProcessingState;
using ContractState = IniaAi.WebApi.Contracts.Enums.DocumentProcessingState;

namespace IniaAi.Application.UnitTests.Documents;

/// <summary>
/// The list projection retypes the domain state to the contract state by ordinal, so the two
/// enums must agree member for member.
/// </summary>
public class DocumentProcessingStateContractTests
{
    [Fact]
    public void TheContractEnumMirrorsTheDomainEnum()
    {
        Assert.Equal(
            Enum.GetValues<DomainState>().Select(v => (v.ToString(), (int)v)),
            Enum.GetValues<ContractState>().Select(v => (v.ToString(), (int)v)));
    }
}
