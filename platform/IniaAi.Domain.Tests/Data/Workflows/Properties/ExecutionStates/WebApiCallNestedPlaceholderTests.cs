using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

/// <summary>
/// A block's text input carries its own template ("Research {Counterparty.Name} …"), and the
/// engine adds the tag nested inside it to the same inputs dictionary, after the text input.
/// Resolving the payload must therefore also resolve tags that only appear once a text input's
/// value has been substituted.
/// </summary>
public class WebApiCallNestedPlaceholderTests
{
    [Fact]
    public void SetExecutionState_PayloadReferencesTextInputCarryingNestedTag_ResolvesNestedTag()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            RequestPayloadTemplate = "{\"input\": \"{ResearchPrompt}\", \"depth\": 3}",
            HttpMethod = "POST",
            ContentType = "application/json",
        };

        var inputs = new Dictionary<string, string?>
        {
            { "ResearchPrompt", "Research {Counterparty.Name} financials" },
            { "Counterparty.Name", "Acme Corp" },
        };

        // Act
        props.SetExecutionState(true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal(
            "{\"input\": \"Research Acme Corp financials\", \"depth\": 3}",
            props.ExecutionState!.RequestPayload);
    }

    [Fact]
    public void SetExecutionState_UrlReferencesTextInputCarryingNestedTag_ResolvesNestedTagThenEncodes()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/tickers/{TickerPath}",
            HttpMethod = "GET",
        };

        var inputs = new Dictionary<string, string?>
        {
            { "TickerPath", "{ExtractTickerSymbol.answer}" },
            { "ExtractTickerSymbol.answer", "NCLH US" },
        };

        // Act
        props.SetExecutionState(true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("https://api.example.com/tickers/NCLH%20US", props.ExecutionState!.Url);
    }
}
