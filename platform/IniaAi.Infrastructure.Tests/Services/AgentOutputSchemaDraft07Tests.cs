using IniaAi.Common.Interfaces.Services.AgentRuntime;
using System.Text.Json.Nodes;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Pins the validator to the dialect the Agent SDK actually enforces (draft-07 compiled by Ajv
/// in strict mode, formats not validated). Every accepted/rejected shape here was measured
/// against Ajv 8 with the SDK CLI's own options before being written down — change a case only
/// after re-measuring, never from the JSON Schema spec alone.
/// </summary>
public class AgentOutputSchemaDraft07Tests
{
    private static List<AgentOutputSchemaDraft07.SchemaViolation> Validate(string json)
    {
        JsonObject schema = Assert.IsType<JsonObject>(JsonNode.Parse(json));
        return AgentOutputSchemaDraft07.Validate(schema);
    }

    private static AgentOutputSchemaDraft07.SchemaViolation Single(string json)
    {
        return Assert.Single(Validate(json));
    }

    [Fact]
    public void RealisticExtractionSchema_IsAccepted()
    {
        Assert.Empty(Validate("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["status", "line_items"],
              "properties": {
                "status": { "type": "string", "enum": ["ok", "input_missing"] },
                "line_items": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": { "Net Sales": { "$ref": "#/$defs/periodValues" } }
                },
                "caveats": { "type": "array", "items": { "type": "string" } }
              },
              "$defs": {
                "periodValues": {
                  "type": "object",
                  "additionalProperties": { "type": ["number", "null"] }
                }
              }
            }
            """));
    }

    [Fact]
    public void EmptyObject_IsAccepted()
    {
        Assert.Empty(Validate("{}"));
    }

    [Fact]
    public void BooleanSubschemas_AreAccepted()
    {
        Assert.Empty(Validate("""{"type":"object","properties":{"a":true,"b":false}}"""));
    }

    [Fact]
    public void Draft07DollarSchema_WithAndWithoutFragment_IsAccepted()
    {
        Assert.Empty(Validate("""{"$schema":"http://json-schema.org/draft-07/schema#","type":"object"}"""));
        Assert.Empty(Validate("""{"$schema":"http://json-schema.org/draft-07/schema","type":"object"}"""));
    }

    [Fact]
    public void MetadataKeywords_TakeAnyValueTheSdkAccepts()
    {
        Assert.Empty(Validate("""
            {
              "title": "Spread",
              "description": "The extraction",
              "default": {"anything": true},
              "examples": [1, "two"],
              "deprecated": "not-a-boolean",
              "writeOnly": "not-a-boolean",
              "format": "no-such-format-is-fine"
            }
            """));
    }

    [Fact]
    public void UnknownRootKeyword_IsRejectedAtRoot()
    {
        AgentOutputSchemaDraft07.SchemaViolation violation =
            Single("""{"type":"object","propertys":{"a":{"type":"string"}}}""");

        Assert.Equal("#", violation.Path);
        Assert.Contains("unknown keyword \"propertys\"", violation.Message);
    }

    [Fact]
    public void UnknownNestedKeyword_CarriesTheOffendingPath()
    {
        AgentOutputSchemaDraft07.SchemaViolation violation =
            Single("""{"type":"object","properties":{"a":{"type":"string","minmumLength":3}}}""");

        Assert.Equal("#/properties/a", violation.Path);
        Assert.Contains("unknown keyword \"minmumLength\"", violation.Message);
    }

    [Theory]
    [InlineData("""{"type":"array","prefixItems":[{"type":"string"}]}""", "prefixItems")]
    [InlineData("""{"type":"object","dependentRequired":{"a":["b"]}}""", "dependentRequired")]
    [InlineData("""{"type":"object","unevaluatedProperties":false}""", "unevaluatedProperties")]
    public void Draft202012OnlyKeywords_AreUnknownToTheSdk(string json, string keyword)
    {
        AgentOutputSchemaDraft07.SchemaViolation violation = Single(json);

        Assert.Contains($"unknown keyword \"{keyword}\"", violation.Message);
    }

    [Fact]
    public void Draft202012DollarSchema_IsRejectedNamingTheEnforcedDraft()
    {
        AgentOutputSchemaDraft07.SchemaViolation violation =
            Single("""{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object"}""");

        Assert.Equal("#/$schema", violation.Path);
        Assert.Contains("draft-07", violation.Message);
    }

    [Fact]
    public void LegacyIdKeyword_IsRejectedPointingAtDollarId()
    {
        AgentOutputSchemaDraft07.SchemaViolation violation = Single("""{"id":"x","type":"object"}""");

        Assert.Equal("#/id", violation.Path);
        Assert.Contains("$id", violation.Message);
    }

    [Theory]
    [InlineData("""{"type":"objct"}""", "#/type")]
    [InlineData("""{"type":["object","objct"]}""", "#/type")]
    [InlineData("""{"type":[]}""", "#/type")]
    [InlineData("""{"type":["object","object"]}""", "#/type")]
    [InlineData("""{"type":3}""", "#/type")]
    public void InvalidTypeValues_AreRejectedAtTheirPath(string json, string expectedPath)
    {
        Assert.Equal(expectedPath, Single(json).Path);
    }

    [Theory]
    [InlineData("""{"type":"object","required":"name"}""")]
    [InlineData("""{"type":"object","required":[3]}""")]
    [InlineData("""{"type":"object","required":["a","a"]}""")]
    public void InvalidRequiredShapes_AreRejected(string json)
    {
        Assert.Equal("#/required", Single(json).Path);
    }

    [Theory]
    [InlineData("""{"enum":[]}""")]
    [InlineData("""{"enum":"abc"}""")]
    [InlineData("""{"enum":[1,1]}""")]
    public void InvalidEnumShapes_AreRejected(string json)
    {
        Assert.Equal("#/enum", Single(json).Path);
    }

    [Theory]
    [InlineData("""{"type":"number","multipleOf":0}""", "#/multipleOf")]
    [InlineData("""{"type":"number","multipleOf":-2}""", "#/multipleOf")]
    [InlineData("""{"type":"string","maxLength":-1}""", "#/maxLength")]
    [InlineData("""{"type":"string","minLength":"3"}""", "#/minLength")]
    [InlineData("""{"type":"string","minLength":1.5}""", "#/minLength")]
    [InlineData("""{"type":"number","exclusiveMaximum":true,"maximum":3}""", "#/exclusiveMaximum")]
    [InlineData("""{"type":"number","maximum":"3"}""", "#/maximum")]
    public void InvalidNumericKeywordShapes_AreRejected(string json, string expectedPath)
    {
        Assert.Equal(expectedPath, Single(json).Path);
    }

    [Theory]
    [InlineData("""{"type":"array","uniqueItems":"yes"}""", "#/uniqueItems")]
    [InlineData("""{"type":"string","readOnly":"yes"}""", "#/readOnly")]
    [InlineData("""{"type":"string","nullable":"yes"}""", "#/nullable")]
    [InlineData("""{"title":3}""", "#/title")]
    [InlineData("""{"description":3}""", "#/description")]
    [InlineData("""{"$comment":3}""", "#/$comment")]
    [InlineData("""{"format":3}""", "#/format")]
    [InlineData("""{"$id":3}""", "#/$id")]
    public void InvalidScalarKeywordShapes_AreRejected(string json, string expectedPath)
    {
        Assert.Equal(expectedPath, Single(json).Path);
    }

    [Theory]
    [InlineData("""{"type":"string","pattern":"(["}""", "#/pattern")]
    [InlineData("""{"type":"object","patternProperties":{"([":{"type":"string"}}}""", "#/patternProperties")]
    public void UncompilableRegexes_AreRejected(string json, string expectedPath)
    {
        Assert.Equal(expectedPath, Single(json).Path);
    }

    [Theory]
    [InlineData("""{"type":"object","properties":3}""", "#/properties")]
    [InlineData("""{"type":"object","properties":{"a":3}}""", "#/properties/a")]
    [InlineData("""{"definitions":{"x":3}}""", "#/definitions/x")]
    [InlineData("""{"$defs":{"x":3}}""", "#/$defs/x")]
    [InlineData("""{"type":"object","additionalProperties":3}""", "#/additionalProperties")]
    [InlineData("""{"type":"array","items":[3]}""", "#/items/0")]
    [InlineData("""{"type":"array","items":[]}""", "#/items")]
    [InlineData("""{"allOf":[]}""", "#/allOf")]
    [InlineData("""{"allOf":[3]}""", "#/allOf/0")]
    [InlineData("""{"type":"object","dependencies":{"a":3}}""", "#/dependencies/a")]
    public void InvalidSubschemaShapes_AreRejectedAtTheirPath(string json, string expectedPath)
    {
        Assert.Equal(expectedPath, Single(json).Path);
    }

    [Fact]
    public void DependenciesAcceptSchemaAndStringArrayForms()
    {
        Assert.Empty(Validate("""
            {"type":"object","dependencies":{"a":["b"],"c":{"required":["d"]}}}
            """));
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"a":{"$ref":"#/$defs/missing"}}}""")]
    [InlineData("""{"type":"object","properties":{"a":{"$ref":"https://example.com/x.json"}}}""")]
    [InlineData("""{"type":"object","properties":{"a":{"$ref":3}}}""")]
    public void UnresolvableRefs_AreRejected(string json)
    {
        Assert.Equal("#/properties/a/$ref", Single(json).Path);
    }

    [Fact]
    public void ResolvableLocalRefs_IncludingEscapedPointerSegments_AreAccepted()
    {
        Assert.Empty(Validate("""
            {
              "type": "object",
              "properties": {
                "a": { "$ref": "#/definitions/a~1b" },
                "b": { "$ref": "#" }
              },
              "definitions": { "a/b": { "type": "string" } }
            }
            """));
    }

    [Fact]
    public void ViolationsInsideRefTargets_AreStillFound()
    {
        AgentOutputSchemaDraft07.SchemaViolation violation = Single("""
            {
              "type": "object",
              "properties": { "a": { "$ref": "#/$defs/x" } },
              "$defs": { "x": { "type": "string", "minLegth": 1 } }
            }
            """);

        Assert.Equal("#/$defs/x", violation.Path);
        Assert.Contains("unknown keyword \"minLegth\"", violation.Message);
    }

    [Fact]
    public void EveryViolationIsReported_NotJustTheFirst()
    {
        List<AgentOutputSchemaDraft07.SchemaViolation> violations = Validate("""
            {"type":"objct","propertys":{},"required":"name"}
            """);

        Assert.Equal(3, violations.Count);
    }

    [Fact]
    public void ANumericKeywordPastTheDecimalRange_IsAViolationNotAnOverflow()
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["value"] = new JsonObject
                {
                    ["type"] = "number",
                    ["maximum"] = 1e300,
                },
            },
        };

        AgentOutputSchemaDraft07.SchemaViolation violation =
            Assert.Single(AgentOutputSchemaDraft07.Validate(schema));
        Assert.Equal("#/properties/value/maximum", violation.Path);
    }

    [Fact]
    public void SchemaBuiltInMemory_NumericKeywordsValidateLikeParsedOnes()
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["evidence"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = 5L,
                },
                ["score"] = new JsonObject { ["type"] = "number", ["maximum"] = 10.5 },
            },
        };

        Assert.Empty(AgentOutputSchemaDraft07.Validate(schema));
    }
}
