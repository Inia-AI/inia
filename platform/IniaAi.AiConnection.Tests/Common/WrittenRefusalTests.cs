using IniaAi.AiConnection.Common.ChatConnector;

namespace IniaAi.AiConnection.Tests.Common;

public class WrittenRefusalTests
{
    [Theory]
    [InlineData("I can't transcribe this one. The image is an identity card that shows a person's photo, name and ID number.")]
    [InlineData("I'm not able to extract the fields from this card. It is an identity document about a private individual.")]
    [InlineData("I'm sorry, but I can't help with transcribing this passport page.")]
    [InlineData("I cannot process this request as it would go against my guidelines.")]
    [InlineData("I apologize, but I can't share a real person's personal information from this document.")]
    [InlineData("Sorry — I can't help with that.")]
    public void IsRefusal_TheModelDeclinesTheTaskAndSaysWhy_IsTrue(string answer)
    {
        Assert.True(WrittenRefusal.IsRefusal(answer));
    }

    [Theory]
    [InlineData("I cannot extract the revenue because the page does not show it.")]
    [InlineData("I can't determine the closing balance from this statement.")]
    [InlineData("The applicant cannot repay the loan on the stated income.")]
    [InlineData("I cannot extract the account holder's personal details because the page is redacted for privacy.")]
    [InlineData("I can't identify the fund's valuation policy from these pages.")]
    [InlineData("I cannot identify the beneficial owner; the passport copy is missing.")]
    [InlineData("I'm unable to read the identification number, the scan is blurred.")]
    [InlineData("I cannot extract the figure because the statement is marked private.")]
    [InlineData("I am unable to provide a copy of my passport until next week, as it is with the embassy.")]
    [InlineData("I can't extract the closing balance of 4,210 because the page is cut off.")]
    [InlineData("<answer>N/A</answer>")]
    [InlineData("")]
    public void IsRefusal_ARealAnswerThatSaysCannot_IsFalse(string answer)
    {
        Assert.False(WrittenRefusal.IsRefusal(answer));
    }

    [Fact]
    public void IsRefusal_ATranscriptionOfAnIdCardThatSaysCannot_IsFalse()
    {
        const string transcription = """
            # IDENTITY CARD

            | Field | Value |
            | --- | --- |
            | Surname | MUSTERMANN |
            | Given names | ERIKA |
            | Date of birth | 12.08.1983 |

            This card cannot be used as a travel document outside the issuing country. Personal data is protected by law.
            """;

        Assert.False(WrittenRefusal.IsRefusal(transcription));
    }

    [Fact]
    public void IsRefusal_ALongAnswerThatOpensWithARefusal_IsFalse()
    {
        string answer = "I can't extract every figure because of my guidelines, but here are the totals. "
            + string.Join(" ", Enumerable.Repeat("Revenue was higher this year than last year.", 20));

        Assert.False(WrittenRefusal.IsRefusal(answer));
    }

    [Fact]
    public void ToRefusal_IsWrittenCategory_WithTheAnswerAsItsExplanation()
    {
        ModelRefusal refusal = WrittenRefusal.ToRefusal("I can't transcribe this identity card.", "us.anthropic.claude-sonnet-5-5");

        Assert.Equal(ModelRefusal.WRITTEN_CATEGORY, refusal.Category);
        Assert.Equal("I can't transcribe this identity card.", refusal.Explanation);
        Assert.Equal("us.anthropic.claude-sonnet-5-5", refusal.ModelId);
    }
}
