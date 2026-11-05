using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class Json2XlsxPropertiesTests
{
    [Fact]
    public void HasPlaceholder_ContainsInJsonInputTemplate_ReturnsTrue()
    {
        // Arrange
        var props = new Json2XlsxProperties
        {
            JsonInputTemplate = "{jsonData}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{jsonData}"));
    }

    [Fact]
    public void HasPlaceholder_ContainsInOutputFileName_ReturnsTrue()
    {
        // Arrange
        var props = new Json2XlsxProperties
        {
            OutputFileName = "report_{date}.xlsx",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{date}"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContain_ReturnsFalse()
    {
        // Arrange
        var props = new Json2XlsxProperties
        {
            JsonInputTemplate = "static json",
            OutputFileName = "report.xlsx",
        };

        // Act & Assert
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void HasPlaceholder_NullProperties_ReturnsFalse()
    {
        // Arrange
        var props = new Json2XlsxProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsAllPlaceholders()
    {
        // Arrange
        var props = new Json2XlsxProperties
        {
            JsonInputTemplate = "{jsonData}",
            OutputFileName = "report_{date}.xlsx",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Contains("jsonData", result);
        Assert.Contains("date", result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNoPlaceholders()
    {
        // Arrange
        var props = new Json2XlsxProperties
        {
            JsonInputTemplate = "static",
            OutputFileName = "report.xlsx",
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
        var props = new Json2XlsxProperties
        {
            JsonInputTemplate = "Original Json",
            OutputFileName = "Original File",
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Replaced Json", props.JsonInputTemplate);
        Assert.Equal("Replaced File", props.OutputFileName);
    }

    [Fact]
    public void ReplaceAll_NullProperties_DoesNotThrow()
    {
        // Arrange
        var props = new Json2XlsxProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public void StampAnalysisConfig_DefaultsToFalse()
    {
        var props = new Json2XlsxProperties();

        Assert.False(props.StampAnalysisConfig);
    }

    [Fact]
    public void StampAnalysisConfig_RoundTripsViaClone()
    {
        var props = new Json2XlsxProperties { StampAnalysisConfig = true };

        var clone = (Json2XlsxProperties)props.Clone();

        Assert.True(clone.StampAnalysisConfig);
    }

    [Fact]
    public void AutoFitTextRows_DefaultsToOff()
    {
        Assert.False(new Json2XlsxProperties().AutoFitTextRows);
    }

    [Fact]
    public void Deserialize_LegacyJsonWithoutAutoFitTextRows_ReadsAsOff()
    {
        string legacyJson = """
        { "JsonInputTemplate": "{data}", "ConversionType": "Corporate Credit Model - Type A" }
        """;

        var props = JsonConvert.DeserializeObject<Json2XlsxProperties>(legacyJson);

        Assert.NotNull(props);
        Assert.False(props.AutoFitTextRows);
    }

    [Fact]
    public void AutoFitTextRows_OptInSurvivesSerializationAndClone()
    {
        var original = new Json2XlsxProperties { AutoFitTextRows = true };

        string json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<Json2XlsxProperties>(json);
        var clone = (Json2XlsxProperties)original.Clone();

        Assert.Contains("\"AutoFitTextRows\":true", json);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.AutoFitTextRows);
        Assert.True(clone.AutoFitTextRows);
    }

    [Fact]
    public void AutoFitTextRows_DefaultIsNotWrittenToJson()
    {
        string json = JsonConvert.SerializeObject(new Json2XlsxProperties());

        Assert.DoesNotContain(nameof(Json2XlsxProperties.AutoFitTextRows), json);
    }

    [Fact]
    public void Deserialize_LegacyJsonWithoutHideFromOutput_ReadsAsOff()
    {
        string legacyJson = """
        { "JsonInputTemplate": "{data}", "ConversionType": "Corporate Credit Model - Type A" }
        """;

        var props = JsonConvert.DeserializeObject<Json2XlsxProperties>(legacyJson);

        Assert.NotNull(props);
        Assert.False(props.HideFromOutput);
    }

    [Fact]
    public void HideFromOutput_OptInSurvivesSerializationAndClone()
    {
        var original = new Json2XlsxProperties { HideFromOutput = true };

        string json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<Json2XlsxProperties>(json);
        var clone = (Json2XlsxProperties)original.Clone();

        Assert.Contains("\"HideFromOutput\":true", json);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.HideFromOutput);
        Assert.True(clone.HideFromOutput);
    }

    [Fact]
    public void HideFromOutput_DefaultIsNotWrittenToJson()
    {
        string json = JsonConvert.SerializeObject(new Json2XlsxProperties());

        Assert.DoesNotContain(nameof(Json2XlsxProperties.HideFromOutput), json);
    }

    [Fact]
    public void Direction_DefaultsToInject()
    {
        Assert.Equal(Json2XlsxDirection.Inject, new Json2XlsxProperties().Direction);
    }

    [Fact]
    public void Direction_RoundTripsViaClone()
    {
        var props = new Json2XlsxProperties { Direction = Json2XlsxDirection.Extract };

        var clone = (Json2XlsxProperties)props.Clone();

        Assert.Equal(Json2XlsxDirection.Extract, clone.Direction);
    }

    [Fact]
    public void InputDocumentClass_RoundTripsViaClone()
    {
        var props = new Json2XlsxProperties { InputDocumentClass = "CorporateFinancialModel" };

        var clone = (Json2XlsxProperties)props.Clone();

        Assert.Equal("CorporateFinancialModel", clone.InputDocumentClass);
    }

    [Fact]
    public void RoundTripSerialization_PreservesDirectionAndInputDocumentClass()
    {
        var original = new Json2XlsxProperties
        {
            Direction = Json2XlsxDirection.Extract,
            InputDocumentClass = "CorporateFinancialModel",
            MappingJson = "{}",
        };

        string json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<Json2XlsxProperties>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(Json2XlsxDirection.Extract, deserialized.Direction);
        Assert.Equal("CorporateFinancialModel", deserialized.InputDocumentClass);
    }

    [Fact]
    public void Deserialize_LegacyJsonWithoutDirection_DefaultsToInject()
    {
        // A workflow saved before the extract feature has no Direction field — must default to Inject.
        string legacyJson = """
        { "JsonInputTemplate": "{data}", "ConversionType": "Corporate Credit Model - Type A" }
        """;

        var props = JsonConvert.DeserializeObject<Json2XlsxProperties>(legacyJson);

        Assert.NotNull(props);
        Assert.Equal(Json2XlsxDirection.Inject, props.Direction);
    }

    [Fact]
    public void OutputFormat_DefaultsToXlsx()
    {
        Assert.Equal(Json2XlsxOutputFormat.Xlsx, new Json2XlsxProperties().OutputFormat);
    }

    [Fact]
    public void OutputFormat_RoundTripsViaClone()
    {
        var props = new Json2XlsxProperties { OutputFormat = Json2XlsxOutputFormat.XlsxAndWord };

        var clone = (Json2XlsxProperties)props.Clone();

        Assert.Equal(Json2XlsxOutputFormat.XlsxAndWord, clone.OutputFormat);
    }

    [Fact]
    public void RoundTripSerialization_PreservesOutputFormat()
    {
        var original = new Json2XlsxProperties
        {
            OutputFormat = Json2XlsxOutputFormat.XlsxAndWord,
        };

        string json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<Json2XlsxProperties>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(Json2XlsxOutputFormat.XlsxAndWord, deserialized.OutputFormat);
    }

    [Fact]
    public void Deserialize_LegacyJsonWithoutOutputFormat_DefaultsToXlsx()
    {
        // A workflow saved before the Word-output feature has no OutputFormat field — must default to Xlsx.
        string legacyJson = """
        { "JsonInputTemplate": "{data}", "ConversionType": "Corporate Credit Model - Type A" }
        """;

        var props = JsonConvert.DeserializeObject<Json2XlsxProperties>(legacyJson);

        Assert.NotNull(props);
        Assert.Equal(Json2XlsxOutputFormat.Xlsx, props.OutputFormat);
    }

    [Fact]
    public void RequiresDocumentClass_InjectDirection_IsTrue()
    {
        Assert.True(new Json2XlsxProperties { Direction = Json2XlsxDirection.Inject }.RequiresDocumentClass);
    }

    [Fact]
    public void RequiresDocumentClass_ExtractDirection_IsFalse()
    {
        Assert.False(new Json2XlsxProperties { Direction = Json2XlsxDirection.Extract }.RequiresDocumentClass);
    }

    [Fact]
    public void RequiresDocumentClass_IsNotSerialized()
    {
        string newtonsoftJson = JsonConvert.SerializeObject(new Json2XlsxProperties());
        string systemTextJson = System.Text.Json.JsonSerializer.Serialize(new Json2XlsxProperties());

        Assert.DoesNotContain(nameof(Json2XlsxProperties.RequiresDocumentClass), newtonsoftJson);
        Assert.DoesNotContain(nameof(Json2XlsxProperties.RequiresDocumentClass), systemTextJson);
    }
}
