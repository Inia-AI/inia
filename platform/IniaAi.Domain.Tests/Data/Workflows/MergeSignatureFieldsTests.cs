using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows;

/// <summary>
/// Covers <see cref="MergeProperties.SignatureFields"/>: JSON round-trip (presence and
/// absence), <see cref="IHasSignatureFields"/> contract, and deep-copy behaviour on
/// <see cref="MergeProperties.Clone"/>. Mirrors <see cref="AiQuerySignatureFieldsTests"/>
/// since both block types share the same signature-fields surface.
/// </summary>
public class MergeSignatureFieldsTests
{
    [Fact]
    public void MergeProperties_ImplementsIHasSignatureFields()
    {
        var props = new MergeProperties();

        Assert.IsAssignableFrom<IHasSignatureFields>(props);
    }

    [Fact]
    public void SerializeDeserialize_WithSignatureFields_RoundTripsAllEntries()
    {
        // Arrange
        var original = new MergeProperties
        {
            TextTemplate = "Use {tone} please.",
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["tone"] = new SignatureFieldDefinition(
                    DisplayName: "Tone",
                    Description: "Writing tone",
                    DefaultValue: "formal"),
                ["jurisdiction"] = new SignatureFieldDefinition(
                    DisplayName: "Jurisdiction",
                    Description: "Governing law clause",
                    DefaultValue: "England and Wales"),
            },
        };

        // Act
        string json = JsonConvert.SerializeObject(original);
        MergeProperties? deserialized = JsonConvert.DeserializeObject<MergeProperties>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.SignatureFields);
        Assert.Equal(2, deserialized.SignatureFields!.Count);

        SignatureFieldDefinition tone = deserialized.SignatureFields["tone"];
        Assert.Equal("Tone", tone.DisplayName);
        Assert.Equal("Writing tone", tone.Description);
        Assert.Equal("formal", tone.DefaultValue);

        SignatureFieldDefinition jurisdiction = deserialized.SignatureFields["jurisdiction"];
        Assert.Equal("Jurisdiction", jurisdiction.DisplayName);
        Assert.Equal("England and Wales", jurisdiction.DefaultValue);
    }

    [Fact]
    public void Serialize_WithNullSignatureFields_OmitsTheProperty()
    {
        var props = new MergeProperties
        {
            TextTemplate = "Hello",
            SignatureFields = null,
        };

        string json = JsonConvert.SerializeObject(props);

        Assert.DoesNotContain("SignatureFields", json);
    }

    [Fact]
    public void Clone_WithSignatureFields_DeepCopiesDictionary()
    {
        // Arrange
        var original = new MergeProperties
        {
            SignatureFields = new Dictionary<string, SignatureFieldDefinition>
            {
                ["rule_a"] = new SignatureFieldDefinition("A", "desc a", "default a"),
            },
        };

        // Act
        var clone = (MergeProperties)original.Clone();
        clone.SignatureFields!["rule_b"] = new SignatureFieldDefinition("B", "desc b", "default b");

        // Assert — mutating the clone must not touch the original
        Assert.NotNull(original.SignatureFields);
        Assert.Single(original.SignatureFields!);
        Assert.True(original.SignatureFields!.ContainsKey("rule_a"));
        Assert.False(original.SignatureFields!.ContainsKey("rule_b"));

        Assert.Equal(2, clone.SignatureFields!.Count);
    }

    [Fact]
    public void Clone_WithNullSignatureFields_StaysNullOnClone()
    {
        var original = new MergeProperties
        {
            SignatureFields = null,
        };

        var clone = (MergeProperties)original.Clone();

        Assert.Null(clone.SignatureFields);
    }

    [Fact]
    public void GetTemplateSources_ReturnsTextTemplate()
    {
        // Merge exposes a single template (TextTemplate) for placeholder scanning.
        // Future block types implementing IHasSignatureFields contribute their own
        // sources via this method so callers don't switch on concrete property type.
        IHasSignatureFields props = new MergeProperties
        {
            TextTemplate = "Use {tone} please.",
        };

        List<string?> sources = [.. props.GetTemplateSources()];

        Assert.Single(sources);
        Assert.Equal("Use {tone} please.", sources[0]);
    }

    [Fact]
    public void GetTemplateSources_NullTemplate_StillReturnsOneEntry()
    {
        // The contract is "every templated string this block has", so a null
        // template still surfaces (callers filter null/empty themselves). This
        // matches the pre-refactor behaviour of the manual switch.
        IHasSignatureFields props = new MergeProperties { TextTemplate = null };

        List<string?> sources = [.. props.GetTemplateSources()];

        Assert.Single(sources);
        Assert.Null(sources[0]);
    }
}
