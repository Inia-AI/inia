using IniaAi.Common.Enums;
using System.Globalization;
using System.Runtime.Serialization;

namespace IniaAi.Common.Tests;

public class UtilsTests : IDisposable
{
    private readonly CultureInfo _originalCulture;

    public UtilsTests()
    {
        _originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        CultureInfo.CurrentCulture = _originalCulture;
    }

    [Fact]
    public void DictionaryToString_ValidDictionary_ReturnsCorrectString()
    {
        // Arrange
        var dictionary = new Dictionary<string, string>
            {
                { "Key1", "Value1" },
                { "Key2", "Value2" },
                { "Key3", "Value3" }
            };

        // Act
        string result = Utils.DictionaryToString(dictionary);

        // Assert
        Assert.Equal("Key1\n\nValue1\n\nKey2\n\nValue2\n\nKey3\n\nValue3", result);
    }

    [Fact]
    public void GetStringFromTag_ValidTextAndTag_ReturnsCorrectString()
    {
        // Arrange
        string text = "<tag>Some text</tag>";
        string tag = "tag";

        // Act
        string? result = Utils.GetStringFromTag(text, tag);

        // Assert
        Assert.Equal("Some text", result);
    }

    [Fact]
    public void GetStringFromTag_TextWithoutTag_ReturnsNull()
    {
        // Arrange
        string text = "Some text";
        string tag = "tag";

        // Act
        string? result = Utils.GetStringFromTag(text, tag);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetStringFromTag_TextWithMismatchedTags_ReturnsNull()
    {
        // Arrange
        string text = "<tag1>Some text</tag2>";
        string tag = "tag";

        // Act
        string? result = Utils.GetStringFromTag(text, tag);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetStringFromTag_TextWithMultipleTagsShouldProcessThem_ReturnsAllTagsContent()
    {
        // Arrange
        string text = "<tag>Some text</tag><tag>Other text</tag>";
        string tag = "tag";

        // Act
        string? result = Utils.GetStringFromTag(text, tag, processMultipleTags: true);

        // Assert
        Assert.Equal("Some text\nOther text", result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void GetStringFromTag_TextWithMultipleTagsShouldNotProcessThem_ReturnsFirstTagContent()
    {
        // Arrange
        string text = "<tag>Some text</tag><tag>Other text</tag>";
        string tag = "tag";

        // Act
        string? result = Utils.GetStringFromTag(text, tag, processMultipleTags: false);

        // Assert
        Assert.Equal("Some text", result);
    }

    [Fact]
    public void ATagRepeatedInAnAnswer_EachReaderTakesItsOwnOccurrence()
    {
        // Arrange
        string text = "<answer>a</answer><answer>b</answer>";

        // Act
        string? first = Utils.GetStringFromTag(text, "answer");
        string? every = Utils.GetStringFromTag(text, "answer", processMultipleTags: true);
        string? last = Utils.GetStringFromLastTag(text, "answer");

        // Assert
        Assert.Equal("a", first);
        Assert.Equal("a\nb", every);
        Assert.Equal("b", last);
    }

    [Fact]
    public void GetStringFromTag_WhenTextHasTagStartLooseSingleProcessMultipleTagsFalse_FindsCorrectContent()
    {
        // Arrange
        string text = "This text contains <tag>, but the problem that it also has that tag unclosed in the beginning. <tag>Some text</tag>";
        string tag = "tag";

        // Act
        string? result = Utils.GetStringFromTag(text, tag);

        // Assert
        Assert.Equal("Some text", result);
    }

    [Fact]
    public void GetStringFromTag_WhenTextHasTagStartLooseSingleProcessMultipleTagsTrue_FindsCorrectContent()
    {
        // Arrange
        string text = "This text contains <tag>, but the problem that it also has that tag unclosed in the beginning. <tag>Some text</tag>";
        string tag = "tag";

        // Act
        string? result = Utils.GetStringFromTag(text, tag, processMultipleTags: true);

        // Assert
        Assert.Equal("Some text", result);
    }

    [Fact]
    public void ConvertToInt_ValidValue_ReturnsCorrectInt()
    {
        // Arrange
        string value = "123";

        // Act
        int result = Utils.ConvertToInt(value);

        // Assert
        Assert.Equal(123, result);
    }

    [Fact]
    public void ConvertToInt_NullValue_ReturnsNegativeOne()
    {
        // Arrange
        string? value = null;

        // Act
        int result = Utils.ConvertToInt(value);

        // Assert
        Assert.Equal(-1, result);
    }

    [Fact]
    public void ConvertToInt_NegativeValueInParentheses_ReturnsNegativeInt()
    {
        // Arrange
        string value = "(456)";

        // Act
        int result = Utils.ConvertToInt(value);

        // Assert
        Assert.Equal(-456, result);
    }

    [Fact]
    public void ConvertToDouble_ValidValue_ReturnsCorrectDouble()
    {
        // Arrange
        string value = "3.14";

        // Act
        double result = Utils.ConvertToDouble(value);

        // Assert
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ConvertToDouble_NullValue_ReturnsNaN()
    {
        // Arrange
        string? value = null;

        // Act
        double result = Utils.ConvertToDouble(value);

        // Assert
        Assert.Equal(double.NaN, result);
    }

    [Fact]
    public void ConvertToDouble_NegativeValueInParentheses_ReturnsNegativeDouble()
    {
        // Arrange
        string value = "(2.718)";

        // Act
        double result = Utils.ConvertToDouble(value);

        // Assert
        Assert.Equal(-2.718, result);
    }

    [Fact]
    public void ParseStringForNumbersList_ValidNumbers_ReturnsCorrectNumberList()
    {
        // Arrange
        string numbers = "1, 2, 3, 4, 5, 6-10, 11, 12, 13-15";
        List<int> expectedNumberList = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

        // Act
        List<int> result = Utils.ParseStringForNumbersList(numbers);

        // Assert
        Assert.Equal(expectedNumberList, result);
    }

    [Fact]
    public void ParseStringForNumbersList_EmptyString_ReturnsEmptyNumberList()
    {
        // Arrange
        string numbers = "";
        List<int> expectedNumberList = [];

        // Act
        List<int> result = Utils.ParseStringForNumbersList(numbers);

        // Assert
        Assert.Equal(expectedNumberList, result);
    }

    [Fact]
    public void ParseStringForNumbersList_InvalidFormat_ReturnsEmptyNumberList()
    {
        // Arrange
        string numbers = "1, 2, 3, 4, 5, 6-10, 11, 12, 13-";

        // Act
        List<int> result = Utils.ParseStringForNumbersList(numbers);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ParseStringForNumbersList_NegativeNumbers_ReturnsCorrectNumberList()
    {
        string numbers = "-1, -2, -3";
        List<int> expected = [-1, -2, -3];

        List<int> result = Utils.ParseStringForNumbersList(numbers);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseStringForNumbersList_NegativeRange_ReturnsCorrectNumberList()
    {
        string numbers = "-3--1";
        List<int> expected = [-3, -2, -1];

        List<int> result = Utils.ParseStringForNumbersList(numbers);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseStringForNumbersList_MixedPositiveAndNegative_ReturnsCorrectNumberList()
    {
        string numbers = "1-3, -2--1";
        List<int> expected = [1, 2, 3, -2, -1];

        List<int> result = Utils.ParseStringForNumbersList(numbers);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseStringForNumbersList_InvalidNegativeRange_ReturnsEmptyList()
    {
        string numbers = "-1--3";

        List<int> result = Utils.ParseStringForNumbersList(numbers);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("-1-")]
    [InlineData("--1")]
    [InlineData("1-")]
    [InlineData("-")]
    [InlineData("1--")]
    [InlineData("1-2-3")]
    [InlineData("1,,-2")]
    public void ParseStringForNumbersList_MalformedInput_ReturnsEmptyList(string numbers)
    {
        List<int> result = Utils.ParseStringForNumbersList(numbers);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseStringForNumbersList_EnDash_TreatedAsHyphen()
    {
        string numbers = "77\u201378, 106\u2013109";
        List<int> expected = [77, 78, 106, 107, 108, 109];

        List<int> result = Utils.ParseStringForNumbersList(numbers);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseStringForNumbersList_EmDash_TreatedAsHyphen()
    {
        string numbers = "10\u201415";
        List<int> expected = [10, 11, 12, 13, 14, 15];

        List<int> result = Utils.ParseStringForNumbersList(numbers);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseStringForNumbersList_MixedDashes_TreatedAsHyphen()
    {
        string numbers = "1-3, 5\u20137, 9\u201411";
        List<int> expected = [1, 2, 3, 5, 6, 7, 9, 10, 11];

        List<int> result = Utils.ParseStringForNumbersList(numbers);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void RemoveDuplicatePages_ValidInput_ReturnsCorrectOutput()
    {
        // Arrange
        string input =
        """
        Content before pages

        <file1.pdf-page1>
        Page 1

        </file1.pdf-page1>
        Content between pages

        <file1.pdf-page2>
        Page 2

        </file1.pdf-page2>

        <file1.pdf-page2>
        Page 2

        </file1.pdf-page2>

        Content between pages

        <file2.pdf-page1>
        Page 1

        </file2.pdf-page1>

        <file2.pdf-page1>
        Page 1 Different Content

        </file2.pdf-page1>
        Content between pages

        <file2.pdf-page2>
        Page 2

        </file2.pdf-page2>

        Content after pages
        """;

        string expected =
        """
        Content before pages

        <file1.pdf-page1>
        Page 1

        </file1.pdf-page1>
        Content between pages

        <file1.pdf-page2>
        Page 2

        </file1.pdf-page2>

        Content between pages

        <file2.pdf-page1>
        Page 1

        </file2.pdf-page1>

        Content between pages

        <file2.pdf-page2>
        Page 2

        </file2.pdf-page2>

        Content after pages
        """;

        // Act
        string? result = Utils.RemoveDuplicatePages(input);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected, result, ignoreCase: true, ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);
    }

    [Fact]
    public void RemoveDuplicatePages_NoDuplicatePages_ReturnsSameInput()
    {
        // Arrange
        string input =
        """
        <File1.pdf-page1>
        Content 1
        </File1.pdf-page1>

        <File1.pdf-page2>
        Content 2
        </File1.pdf-page2>
        """;

        // Act
        string? result = Utils.RemoveDuplicatePages(input);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(input, result, ignoreCase: true, ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);
    }

    [Fact]
    public void RemoveDuplicatePages_EmptyInput_ReturnsEmptyString()
    {
        // Arrange
        string input = "";

        // Act
        string? result = Utils.RemoveDuplicatePages(input);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("", result);
    }

    private enum TestEnum
    {
        [EnumMember(Value = "First Value")]
        First,

        [EnumMember(Value = "Second Value")]
        Second,

        [EnumMember(Value = "Third Value")]
        Third
    }

    [Fact]
    public void TryParseEnumFromEnumMemberAttribute_ShouldReturnTrueAndEnum_WhenValidEnumMemberValue()
    {
        // Arrange
        string input = "First Value";
        TestEnum expected = TestEnum.First;

        // Act
        bool result = Utils.TryParseEnumFromEnumMemberAttribute(input, out TestEnum actual);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryParseEnumFromEnumMemberAttribute_ShouldReturnFalse_WhenInvalidEnumMemberValue()
    {
        // Arrange
        string input = "Invalid Value";

        // Act
        bool result = Utils.TryParseEnumFromEnumMemberAttribute(input, out TestEnum actual);

        // Assert
        Assert.False(result);
        Assert.Equal(default, actual);
    }

    [Fact]
    public void TryParseEnumFromEnumMemberAttribute_ShouldReturnTrueAndEnum_WhenEnumMemberValueIsCaseInsensitive()
    {
        // Arrange
        string input = "second value";
        TestEnum expected = TestEnum.Second;

        // Act
        bool result = Utils.TryParseEnumFromEnumMemberAttribute(input, out TestEnum actual);

        // Assert
        Assert.True(result);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryParseEnumFromEnumMemberAttribute_ShouldReturnFalse_WhenEnumMemberValueIsNull()
    {
        // Arrange
        string? input = null;

        // Act
        bool result = Utils.TryParseEnumFromEnumMemberAttribute(input!, out TestEnum actual);

        // Assert
        Assert.False(result);
        Assert.Equal(default, actual);
    }

    [Fact]
    public void DetermineMediaType_ShouldReturnCorrectMediaType()
    {
        // Arrange & Act & Assert
        Assert.Equal(MediaType.ApplicationPdf, Utils.DetermineMediaType("document.pdf"));
        Assert.Equal(MediaType.ImageJpeg, Utils.DetermineMediaType("image.jpg"));
        Assert.Equal(MediaType.ImageJpeg, Utils.DetermineMediaType("image.jpeg"));
        Assert.Equal(MediaType.ImagePng, Utils.DetermineMediaType("image.png"));
        Assert.Equal(MediaType.TextCsv, Utils.DetermineMediaType("data.csv"));
        Assert.Equal(MediaType.TextPlain, Utils.DetermineMediaType("file.txt"));
        Assert.Equal(MediaType.AudioMp3, Utils.DetermineMediaType("audio.mp3"));
        Assert.Equal(MediaType.AudioWav, Utils.DetermineMediaType("audio.wav"));
        Assert.Equal(MediaType.AudioOgg, Utils.DetermineMediaType("audio.ogg"));
        Assert.Equal(MediaType.AudioAac, Utils.DetermineMediaType("audio.aac"));
        Assert.Equal(MediaType.ApplicationJson, Utils.DetermineMediaType("data.json"));
        Assert.Equal(MediaType.ApplicationXml, Utils.DetermineMediaType("data.xml"));
        Assert.Equal(MediaType.ApplicationRtf, Utils.DetermineMediaType("document.rtf"));
        Assert.Equal(MediaType.ApplicationZip, Utils.DetermineMediaType("archive.zip"));
        Assert.Equal(MediaType.ApplicationVndMsExcel, Utils.DetermineMediaType("spreadsheet.xls"));
        Assert.Equal(MediaType.Unknown, Utils.DetermineMediaType("file"));
        Assert.Equal(MediaType.Unknown, Utils.DetermineMediaType(""));
    }

    [Fact]
    public void ConvertIntToExcelColumnLetters_ValidColumn_ReturnsCorrectLetters()
    {
        // Arrange
        int column = 1;

        // Act
        string result = Utils.ConvertIntToExcelColumnLetters(column);

        // Assert
        Assert.Equal("A", result);
    }

    [Fact]
    public void ConvertIntToExcelColumnLetters_ColumnGreaterThan26_ReturnsCorrectLetters()
    {
        // Arrange
        int column = 27;

        // Act
        string result = Utils.ConvertIntToExcelColumnLetters(column);

        // Assert
        Assert.Equal("AA", result);
    }

    [Fact]
    public void ConvertIntToExcelColumnLetters_ColumnGreaterThan52_ReturnsCorrectLetters()
    {
        // Arrange
        int column = 53;

        // Act
        string result = Utils.ConvertIntToExcelColumnLetters(column);

        // Assert
        Assert.Equal("BA", result);
    }

    [Fact]
    public void ConvertIntToExcelColumnLetters_ColumnGreaterThan702_ReturnsCorrectLetters()
    {
        // Arrange
        int column = 703;

        // Act
        string result = Utils.ConvertIntToExcelColumnLetters(column);

        // Assert
        Assert.Equal("AAA", result);
    }

    [Fact]
    public void ConvertIntToExcelColumnLetters_ColumnZero_ReturnsEmptyString()
    {
        // Arrange
        int column = 0;

        // Act
        string result = Utils.ConvertIntToExcelColumnLetters(column);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ConvertExcelColumnLettersToInt_ShouldReturnCorrectColumnNumber()
    {
        // Arrange
        var testCases = new Dictionary<string, int>
        {
            { "A", 1 },
            { "B", 2 },
            { "Z", 26 },
            { "AA", 27 },
            { "AB", 28 },
            { "ZY", 701 },
            { "ZZ", 702 },
            { "AAA", 703 },
            { "XFD", 16384 }
        };

        foreach ((string column, int expected) in testCases)
        {
            // Act
            int result = Utils.ConvertExcelColumnLettersToInt(column);

            // Assert
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void ConvertExcelColumnLettersToInt_InvalidColumn_ThrowsArgumentException()
    {
        // Arrange
        string column = "Invalid";

        // Act & Assert
        _ = Assert.Throws<ArgumentException>(() => Utils.ConvertExcelColumnLettersToInt(column));
    }

    [Fact]
    public void SetStringToTag_WhenValidInput_ShouldReplaceContent()
    {
        // Arrange
        string input = "<tag>old content</tag>";
        string newValue = "new content";
        string expected =
        """
        <tag>
        new content
        </tag>
        """;

        // Act
        string? result = Utils.SetStringToTag(input, "tag", newValue);

        // Assert
        Assert.Equal(expected, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void SetStringToTag_WhenNullInput_ShouldReturnNull()
    {
        // Act
        string? result = Utils.SetStringToTag(null, "tag", "value");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SetStringToTag_WhenTagNotFound_ShouldReturnOriginalString()
    {
        // Arrange
        string input = "<othertag>content</othertag>";

        // Act
        string? result = Utils.SetStringToTag(input, "tag", "new content");

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void SetStringToTag_WhenNoClosingTag_ShouldReturnOriginalString()
    {
        // Arrange
        string input = "<tag>content";

        // Act
        string? result = Utils.SetStringToTag(input, "tag", "new content");

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void SetStringToTag_WithMixedCase_ShouldWork()
    {
        // Arrange
        string input = "<TAG>old content</TAG>";
        string newValue = "new content";
        string expected =
        """
        <TAG>
        new content
        </TAG>
        """;

        // Act
        string? result = Utils.SetStringToTag(input, "tag", newValue);

        // Assert
        Assert.Equal(expected, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void SetStringToTag_ShouldReplaceExistingTagContent()
    {
        // Arrange
        string input = "<test>old value</test>";
        string expected =
        """
        <test>
        new value
        </test>
        """;

        // Act
        string? result = Utils.SetStringToTag(input, "test", "new value");

        // Assert
        Assert.Equal(expected, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void SetStringToTag_WhenTagDoesNotExist_AndAppendIsFalse_ShouldReturnOriginalString()
    {
        // Arrange
        string input = "text without tags";

        // Act
        string? result = Utils.SetStringToTag(input, "test", "new value", false);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void SetStringToTag_WhenTagDoesNotExist_AndAppendIsTrue_ShouldAppendTagAndValue()
    {
        // Arrange
        string input = "text without tags";
        string expected =
        """
        text without tags

        <test>
        new value
        </test>
        """;

        // Act
        string? result = Utils.SetStringToTag(input, "test", "new value", true);

        // Assert
        Assert.Equal(expected, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void SetStringToTag_WhenInputIsNull_ShouldReturnNull()
    {
        // Act
        string? result = Utils.SetStringToTag(null, "test", "new value");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SetStringToTag_ShouldBeCaseInsensitive()
    {
        // Arrange
        string input = "<TEST>old value</TEST>";
        string expected =
        """
        <TEST>
        new value
        </TEST>
        """;

        // Act
        string? result = Utils.SetStringToTag(input, "test", "new value");

        // Assert
        Assert.Equal(expected, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void CleanWhitespace_WhenInputIsNull_ShouldReturnNull()
    {
        // Arrange
        string? input = null;

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CleanWhitespace_WhenInputIsWhitespace_ShouldReturnEmptyString()
    {
        // Arrange
        string input = "   \t\n\r  ";

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CleanWhitespace_WhenInputIsEmpty_ShouldReturnEmptyString()
    {
        // Arrange
        string input = "";

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CleanWhitespace_ShouldConvertMultipleSpacesToSingleSpace()
    {
        // Arrange
        string input = "Hello    world   with     multiple spaces";
        string expected = "Hello world with multiple spaces";

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CleanWhitespace_ShouldConvertMultipleNewlinesToSingleNewline()
    {
        // Arrange
        string input = "Line 1\n\n\nLine 2\n\n\n\nLine 3";
        string expected = "Line 1\n\nLine 2\n\nLine 3";

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CleanWhitespace_ShouldHandleWindowsLineEndings()
    {
        // Arrange
        string input = "Line 1\r\n\r\n\r\nLine 2";
        string expected = "Line 1\n\nLine 2";

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CleanWhitespace_ShouldHandleOldMacLineEndings()
    {
        // Arrange
        string input = "Line 1\r\r\rLine 2";
        string expected = "Line 1\n\nLine 2";

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CleanWhitespace_ShouldHandleMixedLineEndings()
    {
        // Arrange
        string input = "Line 1\r\n\n\r\nLine 2\r\rLine 3\n\nLine 4";
        string expected = "Line 1\n\nLine 2\n\nLine 3\n\nLine 4";

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CleanWhitespace_ShouldHandleBothSpacesAndNewlines()
    {
        // Arrange
        string input = "Hello   world\n\n\nWith    multiple   spaces\n\n\nAnd   newlines";
        string expected = "Hello world\n\nWith multiple spaces\n\nAnd newlines";

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CleanWhitespace_ShouldPreserveSingleSpacesAndNewlines()
    {
        // Arrange
        string input = "Normal text with single spaces\nAnd single newlines\nShould remain unchanged";
        string expected = "Normal text with single spaces\nAnd single newlines\nShould remain unchanged";

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CleanWhitespace_ShouldHandleTextWithTabsAndSpaces()
    {
        // Arrange
        string input = "Text\twith\t\ttabs   and    spaces";
        string expected = "Text\twith\t\ttabs and spaces";

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CleanWhitespace_ShouldHandleRealWorldExample()
    {
        // Arrange
        string input =
        """
        This is a paragraph with    multiple   spaces.


        This is another paragraph after several newlines.


        And this has both    multiple spaces   and


        multiple newlines.
        """;

        string expected =
        """
        This is a paragraph with multiple spaces.

        This is another paragraph after several newlines.

        And this has both multiple spaces and

        multiple newlines.
        """;

        // Act
        string? result = Utils.CleanWhitespace(input);

        // Assert
        Assert.Equal(expected, result, ignoreLineEndingDifferences: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void IsNaValue_WithNullOrWhitespace_ReturnsTrue(string? value)
    {
        // Act
        bool result = Utils.IsNaValue(value);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("N/A")]
    [InlineData("NA")]
    [InlineData("N.A.")]
    [InlineData("N.A")]
    [InlineData("NaN")]
    [InlineData("null")]
    public void IsNaValue_WithStandardNaValues_ReturnsTrue(string value)
    {
        // Act
        bool result = Utils.IsNaValue(value);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("n/a")]
    [InlineData("na")]
    [InlineData("n.a.")]
    [InlineData("n.a")]
    [InlineData("nan")]
    [InlineData("NULL")]
    [InlineData("Null")]
    public void IsNaValue_WithCaseInsensitiveNaValues_ReturnsTrue(string value)
    {
        // Act
        bool result = Utils.IsNaValue(value);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("valid value")]
    [InlineData("123")]
    [InlineData("Yes")]
    [InlineData("No")]
    [InlineData("0")]
    [InlineData("false")]
    public void IsNaValue_WithValidValues_ReturnsFalse(string value)
    {
        // Act
        bool result = Utils.IsNaValue(value);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsNaValue_WithAdditionalNaValues_DetectsCustomNaValues()
    {
        // Arrange
        string value = "no data";
        string[] additionalNaValues = ["no data", "missing"];

        // Act
        bool result = Utils.IsNaValue(value, additionalNaValues);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsNaValue_WithAdditionalNaValues_IsCaseInsensitive()
    {
        // Arrange
        string value = "NO DATA";
        string[] additionalNaValues = ["no data", "missing"];

        // Act
        bool result = Utils.IsNaValue(value, additionalNaValues);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsNaValue_WithAdditionalNaValues_StillDetectsStandardNaValues()
    {
        // Arrange
        string value = "N/A";
        string[] additionalNaValues = ["no data"];

        // Act
        bool result = Utils.IsNaValue(value, additionalNaValues);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsNaValue_WithNonMatchingAdditionalNaValues_ReturnsFalse()
    {
        // Arrange
        string value = "valid data";
        string[] additionalNaValues = ["no data", "missing"];

        // Act
        bool result = Utils.IsNaValue(value, additionalNaValues);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsNaValue_WithNullAdditionalNaValues_OnlyChecksStandardValues()
    {
        // Arrange
        string value = "N/A";

        // Act
        bool result = Utils.IsNaValue(value, null);

        // Assert
        Assert.True(result);
    }

    #region GetFirstNWords Tests

    [Fact]
    public void GetFirstNWords_WithNullInput_ReturnsEmptyString()
    {
        // Act
        string result = Utils.GetFirstNWords(null, 10);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetFirstNWords_WithEmptyInput_ReturnsEmptyString()
    {
        // Act
        string result = Utils.GetFirstNWords("", 10);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetFirstNWords_WithZeroWordCount_ReturnsEmptyString()
    {
        // Act
        string result = Utils.GetFirstNWords("Hello world", 0);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetFirstNWords_WithNegativeWordCount_ReturnsEmptyString()
    {
        // Act
        string result = Utils.GetFirstNWords("Hello world", -1);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetFirstNWords_WithSimpleText_ReturnsCorrectWords()
    {
        // Arrange
        string input = "Hello world this is a test";

        // Act
        string result = Utils.GetFirstNWords(input, 4);

        // Assert
        Assert.Equal("Hello world this is", result);
    }

    [Fact]
    public void GetFirstNWords_PreservesMultipleSpaces()
    {
        // Arrange
        string input = "Hello   world\n\tthis is   a test";

        // Act
        string result = Utils.GetFirstNWords(input, 4);

        // Assert
        Assert.Equal("Hello   world\n\tthis is", result);
    }

    [Fact]
    public void GetFirstNWords_WithFewerWordsThanRequested_ReturnsEntireInput()
    {
        // Arrange
        string input = "Hello world";

        // Act
        string result = Utils.GetFirstNWords(input, 10);

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void GetFirstNWords_WithExactWordCount_ReturnsEntireInput()
    {
        // Arrange
        string input = "Hello world";

        // Act
        string result = Utils.GetFirstNWords(input, 2);

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void GetFirstNWords_WithLeadingWhitespace_PreservesWhitespace()
    {
        // Arrange
        string input = "   Hello world test";

        // Act
        string result = Utils.GetFirstNWords(input, 2);

        // Assert
        Assert.Equal("   Hello world", result);
    }

    [Fact]
    public void GetFirstNWords_WithTabsAndNewlines_PreservesWhitespace()
    {
        // Arrange
        string input = "Word1\t\tWord2\n\nWord3   Word4";

        // Act
        string result = Utils.GetFirstNWords(input, 3);

        // Assert
        Assert.Equal("Word1\t\tWord2\n\nWord3", result);
    }

    [Fact]
    public void GetFirstNWords_WithSingleWord_ReturnsSingleWord()
    {
        // Arrange
        string input = "Hello";

        // Act
        string result = Utils.GetFirstNWords(input, 1);

        // Assert
        Assert.Equal("Hello", result);
    }

    #endregion

    #region GetLastNWords Tests

    [Fact]
    public void GetLastNWords_WithNullInput_ReturnsEmptyString()
    {
        // Act
        string result = Utils.GetLastNWords(null, 10);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetLastNWords_WithEmptyInput_ReturnsEmptyString()
    {
        // Act
        string result = Utils.GetLastNWords("", 10);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetLastNWords_WithZeroWordCount_ReturnsEmptyString()
    {
        // Act
        string result = Utils.GetLastNWords("Hello world", 0);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetLastNWords_WithNegativeWordCount_ReturnsEmptyString()
    {
        // Act
        string result = Utils.GetLastNWords("Hello world", -1);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetLastNWords_WithSimpleText_ReturnsCorrectWords()
    {
        // Arrange
        string input = "Hello world this is a test";

        // Act
        string result = Utils.GetLastNWords(input, 4);

        // Assert
        Assert.Equal("this is a test", result);
    }

    [Fact]
    public void GetLastNWords_PreservesMultipleSpaces()
    {
        // Arrange
        string input = "Hello   world\n\tthis is   a test";

        // Act
        string result = Utils.GetLastNWords(input, 4);

        // Assert
        Assert.Equal("this is   a test", result);
    }

    [Fact]
    public void GetLastNWords_WithFewerWordsThanRequested_ReturnsEntireInput()
    {
        // Arrange
        string input = "Hello world";

        // Act
        string result = Utils.GetLastNWords(input, 10);

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void GetLastNWords_WithExactWordCount_ReturnsEntireInput()
    {
        // Arrange
        string input = "Hello world";

        // Act
        string result = Utils.GetLastNWords(input, 2);

        // Assert
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void GetLastNWords_WithTrailingWhitespace_DoesNotIncludeTrailingWhitespace()
    {
        // Arrange
        string input = "Hello world test   ";

        // Act
        string result = Utils.GetLastNWords(input, 2);

        // Assert
        Assert.Equal("world test   ", result);
    }

    [Fact]
    public void GetLastNWords_WithTabsAndNewlines_PreservesWhitespace()
    {
        // Arrange
        string input = "Word1\t\tWord2\n\nWord3   Word4";

        // Act
        string result = Utils.GetLastNWords(input, 3);

        // Assert
        Assert.Equal("Word2\n\nWord3   Word4", result);
    }

    [Fact]
    public void GetLastNWords_WithSingleWord_ReturnsSingleWord()
    {
        // Arrange
        string input = "Hello";

        // Act
        string result = Utils.GetLastNWords(input, 1);

        // Assert
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void GetLastNWords_DoesNotIncludeLeadingWhitespaceFromMiddle()
    {
        // Arrange
        string input = "First    second third fourth";

        // Act
        string result = Utils.GetLastNWords(input, 2);

        // Assert
        Assert.Equal("third fourth", result);
    }

    #endregion
}
