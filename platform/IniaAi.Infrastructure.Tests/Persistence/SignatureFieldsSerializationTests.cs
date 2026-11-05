using System.Text.Json;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;

namespace IniaAi.Infrastructure.Tests.Persistence;

public class SignatureFieldsSerializationTests
{
    [Theory]
    [InlineData("COGS_instructions")]
    [InlineData("ARRevenue")]
    [InlineData("region_ID")]
    [InlineData("lowercase_snake")]
    public void CoreOptions_SignatureFieldKey_RoundTripsWithOriginalCasing(string placeholderKey)
    {
        var original = new AiQueryProperties
        {
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                [placeholderKey] = new SignatureFieldDefinition("Display", "desc", "default"),
            },
        };

        string json = JsonSerializer.Serialize<BlockProperties>(original, BlockPropertiesJsonDefaults.CoreOptions);
        var deserialized = (AiQueryProperties)JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.CoreOptions)!;

        Assert.Contains($"\"{placeholderKey}\"", json);
        Assert.True(deserialized.SignatureFields!.ContainsKey(placeholderKey));
        // Active override values live in a dedicated table now, never in JSON.
        Assert.DoesNotContain("signatureFieldValues", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoreOptions_BlockTemplateStyleInlineFields_RoundTripsAsCamelCaseSignatureFields()
    {
        // Rows of the retired Prompt Library keep the inline dict, and the startup
        // migration reads them. The Properties column serializer must emit the property
        // under the camelCase name 'signatureFields' (values intact) and read it back.
        var original = new AiQueryProperties
        {
            UserPromptTemplate = "Explain {COGS_instructions}",
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["COGS_instructions"] = new SignatureFieldDefinition(
                    DisplayName: "COGS instructions",
                    Description: "How to classify COGS",
                    DefaultValue: "Include direct production costs only."),
            },
        };

        string json = JsonSerializer.Serialize<BlockProperties>(original, BlockPropertiesJsonDefaults.CoreOptions);
        var deserialized = (AiQueryProperties)JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.CoreOptions)!;

        Assert.Contains("\"signatureFields\"", json);
        Assert.DoesNotContain("\"SignatureFields\"", json);

        Assert.NotNull(deserialized.SignatureFields);
        SignatureFieldDefinition definition = deserialized.SignatureFields!["COGS_instructions"];
        Assert.Equal("COGS instructions", definition.DisplayName);
        Assert.Equal("How to classify COGS", definition.Description);
        Assert.Equal("Include direct production costs only.", definition.DefaultValue);
    }

    [Fact]
    public void Options_SignatureFieldKey_RoundTripsWithOriginalCasing()
    {
        var original = new AiQueryProperties
        {
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["COGS_instructions"] = new SignatureFieldDefinition("Display", "desc", "default"),
            },
        };

        string json = JsonSerializer.Serialize<BlockProperties>(original, BlockPropertiesJsonDefaults.Options);
        var deserialized = (AiQueryProperties)JsonSerializer.Deserialize<BlockProperties>(json, BlockPropertiesJsonDefaults.Options)!;

        Assert.Contains("\"COGS_instructions\"", json);
        Assert.True(deserialized.SignatureFields!.ContainsKey("COGS_instructions"));
    }
}
