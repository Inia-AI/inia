using IniaAi.Core.Documents;
using System.Text;

namespace IniaAi.Core.Tests.Documents;

/// <summary>Whether a page's text can stand in for the page: document processing reads a page with a fault by OCR.</summary>
public class PageTextQualityTests
{
    [Fact]
    public void FaultOf_GlyphRemappedText_FindsAFault()
    {
        string remappedText = ToGlyphRemappedText(
            """
            We have audited the accompanying statement of financial condition
            of the Company as of December 31, 2025, and the related notes to
            the statement. In our opinion, the statement presents fairly, in
            all material respects, the financial position of the Company in
            conformity with accounting principles generally accepted in the
            United States of America. Management is responsible for the
            preparation and fair presentation of the statement. Our
            responsibility is to express an opinion on the statement based
            on our audit. We conducted the audit in accordance with the
            applicable auditing standards and performed the procedures we
            considered necessary in the circumstances to obtain reasonable
            assurance about whether the statement is free of misstatement.
            """);

        Assert.NotNull(PageTextQuality.FaultOf(remappedText));
    }

    [Theory]
    [InlineData("The Company is controlled through a board of Directors, comprising a non-executive Chairman, two\n"
        + "\u018B\u0118\u00B1\u018B\u0003\u0178\u00E5\u0173\u01B4\u00E5\u00DA\u0003\u0145\u0139\u0003\u018B\u0118\u00E5\u0003\u0018\u0145\u00B1\u0173\u00DA\u0003\u00DA\u019A\u0173\u011C\u0139\u010D\n"
        + "executive Directors and three independent non-executive Directors.")]
    [InlineData("At the reporting date, the Group has recognised \u0173\u00E5\u0178\u015E\u00E5\u00CF\u018B\u0003\u0145\u00FC\u0003\u00B1\u012C\u012C\u0003\u00FC\u019A\u012C\u012C\u0003\u0178\u00CF\u0145\u015E\u00E5")]
    [InlineData("The Group models cashflow scenarios to\n"
        + "                               \u0190\u014E\u01A4%\u00E5\u00CF\u00E5\u0135\u00C6\u00E5\u0173\u0003\u0196\u01C8\u0196\u0180\u0163          and management believes there is sufficient \n"
        + "headroom in its facilities to continue in operation.")]
    public void FaultOf_TextLayerScrambledByAFontWithNoUnicodeMap_FindsAFault(string pageText)
    {
        Assert.NotNull(PageTextQuality.FaultOf(pageText));
    }

    [Theory]
    [InlineData("Income Statement\n\nRevenue fell to £41.5m (FY24: £36.6m) and the operating loss was £2.4m.\n"
        + "Adjusted operating profit (£m)      1.2      0.6")]
    [InlineData("Spółka prowadzi działalność w Łodzi i Wrocławiu, a jej przychody wzrosły o dwanaście procent w ciągu roku.")]
    [InlineData("Công ty đã tăng doanh thu trong năm nay và mở rộng hoạt động sang các thị trường mới ở châu Á.")]
    [InlineData("Rīgā ūdeņi tīrāki")]
    [InlineData("Większa część przychodów pochodzi z umów zawartych przez spółkę zależną w Łodzi.")]
    [InlineData("Þórður Ármannsson stýrði félaginu í Reykjavík á árinu.")]
    [InlineData("Şirketin ağırlığı arttı; ışıklı tabelalar sığınağı ve çığlığı gösteriyor.")]
    [InlineData("Koryto řečiště bylo vyčištěno a zpevněno během roku.")]
    [InlineData("Zażółć gęślą jaźń, napisała spółka w raporcie rocznym.")]
    public void FaultOf_CleanTextWithAccentedLetters_FindsNone(string pageText)
    {
        Assert.Null(PageTextQuality.FaultOf(pageText));
    }

    [Fact]
    public void FaultOf_FontThatMapsOnlyItsSpaceToAControlCharacter_FindsNone()
    {
        const string TEXT =
            "Sustainability report for the year under review, covering our plants and offices.\n"
            + "Energy consumption at the plant during the reporting period fell 3.7%\u0007in\u0007FY\u000722-23\u0007to\u0007the\u0007lowest level.\n"
            + "The company maintains its Quality\u0007Management\u0007System certification across all sites.\n"
            + "Training programmes for new joinees were expanded to cover safety and compliance.";

        Assert.Null(PageTextQuality.FaultOf(TEXT));
    }

    [Fact]
    public void FaultOf_NormalEnglishParagraph_FindsNone()
    {
        const string TEXT =
            """
            The quarterly review covers liquidity, funding sources, and the
            valuation of positions held at the reporting date. Balances are
            reconciled against custodian statements before publication, and
            any differences are investigated and resolved by the operations
            team within five business days of the reporting date.
            """;

        Assert.Null(PageTextQuality.FaultOf(TEXT));
    }

    [Fact]
    public void FaultOf_JapaneseTextWithoutSpaces_FindsNone()
    {
        const string TEXT =
            "当社は財務諸表の作成および適正な表示に関する内部統制を整備し、一般に公正妥当と認められる\n" +
            "企業会計の基準に準拠して財務諸表を作成する責任を負っています。監査人の責任は、実施した監査に\n" +
            "基づいて財務諸表に対する意見を表明することにあります。監査の過程において、経営者が採用した\n" +
            "会計方針およびその適用方法ならびに経営者によって行われた見積りの評価も含め全体としての\n" +
            "財務諸表の表示を検討しています。";

        Assert.Null(PageTextQuality.FaultOf(TEXT));
    }

