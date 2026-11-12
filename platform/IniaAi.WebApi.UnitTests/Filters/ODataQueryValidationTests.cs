using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using IniaAi.WebApi.UnitTests.Configuration;

namespace IniaAi.WebApi.UnitTests.Filters;

public sealed class ODataQueryValidationTests : IClassFixture<ApiPipelineHost>
{
    private const string PROBE_ROUTE = "api/v1/odata-probe";
    private const string BARE_GUID = "00000000-0000-0000-0000-000000000000";

    private readonly HttpClient _client;

    public ODataQueryValidationTests(ApiPipelineHost host)
    {
        _client = host.Client;
    }

    [Theory]
    [InlineData("$filter=id eq " + BARE_GUID, "$filter")]
    [InlineData("$orderby=noSuchProperty", "$orderby")]
    [InlineData("$top=many", "$top")]
    [InlineData("$skip=-1", "$skip")]
    public async Task MalformedClause_AnswersBadRequestNamingTheClause(string query, string clause)
    {
        HttpResponseMessage response = await _client.GetAsync($"/{PROBE_ROUTE}?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(
            body.GetProperty("errors").TryGetProperty(clause, out JsonElement messages),
            $"The problem details do not name {clause}: {body}");
        Assert.NotEmpty(messages.EnumerateArray().First().GetString()!);
    }

    [Theory]
    [InlineData("$search=revenue")]
    [InlineData("search=revenue")]
    public async Task Search_IsRefusedAsUnsupported_NotAnsweredWithAServerError(string query)
    {
        HttpResponseMessage response = await _client.GetAsync($"/{PROBE_ROUTE}?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(
            body.GetProperty("errors").TryGetProperty("$search", out JsonElement messages),
            $"The problem details do not name $search: {body}");
        Assert.NotEmpty(messages.EnumerateArray().First().GetString()!);
    }

    [Theory]
    [InlineData("$filter=true&$filter=false")]
    [InlineData("filter=true&$FILTER=true")]
    [InlineData("$top=1&top=2")]
    [InlineData("$orderby=title&$ORDERBY=title")]
    public async Task AClauseGivenTwice_AnswersBadRequest_NotAServerError(string query)
    {
        HttpResponseMessage response = await _client.GetAsync($"/{PROBE_ROUTE}?{query}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MalformedDateFilter_AnswersBadRequestWithoutInternals()
    {
        HttpResponseMessage response = await _client.GetAsync(
            $"/{PROBE_ROUTE}?$filter=createdAt eq 9999-99-01",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("   at ", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("Microsoft.", body);
    }

    [Fact]
    public async Task QuotedGuidFilter_StillAnswersOk()
    {
        HttpResponseMessage response = await _client.GetAsync(
            $"/{PROBE_ROUTE}?$filter=id eq '{BARE_GUID}'",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
