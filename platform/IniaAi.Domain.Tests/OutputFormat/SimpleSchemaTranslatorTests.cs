using IniaAi.Domain.OutputFormat;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.OutputFormat;

public class SimpleSchemaTranslatorTests
{
    [Fact]
    public void ToJsonSchema_Empty_ReturnsNull()
    {
        Assert.Null(SimpleSchemaTranslator.ToJsonSchema([]));
    }

    [Fact]
    public void ToJsonSchema_FlatRequiredText_BuildsObjectSchema()
    {
        SchemaFieldDraft[] fields =
        [
            new() { Name = "name", Type = SchemaFieldType.Text, Required = true },
            new() { Name = "age", Type = SchemaFieldType.WholeNumber, Required = true },
        ];

        string? json = SimpleSchemaTranslator.ToJsonSchema(fields);
        Assert.NotNull(json);

        var parsed = JObject.Parse(json!);
        Assert.Equal("object", (string?)parsed["type"]);
        Assert.Equal("string", (string?)parsed["properties"]?["name"]?["type"]);
        Assert.Equal("integer", (string?)parsed["properties"]?["age"]?["type"]);
        Assert.Equal(2, (parsed["required"] as JArray)?.Count);
    }

    [Fact]
    public void ToJsonSchema_OptionalFieldOmittedFromRequired()
    {
        SchemaFieldDraft[] fields =
        [
            new() { Name = "name", Type = SchemaFieldType.Text, Required = true },
            new() { Name = "nickname", Type = SchemaFieldType.Text, Required = false },
        ];

        string? json = SimpleSchemaTranslator.ToJsonSchema(fields);
        var parsed = JObject.Parse(json!);

        var required = (JArray)parsed["required"]!;
        Assert.Single(required);
        Assert.Equal("name", (string?)required[0]);
    }

    [Fact]
    public void ToJsonSchema_TextEnum_AddsEnumKeyword()
    {
        SchemaFieldDraft[] fields =
        [
            new()
            {
                Name = "sentiment",
                Type = SchemaFieldType.Text,
                Required = true,
                EnumValues = "positive, neutral, negative",
            },
        ];

        var parsed = JObject.Parse(SimpleSchemaTranslator.ToJsonSchema(fields)!);
        var enumArr = parsed["properties"]?["sentiment"]?["enum"] as JArray;
        Assert.NotNull(enumArr);
        Assert.Equal(3, enumArr!.Count);
        Assert.Equal("positive", (string?)enumArr[0]);
    }

    [Fact]
    public void ToJsonSchema_NumberMinMax_AddsBounds()
    {
        SchemaFieldDraft[] fields =
        [
            new() { Name = "confidence", Type = SchemaFieldType.Number, Required = true, Min = 0, Max = 1 },
        ];

        var parsed = JObject.Parse(SimpleSchemaTranslator.ToJsonSchema(fields)!);
        Assert.Equal(0d, (double?)parsed["properties"]?["confidence"]?["minimum"]);
        Assert.Equal(1d, (double?)parsed["properties"]?["confidence"]?["maximum"]);
    }

    [Fact]
    public void ToJsonSchema_List_BuildsArrayWithItemType()
    {
        SchemaFieldDraft[] fields =
        [
            new() { Name = "tags", Type = SchemaFieldType.List, ListItemType = SchemaFieldType.Text, Required = true },
        ];

        var parsed = JObject.Parse(SimpleSchemaTranslator.ToJsonSchema(fields)!);
        Assert.Equal("array", (string?)parsed["properties"]?["tags"]?["type"]);
        Assert.Equal("string", (string?)parsed["properties"]?["tags"]?["items"]?["type"]);
    }

    [Fact]
    public void ToJsonSchema_BlankNameSkipped()
    {
        SchemaFieldDraft[] fields =
        [
            new() { Name = "name", Type = SchemaFieldType.Text },
            new() { Name = "   ", Type = SchemaFieldType.Text },
        ];

        var parsed = JObject.Parse(SimpleSchemaTranslator.ToJsonSchema(fields)!);
        Assert.Single((JObject)parsed["properties"]!);
    }

    [Fact]
    public void FromJsonSchema_FlatTypes_RoundTrips()
    {
        string source = """
            {
              "type": "object",
              "required": ["name", "age", "active"],
              "properties": {
                "name": { "type": "string" },
                "age":  { "type": "integer", "minimum": 0 },
                "active": { "type": "boolean" }
              }
            }
            """;
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema(source);
        Assert.True(result.IsFullyRepresentable);
        Assert.Equal(3, result.Fields.Count);

        SchemaFieldDraft name = result.Fields.Single(f => f.Name == "name");
        Assert.Equal(SchemaFieldType.Text, name.Type);
        Assert.True(name.Required);

        SchemaFieldDraft age = result.Fields.Single(f => f.Name == "age");
        Assert.Equal(SchemaFieldType.WholeNumber, age.Type);
        Assert.Equal(0d, age.Min);

        SchemaFieldDraft active = result.Fields.Single(f => f.Name == "active");
        Assert.Equal(SchemaFieldType.TrueFalse, active.Type);
    }

    [Fact]
    public void FromJsonSchema_EnumPreserved()
    {
        string source = """
            {
              "type": "object",
              "properties": {
                "sentiment": { "type": "string", "enum": ["positive", "neutral", "negative"] }
              }
            }
            """;
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema(source);
        Assert.True(result.IsFullyRepresentable);
        SchemaFieldDraft sentiment = result.Fields.Single();
        Assert.Equal("positive, neutral, negative", sentiment.EnumValues);
    }

