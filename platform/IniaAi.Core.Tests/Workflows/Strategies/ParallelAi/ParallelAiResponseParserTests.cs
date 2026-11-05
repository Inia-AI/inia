using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Strategies.ParallelAi;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Tests.Workflows.Strategies.ParallelAi;

public class ParallelAiResponseParserTests
{
    private static string BuildBody(string contentJson, string basisJson)
    {
        return $$"""
            {
                "output": {
                    "content": {{contentJson}},
                    "basis": {{basisJson}}
                }
            }
            """;
    }

    private static string BasisEntry(string fieldPath, string citationsJson = "[]")
    {
        return $$"""{ "field": "{{fieldPath}}", "citations": {{citationsJson}} }""";
    }

    [Fact]
    public void TryParse_NullBody_ReturnsFalse()
    {
        bool parsed = ParallelAiResponseParser.TryParse(null, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_EmptyBody_ReturnsFalse()
    {
        bool parsed = ParallelAiResponseParser.TryParse(string.Empty, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_WhitespaceBody_ReturnsFalse()
    {
        bool parsed = ParallelAiResponseParser.TryParse("   \n\t  ", out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsFalse()
    {
        bool parsed = ParallelAiResponseParser.TryParse("not json at all", out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_MissingOutput_ReturnsFalse()
    {
        const string BODY = """
            {
                "run": { "status": "completed" }
            }
            """;

        bool parsed = ParallelAiResponseParser.TryParse(BODY, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_MissingOutputContent_ReturnsFalse()
    {
        const string BODY = """
            {
                "output": {
                    "basis": [
                        { "field": "foo", "citations": [] }
                    ]
                }
            }
            """;

        bool parsed = ParallelAiResponseParser.TryParse(BODY, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_MissingOutputBasis_ReturnsFalse()
    {
        const string BODY = """
            {
                "output": {
                    "content": { "executive_summary": "Some text." }
                }
            }
            """;

        bool parsed = ParallelAiResponseParser.TryParse(BODY, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_EmptyBasisArray_ReturnsFalse()
    {
        const string BODY = """
            {
                "output": {
                    "content": { "executive_summary": "Some text." },
                    "basis": []
                }
            }
            """;

        bool parsed = ParallelAiResponseParser.TryParse(BODY, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_BasisIsObjectNotArray_ReturnsFalse()
    {
        const string BODY = """
            {
                "output": {
                    "content": { "executive_summary": "Some text." },
                    "basis": { "not": "an array" }
                }
            }
            """;

        bool parsed = ParallelAiResponseParser.TryParse(BODY, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void Parse_TopLevelStringField_EmitsFactWithClaim()
    {
        string body = BuildBody(
            """{ "executive_summary": "Summary text." }""",
            $"[{BasisEntry("executive_summary")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Contains(result.Facts, f => f.Value == "Summary text.");
    }

    [Fact]
    public void Parse_NestedStringField_EmitsFactWithClaim()
    {
        string body = BuildBody(
            """{ "a": { "b": { "c": "Deep value." } } }""",
            $"[{BasisEntry("a.b.c")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Contains(result.Facts, f => f.Value == "Deep value.");
    }

    [Fact]
    public void Parse_ArrayIndexedField_EmitsFactWithClaim()
    {
        string body = BuildBody(
            """{ "items": [ { "name": "First" }, { "name": "Second" } ] }""",
            $"[{BasisEntry("items.1.name")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Contains(result.Facts, f => f.Value == "Second");
    }

    [Fact]
    public void Parse_DeepArrayPath_EmitsFactWithClaim()
    {
        string body = BuildBody(
            """{ "a": [ {"skip": 0}, {"skip": 1}, { "b": [ "p", "q", "r" ] } ] }""",
            $"[{BasisEntry("a.2.b.1")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Contains(result.Facts, f => f.Value == "q");
    }

    [Fact]
    public void Parse_NumericPropertyOnObject_ResolvesAsKey()
    {
        // Object property names that look like integers (e.g., "2024") must be
        // treated as property keys, not array indices.
        string body = BuildBody(
            """{ "yearly": { "2024": "Q4 figure." } }""",
            $"[{BasisEntry("yearly.2024")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Contains(result.Facts, f => f.Value == "Q4 figure.");
    }

    [Fact]
    public void Parse_NumericPropertyDeepInside_ResolvesAsKey()
    {
        string body = BuildBody(
            """{ "data": [ { "1": "first" }, { "1": "second" } ] }""",
            $"[{BasisEntry("data.1.1")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        // data → array, 1 → array index → second element {"1": "second"}, then "1" → property "second"
        Assert.Contains(result.Facts, f => f.Value == "second");
    }

    [Fact]
    public void Parse_MissingPropertySegment_EntrySkipped()
    {
        string body = BuildBody(
            """{ "a": { "b": "X" } }""",
            $"[{BasisEntry("a.nonexistent.c")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Parse_ArrayIndexOutOfRange_EntrySkipped()
    {
        string body = BuildBody(
            """{ "items": [ { "name": "only one" } ] }""",
            $"[{BasisEntry("items.5.name")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Parse_NegativeArrayIndex_EntrySkipped()
    {
        string body = BuildBody(
            """{ "items": [ { "name": "only one" } ] }""",
            $"[{BasisEntry("items.-1.name")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Parse_NonNumericSegmentOnArray_EntrySkipped()
    {
        string body = BuildBody(
            """{ "items": [ "a", "b" ] }""",
            $"[{BasisEntry("items.notanumber")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Parse_FieldResolvingToObject_EntrySkipped()
    {
        string body = BuildBody(
            """{ "obj": { "nested": "value" } }""",
            $"[{BasisEntry("obj")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Parse_FieldResolvingToNumber_EntrySkipped()
    {
        string body = BuildBody(
            """{ "count": 42 }""",
            $"[{BasisEntry("count")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Parse_FieldResolvingToNull_EntrySkipped()
    {
        string body = BuildBody(
            """{ "maybe": null }""",
            $"[{BasisEntry("maybe")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Parse_FieldResolvingToWhitespaceOnlyString_EntrySkipped()
    {
        string body = BuildBody(
            """{ "blank": "   \n\t  " }""",
            $"[{BasisEntry("blank")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Parse_SingleBasisEntry_EmitsOneBroadFactWithIsNumericalFalse()
    {
        string body = BuildBody(
            """{ "summary": "A single claim." }""",
            $"[{BasisEntry("summary")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Single(result.Facts);
        Fact fact = result.Facts[0];
        Assert.Equal("A single claim.", fact.Value);
        Assert.False(fact.IsNumerical);
    }

    [Fact]
    public void Parse_MultipleBasisEntries_EmitsOneFactPerEntryInOrder()
    {
        string body = BuildBody(
            """{ "a": "First claim.", "b": "Second claim.", "c": "Third claim." }""",
            $"[{BasisEntry("a")}, {BasisEntry("b")}, {BasisEntry("c")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Equal(3, result.Facts.Count);
        Assert.Equal("First claim.", result.Facts[0].Value);
        Assert.Equal("Second claim.", result.Facts[1].Value);
        Assert.Equal("Third claim.", result.Facts[2].Value);
    }

    [Fact]
    public void Parse_MixedEntries_SkipsInvalidKeepsValid()
    {
        string body = BuildBody(
            """{ "ok": "Valid claim.", "obj": { "nested": "thing" } }""",
            $"[{BasisEntry("obj")}, {BasisEntry("missing.path")}, {BasisEntry("ok")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Single(result.Facts);
        Assert.Equal("Valid claim.", result.Facts[0].Value);
    }

    [Fact]
    public void Parse_BasisEntryWithMissingField_Skipped()
    {
        string body = BuildBody(
            """{ "ok": "Valid." }""",
            """[{ "citations": [] }, { "field": "ok", "citations": [] }]""");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Single(result.Facts);
        Assert.Equal("Valid.", result.Facts[0].Value);
    }

    [Fact]
    public void Parse_BasisEntryWithBlankField_Skipped()
    {
        const string BLANK_ENTRY = """{ "field": "  ", "citations": [] }""";
        string body = BuildBody(
            """{ "ok": "Valid." }""",
            "[" + BLANK_ENTRY + ", " + BasisEntry("ok") + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Single(result.Facts);
        Assert.Equal("Valid.", result.Facts[0].Value);
    }

    [Fact]
    public void Parse_AllEntriesSkipped_ReturnsFalseAndNoFacts()
    {
        string body = BuildBody(
            """{ "obj": { "a": 1 } }""",
            $"[{BasisEntry("obj")}, {BasisEntry("missing")}, {BasisEntry("obj.a")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.False(parsed);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Parse_ClaimIsTrimmedOfSurroundingWhitespace()
    {
        string body = BuildBody(
            """{ "s": "  \n  Trimmed claim.\t  " }""",
            $"[{BasisEntry("s")}]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Single(result.Facts);
        Assert.Equal("Trimmed claim.", result.Facts[0].Value);
    }

    private static string Citation(string url, params string[] excerpts)
    {
        string excerptsJson = "[" + string.Join(", ", excerpts.Select(e => JsonEscape(e))) + "]";
        return $$"""{ "title": "T", "url": {{JsonEscape(url)}}, "excerpts": {{excerptsJson}} }""";
    }

    private static string JsonEscape(string s) => Newtonsoft.Json.JsonConvert.ToString(s);

    [Fact]
    public void Parse_OneCitationOneExcerpt_AttachesOneUrlSource()
    {
        string body = BuildBody(
            """{ "s": "Claim text." }""",
            "[" + BasisEntry("s", "[" + Citation("https://example.com/a", "excerpt text") + "]") + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Fact fact = result.Facts[0];
        Assert.Single(fact.Sources);
        UrlSource source = Assert.IsType<UrlSource>(fact.Sources[0]);
        Assert.Equal("https://example.com/a", source.Url);
        Assert.Equal("excerpt text", source.CitedText);
    }

    [Fact]
    public void Parse_MultipleCitationsDifferentUrls_OneSourcePerUrl()
    {
        string citations = "[" +
            Citation("https://example.com/a", "first") + ", " +
            Citation("https://example.com/b", "second") + "]";
        string body = BuildBody(
            """{ "s": "Claim." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Fact fact = result.Facts[0];
        Assert.Equal(2, fact.Sources.Count);
        IEnumerable<string> urls = fact.Sources.OfType<UrlSource>().Select(s => s.Url);
        Assert.Contains("https://example.com/a", urls);
        Assert.Contains("https://example.com/b", urls);
    }

    [Fact]
    public void Parse_CitationWithMultipleExcerpts_DedupesToSingleUrlSourceByUrl()
    {
        string citations = "[" + Citation("https://example.com/a", "first", "second", "third") + "]";
        string body = BuildBody(
            """{ "s": "Claim." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Fact fact = result.Facts[0];
        Assert.Single(fact.Sources);
        UrlSource source = Assert.IsType<UrlSource>(fact.Sources[0]);
        Assert.Equal("https://example.com/a", source.Url);
    }

    [Fact]
    public void Parse_CitationWithEmptyExcerptsArray_OneSourceWithEmptyCitedText()
    {
        string citations = "[" + Citation("https://example.com/a") + "]";
        string body = BuildBody(
            """{ "s": "Claim." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Fact fact = result.Facts[0];
        Assert.Single(fact.Sources);
        UrlSource source = Assert.IsType<UrlSource>(fact.Sources[0]);
        Assert.Equal("https://example.com/a", source.Url);
        Assert.Equal(string.Empty, source.CitedText);
    }

    [Fact]
    public void Parse_CitationWithMissingUrl_Skipped()
    {
        const string CITATION_NO_URL = """{ "title": "T", "excerpts": ["text"] }""";
        string body = BuildBody(
            """{ "s": "Claim." }""",
            "[" + BasisEntry("s", "[" + CITATION_NO_URL + "]") + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Empty(result.Facts[0].Sources);
    }

    [Fact]
    public void Parse_CitationWithEmptyUrl_Skipped()
    {
        const string CITATION_EMPTY_URL = """{ "title": "T", "url": "", "excerpts": ["text"] }""";
        string body = BuildBody(
            """{ "s": "Claim." }""",
            "[" + BasisEntry("s", "[" + CITATION_EMPTY_URL + "]") + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Empty(result.Facts[0].Sources);
    }

    [Fact]
    public void Parse_CitationWithWhitespaceUrl_Skipped()
    {
        const string CITATION_WS_URL = """{ "title": "T", "url": "   ", "excerpts": ["text"] }""";
        string body = BuildBody(
            """{ "s": "Claim." }""",
            "[" + BasisEntry("s", "[" + CITATION_WS_URL + "]") + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Empty(result.Facts[0].Sources);
    }

    [Fact]
    public void Parse_MixedCitationsSomeWithEmptyUrl_KeepsOnlyValidOnes()
    {
        string citations = "[" +
            Citation("https://example.com/keeps", "kept") + ", " +
            """{ "title": "T", "url": "", "excerpts": ["dropped"] }""" + ", " +
            Citation("https://example.com/also-keeps", "kept2") + "]";
        string body = BuildBody(
            """{ "s": "Claim." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        List<string> urls = [.. result.Facts[0].Sources.OfType<UrlSource>().Select(s => s.Url)];
        Assert.Equal(2, urls.Count);
        Assert.Contains("https://example.com/keeps", urls);
        Assert.Contains("https://example.com/also-keeps", urls);
    }

    [Fact]
    public void Parse_BasisEntryWithEmptyCitationsArray_BroadFactEmittedWithZeroSources()
    {
        string body = BuildBody(
            """{ "s": "Claim without sources." }""",
            "[" + BasisEntry("s", "[]") + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Fact fact = result.Facts[0];
        Assert.Equal("Claim without sources.", fact.Value);
        Assert.Empty(fact.Sources);
    }

    [Fact]
    public void Parse_BasisEntryWithMissingCitationsField_BroadFactEmittedWithZeroSources()
    {
        const string ENTRY_NO_CITATIONS = """{ "field": "s" }""";
        string body = BuildBody(
            """{ "s": "Claim." }""",
            "[" + ENTRY_NO_CITATIONS + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Fact fact = result.Facts[0];
        Assert.Empty(fact.Sources);
    }

    [Fact]
    public void Parse_CitationPreservesAttachmentOrderAcrossCitations()
    {
        string citations = "[" +
            Citation("https://example.com/first", "fx") + ", " +
            Citation("https://example.com/second", "sx") + ", " +
            Citation("https://example.com/third", "tx") + "]";
        string body = BuildBody(
            """{ "s": "Claim." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        List<string> urls = [.. result.Facts[0].Sources.OfType<UrlSource>().Select(s => s.Url)];
        Assert.Equal(
            ["https://example.com/first", "https://example.com/second", "https://example.com/third"],
            urls);
    }

    [Fact]
    public void Parse_ClaimWithOneNumericalValue_EmitsOneNumericalSubFact()
    {
        string citations = "[" + Citation("https://example.com/a", "revenue $12,278 million") + "]";
        string body = BuildBody(
            """{ "s": "Total revenue was $12,278 million." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        List<Fact> numerical = [.. result.Facts.Where(f => f.IsNumerical)];
        Assert.Single(numerical);
        Assert.Equal("$12,278 million", numerical[0].Value);
        Assert.Single(numerical[0].Sources);
        UrlSource src = Assert.IsType<UrlSource>(numerical[0].Sources[0]);
        Assert.Equal("https://example.com/a", src.Url);
    }

    [Fact]
    public void Parse_ClaimWithMultipleNumericalValues_EmitsOneSubFactPerValue()
    {
        string citations = "[" + Citation("https://example.com/a", "rev $6,238 million, ebitda $1,366 million") + "]";
        string body = BuildBody(
            """{ "s": "Revenue $6,238 million and EBITDA $1,366 million." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        List<string> numericalValues = [.. result.Facts.Where(f => f.IsNumerical).Select(f => f.Value)];
        Assert.Contains("$6,238 million", numericalValues);
        Assert.Contains("$1,366 million", numericalValues);
    }

    [Fact]
    public void Parse_NumericalSubFactSources_AreDistinctInstancesFromBroadFact()
    {
        string citations = "[" + Citation("https://example.com/a", "text with 530,000 in it") + "]";
        string body = BuildBody(
            """{ "s": "Serves 530,000 customers." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Fact broadFact = result.Facts.First(f => !f.IsNumerical);
        Fact numericalFact = result.Facts.First(f => f.IsNumerical);

        UrlSource broadSrc = Assert.IsType<UrlSource>(broadFact.Sources[0]);
        UrlSource numSrc = Assert.IsType<UrlSource>(numericalFact.Sources[0]);

        Assert.NotSame(broadSrc, numSrc);
        Assert.Equal(broadSrc.Url, numSrc.Url);
    }

    [Fact]
    public void Parse_ClaimWithNoNumericalValues_OnlyBroadFact()
    {
        string citations = "[" + Citation("https://example.com/a", "just prose") + "]";
        string body = BuildBody(
            """{ "s": "Just prose without any numbers." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Single(result.Facts);
        Assert.False(result.Facts[0].IsNumerical);
    }

    [Fact]
    public void Parse_NumericalSubFactWithMultipleCitations_InheritsAllUrlsThatGroundTheValue()
    {
        string citations = "[" +
            Citation("https://example.com/one", "mentions 40.3%") + ", " +
            Citation("https://example.com/two", "also 40.3%") + "]";
        string body = BuildBody(
            """{ "s": "Ownership at 40.3% of the joint venture." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Fact numerical = result.Facts.First(f => f.IsNumerical && f.Value == "40.3%");
        List<string> urls = [.. numerical.Sources.OfType<UrlSource>().Select(s => s.Url)];
        Assert.Contains("https://example.com/one", urls);
        Assert.Contains("https://example.com/two", urls);
    }

    [Fact]
    public void Parse_NumericalSubFact_DropsUrlSourcesWhoseExcerptDoesNotGroundTheValue()
    {
        // Two URLs: one excerpt grounds "$1.15 billion", the other grounds "180,000".
        // Each numerical sub-fact should only inherit the URL whose excerpt actually
        // contains its value — not every parent source.
        string citations = "[" +
            Citation("https://example.com/cost", "the project cost reached $1.15 billion total") + ", " +
            Citation("https://example.com/capacity", "with storage capacity of 180,000 cubic meters") + "]";
        string body = BuildBody(
            """{ "s": "The Panama LNG terminal cost over $1.15 billion to build, with storage of 180,000 cubic meters." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);

        Fact dollarFact = Assert.Single(result.Facts, f => f.IsNumerical && f.Value == "$1.15 billion");
        List<string> dollarUrls = [.. dollarFact.Sources.OfType<UrlSource>().Select(s => s.Url)];
        Assert.Equal(["https://example.com/cost"], dollarUrls);

        Fact capacityFact = Assert.Single(result.Facts, f => f.IsNumerical && f.Value == "180,000");
        List<string> capacityUrls = [.. capacityFact.Sources.OfType<UrlSource>().Select(s => s.Url)];
        Assert.Equal(["https://example.com/capacity"], capacityUrls);
    }

    [Fact]
    public void Parse_NumericalSubFact_DroppedEntirelyWhenNoExcerptGroundsTheValue()
    {
        // Neither excerpt contains "$2,510 million", literally or numerically. The
        // sub-fact should be dropped rather than emitted with all parent URLs attached.
        // The broad parent fact still preserves the data and its sources.
        string citations = "[" +
            Citation("https://example.com/argentina", "AES Argentina overview text") + ", " +
            Citation("https://example.com/elsalvador", "El Salvador electric service info") + "]";
        string body = BuildBody(
            """{ "s": "The segment generated $2,510 million in revenue." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);

        Fact broadFact = Assert.Single(result.Facts, f => !f.IsNumerical);
        Assert.Equal("The segment generated $2,510 million in revenue.", broadFact.Value);
        Assert.Equal(2, broadFact.Sources.Count);

        Assert.DoesNotContain(result.Facts, f => f.IsNumerical && f.Value == "$2,510 million");
    }

    [Fact]
    public void Parse_NumericalSubFact_GroundedByMultiplierStrippedExcerpt_KeepsOnlyMatchingSource()
    {
        // "$12,278 million" doesn't appear literally in either excerpt, but URL B has
        // "$12,278" (the multiplier-stripped form). That should ground via Step 2.
        string citations = "[" +
            Citation("https://example.com/unrelated", "generic boilerplate text") + ", " +
            Citation("https://example.com/figures", "Total Revenue: $12,278 (in millions)") + "]";
        string body = BuildBody(
            """{ "s": "Total revenue was $12,278 million." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);

        Fact revenueFact = Assert.Single(result.Facts, f => f.IsNumerical && f.Value == "$12,278 million");
        List<string> urls = [.. revenueFact.Sources.OfType<UrlSource>().Select(s => s.Url)];
        Assert.Equal(["https://example.com/figures"], urls);
    }

    [Fact]
    public void Parse_NumericalSubFact_GroundedByQuantityEquivalentExcerpt_KeepsOnlyMatchingSource()
    {
        // Claim rounds "$183,990 million" to "$184 billion" (~0.005% drift) —
        // grounds via Step 3 (kind-aware quantity equivalence) under any
        // reasonable Currency tolerance.
        string citations = "[" +
            Citation("https://example.com/unrelated", "generic boilerplate text") + ", " +
            Citation("https://example.com/figures", "Total assets: $183,990 million.") + "]";
        string body = BuildBody(
            """{ "s": "Total assets stood at $184 billion." }""",
            "[" + BasisEntry("s", citations) + "]");

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);

        Fact assetsFact = Assert.Single(result.Facts, f => f.IsNumerical && f.Value == "$184 billion");
        List<string> urls = [.. assetsFact.Sources.OfType<UrlSource>().Select(s => s.Url)];
        Assert.Equal(["https://example.com/figures"], urls);
    }

    private static string LoadAesSample()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Workflows",
            "Strategies",
            "ParallelAi",
            "SampleResponses",
            "AesMarketDeepSearch.json");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Parse_FullAesSample_ReturnsTrue()
    {
        string body = LoadAesSample();

        bool parsed = ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.NotEmpty(result.Facts);
    }

    [Fact]
    public void Parse_FullAesSample_DropsUngroundedCurrencyFactsButKeepsGroundedFigures()
    {
        // The AES fixture's revenue/EBITDA paragraphs include several "$X million"
        // figures that the broad claim mentions but no citation excerpt actually
        // contains. Those numerical sub-facts must be dropped — they cannot be
        // rendered as citations whose tooltip would show evidence the user can verify.
        // The broad parent fact still preserves the data (with all its parent URLs).
        string body = LoadAesSample();

        ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        List<string> numericalValues = [.. result.Facts.Where(f => f.IsNumerical).Select(f => f.Value)];

        // Ungrounded — no excerpt contains them, literally or numerically. Dropped.
        Assert.DoesNotContain("$12,278 million", numericalValues);
        Assert.DoesNotContain("$6,238 million", numericalValues);
        Assert.DoesNotContain("$3,608 million", numericalValues);
        Assert.DoesNotContain("$2,510 million", numericalValues);
        Assert.DoesNotContain("$1,366 million", numericalValues);

        // The broad claims that mention these figures still appear, with sources.
        Assert.Contains(result.Facts, f => !f.IsNumerical
            && f.Value.Contains("$12,278 million", StringComparison.Ordinal)
            && f.Sources.Count > 0);

        // Grounded numerical facts (some excerpt does contain them) come through.
        Assert.Contains("530,000", numericalValues);
    }

    [Fact]
    public void Parse_FullAesSample_NumericalFactsHaveUrlSources()
    {
        string body = LoadAesSample();

        ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        List<Fact> numerical = [.. result.Facts.Where(f => f.IsNumerical)];
        Assert.NotEmpty(numerical);
        foreach (Fact fact in numerical)
        {
            Assert.NotEmpty(fact.Sources);
            Assert.All(fact.Sources, s => Assert.IsType<UrlSource>(s));
            UrlSource url = Assert.IsType<UrlSource>(fact.Sources[0]);
            Assert.False(string.IsNullOrWhiteSpace(url.Url));
        }
    }

    [Fact]
    public void Parse_FullAesSample_SkipsObjectValuedBasisEntries()
    {
        string body = LoadAesSample();

        ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        // "utilities_sbu_deep_dive" basis entry resolves to an object — no broad fact should be emitted with
        // the whole object serialized as its claim text.
        Assert.DoesNotContain(result.Facts, f => f.Value.Contains("utility_name", StringComparison.Ordinal)
            && f.Value.Contains("market_details", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_FullAesSample_CustomerCountFactCitesAesIndiana()
    {
        string body = LoadAesSample();

        ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Fact customerFact = result.Facts.First(f => f.IsNumerical && f.Value == "530,000");
        List<string> urls = [.. customerFact.Sources.OfType<UrlSource>().Select(s => s.Url)];
        Assert.Contains(urls, u => u.Contains("aesindiana.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_FullAesSample_IncludesAesIndianaUtilityNameBroadFact()
    {
        string body = LoadAesSample();

        ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.Contains(result.Facts, f => f.Value == "AES Indiana" && !f.IsNumerical);
    }

    [Fact]
    public void Parse_FullAesSample_PreservesCleanedBodyUnchanged()
    {
        string body = LoadAesSample();

        ParallelAiResponseParser.TryParse(body, out ParallelAiParseResult result);

        Assert.Equal(body, result.CleanedBody);
    }

    [Fact]
    public void TryParse_BasisContainsNonObjectElement_SkipsItGracefully()
    {
        const string BODY = """
            {
                "output": {
                    "content": { "ok": "Valid claim." },
                    "basis": [
                        "primitive string",
                        123,
                        null,
                        { "field": "ok", "citations": [] }
                    ]
                }
            }
            """;

        bool parsed = ParallelAiResponseParser.TryParse(BODY, out ParallelAiParseResult result);

        Assert.True(parsed);
        Assert.Single(result.Facts);
        Assert.Equal("Valid claim.", result.Facts[0].Value);
    }

    [Fact]
    public void TryParse_CitationsContainsNonObjectElement_SkipsItGracefully()
    {
        const string BODY = """
            {
                "output": {
                    "content": { "ok": "Claim." },
                    "basis": [
                        {
                            "field": "ok",
                            "citations": [
                                123,
                                null,
                                "nope",
                                { "title": "T", "url": "https://example.com/a", "excerpts": ["x"] }
                            ]
                        }
                    ]
                }
            }
            """;

        bool parsed = ParallelAiResponseParser.TryParse(BODY, out ParallelAiParseResult result);

        Assert.True(parsed);
        Fact fact = result.Facts[0];
        Assert.Single(fact.Sources);
        UrlSource src = Assert.IsType<UrlSource>(fact.Sources[0]);
        Assert.Equal("https://example.com/a", src.Url);
    }

    [Fact]
    public void TryParse_ExcerptsContainsNonStringElement_ReplacesWithEmptyString()
    {
        const string BODY = """
            {
                "output": {
                    "content": { "ok": "Claim." },
                    "basis": [
                        {
                            "field": "ok",
                            "citations": [
                                {
                                    "title": "T",
                                    "url": "https://example.com/a",
                                    "excerpts": [ { "nested": "x" }, [1, 2], "actual text" ]
                                }
                            ]
                        }
                    ]
                }
            }
            """;

        bool parsed = ParallelAiResponseParser.TryParse(BODY, out ParallelAiParseResult result);

        Assert.True(parsed);
        Fact fact = result.Facts[0];
        Assert.Single(fact.Sources);
        UrlSource src = Assert.IsType<UrlSource>(fact.Sources[0]);
        Assert.Equal("https://example.com/a", src.Url);
    }
}
