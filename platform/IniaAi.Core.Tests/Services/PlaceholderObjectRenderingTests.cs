using IniaAi.Core.Services;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace IniaAi.Core.Tests.Services;

/// <summary>
/// How a placeholder renders a JSON sub-path that selects an object or array
/// (<c>{tag.files.rows}</c>): letters of every script stay as written, HTML-sensitive characters,
/// controls and bidirectional overrides stay escaped, and the output always parses back to the
/// value it selected.
/// </summary>
public class PlaceholderObjectRenderingTests
{
    private const string PATH = "o";

    private static readonly char[] _htmlSensitive = ['<', '>', '&', '\''];
    private static readonly char[] _bidiControls =
        ['\u202A', '\u202B', '\u202C', '\u202D', '\u202E', '\u2066', '\u2067', '\u2068', '\u2069'];

    private readonly JsonStringReplacerStrategy _strategy = new();

    private string Render(string json)
    {
        return _strategy.ReplacePlaceholders(PATH, json, embeddedMode: false);
    }

    public static TheoryData<string, string, string?, string> Values => new()
    {
        // label, JSON literal for the value, text that must appear raw, characters that must not appear raw
        { "plain ASCII", "\"plain text 123\"", "plain text 123", "" },
        { "Arabic", "\"حوالة واردة من شركة الأفق\"", "حوالة واردة من شركة الأفق", "" },
        { "Hebrew", "\"שלום עולם\"", "שלום עולם", "" },
        { "Cyrillic", "\"Иван Петров\"", "Иван Петров", "" },
        { "CJK", "\"銀行取引明細\"", "銀行取引明細", "" },
        { "accented Latin", "\"Café crème, Ångström\"", "Café crème, Ångström", "" },
        { "emoji surrogate pair", "\"ok 😀 done\"", "ok ", "😀" },
        { "Arabic letter mark and RLM", "\"\\u061C-1,250.000 \\u200F\"", "-1,250.000", "\u061C\u200F" },
        { "zero-width space, soft hyphen and BOM", "\"a\\u200Bb\\u00ADd\\uFEFF\"", null, "\u200B\u00AD\uFEFF" },
        { "Persian with zero-width non-joiner", "\"می\\u200Cخواهم\"", "می\u200Cخواهم", "" },
        { "control characters", "\"tab\\there\\nnew\\u0001\\u007F\\u0085\"", "tab", "\t\n\u0001\u007F\u0085" },
        { "line and paragraph separators", "\"a\\u2028b\\u2029c\"", null, "\u2028\u2029" },
        { "quotes and backslashes", "\"say \\\"hi\\\" C:\\\\path\"", "say ", "" },
        { "HTML-sensitive", "\"<script>alert('x')</script> & 1+1\"", "script", "<>&'" },
        { "bidi override", "\"abc\\u202Edef\\u2066ghi\\u2069\"", "abc", "\u202E\u2066\u2069" },
        { "integer", "42", "42", "" },
        { "decimal keeps its text", "1250.10", "1250.10", "" },
        { "negative exponent", "-3.5e-2", "-3.5e-2", "" },
        { "true", "true", "true", "" },
        { "false", "false", "false", "" },
        { "null", "null", "null", "" },
        { "nested object", "{\"a\":{\"b\":[1,{\"c\":\"مدين\"}]}}", "مدين", "" },
        { "nested array", "[[\"دائن\",[2,3]],[]]", "دائن", "" },
        { "empty object", "{}", "{}", "" },
        { "empty array", "[]", "[]", "" },
    };

