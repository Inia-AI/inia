using ErrorOr;
using IniaAi.Application.Errors;
using IniaAi.Common.Constants;

namespace IniaAi.Application.UnitTests.Common.Errors;

/// <summary>
/// The words a person reads for a counterparty, written out. Every other wording test builds its
/// expected text from <see cref="CounterpartyTerms"/>, so only these catch a wrong term.
/// </summary>
public class CounterpartyTermsWordingTests
{
    [Fact]
    public void TheTerms_AreCompanyAndCompanies()
    {
        Assert.Equal("Company", CounterpartyTerms.SINGULAR);
        Assert.Equal("Companies", CounterpartyTerms.PLURAL);
        Assert.Equal("company", CounterpartyTerms.SINGULAR_LOWER);
        Assert.Equal("companies", CounterpartyTerms.PLURAL_LOWER);
    }

    [Fact]
    public void NotFound_SaysCompany_AndKeepsTheCounterpartyCode()
    {
        Error error = CounterpartyErrorFactory.NotFound("x");

        Assert.Equal("Company with ID 'x' was not found.", error.Description);
        Assert.Equal("Counterparty.NotFound", error.Code);
    }
}