    [Fact]
    public void FromJsonSchema_OneOfAtRoot_NotRepresentable()
    {
        string source = """{ "type": "object", "oneOf": [{}] }""";
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema(source);
        Assert.False(result.IsFullyRepresentable);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void FromJsonSchema_PatternConstraint_FieldKeptButFlaggedNotRepresentable()
    {
        string source = """
            {
              "type": "object",
              "properties": {
                "code": { "type": "string", "pattern": "^[A-Z]{3}$" }
              }
            }
            """;
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema(source);
        Assert.False(result.IsFullyRepresentable);
        Assert.Single(result.Fields);
        Assert.Equal(SchemaFieldType.Text, result.Fields[0].Type);
    }

    [Fact]
    public void FromJsonSchema_MalformedJson_NotRepresentable()
    {
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema("{not json");
        Assert.False(result.IsFullyRepresentable);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void FromJsonSchema_EmptyOrNull_RepresentableNoFields()
    {
        SimpleSchemaTranslator.ParseResult a = SimpleSchemaTranslator.FromJsonSchema(null);
        SimpleSchemaTranslator.ParseResult b = SimpleSchemaTranslator.FromJsonSchema("");
        Assert.True(a.IsFullyRepresentable);
        Assert.True(b.IsFullyRepresentable);
        Assert.Empty(a.Fields);
        Assert.Empty(b.Fields);
    }

    [Fact]
    public void FromJsonSchema_MalformedRequiredArray_KeepsValidEntriesAndFlagsNotRepresentable()
    {
        string source = """
            {
              "type": "object",
              "required": ["name", { "obj": 1 }, 42],
              "properties": {
                "name": { "type": "string" }
              }
            }
            """;
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema(source);
        Assert.False(result.IsFullyRepresentable);
        Assert.Single(result.Fields);
        Assert.True(result.Fields[0].Required);
    }

    [Fact]
    public void ToJsonSchema_NumberAndWholeNumber_DistinctTypes()
    {
        SchemaFieldDraft[] fields =
        [
            new() { Name = "price", Type = SchemaFieldType.Number },
            new() { Name = "count", Type = SchemaFieldType.WholeNumber },
        ];
        var parsed = JObject.Parse(SimpleSchemaTranslator.ToJsonSchema(fields)!);
        Assert.Equal("number", (string?)parsed["properties"]?["price"]?["type"]);
        Assert.Equal("integer", (string?)parsed["properties"]?["count"]?["type"]);
    }

    [Theory]
    [InlineData(SchemaFieldType.Text, "string")]
    [InlineData(SchemaFieldType.Number, "number")]
    [InlineData(SchemaFieldType.WholeNumber, "integer")]
    [InlineData(SchemaFieldType.TrueFalse, "boolean")]
    [InlineData(SchemaFieldType.List, "array")]
    public void ToJsonSchema_ListItemTypes_AllPrimitives(SchemaFieldType itemType, string expectedJsonType)
    {
        SchemaFieldDraft[] fields =
        [
            new() { Name = "items", Type = SchemaFieldType.List, ListItemType = itemType },
        ];
        var parsed = JObject.Parse(SimpleSchemaTranslator.ToJsonSchema(fields)!);
        Assert.Equal(expectedJsonType, (string?)parsed["properties"]?["items"]?["items"]?["type"]);
    }

    [Theory]
    [InlineData("\"oneOf\":[{}]")]
    [InlineData("\"anyOf\":[{}]")]
    [InlineData("\"allOf\":[{}]")]
    [InlineData("\"not\":{}")]
    public void FromJsonSchema_RootCombinators_NotRepresentable(string keywordJson)
    {
        string source = $"{{ \"type\": \"object\", {keywordJson} }}";
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema(source);
        Assert.False(result.IsFullyRepresentable);
    }

    [Theory]
    [InlineData("pattern", "\"^x$\"")]
    [InlineData("format", "\"email\"")]
    [InlineData("minLength", "1")]
    [InlineData("maxLength", "10")]
    [InlineData("multipleOf", "5")]
    public void FromJsonSchema_PropertyKeywords_FieldKeptButFlagged(string keyword, string value)
    {
        string source = $$"""
            {
              "type": "object",
              "properties": {
                "x": { "type": "string", "{{keyword}}": {{value}} }
              }
            }
            """;
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema(source);
        Assert.False(result.IsFullyRepresentable);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void FromJsonSchema_PropertiesValueNotObject_FlaggedNotRepresentable()
    {
        string source = """
            {
              "type": "object",
              "properties": {
                "x": "this should be a schema object"
              }
            }
            """;
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema(source);
        Assert.False(result.IsFullyRepresentable);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void FromJsonSchema_UnknownPropertyType_FieldDropped()
    {
        string source = """
            {
              "type": "object",
              "properties": {
                "x": { "type": "weirdtype" }
              }
            }
            """;
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema(source);
        Assert.False(result.IsFullyRepresentable);
        Assert.Empty(result.Fields);
    }

    [Fact]
    public void FromJsonSchema_RootTypeNotObject_NotRepresentable()
    {
        SimpleSchemaTranslator.ParseResult result = SimpleSchemaTranslator.FromJsonSchema("{\"type\":\"string\"}");
        Assert.False(result.IsFullyRepresentable);
        Assert.Empty(result.Fields);
    }
}
