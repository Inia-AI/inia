using IniaAi.Core.Services;

namespace IniaAi.Core.Tests.Services;

public class OcrJudgeResponseParserTests
{
    [Fact]
    public void Parse_GoodVerdict_NotBad()
    {
        string response =
            "<page_blocks>blocks</page_blocks><comparison>cmp</comparison><ocr_quality>good</ocr_quality>";

        OcrJudgeVerdict verdict = OcrJudgeResponseParser.Parse(response);

        Assert.False(verdict.QualityBad);
        Assert.True(verdict.VerdictPresent);
    }

    [Fact]
    public void Parse_BadVerdict_QualityBad()
    {
        string response =
            "<page_blocks>blocks</page_blocks><comparison>cmp</comparison><ocr_quality>bad</ocr_quality>";

        OcrJudgeVerdict verdict = OcrJudgeResponseParser.Parse(response);

        Assert.True(verdict.QualityBad);
    }

    [Fact]
    public void Parse_MissingVerdict_DefaultsToNotBad()
    {
        OcrJudgeVerdict verdict = OcrJudgeResponseParser.Parse("<comparison>content</comparison>");

        Assert.False(verdict.QualityBad);
    }

    [Theory]
    [InlineData("<ocr_quality>bad</ocr_quality>")]
    [InlineData("<ocr_quality>BAD</ocr_quality>")]
    [InlineData("<ocr_quality> bad </ocr_quality>")]
    public void Parse_BadVerdict_CaseAndWhitespaceInsensitive(string response)
    {
        Assert.True(OcrJudgeResponseParser.Parse(response).QualityBad);
    }

    [Fact]
    public void Parse_NullOrEmpty_NotBad()
    {
        Assert.False(OcrJudgeResponseParser.Parse(null).QualityBad);
        Assert.False(OcrJudgeResponseParser.Parse(string.Empty).QualityBad);
    }

    [Fact]
    public void Parse_VerdictAbsent_VerdictNotPresent()
    {
        OcrJudgeVerdict verdict = OcrJudgeResponseParser.Parse(
            "<page_blocks>blocks</page_blocks><comparison>partial reasoning");

        Assert.False(verdict.VerdictPresent);
        Assert.False(verdict.QualityBad);
    }

    [Fact]
    public void Parse_VerdictPresent_ReportsPresent()
    {
        OcrJudgeVerdict verdict = OcrJudgeResponseParser.Parse("<ocr_quality>good</ocr_quality>");

        Assert.True(verdict.VerdictPresent);
    }

    [Theory]
    [InlineData("<ocr_quality>bad.</ocr_quality>")]
    [InlineData("<ocr_quality>bad - table obscured</ocr_quality>")]
    [InlineData("<ocr_quality>likely bad</ocr_quality>")]
    [InlineData("<ocr_quality>BAD!</ocr_quality>")]
    public void Parse_BadVerdict_ToleratesExtraWording(string response)
    {
        Assert.True(OcrJudgeResponseParser.Parse(response).QualityBad);
    }

    [Theory]
    [InlineData("<ocr_quality>good</ocr_quality>")]
    [InlineData("<ocr_quality>badly cropped but readable</ocr_quality>")]
    public void Parse_NonBadVerdict_NotBad(string response)
    {
        Assert.False(OcrJudgeResponseParser.Parse(response).QualityBad);
    }

    [Theory]
    [InlineData("<ocr_quality>not bad</ocr_quality>")]
    [InlineData("<ocr_quality>quality is not bad, just a bit blurry</ocr_quality>")]
    [InlineData("<ocr_quality>no bad blocks found</ocr_quality>")]
    [InlineData("<ocr_quality>never bad</ocr_quality>")]
    public void Parse_NegatedBadVerdict_NotBad(string response)
    {
        Assert.False(OcrJudgeResponseParser.Parse(response).QualityBad);
    }

    [Theory]
    [InlineData("<ocr_quality>not good, bad overall</ocr_quality>")]
    [InlineData("<ocr_quality>not perfect and bad</ocr_quality>")]
    public void Parse_BadWithUnrelatedNegation_StillBad(string response)
    {
        Assert.True(OcrJudgeResponseParser.Parse(response).QualityBad);
    }

    [Fact]
    public void Parse_EchoedVerdictTagInInventory_UsesLastVerdict()
    {
        string response =
            "<page_blocks>the page prints the literal string <ocr_quality>good</ocr_quality> in a code sample</page_blocks>"
            + "<comparison>cmp</comparison><ocr_quality>bad</ocr_quality>";

        OcrJudgeVerdict verdict = OcrJudgeResponseParser.Parse(response);

        Assert.True(verdict.QualityBad);
    }
}