    [Fact]
    public void FaultOf_SparseCoverPageWithDomainsAndPhone_FindsNone()
    {
        const string TEXT = "800.555.0100 | ExampleBancorp.com | TheExampleBank.com";

        Assert.Null(PageTextQuality.FaultOf(TEXT));
    }

    [Fact]
    public void FaultOf_TableOfContentsWithDotLeaders_FindsNone()
    {
        const string TEXT =
            """
            Table of Contents
            Disclaimer ................................................................................ 2
            General Information ............................................................. 4
            Contact List ................................................................................ 5
            Executive Summary ....................................................................... 7
            Market Overview ................................................................... 12
            Financial Projections .......................................................... 18
            """;

        Assert.Null(PageTextQuality.FaultOf(TEXT));
    }

    [Fact]
    public void FaultOf_TocWithDotLeadersGluedToWords_FindsNone()
    {
        string leaders = new('.', 90);
        string text =
            $"""
            Table of Contents
            Japan{leaders} 3
            Canada{leaders} 7
            Germany{leaders} 12
            France{leaders} 18
            Australia{leaders} 24
            Singapore{leaders} 29
            """;

        Assert.Null(PageTextQuality.FaultOf(text));
    }

    [Fact]
    public void FaultOf_FinancialTableWithCurrencyPrefixedNumbers_FindsNone()
    {
        const string TEXT =
            """
            Consolidated balance sheet
            December 31
            2024 2023
            P=98,115,973,998,761,234 P=97,121,875,071,972,551
            P=96,108,064,548,812,345 P=95,100,277,645,655,432
            P=94,115,973,998,761,234 P=93,121,875,071,972,551
            P=92,108,064,548,812,345 P=91,100,277,645,655,432
            P=90,115,973,998,761,234 P=89,121,875,071,972,551
            Total equity
            """;

        Assert.Null(PageTextQuality.FaultOf(TEXT));
    }

    [Fact]
    public void FaultOf_TocWithShortDotLeadersGluedToWords_FindsNone()
    {
        string leaders = new('.', 12);
        string text =
            $"""
            Table of Contents
            Introduction{leaders} 2
            Methodology{leaders} 7
            Governance{leaders} 12
            Operations{leaders} 18
            Compliance{leaders} 24
            Appendices{leaders} 29
            """;

        Assert.Null(PageTextQuality.FaultOf(text));
    }

    [Fact]
    public void FaultOf_NumericOnlyPageWithLongTokens_FindsNone()
    {
        string text = string.Join('\n', Enumerable.Repeat("1234567890123456789012", 6));

        Assert.Null(PageTextQuality.FaultOf(text));
    }

    [Fact]
    public void FaultOf_AvgWordLengthExactlyAtThreshold_FindsNone()
    {
        string text = string.Join('\n', Enumerable.Repeat(new string('a', 15), 6));

        Assert.Null(PageTextQuality.FaultOf(text));
    }

    [Fact]
    public void FaultOf_LongWordsJustBelowMinimumWordCount_FindsNone()
    {
        string text = string.Join('\n', Enumerable.Repeat(new string('a', 40), 4));

        Assert.Null(PageTextQuality.FaultOf(text));
    }

    [Fact]
    public void FaultOf_LongWordsAtMinimumWordCount_FindsAFault()
    {
        string text = string.Join('\n', Enumerable.Repeat(new string('a', 40), 5));

        Assert.NotNull(PageTextQuality.FaultOf(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData("Page 4")]
    public void FaultOf_BlankOrVeryShortText_FindsAFault(string text)
    {
        Assert.NotNull(PageTextQuality.FaultOf(text));
    }

    [Fact]
    public void FaultOf_ControlCharacterHeavyText_FindsAFault()
    {
        string text = string.Concat(Enumerable.Repeat("ab\u0001\u0002", 30));

        Assert.NotNull(PageTextQuality.FaultOf(text));
    }

    [Fact]
    public void FaultOf_SpacedOutSingleCharacters_FindsAFault()
    {
        const string TEXT = "T h e q u a r t e r l y r e v i e w c o v e r s l i q u i d i t y";

        Assert.NotNull(PageTextQuality.FaultOf(TEXT));
    }

    private static string ToGlyphRemappedText(string plainText)
    {
        const string GLYPH_POOL =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!\"#$%&'()*+,-./:;<=>?@[]^_`{|}~";

        Dictionary<char, char> map = new() { [' '] = 'ÿ' };
        int nextGlyph = 0;
        StringBuilder remapped = new(plainText.Length);

        foreach (char c in plainText)
        {
            if (c is '\n' or '\r')
            {
                _ = remapped.Append(c);
                continue;
            }

            if (!map.TryGetValue(c, out char mapped))
            {
                mapped = GLYPH_POOL[nextGlyph % GLYPH_POOL.Length];
                nextGlyph++;
                map[c] = mapped;
            }

            _ = remapped.Append(mapped);
        }

        return remapped.ToString();
    }
}
