using System.Xml.Linq;
using IniaAi.Testing.Documents;
using IniaAi.Testing.Ooxml;

namespace IniaAi.FileFormats.Tests.Word;

public class WordFileTests
{
    private const string LETTER_PAGE_SECTION =
        """<w:p><w:pPr><w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr></w:pPr></w:p>""";

    [Fact]
    public void Text_ReadsTheRunsOfAHyperlinkWhereTheyStand()
    {
        WordFile file = new(WordFixtures.FromXml(
            """<w:p><w:r><w:t xml:space="preserve">See </w:t></w:r><w:hyperlink w:anchor="Methodology"><w:r><w:t>the method</w:t></w:r></w:hyperlink><w:r><w:t xml:space="preserve"> for details.</w:t></w:r></w:p>"""));

        Assert.Equal("See the method for details.", file.Text);
    }

    [Fact]
    public void Text_ReadsTheRunsInsideContentControlsCustomXmlAndMovedText()
    {
        WordFile file = new(WordFixtures.FromXml(
            """<w:p><w:r><w:t xml:space="preserve">A </w:t></w:r><w:sdt><w:sdtPr/><w:sdtContent><w:r><w:t xml:space="preserve">B </w:t></w:r></w:sdtContent></w:sdt><w:customXml w:element="note"><w:r><w:t xml:space="preserve">C </w:t></w:r></w:customXml><w:moveTo w:id="1" w:author="Analyst" w:date="2026-01-01T00:00:00Z"><w:r><w:t>D</w:t></w:r></w:moveTo></w:p>"""));

        Assert.Equal("A B C D", file.Text);
    }

    [Fact]
    public void HyperlinkAddresses_ReadFieldsWhoseCodeIsSplitAcrossRuns_InDocumentOrder()
    {
        WordFile file = new(WordFixtures.FromXml(
            """
            <w:p>
            <w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve"> HYPERLINK </w:instrText></w:r><w:r><w:instrText>"mailto:desk@example.com"</w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>the desk</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r>
            <w:hyperlink w:anchor="Methodology"><w:r><w:t>the method</w:t></w:r></w:hyperlink>
            <w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve">HYPERLINK "/reports/</w:instrText></w:r><w:r><w:instrText>42"</w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>the report</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r>
            <w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve">HYPERLINK \l </w:instrText></w:r><w:r><w:instrText>"Risks"</w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>the risks</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r>
            </w:p>
            """));

        Assert.Equal(["mailto:desk@example.com", "Methodology", "/reports/42", "Risks"], file.HyperlinkAddresses);
    }

    [Fact]
    public void ShowsPhrase_OfAnEmptyPhrase_IsFalse()
    {
        WordFile file = new(WordFixtures.Document("Body"));

        Assert.False(file.ShowsPhrase(file.Paragraphs[0], string.Empty, _ => true));
    }

    [Fact]
    public void ShowsPhrase_SkipsARunThatShowsNoText()
    {
        WordFile file = new(WordFixtures.FromXml(
            """<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Bo</w:t></w:r><w:r><w:lastRenderedPageBreak/></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>ld</w:t></w:r></w:p>"""));

        Assert.True(file.ShowsPhrase(file.Paragraphs[0], "Bold", file.IsBold));
    }

    [Fact]
    public void CellWidth_CountsTheColumnsARowSkips_AndTheCellsInsideContentControls()
    {
        WordFile file = new(WordFixtures.FromXml(
            """
            <w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="1000"/><w:gridCol w:w="2000"/><w:gridCol w:w="3000"/></w:tblGrid>
            <w:tr><w:trPr><w:gridBefore w:val="1"/></w:trPr>
            <w:sdt><w:sdtContent><w:tc><w:p><w:r><w:t>Wrapped</w:t></w:r></w:p></w:tc></w:sdtContent></w:sdt>
            <w:tc><w:p><w:r><w:t>Last</w:t></w:r></w:p></w:tc>
            </w:tr></w:tbl><w:p/>
            """));

        Assert.Equal(2000, WordFile.CellWidth(Paragraph(file, "Wrapped")));
        Assert.Equal(3000, WordFile.CellWidth(Paragraph(file, "Last")));
    }

    [Fact]
    public void CellWidth_OfAPercentCellInATableWithoutColumnWidths_IsItsShareOfThePageText()
    {
        WordFile file = new(WordFixtures.FromXml(
            """
            <w:tbl><w:tblPr><w:tblW w:w="5000" w:type="pct"/></w:tblPr><w:tblGrid><w:gridCol/><w:gridCol/></w:tblGrid>
            <w:tr><w:tc><w:tcPr><w:tcW w:w="2500" w:type="pct"/></w:tcPr><w:p><w:r><w:t>Fiftieths</w:t></w:r></w:p></w:tc>
            <w:tc><w:tcPr><w:tcW w:w="50%" w:type="pct"/></w:tcPr><w:p><w:r><w:t>Percent</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            """ + LETTER_PAGE_SECTION));

        Assert.Equal(4680, WordFile.CellWidth(Paragraph(file, "Fiftieths")));
        Assert.Equal(4680, WordFile.CellWidth(Paragraph(file, "Percent")));
    }

