using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentOutputSchemasTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankInput_IsAValidNoSchema(string? blank)
    {
        ErrorOr<JsonObject?> parsed = AgentOutputSchemas.Parse(blank);

        Assert.False(parsed.IsError);
        Assert.Null(parsed.Value);
    }

    [Fact]
    public void Parse_ObjectSchema_ReturnsTheObject()
    {
        ErrorOr<JsonObject?> parsed = AgentOutputSchemas.Parse(
            """{"type":"object","properties":{"MarkerCode":{"type":"string"}}}""");

        Assert.False(parsed.IsError);
        Assert.NotNull(parsed.Value);
        Assert.NotNull(parsed.Value["properties"]?["MarkerCode"]);
    }

    [Fact]
    public void Parse_TextThatIsNotJson_ErrorsCarryingTheParserException()
    {
        ErrorOr<JsonObject?> parsed = AgentOutputSchemas.Parse("{not json");

        Assert.True(parsed.IsError);
        Assert.Equal(AgentOutputSchemas.ERROR_INVALID_JSON, parsed.FirstError.Code);
        Assert.NotNull(parsed.FirstError.Metadata);
        _ = Assert.IsAssignableFrom<JsonException>(
            parsed.FirstError.Metadata[AgentOutputSchemas.EXCEPTION_METADATA_KEY]);
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    public void Parse_NonObjectJson_ErrorsAsNotAnObject(string nonObject)
    {
        ErrorOr<JsonObject?> parsed = AgentOutputSchemas.Parse(nonObject);

        Assert.True(parsed.IsError);
        Assert.Equal(AgentOutputSchemas.ERROR_NOT_AN_OBJECT, parsed.FirstError.Code);
    }
}
