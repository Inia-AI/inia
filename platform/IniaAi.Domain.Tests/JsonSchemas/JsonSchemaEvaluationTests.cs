using IniaAi.Domain.JsonSchemas;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.JsonSchemas;

public class JsonSchemaEvaluationTests
{
    private const string SCHEMA_WITHOUT_DIALECT = """
    {
      "type": "object",
      "required": ["name", "age"],
      "properties": {
        "name": { "type": "string" },
        "age": { "type": "integer" }
      },
      "additionalProperties": false
    }
    """;

    [Fact]
    public void FindViolations_InstanceMatchesSchema_ReturnsNone()
    {
        CompiledJsonSchema schema = JsonSchemaEvaluation.CompileSchema(SCHEMA_WITHOUT_DIALECT);

        IReadOnlyList<string> violations = JsonSchemaEvaluation.FindViolations(
            schema,
            JToken.Parse("""{ "name": "alice", "age": 30 }"""));

        Assert.Empty(violations);
    }

    [Fact]
    public void FindViolations_SchemaWithoutDialectKeyword_StillEnforcesConstraints()
    {
        CompiledJsonSchema schema = JsonSchemaEvaluation.CompileSchema(SCHEMA_WITHOUT_DIALECT);

        IReadOnlyList<string> violations = JsonSchemaEvaluation.FindViolations(
            schema,
            JToken.Parse("""{ "name": 42 }"""));

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void FindViolations_SeveralBrokenConstraints_ReportsOneMessagePerViolation()
    {
        CompiledJsonSchema schema = JsonSchemaEvaluation.CompileSchema(SCHEMA_WITHOUT_DIALECT);

        IReadOnlyList<string> violations = JsonSchemaEvaluation.FindViolations(
            schema,
            JToken.Parse("""{ "age": "thirty", "unexpected": true }"""));

        Assert.True(violations.Count > 1, $"expected several violations, got: {string.Join(" | ", violations)}");
        Assert.All(violations, v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    [Fact]
    public void FindViolations_ViolationInsideNestedObject_NamesTheLocation()
    {
        CompiledJsonSchema schema = JsonSchemaEvaluation.CompileSchema("""
        {
          "type": "object",
          "properties": {
            "holder": {
              "type": "object",
              "properties": { "amount": { "type": "number" } }
            }
          }
        }
        """);

        IReadOnlyList<string> violations = JsonSchemaEvaluation.FindViolations(
            schema,
            JToken.Parse("""{ "holder": { "amount": "not a number" } }"""));

        string all = string.Join(" | ", violations);
        Assert.Contains("amount", all);
    }

    [Fact]
    public void CompileSchema_MalformedSchemaText_Throws()
    {
        _ = Assert.ThrowsAny<Exception>(() => JsonSchemaEvaluation.CompileSchema("{not a schema"));
    }

    [Fact]
    public void FindViolations_EnumConstraint_RejectsValueOutsideTheSet()
    {
        CompiledJsonSchema schema = JsonSchemaEvaluation.CompileSchema("""
        { "type": "object", "properties": { "colour": { "enum": ["red", "green"] } } }
        """);

        IReadOnlyList<string> violations = JsonSchemaEvaluation.FindViolations(
            schema,
            JToken.Parse("""{ "colour": "blue" }"""));

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void FindViolations_PropertyTheSchemaForbids_SaysThePropertyIsNotAllowed()
    {
        var schema = JsonSchemaEvaluation.CompileSchema(SCHEMA_WITHOUT_DIALECT);

        IReadOnlyList<string> violations = JsonSchemaEvaluation.FindViolations(
            schema,
            JToken.Parse("""{ "name": "alice", "age": 30, "unexpected": true }"""));

        Assert.Equal(["/unexpected: the schema does not allow this property"], violations);
    }

    [Fact]
    public void FindViolations_TypeMismatchInsideAnObject_ReportsOnlyTheMismatch()
    {
        var schema = JsonSchemaEvaluation.CompileSchema(SCHEMA_WITHOUT_DIALECT);

        IReadOnlyList<JsonSchemaViolation> violations = JsonSchemaEvaluation.FindLeafViolations(
            schema,
            JToken.Parse("""{ "name": "alice", "age": "thirty" }"""));

        JsonSchemaViolation violation = Assert.Single(violations);
        Assert.Equal("/age", violation.Location);
        Assert.Contains("integer", violation.Message);
    }

    [Fact]
    public void FindViolations_ValueOutsideAnEnum_SaysItIsNotOneOfTheListedValues()
    {
        var schema = JsonSchemaEvaluation.CompileSchema("""
        { "type": "object", "properties": { "colour": { "enum": ["red", "green"] } } }
        """);

        IReadOnlyList<string> violations = JsonSchemaEvaluation.FindViolations(schema, JToken.Parse("""{ "colour": "blue" }"""));

        Assert.Equal(["/colour: the value is not one of the values the schema lists"], violations);
    }

    [Fact]
    public void FindViolations_FormatIsAnAnnotation_NotACheck()
    {
        var schema = JsonSchemaEvaluation.CompileSchema("""{ "type": "string", "format": "date" }""");

        Assert.Empty(JsonSchemaEvaluation.FindViolations(schema, JToken.Parse("\"not a date\"")));
    }

    [Fact]
    public void CompileSchema_ReferenceToAFile_IsNotRead()
    {
        string file = Path.Combine(Path.GetTempPath(), $"schema-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, """{ "type": "string" }""");

        try
        {
            _ = Assert.ThrowsAny<Exception>(() => JsonSchemaEvaluation.FindViolations(
                JsonSchemaEvaluation.CompileSchema($$"""{ "$ref": "{{new Uri(file).AbsoluteUri}}" }"""),
                JToken.Parse("42")));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void CompileSchema_TwoSchemasWithTheSameId_EachKeepsItsOwnRules()
    {
        var text = JsonSchemaEvaluation.CompileSchema("""{ "$id": "https://example.com/answer", "type": "string" }""");
        var number = JsonSchemaEvaluation.CompileSchema("""{ "$id": "https://example.com/answer", "type": "number" }""");

        Assert.Empty(JsonSchemaEvaluation.FindViolations(text, JToken.Parse("\"x\"")));
        Assert.Empty(JsonSchemaEvaluation.FindViolations(number, JToken.Parse("5")));
        Assert.NotEmpty(JsonSchemaEvaluation.FindViolations(number, JToken.Parse("\"x\"")));
    }
}