    [Theory]
    [MemberData(nameof(Values))]
    public void ObjectContainingTheValue_IsValidJson_RoundTrips_AndEscapesOnlyWhatIsUnsafe(
        string label,
        string valueJson,
        string? rawText,
        string mustBeEscaped)
    {
        string json = $"{{\"{PATH}\":{{\"v\":{valueJson},\"arr\":[{valueJson}]}}}}";

        string rendered = Render(json);

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(json)![PATH], JsonNode.Parse(rendered)),
            $"{label}: '{rendered}' does not round-trip");
        AssertEscaping(label, rendered, rawText, mustBeEscaped);
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void ArrayContainingTheValue_IsValidJson_RoundTrips_AndEscapesOnlyWhatIsUnsafe(
        string label,
        string valueJson,
        string? rawText,
        string mustBeEscaped)
    {
        string json = $"{{\"{PATH}\":[{valueJson},{{\"k\":{valueJson}}}]}}";

        string rendered = Render(json);

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(json)![PATH], JsonNode.Parse(rendered)),
            $"{label}: '{rendered}' does not round-trip");
        AssertEscaping(label, rendered, rawText, mustBeEscaped);
    }

    [Fact]
    public void EveryBmpLetter_StaysRaw_AndEveryFormatCharacter_IsEscaped()
    {
        var letters = new StringBuilder();
        var formats = new StringBuilder();
        for (int code = 0x80; code < 0xD800; code++)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(code);
            if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark)
            {
                _ = letters.Append((char)code);
            }
            else if (category == UnicodeCategory.Format)
            {
                _ = formats.Append((char)code);
            }
        }

        string json = new JsonObject
        {
            [PATH] = new JsonObject { ["letters"] = letters.ToString(), ["formats"] = formats.ToString() },
        }.ToJsonString();

        string rendered = Render(json);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json)![PATH], JsonNode.Parse(rendered)));
        AssertEscaping("formats", rendered, rawText: null, formats.ToString().Replace("\u200C", "").Replace("\u200D", ""));
        // The encoder still escapes code points its own global block list names (unassigned or
        // reserved ones), and that list changes between releases, so only the share is pinned.
        int rawLetters = letters.ToString().Count(letter => rendered.Contains(letter, StringComparison.Ordinal));
        Assert.True(rawLetters > letters.Length * 0.99, $"only {rawLetters} of {letters.Length} BMP letters stayed raw");
    }

    private static void AssertEscaping(string label, string rendered, string? rawText, string mustBeEscaped)
    {
        Assert.True(rendered.IndexOfAny(_htmlSensitive) < 0, $"{label}: HTML-sensitive character raw in '{rendered}'");
        Assert.True(rendered.IndexOfAny(_bidiControls) < 0, $"{label}: bidi override raw in '{rendered}'");
        Assert.DoesNotContain(rendered, char.IsControl);
        foreach (char unsafeCharacter in mustBeEscaped)
        {
            Assert.True(
                rendered.IndexOf(unsafeCharacter) < 0,
                $"{label}: U+{(int)unsafeCharacter:X4} raw in '{rendered}'");
        }

        if (rawText is not null)
        {
            Assert.Contains(rawText, rendered, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Captured from develop before the encoder change (the default <c>ToJsonString()</c>): an
    /// object of ASCII text renders byte for byte as it did.
    /// </summary>
    [Theory]
    [InlineData(
        """{"a":1,"b":"plain text","c":[true,false,null],"d":{"e":-3.50}}""",
        """{"a":1,"b":"plain text","c":[true,false,null],"d":{"e":-3.50}}""")]
    [InlineData(
        """{"q":"say \"hi\" \\ back","plus":"1+1","tags":"<b>&'"}""",
        """{"q":"say \u0022hi\u0022 \\ back","plus":"1\u002B1","tags":"\u003Cb\u003E\u0026\u0027"}""")]
    [InlineData("""{"ctl":"tab\there\nnew"}""", """{"ctl":"tab\there\nnew"}""")]
    [InlineData("[]", "[]")]
    [InlineData("{}", "{}")]
    public void AsciiOnlyObject_RendersExactlyAsBeforeTheChange(string value, string expectedFromDevelop)
    {
        string rendered = Render($"{{\"{PATH}\":{value}}}");

        Assert.Equal(expectedFromDevelop, rendered);
    }

    [Fact]
    public void LeafString_IsReturnedAsWritten_HtmlAndBidiIncluded()
    {
        string leaf = _strategy.ReplacePlaceholders(
            "o.memo", "{\"o\":{\"memo\":\"<b>حوالة</b> & \\u202E\"}}", embeddedMode: false);

        Assert.Equal("<b>حوالة</b> & \u202E", leaf);
    }

    [Fact]
    public void JsonPayload_NestsARenderedObjectIntact_AndEscapesItInsideAString()
    {
        string rendered = Render("{\"o\":{\"name\":\"شركة \\\"الأفق\\\"\",\"note\":\"a\\\\b\"}}");
        var props = new WebApiCallProperties
        {
            RequestPayloadTemplate = "{\"data\": {src.o}, \"text\": \"{src.o}\"}",
            HttpMethod = "POST",
            ContentType = "application/json",
        };

        props.SetExecutionState(true, new Dictionary<string, string?> { ["src.o"] = rendered });

        JsonNode payload = JsonNode.Parse(props.ExecutionState!.RequestPayload!)!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(rendered), payload["data"]));
        Assert.Equal(rendered, payload["text"]!.GetValue<string>());
    }

    [Fact]
    public void Url_PercentEncodesTheRenderedObject()
    {
        string rendered = Render("{\"o\":{\"name\":\"حوالة\"}}");
        var props = new WebApiCallProperties
        {
            UrlTemplate = "https://api.example.com/search?q={src.o}",
            HttpMethod = "GET",
        };

        props.SetExecutionState(true, new Dictionary<string, string?> { ["src.o"] = rendered });

        string url = props.ExecutionState!.Url!;
        Assert.True(Ascii.IsValid(url));
        Assert.Equal(rendered, Uri.UnescapeDataString(url["https://api.example.com/search?q=".Length..]));
    }

    [Fact]
    public void Header_CarriesARenderedObjectInTheAsciiFormItAlwaysHad()
    {
        const string SOURCE = "{\"o\":{\"name\":\"حوالة Café\",\"n\":1}}";
        string rendered = Render(SOURCE);
        var props = new WebApiCallProperties
        {
            HttpMethod = "GET",
            AcceptHeader = "{src.o}",
            AdditionalHeaders = new Dictionary<string, string> { ["X-Filter"] = "filter={src.o}" },
        };

        props.SetExecutionState(true, new Dictionary<string, string?> { ["src.o"] = rendered });

        string before = JsonNode.Parse(SOURCE)![PATH]!.ToJsonString();
        Assert.Equal("filter=" + before, props.ExecutionState!.AdditionalHeaders["X-Filter"]);
        Assert.Equal(before, props.ExecutionState.AcceptHeader);
        Assert.True(Ascii.IsValid(props.ExecutionState.AdditionalHeaders["X-Filter"]));
    }

    [Fact]
    public void Header_LeavesALeafValueAsItWas()
    {
        string leaf = _strategy.ReplacePlaceholders("o.name", "{\"o\":{\"name\":\"Café\"}}", embeddedMode: false);
        var props = new WebApiCallProperties
        {
            AdditionalHeaders = new Dictionary<string, string> { ["X-Name"] = "{src.name}" },
        };

        props.SetExecutionState(true, new Dictionary<string, string?> { ["src.name"] = leaf });

        Assert.Equal("Café", props.ExecutionState!.AdditionalHeaders["X-Name"]);
    }

    [Fact]
    public void HeaderControlCheck_StillRejectsALeafWithAControlCharacter()
    {
        string leaf = _strategy.ReplacePlaceholders("o.v", "{\"o\":{\"v\":\"a\\r\\nb\"}}", embeddedMode: false);
        var props = new WebApiCallProperties
        {
            AdditionalHeaders = new Dictionary<string, string> { ["X-Note"] = "{src.v}" },
        };

        _ = Assert.Throws<InvalidOperationException>(() =>
            props.SetExecutionState(true, new Dictionary<string, string?> { ["src.v"] = leaf }));
    }
}
