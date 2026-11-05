using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Common.Tests.Serialization;

public class JsonTextTests
{
    private const string PRECISE_DATE = "2024-03-12T10:15:00.000+02:00";

    [Fact]
    public void Parse_KeepsADateAsTheTextItWasWrittenAs()
    {
        JToken token = JsonText.Parse($$"""{"at":"{{PRECISE_DATE}}"}""");

        Assert.Equal(JTokenType.String, token["at"]!.Type);
        Assert.Equal($$"""{"at":"{{PRECISE_DATE}}"}""", token.ToString(Formatting.None));
    }

    [Fact]
    public void Parse_RejectsContentAfterTheValue()
    {
        _ = Assert.Throws<JsonReaderException>(() => JsonText.Parse("""{"a":1} {"b":2}"""));
    }

    [Fact]
    public void Load_KeepsTheDateAsText_AndGivesTheReaderBackWithItsOwnDateHandling()
    {
        using var reader = new JsonTextReader(new StringReader($$"""[{"at":"{{PRECISE_DATE}}"},"{{PRECISE_DATE}}"]"""));
        _ = reader.Read();
        _ = reader.Read();

        JObject loaded = JsonText.LoadObject(reader);
        _ = reader.Read();

        Assert.Equal(JTokenType.String, loaded["at"]!.Type);
        Assert.Equal(DateParseHandling.DateTime, reader.DateParseHandling);
        Assert.Equal(JsonToken.Date, reader.TokenType);
    }

    [Fact]
    public void LoadObject_RefusesAValueThatIsNotAnObject()
    {
        using var reader = new JsonTextReader(new StringReader("[1,2]"));
        _ = reader.Read();

        _ = Assert.Throws<JsonReaderException>(() => JsonText.LoadObject(reader));
    }
}
