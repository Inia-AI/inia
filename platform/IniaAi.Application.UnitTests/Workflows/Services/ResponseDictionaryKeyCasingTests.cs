using System.Text.Json;

namespace IniaAi.Application.UnitTests.Workflows.Services;

/// <summary>
/// Guards the root-cause premise: the response serializer camelCases dictionary keys
/// (mirrors ControllerConfiguration.cs, DictionaryKeyPolicy = CamelCase).
/// </summary>
public class ResponseDictionaryKeyCasingTests
{
    [Fact]
    public void CamelCasePolicy_ConvertsPascalCaseReplacementTag_ToCamelCase()
    {
        Assert.Equal("interimPeriod", JsonNamingPolicy.CamelCase.ConvertName("InterimPeriod"));
    }

    [Fact]
    public void ResponseSerializerOptions_CamelCaseDictionaryKeys()
    {
        // Mirrors ControllerConfiguration.cs (DictionaryKeyPolicy = JsonNamingPolicy.CamelCase).
        var responseOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        };

        var variables = new Dictionary<string, JsonElement>
        {
            ["InterimPeriod"] = JsonSerializer.SerializeToElement("12"),
        };

        string json = JsonSerializer.Serialize(variables, responseOptions);

        // The dictionary KEY is camelCased on the way out — the exact behaviour that makes a
        // re-run resubmit 'interimPeriod' against the template's 'InterimPeriod'.
        Assert.Contains("\"interimPeriod\"", json);
        Assert.DoesNotContain("\"InterimPeriod\"", json);
    }
}
