using IniaAi.Core.Common.Helpers;

namespace IniaAi.Core.Tests.Common.Helpers;

public class TextPassageTests
{
    private const int MAX_LENGTH = 300;

    [Theory]
    [InlineData("Revenue at Acme Inc. rose to $5m in 2024. Costs fell.", "Revenue at Acme Inc. rose to $5m in 2024.")]
    [InlineData("Callowmere Fund III, L.P. reported a NAV of $5m. Costs fell.", "Callowmere Fund III, L.P. reported a NAV of $5m.")]
    [InlineData("Dr. Rivera said sales grew approx. $5m, e.g. in Europe. Costs fell.", "Dr. Rivera said sales grew approx. $5m, e.g. in Europe.")]
    [InlineData("Since Jan. 2024 the fund, managed by John F. Smith, added $5m. Costs fell.", "Since Jan. 2024 the fund, managed by John F. Smith, added $5m.")]
    public void SentenceAround_AbbreviationInsideTheSentence_KeepsTheWholeSentence(string text, string expected)
    {
        int start = text.IndexOf("$5m", StringComparison.Ordinal);

        Assert.Equal(expected, TextPassage.SentenceAround(text, start, start + 3, MAX_LENGTH));
    }

    [Fact]
    public void SentenceAround_FigureAfterAFullStop_StartsAtTheNextSentence()
    {
        const string TEXT = "Costs fell by 2.5% in the year. Revenue rose to $5m.";
        int start = TEXT.IndexOf("$5m", StringComparison.Ordinal);

        Assert.Equal("Revenue rose to $5m.", TextPassage.SentenceAround(TEXT, start, start + 3, MAX_LENGTH));
    }

    [Fact]
    public void Sentences_FigureWithALetterUnitAtTheEnd_EndsTheSentence()
    {
        Assert.Equal(
            ["Revenue rose to $5m.", "Cover was 3.5x.", "EBITDA was negative at £(0.62)m.", "Costs fell."],
            TextPassage.Sentences("Revenue rose to $5m. Cover was 3.5x. EBITDA was negative at £(0.62)m. Costs fell."));
    }

    [Fact]
    public void Sentences_WordThatOftenClosesASentence_StillEndsIt()
    {
        Assert.Equal(
            ["Fees rose, as did bonuses etc.", "Costs fell at Harbour Media Group plc.", "Margins held."],
            TextPassage.Sentences("Fees rose, as did bonuses etc. Costs fell at Harbour Media Group plc. Margins held."));
    }

    [Fact]
    public void Sentences_AbbreviationsAndInitials_DoNotSplit()
    {
        Assert.Equal(
            ["Sales at Acme Ltd. in the U.K. rose approx. 5% vs. 2023.", "Costs fell."],
            TextPassage.Sentences("Sales at Acme Ltd. in the U.K. rose approx. 5% vs. 2023. Costs fell."));
    }
}