    [Fact]
    public void ListLevel_ComesFromTheStyle_WhenTheParagraphsOwnNumberingSetsOnlyTheLevel()
    {
        WordFile file = new(WordFixtures.FromXml(
            """<w:p><w:pPr><w:pStyle w:val="ListPara"/><w:numPr><w:ilvl w:val="1"/></w:numPr></w:pPr><w:r><w:t>Item</w:t></w:r></w:p>""",
            stylesXml: """<w:style w:type="paragraph" w:styleId="ListPara"><w:name w:val="List Para"/><w:pPr><w:numPr><w:numId w:val="1"/></w:numPr></w:pPr></w:style>""",
            numberingXml:
            """
            <w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl><w:lvl w:ilvl="1"><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%2)"/></w:lvl></w:abstractNum>
            <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
            """));
        XElement item = Paragraph(file, "Item");

        Assert.True(file.IsNumberedListItem(item));
        Assert.Equal(1, file.ListLevelNumber(item));
        Assert.Equal("%2)", file.ListMarker(item));
    }

    [Fact]
    public void ListLevel_FollowsAListThatLinksToANumberingStyle()
    {
        WordFile file = new(WordFixtures.FromXml(
            """<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>Item</w:t></w:r></w:p>""",
            stylesXml: """<w:style w:type="numbering" w:styleId="OutlineList"><w:name w:val="Outline List"/><w:pPr><w:numPr><w:numId w:val="2"/></w:numPr></w:pPr></w:style>""",
            numberingXml:
            """
            <w:abstractNum w:abstractNumId="10"><w:numStyleLink w:val="OutlineList"/></w:abstractNum>
            <w:abstractNum w:abstractNumId="20"><w:styleLink w:val="OutlineList"/><w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/><w:lvlText w:val="%1)"/></w:lvl></w:abstractNum>
            <w:num w:numId="1"><w:abstractNumId w:val="10"/></w:num>
            <w:num w:numId="2"><w:abstractNumId w:val="20"/></w:num>
            """));
        XElement item = Paragraph(file, "Item");

        Assert.True(file.IsNumberedListItem(item));
        Assert.Equal("%1)", file.ListMarker(item));
    }

    [Fact]
    public void ATextBoxWithAFallback_CountsAsOneDrawing_WithItsTextOnce()
    {
        WordFile file = new(WordFixtures.FromXml(
            """
            <w:p><w:r><mc:AlternateContent>
            <mc:Choice Requires="wps"><w:drawing><wp:anchor><a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><wps:wsp><wps:txbx><w:txbxContent><w:p><w:r><w:t>Boxed</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></mc:Choice>
            <mc:Fallback><w:pict><v:shape style="position:absolute"><v:textbox><w:txbxContent><w:p><w:r><w:t>Boxed</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback>
            </mc:AlternateContent></w:r></w:p>
            """));

        Assert.Equal(1, file.Drawings);
        Assert.Single(file.ParagraphsWith("Boxed"));
        Assert.Single(file.AllText.Split('\n'), line => line == "Boxed");
    }

    [Fact]
    public void HorizontalRules_CountAnEmptyParagraphWhoseStyleDrawsABorder()
    {
        WordFile file = new(WordFixtures.FromXml(
            """<w:p><w:pPr><w:pStyle w:val="Rule"/></w:pPr></w:p><w:p><w:r><w:t>Title</w:t></w:r></w:p>""",
            stylesXml: """<w:style w:type="paragraph" w:styleId="Rule"><w:name w:val="Rule"/><w:pPr><w:pBdr><w:bottom w:val="single" w:sz="6" w:space="1" w:color="auto"/></w:pBdr></w:pPr></w:style>"""));

        Assert.Equal(1, file.HorizontalRules);
    }

    [Fact]
    public void Alignment_ComesFromTheDefaultParagraphStyle_HoweverItsDefaultFlagIsWritten()
    {
        WordFile file = new(WordFixtures.FromXml(
            """<w:p><w:r><w:t>Text</w:t></w:r></w:p>""",
            stylesXml: """<w:style w:type="paragraph" w:default="on" w:styleId="Body"><w:name w:val="Body"/><w:pPr><w:jc w:val="both"/></w:pPr></w:style>"""));

        Assert.Equal("both", file.Alignment(Paragraph(file, "Text")));
    }

    private static XElement Paragraph(WordFile file, string phrase)
    {
        return Assert.Single(file.ParagraphsWith(phrase));
    }
}
