using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class JsonIndenterTests
{
    [Fact]
    public void AMinifiedObject_ShowsOneFieldPerLine()
    {
        string indented = JsonIndenter.Indent("""{"name":"Acme","tags":["a","b"],"meta":{"region":"EU","listed":true,"parent":null}}""");

        Assert.Equal(
            """
            {
              "name": "Acme",
              "tags": [
                "a",
                "b"
              ],
              "meta": {
                "region": "EU",
                "listed": true,
                "parent": null
              }
            }
            """,
            indented);
    }

    [Fact]
    public void AMinifiedArray_ShowsOneItemPerLine()
    {
        string indented = JsonIndenter.Indent("""[{"id":1},{"id":2}]""");

        Assert.Equal(
            """
            [
              {
                "id": 1
              },
              {
                "id": 2
              }
            ]
            """,
            indented);
    }

    [Fact]
    public void AnEmptyObjectOrArray_StaysOnItsLine()
    {
        string indented = JsonIndenter.Indent("""{"rows":[ ],"meta":{ }}""");

        Assert.Equal(
            """
            {
              "rows": [],
              "meta": {}
            }
            """,
            indented);
    }

    [Fact]
    public void JsonWithBlankLinesAround_IsIndented()
    {
        Assert.Equal("{\n  \"a\": 1\n}", JsonIndenter.Indent("\n  {\"a\":1}\n"));
    }

    [Fact]
    public void AnObjectInAWrappingCodeFence_KeepsTheFenceAndIndentsTheJson()
    {
        string indented = JsonIndenter.Indent("```json\n{\"total\":3,\"currency\":\"USD\"}\n```");

        Assert.Equal(
            """
            ```json
            {
              "total": 3,
              "currency": "USD"
            }
            ```
            """,
            indented);
    }

    [Fact]
    public void Numbers_KeepTheirWrittenText()
    {
        string indented = JsonIndenter.Indent("""{"rate":1.10,"id":12345678901234567890,"small":1E-7,"neg":-0.0}""");

        Assert.Equal(
            """
            {
              "rate": 1.10,
              "id": 12345678901234567890,
              "small": 1E-7,
              "neg": -0.0
            }
            """,
            indented);
    }

    [Fact]
    public void StringsAndKeys_KeepTheirEscapesAndOrder()
    {
        const string ORIGINAL = """{"z":"café \"quoted\" a\\b <tag> {not:json,}","a":"line\nbreak","é":"\/"}""";

        string indented = JsonIndenter.Indent(ORIGINAL);

        Assert.Equal(
            """
            {
              "z": "café \"quoted\" a\\b <tag> {not:json,}",
              "a": "line\nbreak",
              "é": "\/"
            }
            """,
            indented);
    }

    [Fact]
    public void AStringEndingInAnEscapedBackslash_EndsWhereItsQuoteDoes()
    {
        string indented = JsonIndenter.Indent("""{"path":"C:\\","next":1}""");

        Assert.Equal(
            """
            {
              "path": "C:\\",
              "next": 1
            }
            """,
            indented);
    }

    [Fact]
    public void AFenceWithWindowsLineEnds_KeepsTheFenceAndIndentsTheJson()
    {
        string indented = JsonIndenter.Indent("```json  \r\n{\"a\":[1,2]}\r\n```");

        Assert.Equal("```json\n{\n  \"a\": [\n    1,\n    2\n  ]\n}\n```", indented);
    }

    [Fact]
    public void JsonCutThroughAnEmoji_ShowsAsStored()
    {
        string stored = "{\"label\":\"😀 ok\"}"[..11] + "\"}";

        Assert.Equal(stored, JsonIndenter.Indent(stored));
    }

    [Fact]
    public void AnOutputOverTheSizeCap_ShowsAsStored()
    {
        string stored = $"[{string.Join(',', Enumerable.Repeat('1', 500_001))}]";

        Assert.Same(stored, JsonIndenter.Indent(stored));
    }

    [Fact]
    public void IndentedJson_IndentsTheSameWay()
    {
        string once = JsonIndenter.Indent("""{"a":[1,{"b":2}],"c":{}}""");

        Assert.Equal(once, JsonIndenter.Indent(once));
    }

    [Theory]
    [InlineData("""{"a":1,}""")]
    [InlineData("""{"a":1""")]
    [InlineData("""[1, 2] trailing""")]
    [InlineData("""{'a': 1}""")]
    public void InvalidJson_ShowsAsStored(string stored)
    {
        Assert.Equal(stored, JsonIndenter.Indent(stored));
    }

    [Theory]
    [InlineData("The total is 3 USD.")]
    [InlineData("# Heading\n\n- item")]
    [InlineData("<p>{\"a\":1}</p>")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("  ")]
    [InlineData("")]
    public void AnythingButAJsonObjectOrArray_ShowsAsStored(string stored)
    {
        Assert.Equal(stored, JsonIndenter.Indent(stored));
    }

    [Theory]
    [InlineData("```python\nprint({\"a\": 1})\n```")]
    [InlineData("```json\n\n```")]
    public void AFenceAroundSomethingElse_ShowsAsStored(string stored)
    {
        Assert.Equal(stored, JsonIndenter.Indent(stored));
    }
}
