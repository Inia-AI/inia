using IniaAi.Common.Text;

namespace IniaAi.Common.Tests.Text;

public class SecretMaskerTests
{
    [Fact]
    public void Mask_Null_ReturnsFourAsterisks()
    {
        Assert.Equal("****", SecretMasker.Mask(null));
    }

    [Fact]
    public void Mask_Empty_ReturnsFourAsterisks()
    {
        Assert.Equal("****", SecretMasker.Mask(string.Empty));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Mask_WhitespaceOnly_ReturnsFourAsterisks(string input)
    {
        Assert.Equal("****", SecretMasker.Mask(input));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    public void Mask_FourCharsOrFewer_ReturnsUniformFourAsterisks(string input)
    {
        // Short values collapse to a uniform literal so the mask does not leak
        // the original length (e.g. "a" and "abcd" both surface as "****").
        Assert.Equal("****", SecretMasker.Mask(input));
    }

    [Fact]
    public void Mask_FiveChars_RevealsFirstThreeCharsOnly()
    {
        // Threshold case: at five chars the partial-reveal branch kicks in.
        Assert.Equal("abc**", SecretMasker.Mask("abcde"));
    }

    [Fact]
    public void Mask_LongValue_RevealsOnlyFirstThreeCharsRegardlessOfLength()
    {
        // The visible prefix is capped at three characters even for very long
        // secrets, so the disclosure budget does not grow with input length.
        string input = new('x', 100);
        string result = SecretMasker.Mask(input);

        Assert.Equal(100, result.Length);
        Assert.StartsWith("xxx", result);
        Assert.EndsWith(new string('*', 97), result);
    }

    [Fact]
    public void Mask_PreservesOriginalLengthForLongValues()
    {
        string input = "sk-supersecretvalue-1234";

        string result = SecretMasker.Mask(input);

        Assert.Equal(input.Length, result.Length);
        Assert.Equal("sk-*********************", result);
    }

    [Fact]
    public void Mask_DoesNotLeakOriginalValue()
    {
        string input = "p@ssw0rd";

        string result = SecretMasker.Mask(input);

        Assert.DoesNotContain("p@ssw0rd", result);
        Assert.Contains("*", result);
    }

    [Fact]
    public void Mask_AnthropicStyleKey_ShowsFamiliarPrefix()
    {
        // Behaviour we rely on for "is this my token?" recognisability in logs
        // and clipboard exports: the leading provider-tag remains visible.
        string result = SecretMasker.Mask("ant-api03-AAAAAAAAAAAAAAAAAAAAAAAAA");

        Assert.StartsWith("ant", result);
        Assert.DoesNotContain("api03", result);
    }
}
