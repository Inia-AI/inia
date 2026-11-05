using IniaAi.Core.Workflows.Agents;

namespace IniaAi.Core.Tests.Workflows.Agents;

public class AgentCitationClaimsTests
{
    [Fact]
    public void Parse_FieldKeyedEntries_YieldsOneClaimPerField()
    {
        const string output = """
            {"fields":{"totalAssets":{"values":{"FY0":4027553656}}},
             "citations":{
               "totalAssets":{"document":"ar.pdf","page":267,"as_printed":"Total assets 4,027,553,656"},
               "inventories":"Inventories 22,629,482"}}
            """;

        List<AgentCitationClaim> claims = AgentCitationClaims.Parse(output);

        Assert.Equal(2, claims.Count);
        Assert.Equal("ar.pdf", claims[0].Document);
        Assert.Equal(267, claims[0].FilePage);
        Assert.Equal("Total assets 4,027,553,656", claims[0].AsPrinted);
        Assert.Null(claims[1].Document);
        Assert.Null(claims[1].FilePage);
        Assert.Equal("Inventories 22,629,482", claims[1].AsPrinted);
    }

    [Fact]
    public void Parse_ArrayOfEntries_YieldsClaimsInOrder()
    {
        const string output = """
            {"citations":[
               {"document":"a.pdf","page":1,"as_printed":"Revenue 1,000"},
               {"as_printed":"Costs (500)"}]}
            """;

        List<AgentCitationClaim> claims = AgentCitationClaims.Parse(output);

        Assert.Equal(2, claims.Count);
        Assert.Equal("a.pdf", claims[0].Document);
        Assert.Equal("Costs (500)", claims[1].AsPrinted);
    }

    [Fact]
    public void Parse_FieldKeyedListOfEntries_Flattens()
    {
        const string output = """
            {"citations":{"revenue":[
               {"document":"a.pdf","page":2,"as_printed":"Revenue 1,000"},
               {"document":"b.pdf","as_printed":"Revenue restated 1,100"}]}}
            """;

        List<AgentCitationClaim> claims = AgentCitationClaims.Parse(output);

        Assert.Equal(2, claims.Count);
        Assert.Equal("b.pdf", claims[1].Document);
    }

    [Fact]
    public void Parse_EntriesWithoutAsPrintedOrBlank_AreSkipped()
    {
        const string output = """
            {"citations":{"a":{"document":"x.pdf","page":1},"b":{"as_printed":"  "},"c":{"as_printed":"Kept 1"}}}
            """;

        List<AgentCitationClaim> claims = AgentCitationClaims.Parse(output);

        AgentCitationClaim claim = Assert.Single(claims);
        Assert.Equal("Kept 1", claim.AsPrinted);
    }

    [Fact]
    public void Parse_NonPositiveOrNonIntegerPage_KeepsTheQuoteWithoutAPage()
    {
        const string output = """
            {"citations":{"a":{"page":0,"as_printed":"Zero 1"},"b":{"page":"five","as_printed":"Word 2"}}}
            """;

        List<AgentCitationClaim> claims = AgentCitationClaims.Parse(output);

        Assert.Equal(2, claims.Count);
        Assert.All(claims, static claim => Assert.Null(claim.FilePage));
    }

    [Fact]
    public void Parse_DuplicateJsonKeys_YieldsNoClaimsInsteadOfThrowing()
    {
        const string duplicateField = """
            {"citations":{"a":{"as_printed":"First 1"},"a":{"as_printed":"Second 2"}}}
            """;
        const string duplicateRoot = """
            {"citations":{"a":{"as_printed":"Kept 1"}},"citations":{"b":{"as_printed":"Other 2"}}}
            """;

        Assert.Empty(AgentCitationClaims.Parse(duplicateField));
        Assert.Empty(AgentCitationClaims.Parse(duplicateRoot));
    }

    [Fact]
    public void Parse_IntegralDoublePage_ReadsThePage()
    {
        const string output = """
            {"citations":{"a":{"page":267.0,"as_printed":"Total assets 4,027"}}}
            """;

        AgentCitationClaim claim = Assert.Single(AgentCitationClaims.Parse(output));
        Assert.Equal(267, claim.FilePage);
    }

    [Fact]
    public void Parse_OutputWithoutCitationsOrUnparseable_YieldsNoClaims()
    {
        Assert.Empty(AgentCitationClaims.Parse("""{"fields":{}}"""));
        Assert.Empty(AgentCitationClaims.Parse("not json at all"));
        Assert.Empty(AgentCitationClaims.Parse(null));
        Assert.Empty(AgentCitationClaims.Parse("""{"citations":"just a string"}"""));
    }
}
