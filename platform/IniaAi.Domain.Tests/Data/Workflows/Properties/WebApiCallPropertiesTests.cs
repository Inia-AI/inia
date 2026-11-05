using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static IniaAi.Domain.Tests.Data.Workflows.Properties.BlockPropertiesTestCodec;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class WebApiCallPropertiesTests
{
    [Fact]
    public void AStoredOutputDocumentClass_IsDroppedOnTheNextSave()
    {
        var stored = JObject.Parse(JsonConvert.SerializeObject(new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/rates",
        }));
        stored["CreatesDocumentClass"] = "api-data";

        var loaded = Assert.IsType<WebApiCallProperties>(DeserializeViaConverter(stored.ToString()));
        JObject saved = JObject.Parse(JsonConvert.SerializeObject(loaded));

        Assert.Null(saved.Property("CreatesDocumentClass"));
        Assert.Equal("https://api.example.com/rates", saved.Value<string>(nameof(WebApiCallProperties.UrlTemplate)));
    }

    [Fact]
    public void TheBlock_DoesNotProduceADocument()
    {
        Assert.False(typeof(IProducesDocumentBlockProperties).IsAssignableFrom(typeof(WebApiCallProperties)));
    }

    [Fact]
    public void HasPlaceholder_ContainsInUrlTemplate_ReturnsTrue()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/{endpoint}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{endpoint}"));
    }

    [Fact]
    public void HasPlaceholder_ContainsInRequestPayloadTemplate_ReturnsTrue()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            RequestPayloadTemplate = "{\"key\": \"{value}\"}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{value}"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContain_ReturnsFalse()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/fixed",
            RequestPayloadTemplate = "{\"key\": \"fixed\"}",
        };

        // Act & Assert
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void HasPlaceholder_NullProperties_ReturnsFalse()
    {
        // Arrange
        var props = new WebApiCallProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsAllPlaceholders()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/{endpoint}",
            RequestPayloadTemplate = "{\"data\": \"{payload}\"}",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Contains("endpoint", result);
        Assert.Contains("payload", result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNoPlaceholders()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/fixed",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReplaceAll_ReplacesInAllTemplateProperties()
    {
        // Arrange
        var props = new WebApiCallProperties
        {
            UrlTemplate = "Original Url",
            RequestPayloadTemplate = "Original Payload",
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Replaced Url", props.UrlTemplate);
        Assert.Equal("Replaced Payload", props.RequestPayloadTemplate);
    }

    [Fact]
    public void ReplaceAll_NullProperties_DoesNotThrow()
    {
        // Arrange
        var props = new WebApiCallProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }
}
