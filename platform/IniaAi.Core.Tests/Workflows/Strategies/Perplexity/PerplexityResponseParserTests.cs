using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Strategies.Perplexity;
using IniaAi.Domain.Entities.Citations.Sources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Tests.Workflows.Strategies.Perplexity;

public class PerplexityResponseParserTests
{
    [Fact]
    public void TryParse_NullBody_ReturnsFalse()
    {
        bool parsed = PerplexityResponseParser.TryParse(null, out PerplexityParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_EmptyBody_ReturnsFalse()
    {
        bool parsed = PerplexityResponseParser.TryParse(string.Empty, out PerplexityParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsFalse()
    {
        bool parsed = PerplexityResponseParser.TryParse("not json at all", out PerplexityParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_ValidJsonWithoutChoices_ReturnsFalse()
    {
        const string BODY = """
            {
                "search_results": [
                    { "title": "t", "url": "https://example.com/a", "snippet": "s" }
                ]
            }
            """;

        bool parsed = PerplexityResponseParser.TryParse(BODY, out PerplexityParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_SearchResultsIsObjectNotArray_ReturnsFalse()
    {
        const string BODY = """
            {
                "search_results": { "not": "an array" },
                "choices": [ { "message": { "role": "assistant", "content": "Hi.[1]" } } ]
            }
            """;

        bool parsed = PerplexityResponseParser.TryParse(BODY, out PerplexityParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_SearchResultsEntryMalformed_ReturnsFalse()
    {
        const string BODY = """
            {
                "search_results": [
                    { "title": "t1", "url": { "nested": "object instead of string" }, "snippet": "s1" }
                ],
                "choices": [ { "message": { "role": "assistant", "content": "Hi.[1]" } } ]
            }
            """;

        bool parsed = PerplexityResponseParser.TryParse(BODY, out PerplexityParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_ValidJsonWithEmptySearchResults_ReturnsFalse()
    {
        const string BODY = """
            {
                "search_results": [],
                "choices": [
                    { "message": { "role": "assistant", "content": "Hello.[1]" } }
                ]
            }
            """;

        bool parsed = PerplexityResponseParser.TryParse(BODY, out PerplexityParseResult result);

        Assert.False(parsed);
        Assert.NotNull(result);
    }

    private const string TWO_SEARCH_RESULTS = """
        "search_results": [
            { "title": "t1", "url": "https://example.com/a", "snippet": "s1" },
            { "title": "t2", "url": "https://example.com/b", "snippet": "s2" }
        ]
        """;

    private const string FIVE_SEARCH_RESULTS = """
        "search_results": [
            { "title": "t1", "url": "https://example.com/1", "snippet": "s1" },
            { "title": "t2", "url": "https://example.com/2", "snippet": "s2" },
            { "title": "t3", "url": "https://example.com/3", "snippet": "s3" },
            { "title": "t4", "url": "https://example.com/4", "snippet": "s4" },
            { "title": "t5", "url": "https://example.com/5", "snippet": "s5" }
        ]
        """;

    private static string BuildBody(string content, string searchResultsJson)
    {
        string escaped = JsonConvert.ToString(content);
        return $$"""
            {
                {{searchResultsJson}},
                "choices": [
                    { "message": { "role": "assistant", "content": {{escaped}} } }
                ]
            }
            """;
    }

    private static string GetAnswer(PerplexityParseResult result)
    {
        var json = JObject.Parse(result.CleanedBody);
        return json["choices"]![0]!["message"]!["content"]!.Value<string>()!;
    }

    [Fact]
    public void Parse_AnswerWithMarkers_StripsMarkerRuns()
    {
        string body = BuildBody("Foo.[1][2] Bar.[3]", FIVE_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Assert.Equal("Foo. Bar.", GetAnswer(result));
    }

    [Fact]
    public void Parse_AnswerWithoutMarkers_CleanedAnswerEqualsInput()
    {
        string body = BuildBody("No markers here at all.", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Assert.Equal("No markers here at all.", GetAnswer(result));
    }

    private const string COMPANY_SEARCH_RESULTS = """
        "search_results": [
            { "title": "Companies House", "url": "https://example.com/companies-house", "snippet": "HARBOUR MEDIA GROUP PLC was incorporated on 9 June 1981 as a public limited company." },
            { "title": "Results notice", "url": "https://example.com/results-2023", "snippet": "Revenue for the year ended 31 December 2023 was £36.6m (2022: £27.2m)." },
            { "title": "About us", "url": "https://example.com/about", "snippet": "We make television for every major UK broadcaster." }
        ]
        """;

    [Fact]
    public void Parse_AnswerWithoutMarkers_EachFigureIsSourcedFromTheSnippetsThatStateIt()
    {
        string body = BuildBody(
            "Harbour Media Group was founded in 1981. Revenue in 2023 was £36.6m. It employs about 200 staff.",
            COMPANY_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact founded = Assert.Single(result.Facts, fact => fact.IsNumerical && fact.Value == "1981");
        Assert.Equal("https://example.com/companies-house", Assert.IsType<UrlSource>(Assert.Single(founded.Sources)).Url);
        Fact revenue = Assert.Single(result.Facts, fact => fact.IsNumerical && fact.Value == "£36.6m");
        Assert.Equal("https://example.com/results-2023", Assert.IsType<UrlSource>(Assert.Single(revenue.Sources)).Url);
        Assert.DoesNotContain(result.Facts, fact => fact.Value.Contains("200"));
    }

    [Fact]
    public void Parse_FigureAfterTheLastMarker_IsSourcedFromTheSnippetThatStatesIt()
    {
        string body = BuildBody(
            "Harbour Media Group was incorporated in 1981.[1] Revenue in 2023 was £36.6m. It employs about 200 staff.",
            COMPANY_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact revenue = Assert.Single(result.Facts, fact => fact.IsNumerical && fact.Value == "£36.6m");
        Assert.Equal("https://example.com/results-2023", Assert.IsType<UrlSource>(Assert.Single(revenue.Sources)).Url);
        Assert.DoesNotContain(result.Facts, fact => fact.Value.Contains("200"));
    }

    [Fact]
    public void Parse_AnswerWithoutMarkers_ASentenceNoSnippetSupportsGetsNoSources()
    {
        string body = BuildBody("The group has 14 offices.", COMPANY_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Assert.Empty(result.Facts);
    }

    private const string UNRELATED_SEARCH_RESULTS = """
        "search_results": [
            { "title": "Olympics", "url": "https://example.com/olympics", "snippet": "The 2024 Olympic Games were held in Paris with 12 new events." },
            { "title": "Startup", "url": "https://example.com/startup", "snippet": "Founded 2023 with 12 staff, the studio now makes podcasts." }
        ]
        """;

    [Fact]
    public void Parse_AnswerWithoutMarkers_FiguresOnlyUnrelatedSnippetsShareDigitsWith_GetNoSources()
    {
        string body = BuildBody("It grew 12% versus 2023. Revenue was $1.5bn in 2024.", UNRELATED_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Parse_MultipleConsecutiveMarkerRuns_AllStripped()
    {
        string body = BuildBody("X.[1][2][3][4] Y.[5]", FIVE_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Assert.Equal("X. Y.", GetAnswer(result));
    }

    [Fact]
    public void Parse_MarkersWithInternalWhitespaceCollapseCleanly()
    {
        string body = BuildBody("A.[1]  B.[2]", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Assert.Equal("A. B.", GetAnswer(result));
    }

    [Fact]
    public void Parse_MarkersBetweenParagraphsPreserveNewlines()
    {
        string body = BuildBody("Para one.[1]\n\nPara two.[2]", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Assert.Equal("Para one.\n\nPara two.", GetAnswer(result));
    }

    [Fact]
    public void Parse_CleanedBodyPreservesJsonStructure()
    {
        string body = BuildBody("Foo.[1][2] Bar.[3]", FIVE_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        var json = JObject.Parse(result.CleanedBody);
        Assert.NotNull(json["search_results"]);
        Assert.NotNull(json["choices"]);
        Assert.DoesNotContain("[1]", result.CleanedBody);
        Assert.DoesNotContain("[2]", result.CleanedBody);
        Assert.DoesNotContain("[3]", result.CleanedBody);
    }

    private static IEnumerable<Fact> BroadFacts(PerplexityParseResult result)
    {
        return result.Facts.Where(f => !f.IsNumerical);
    }

    [Fact]
    public void Parse_SingleClaim_EmitsOneBroadFactWithClaimText()
    {
        string body = BuildBody("Claim A.[1]", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        List<Fact> broad = [.. BroadFacts(result)];
        Assert.Single(broad);
        Assert.Equal("Claim A.", broad[0].Value);
        Assert.False(broad[0].IsNumerical);
    }

    [Fact]
    public void Parse_TwoClaimsBackToBack_EmitsTwoBroadFacts()
    {
        string body = BuildBody("A.[1] B.[2]", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        List<Fact> broad = [.. BroadFacts(result)];
        Assert.Equal(2, broad.Count);
        Assert.Equal("A.", broad[0].Value);
        Assert.Equal("B.", broad[1].Value);
    }

    [Fact]
    public void Parse_ClaimWithLeadingWhitespace_Trimmed()
    {
        string body = BuildBody("   Claim with leading space.[1]", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        List<Fact> broad = [.. BroadFacts(result)];
        Assert.Single(broad);
        Assert.Equal("Claim with leading space.", broad[0].Value);
    }

    [Fact]
    public void Parse_AnswerStartingWithMarkerGroup_ProducesNoFactForEmptyClaim()
    {
        string body = BuildBody("[1][2] The body.[1]", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        List<Fact> broad = [.. BroadFacts(result)];
        Assert.Single(broad);
        Assert.Equal("The body.", broad[0].Value);
    }

    [Fact]
    public void Parse_TextAfterLastMarkerGroup_NotEmittedAsFact()
    {
        string body = BuildBody("Cited claim.[1] Uncited tail text.", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        List<Fact> broad = [.. BroadFacts(result)];
        Assert.Single(broad);
        Assert.Equal("Cited claim.", broad[0].Value);
    }

    [Fact]
    public void Parse_SingleMarker_AttachesOneUrlSource()
    {
        string body = BuildBody("Claim.[1]", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact fact = BroadFacts(result).Single();
        Assert.Single(fact.Sources);
        UrlSource urlSource = Assert.IsType<UrlSource>(fact.Sources[0]);
        Assert.Equal("https://example.com/a", urlSource.Url);
        Assert.Equal("s1", urlSource.CitedText);
    }

    [Fact]
    public void Parse_MarkerRun_AttachesUrlSourceForEachIndex()
    {
        string body = BuildBody("Multi-sourced claim.[1][2][5]", FIVE_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact fact = BroadFacts(result).Single();
        Assert.Equal(3, fact.Sources.Count);
        Assert.Equal("https://example.com/1", ((UrlSource)fact.Sources[0]).Url);
        Assert.Equal("https://example.com/2", ((UrlSource)fact.Sources[1]).Url);
        Assert.Equal("https://example.com/5", ((UrlSource)fact.Sources[2]).Url);
    }

    [Fact]
    public void Parse_OutOfRangeMarkerIndex_SkipsMissingUrlKeepsOthers()
    {
        string body = BuildBody("Claim with bad idx.[1][99]", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact fact = BroadFacts(result).Single();
        Assert.Single(fact.Sources);
        Assert.Equal("https://example.com/a", ((UrlSource)fact.Sources[0]).Url);
    }

    [Fact]
    public void Parse_MarkerIndexZero_Skipped()
    {
        string body = BuildBody("Claim.[0][1]", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact fact = BroadFacts(result).Single();
        Assert.Single(fact.Sources);
        Assert.Equal("https://example.com/a", ((UrlSource)fact.Sources[0]).Url);
    }

    [Fact]
    public void Parse_SearchResultSnippetNull_CitedTextIsEmptyString()
    {
        const string NULL_SNIPPET = """
            "search_results": [
                { "title": "t1", "url": "https://example.com/a", "snippet": null }
            ]
            """;
        string body = BuildBody("Claim.[1]", NULL_SNIPPET);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact fact = BroadFacts(result).Single();
        UrlSource urlSource = Assert.IsType<UrlSource>(fact.Sources[0]);
        Assert.Equal("https://example.com/a", urlSource.Url);
        Assert.Equal(string.Empty, urlSource.CitedText);
    }

    private static IEnumerable<Fact> NumericalFacts(PerplexityParseResult result)
    {
        return result.Facts.Where(f => f.IsNumerical);
    }

    [Fact]
    public void Parse_ClaimWithOneNumericalValue_EmitsNumericalSubFact()
    {
        const string GROUNDED = """
            "search_results": [
                { "title": "t1", "url": "https://example.com/a", "snippet": "Deal valued at $10.7 billion in cash" }
            ]
            """;
        string body = BuildBody("Deal valued at $10.7 billion.[1]", GROUNDED);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact numerical = Assert.Single(NumericalFacts(result));
        Assert.Equal("$10.7 billion", numerical.Value);
        Assert.True(numerical.IsNumerical);
        Assert.Single(numerical.Sources);
        Assert.Equal("https://example.com/a", ((UrlSource)numerical.Sources[0]).Url);
    }

    [Fact]
    public void Parse_ClaimWithMultipleNumericalValues_EmitsOneSubFactPerValue()
    {
        // Both snippets ground both values, so each sub-fact gets both sources.
        const string GROUNDED = """
            "search_results": [
                { "title": "t1", "url": "https://example.com/a", "snippet": "Shares priced at $15.00, total equity $10.7 billion" },
                { "title": "t2", "url": "https://example.com/b", "snippet": "Equity value $10.7 billion ($15.00/share)" }
            ]
            """;
        string body = BuildBody("$15.00 per share, total equity $10.7 billion.[1][2]", GROUNDED);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        List<Fact> numerical = [.. NumericalFacts(result)];
        Assert.Contains(numerical, f => f.Value == "$15.00");
        Assert.Contains(numerical, f => f.Value == "$10.7 billion");
        foreach (Fact f in numerical)
        {
            Assert.Equal(2, f.Sources.Count);
            Assert.Equal("https://example.com/a", ((UrlSource)f.Sources[0]).Url);
            Assert.Equal("https://example.com/b", ((UrlSource)f.Sources[1]).Url);
        }
    }

    [Fact]
    public void Parse_ClaimWithNoNumericalValues_NoSubFactsEmitted()
    {
        string body = BuildBody("A plain prose claim without values.[1]", TWO_SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Assert.Empty(NumericalFacts(result));
        Assert.Single(BroadFacts(result));
    }

    [Fact]
    public void Parse_NumericalSubFactSources_AreDistinctInstancesFromBroadFact()
    {
        const string GROUNDED = """
            "search_results": [
                { "title": "t1", "url": "https://example.com/a", "snippet": "It is $10.7 billion" }
            ]
            """;
        string body = BuildBody("It is $10.7 billion.[1]", GROUNDED);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact broad = BroadFacts(result).Single();
        Fact numerical = NumericalFacts(result).Single();
        Assert.NotSame(broad.Sources[0], numerical.Sources[0]);
        // Mutating one should not affect the other
        ((UrlSource)broad.Sources[0]).CitedText = "MUTATED";
        Assert.NotEqual("MUTATED", ((UrlSource)numerical.Sources[0]).CitedText);
    }

    [Fact]
    public void Parse_NumericalSubFact_OnlyAttachesSnippetsThatGroundTheValue()
    {
        // findings.md Path 1: Perplexity inherits every parent URL onto each
        // numerical sub-fact, regardless of whether the snippet contains the
        // value. After the grounding fix, only snippets that mention the value
        // (literally, multiplier-stripped, or kind-aware-equivalent) attach.
        const string SEARCH_RESULTS = """
            "search_results": [
                { "title": "t1", "url": "https://grounded.example/a", "snippet": "Deal valued at $10.7 billion in cash" },
                { "title": "t2", "url": "https://blurb.example/b", "snippet": "Generic page description with no value" }
            ]
            """;
        string body = BuildBody("Deal valued at $10.7 billion.[1][2]", SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact numerical = Assert.Single(NumericalFacts(result));
        Assert.Equal("$10.7 billion", numerical.Value);
        UrlSource only = Assert.IsType<UrlSource>(Assert.Single(numerical.Sources));
        Assert.Equal("https://grounded.example/a", only.Url);
    }

    [Fact]
    public void Parse_NumericalSubFact_NoGroundingSnippet_DropsSubFactEntirely()
    {
        // When no parent snippet grounds the value, the sub-fact is dropped
        // (consistent with FactExtractor and ParallelAiResponseParser). The
        // broad fact still keeps the URLs so the broader context isn't lost.
        const string SEARCH_RESULTS = """
            "search_results": [
                { "title": "t1", "url": "https://blurb.example/a", "snippet": "Generic page summary" },
                { "title": "t2", "url": "https://blurb.example/b", "snippet": "Another generic description" }
            ]
            """;
        string body = BuildBody("Revenue was $5M last quarter.[1][2]", SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Assert.Empty(NumericalFacts(result));
        // Broad fact preserved with both URLs.
        Fact broad = Assert.Single(BroadFacts(result));
        Assert.Equal(2, broad.Sources.Count);
    }

    [Fact]
    public void Parse_NumericalSubFact_GroundingViaKindAwareQuantityEquivalence()
    {
        // Re-synthesis case: claim rounds "$183,990 million" to "$184 billion"
        // (~0.005% drift). Within any reasonable Currency tolerance — grounds
        // via kind-aware quantity equivalence.
        const string SEARCH_RESULTS = """
            "search_results": [
                { "title": "t1", "url": "https://filings.example/a", "snippet": "Total NAV $183,990 million across the portfolio" }
            ]
            """;
        string body = BuildBody("NAV was $184 billion.[1]", SEARCH_RESULTS);

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact numerical = Assert.Single(NumericalFacts(result));
        Assert.Equal("$184 billion", numerical.Value);
        UrlSource only = Assert.IsType<UrlSource>(Assert.Single(numerical.Sources));
        Assert.Equal("https://filings.example/a", only.Url);
    }

    private static string LoadSampleResponse(string name)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Workflows",
            "Strategies",
            "Perplexity",
            "SampleResponses",
            name);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Parse_FullAesSampleResponse_ProducesExpectedShape()
    {
        string body = LoadSampleResponse("AesAcquisition.json");

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);

        // Marker stripping — nowhere in the body
        Assert.DoesNotContain("[1]", result.CleanedBody);
        Assert.DoesNotContain("[2]", result.CleanedBody);
        Assert.DoesNotContain("[3]", result.CleanedBody);
        Assert.DoesNotContain("[4]", result.CleanedBody);
        Assert.DoesNotContain("[5]", result.CleanedBody);
        Assert.DoesNotContain("[6]", result.CleanedBody);

        // JSON structure preserved
        var json = JObject.Parse(result.CleanedBody);
        Assert.NotNull(json["search_results"]);

        // Observability metadata is preserved in the cleaned body
        Assert.NotNull(json["model"]);
        Assert.NotNull(json["usage"]);
        Assert.NotNull(json["usage"]?["total_tokens"]);
        Assert.NotNull(json["usage"]?["cost"]);
        Assert.NotNull(json["usage"]?["cost"]?["total_cost"]);

        // First sentence preserved (after bold markdown)
        string answer = GetAnswer(result);
        Assert.StartsWith("**On March 2, 2026", answer);

        // Numerical facts that survive grounding: only sub-facts whose value
        // appears in at least one parent snippet are kept. In the AES sample
        // the snippets are page-level blurbs; only "$10.7 billion" (in
        // snippet [6]) and "March 1, 2026" (in snippet [4]) ground. The
        // others ($15.00, $33.4 billion, $22.7 billion, 40.3%, December 31,
        // 2025) appear in the AI's prose but not in any provided snippet,
        // so the grounding pass drops them — findings.md Path 1 baseline
        // was 89% ungrounded numerical facts in this very trace.
        List<Fact> numerical = [.. NumericalFacts(result)];
        Assert.NotEmpty(numerical);
        Assert.Contains(numerical, f => f.Value == "$10.7 billion");

        // Every kept numerical fact has at least one https URL whose snippet
        // grounds the value (literal, multiplier-stripped, or quantity-equivalent).
        foreach (Fact f in numerical)
        {
            Assert.NotEmpty(f.Sources);
            Assert.All(f.Sources, s =>
                Assert.StartsWith("https://", ((UrlSource)s).Url));
        }

        // $10.7 billion appears literally only in snippet [6] (spglobal.com).
        // [1] aes.com and [2] prnewswire.com share an EQT-acquisition blurb
        // that doesn't mention the value, so they're correctly excluded.
        Fact tenSeven = numerical.First(f => f.Value == "$10.7 billion");
        List<string> urls = [.. tenSeven.Sources.Select(s => ((UrlSource)s).Url)];
        Assert.Contains(urls, u => u.Contains("spglobal.com"));
        Assert.DoesNotContain(urls, u => u.Contains("aes.com"));
        Assert.DoesNotContain(urls, u => u.Contains("prnewswire.com"));
    }

    [Fact]
    public void Parse_MarkerToSearchResultWithEmptyUrl_SkipsThatSource()
    {
        const string BODY = """
            {
                "search_results": [
                    { "title": "T1", "url": "", "snippet": "no url" },
                    { "title": "T2", "url": "https://example.com/two", "snippet": "has url" }
                ],
                "choices": [
                    { "message": { "role": "assistant", "content": "The claim is here.[1][2]" } }
                ]
            }
            """;

        bool parsed = PerplexityResponseParser.TryParse(BODY, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact broad = result.Facts.First(f => !f.IsNumerical);
        Assert.Single(broad.Sources);
        UrlSource only = Assert.IsType<UrlSource>(broad.Sources[0]);
        Assert.Equal("https://example.com/two", only.Url);
    }

    [Fact]
    public void Parse_MarkerToSearchResultWithNullUrl_SkipsThatSource()
    {
        const string BODY = """
            {
                "search_results": [
                    { "title": "T1", "url": null, "snippet": "no url" },
                    { "title": "T2", "url": "https://example.com/two", "snippet": "has url" }
                ],
                "choices": [
                    { "message": { "role": "assistant", "content": "The claim is here.[1][2]" } }
                ]
            }
            """;

        bool parsed = PerplexityResponseParser.TryParse(BODY, out PerplexityParseResult result);

        Assert.True(parsed);
        Fact broad = result.Facts.First(f => !f.IsNumerical);
        Assert.Single(broad.Sources);
        UrlSource only = Assert.IsType<UrlSource>(broad.Sources[0]);
        Assert.Equal("https://example.com/two", only.Url);
    }

    [Fact]
    public void Parse_AesOwnershipSampleResponse_ExtractsAllExpectedNumericalFacts()
    {
        // Body: SampleResponses/AesOwnership.json — Perplexity sonar-pro answer
        // about AES Corporation ownership. Asserts:
        //   1. Every numerical Fact is grounded by every one of its sources.
        //   2. The set of grounded numerical Facts is exactly the set of AI
        //      claims that one of the marker-run snippets actually supports.
        // The expected list below is derived purely from "what the AI claimed"
        // crossed with "what the matching snippets actually mention" — not from
        // running the parser.
        string body = LoadSampleResponse("AesOwnership.json");

        bool parsed = PerplexityResponseParser.TryParse(body, out PerplexityParseResult result);

        Assert.True(parsed);

        // Markers stripped end-to-end
        for (int i = 1; i <= 9; i++)
        {
            Assert.DoesNotContain($"[{i}]", result.CleanedBody);
        }

        // Every fact carries at least one URL source
        Assert.NotEmpty(result.Facts);
        Assert.All(result.Facts, f => Assert.NotEmpty(f.Sources));
        Assert.All(result.Facts, f => Assert.All(f.Sources, s => Assert.IsType<UrlSource>(s)));

        // Every numerical fact's Value is grounded by every one of its sources —
        // literal substring, multiplier-stripped, or kind-aware quantity-equivalent.
        foreach (Fact n in NumericalFacts(result))
        {
            Assert.All(n.Sources, s =>
            {
                var u = (UrlSource)s;
                Assert.True(
                    NumericalGrounding.TextContainsValue(u.CitedText, n.Value),
                    $"Source {u.Url} doesn't ground numerical fact '{n.Value}'.");
            });
        }

        // The complete set of numerical facts that should survive grounding.
        // Each entry: AI's surface form → URL whose snippet supports it.
        // Snippets ([N] = search_results[N-1]):
        //   [2] wallstreetzen — "85.05% institutional shareholders, 13.69% Aes
        //                       insiders, and 1.26% retail investors"
        //   [7] marketscreener — "Vanguard Fiduciary Trust Co. 12.44 %.
        //                        88,596,463, 12.44 % ; STATE STREET CORPORATION.
        //                        6.263 %. 44,598,987, 6.263 %"
        // All other AI numerical claims (15 countries, 85% intro, 8.4%, 12.4%,
        // 50%, 27%, 1991, 2.85%, table rows for Terrific/BlackRock/Ameriprise/
        // Morgan Stanley/Geode, 1,331 owners, 754M shares, July 2025, 13.66%,
        // 1.29%, 30%, 2024, 13,751, Feb 24 2026, March 2026, 100%) appear in
        // the AI's prose but not in any snippet of their marker run, so the
        // grounding pass correctly drops them.
        // Concentration tolerance is 0.001 (0.1%). Each entry below grounds
        // either by literal substring or kind-aware quantity equivalence under
        // that tolerance.
        (string Value, string UrlSubstring)[] expectedNumerical =
        [
            ("85%",        "wallstreetzen.com"),    // intro "approximately 85%"             → URL 2 "85.05%" (delta/max ≈ 0.000588 < 0.001)
            ("85.05%",     "wallstreetzen.com"),    // "Overall: 85.05% institutions"        → URL 2 literal
            ("88,596,463", "marketscreener.com"),   // Vanguard shares (table row)           → URL 7 literal
            ("12.43%",     "marketscreener.com"),   // Vanguard range upper bound            → URL 7 "12.44 %" (delta/max ≈ 0.000804 < 0.001)
            ("44,598,987", "marketscreener.com"),   // State Street shares (table row)       → URL 7 literal
            ("6.26%",      "marketscreener.com"),   // State Street range lower bound        → URL 7 "6.263 %" (delta/max ≈ 0.000479 < 0.001)
            ("6.263%",     "marketscreener.com"),   // State Street range upper bound        → URL 7 literal "6.263 %"
        ];

        List<string> actualValues = [.. NumericalFacts(result).Select(f => f.Value)];

        foreach ((string value, string url) in expectedNumerical)
        {
            AssertGroundedBy(result, value, url);
        }

        Assert.Equal(
            expectedNumerical.Length,
            actualValues.Count);
    }

    private static void AssertGroundedBy(
        PerplexityParseResult result,
        string factValue,
        string urlSubstring)
    {
        Fact fact = NumericalFacts(result).FirstOrDefault(f => f.Value == factValue)
            ?? throw new Xunit.Sdk.XunitException(
                $"Expected numerical fact '{factValue}' but it was not present.");
        Assert.Contains(
            fact.Sources.Cast<UrlSource>(),
            u => u.Url.Contains(urlSubstring, StringComparison.OrdinalIgnoreCase));
    }
}
