using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows;

/// <summary>
/// Covers the inline <see cref="AiQueryProperties.SignatureFields"/> dict contract:
/// JSON round-trip (presence and absence) and deep-copy behavior on
/// <see cref="AiQueryProperties.Clone"/>. On workflow blocks the dict is legacy input
/// only (hoisted to workflow-level definitions on load/save); it remains the permanent
/// storage format for block-template library rows. Override values never live in JSON.
/// </summary>
public class AiQuerySignatureFieldsTests
{
    [Fact]
    public void SerializeDeserialize_WithSignatureFields_RoundTripsAllEntries()
    {
        // Arrange
        var original = new AiQueryProperties
        {
            UserPromptTemplate = "Explain {cogs_instructions}",
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["cogs_instructions"] = new SignatureFieldDefinition(
                    DisplayName: "COGS instructions",
                    Description: "How to classify COGS line items",
                    DefaultValue: "Include direct production costs only."),
                ["tone"] = new SignatureFieldDefinition(
                    DisplayName: "Tone",
                    Description: "Writing tone for the summary",
                    DefaultValue: "formal"),
            },
        };

        // Act
        string json = JsonConvert.SerializeObject(original);
        AiQueryProperties? deserialized = JsonConvert.DeserializeObject<AiQueryProperties>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.SignatureFields);
        Assert.Equal(2, deserialized.SignatureFields!.Count);

        SignatureFieldDefinition cogs = deserialized.SignatureFields["cogs_instructions"];
        Assert.Equal("COGS instructions", cogs.DisplayName);
        Assert.Equal("How to classify COGS line items", cogs.Description);
        Assert.Equal("Include direct production costs only.", cogs.DefaultValue);

        SignatureFieldDefinition tone = deserialized.SignatureFields["tone"];
        Assert.Equal("Tone", tone.DisplayName);
        Assert.Equal("formal", tone.DefaultValue);
    }

    [Fact]
    public void Serialize_WithNullSignatureFields_OmitsTheProperty()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "Hello",
            SignatureFields = null,
        };

        // Act
        string json = JsonConvert.SerializeObject(props);

        // Assert
        Assert.DoesNotContain("SignatureFields", json);
    }

    [Fact]
    public void Clone_WithSignatureFields_DeepCopiesDictionary()
    {
        // Arrange
        var original = new AiQueryProperties
        {
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["rule_a"] = new SignatureFieldDefinition("A", "desc a", "default a"),
            },
        };

        // Act
        var clone = (AiQueryProperties)original.Clone();
        clone.SignatureFields!["rule_b"] = new SignatureFieldDefinition("B", "desc b", "default b");

        // Assert — mutating the clone must not touch the original
        Assert.NotNull(original.SignatureFields);
        Assert.Single(original.SignatureFields!);
        Assert.True(original.SignatureFields!.ContainsKey("rule_a"));
        Assert.False(original.SignatureFields!.ContainsKey("rule_b"));

        Assert.Equal(2, clone.SignatureFields!.Count);
    }

    [Fact]
    public void Serialize_DoesNotCarryAnyOverrideValues()
    {
        // Active override values live in a dedicated SignatureFieldOverrides table, not on the
        // block's JSON properties. Serialising a block — with or without signature fields
        // declared — must never produce a `signatureFieldValues` key that could leak client data.
        var propsWithFields = new AiQueryProperties
        {
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["cogs"] = new SignatureFieldDefinition("COGS", "desc", "default_cogs"),
            },
        };

        string json = JsonConvert.SerializeObject(propsWithFields);

        Assert.DoesNotContain("signatureFieldValues", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clone_WithNullSignatureFields_StaysNullOnClone()
    {
        // Arrange
        var original = new AiQueryProperties
        {
            SignatureFields = null,
        };

        // Act
        var clone = (AiQueryProperties)original.Clone();

        // Assert
        Assert.Null(clone.SignatureFields);
    }
}
