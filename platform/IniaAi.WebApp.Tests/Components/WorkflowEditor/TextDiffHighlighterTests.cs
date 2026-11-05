using IniaAi.Testing;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using Microsoft.AspNetCore.Components;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class TextDiffHighlighterTests
{
    private const string REMOVED_OPEN = "<span class=\"mud-sr-only\">removed: </span><mark class=\"diff-removed\">";
    private const string ADDED_OPEN = "<span class=\"mud-sr-only\">added: </span><mark class=\"diff-added\">";
    private const string REMOVED_CLOSE = "</mark><span class=\"mud-sr-only\"> (end of removed) </span>";
    private const string ADDED_CLOSE = "</mark><span class=\"mud-sr-only\"> (end of added) </span>";
    private const string APP_CSS = "IniaAi.WebApp/wwwroot/app.css";

    private static string Merged(string? before, string? after)
    {
        MarkupString result = TextDiffHighlighter.HighlightMerged(before, after);

        return result.Value;
    }

    private static int CountOf(string html, string fragment)
    {
        int count = 0;
        int index = 0;
        while ((index = html.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    [Fact]
    public void EachChangedRun_TellsAScreenReaderWhetherItWasAddedOrRemoved_AndWhereItEnds()
    {
        string html = Merged("Revision 1 of the block.", "Revision 2 of the block.");

        Assert.Equal($"Revision {ADDED_OPEN}2 {ADDED_CLOSE}{REMOVED_OPEN}1 {REMOVED_CLOSE}of the block.", html);
    }

    [Fact]
    public void RemovedTextIsStruckThrough_AndAddedTextUnderlined_SoTheyDifferWithoutColour()
    {
        string css = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), APP_CSS));

        Assert.Contains("line-through", Rule(css, ".field-diff-pane mark.diff-removed"), StringComparison.Ordinal);
        Assert.Contains("underline", Rule(css, ".field-diff-pane mark.diff-added"), StringComparison.Ordinal);
        Assert.Contains("user-select: none", Rule(css, ".field-diff-pane .mud-sr-only"), StringComparison.Ordinal);
    }

    private static string Rule(string css, string selector)
    {
        int start = css.IndexOf($"\n{selector} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"app.css has no rule for {selector}");

        return css[start..css.IndexOf('}', start)];
    }

    [Fact]
    public void IdenticalTexts_ProduceNoMarks()
    {
        string html = Merged("The same prompt text.", "The same prompt text.");

        Assert.Equal("The same prompt text.", html);
        Assert.DoesNotContain("<mark", html);
    }

    [Fact]
    public void BothEmpty_ProducesEmptyOutput()
    {
        Assert.Equal(string.Empty, Merged(null, null));
        Assert.Equal(string.Empty, Merged(string.Empty, string.Empty));
    }

    [Fact]
    public void EmptyBefore_MarksEverythingAdded()
    {
        string html = Merged(null, "Brand new prompt");

        Assert.Equal($"{ADDED_OPEN}Brand new prompt{ADDED_CLOSE}", html);
        Assert.DoesNotContain(REMOVED_OPEN, html);
    }

    [Fact]
    public void EmptyAfter_MarksEverythingRemoved()
    {
        string html = Merged("Old prompt gone", string.Empty);

        Assert.Equal($"{REMOVED_OPEN}Old prompt gone{REMOVED_CLOSE}", html);
        Assert.DoesNotContain(ADDED_OPEN, html);
    }

    [Fact]
    public void WordReplacement_ShowsAddedBeforeRemoved_WithPlainAnchors()
    {
        string html = Merged("The price is five dollars", "The price is six dollars");

        Assert.StartsWith("The price is ", html, StringComparison.Ordinal);
        Assert.EndsWith("dollars", html, StringComparison.Ordinal);
        int addedIndex = html.IndexOf($"{ADDED_OPEN}six", StringComparison.Ordinal);
        int removedIndex = html.IndexOf($"{REMOVED_OPEN}five", StringComparison.Ordinal);
        Assert.True(addedIndex >= 0);
        Assert.True(removedIndex >= 0);
        Assert.True(addedIndex < removedIndex);
    }

    [Fact]
    public void PureInsertion_ProducesOnlyAddedMark()
    {
        string html = Merged("Start end", "Start middle end");

        Assert.Contains($"{ADDED_OPEN}middle ", html);
        Assert.DoesNotContain(REMOVED_OPEN, html);
    }

    [Fact]
    public void PureDeletion_ProducesOnlyRemovedMark()
    {
        string html = Merged("Start middle end", "Start end");

        Assert.Contains($"{REMOVED_OPEN}middle ", html);
        Assert.DoesNotContain(ADDED_OPEN, html);
    }

    [Fact]
    public void InsertedBlankLine_DoesNotFlagTheSurroundingWords()
    {
        string before = "<governance>\n4,14,16-20\n</governance>\n<capex>";
        string after = "<governance>\n4,14,16-20\n</governance>\n\nTesting here\n\n<capex>";

        string html = Merged(before, after);

        Assert.DoesNotContain(REMOVED_OPEN, html);
        Assert.DoesNotContain("governance&gt;</mark>", html);
        Assert.Contains($"{ADDED_OPEN}Testing here", html);
    }

    [Fact]
    public void MatchedTokens_RenderAfterSideWhitespace()
    {
        string html = Merged("word\nnext", "word\n\n\nnext");

        Assert.DoesNotContain("<mark", html);
        Assert.Contains("word\n\n\nnext", html);
    }

    [Fact]
    public void HtmlInContent_IsEncoded_OnlyTheMarksAndTheirHiddenLabelsAreRawMarkup()
    {
        string html = Merged("safe <script>alert(1)</script>", "safe <b>bold</b> & more");

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;b&gt;", html);
        Assert.Contains("&amp;", html);
        int totalTags = CountOf(html, "<");
        int ownTags = CountOf(html, "<mark") + CountOf(html, "</mark>")
            + CountOf(html, "<span class=\"mud-sr-only\">") + CountOf(html, "</span>");
        Assert.Equal(ownTags, totalTags);
    }

    [Fact]
    public void ConsecutiveChangedWords_CoalesceIntoOneMarkRun()
    {
        string html = Merged("Keep aaa bbb ccc keep", "Keep xxx yyy zzz keep");

        Assert.Equal(1, CountOf(html, ADDED_OPEN));
        Assert.Equal(1, CountOf(html, REMOVED_OPEN));
        Assert.Contains($"{ADDED_OPEN}xxx yyy zzz </mark>", html);
        Assert.Contains($"{REMOVED_OPEN}aaa bbb ccc </mark>", html);
    }

    [Fact]
    public void TwoSeparateEdits_ProduceTwoMarkRegions_AnchoredByCommonText()
    {
        string html = Merged(
            "alpha one bravo two charlie",
            "alpha ONE bravo TWO charlie");

        Assert.Equal(2, CountOf(html, ADDED_OPEN));
        Assert.Equal(2, CountOf(html, REMOVED_OPEN));
        int bravoIndex = html.IndexOf("bravo", StringComparison.Ordinal);
        int firstAdded = html.IndexOf(ADDED_OPEN, StringComparison.Ordinal);
        int lastAdded = html.LastIndexOf(ADDED_OPEN, StringComparison.Ordinal);
        Assert.True(firstAdded < bravoIndex);
        Assert.True(lastAdded > bravoIndex);
    }

    [Fact]
    public void Comparison_IsCaseSensitive()
    {
        string html = Merged("Word", "word");

        Assert.Contains($"{ADDED_OPEN}word</mark>", html);
        Assert.Contains($"{REMOVED_OPEN}Word</mark>", html);
    }

    [Fact]
    public void AnnotationAboveRemovedBlock_RendersAnnotationFirst()
    {
        string before = "</auditor>\n\n<liquidity>\n[pages]\n</liquidity>\n\n<capex>";
        string after = "</auditor>\n\nRemove below:\n\n<capex>";

        string html = Merged(before, after);

        int addedIndex = html.IndexOf($"{ADDED_OPEN}Remove below:", StringComparison.Ordinal);
        int removedIndex = html.IndexOf(REMOVED_OPEN, StringComparison.Ordinal);
        Assert.True(addedIndex >= 0);
        Assert.True(removedIndex >= 0);
        Assert.True(addedIndex < removedIndex);
        Assert.Contains("&lt;liquidity&gt;", html);
    }

    [Fact]
    public void LeadingWhitespace_IsPreserved()
    {
        string html = Merged("  indented text", "  indented text");

        Assert.StartsWith("  indented", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<mark", html);
    }

    [Fact]
    public void VeryLargeInputs_FallBackToWholesaleMarks_WithoutHanging()
    {
        string sharedPrefix = "PREFIX ";
        string before = sharedPrefix + string.Join(" ", Enumerable.Range(0, 2500).Select(i => $"old{i}"));
        string after = sharedPrefix + string.Join(" ", Enumerable.Range(0, 2500).Select(i => $"new{i}"));

        string html = Merged(before, after);

        Assert.StartsWith("PREFIX ", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(html, ADDED_OPEN));
        Assert.Equal(1, CountOf(html, REMOVED_OPEN));
        Assert.Contains("old0", html);
        Assert.Contains("new0", html);
    }

    [Fact]
    public void SignaturePlaceholders_DiffLikeOrdinaryWords()
    {
        string html = Merged(
            "Sign here {client_signature} thanks",
            "Sign here {advisor_signature} thanks");

        Assert.Contains($"{ADDED_OPEN}{{advisor_signature}} </mark>", html);
        Assert.Contains($"{REMOVED_OPEN}{{client_signature}} </mark>", html);
    }

    [Fact]
    public void UnchangedTail_AfterEdit_StaysPlain()
    {
        string html = Merged(
            "Edit this word then a very long unchanged tail follows here",
            "Edit that word then a very long unchanged tail follows here");

        Assert.EndsWith("tail follows here", html, StringComparison.Ordinal);
        int closeOfLastMark = html.LastIndexOf("</mark>", StringComparison.Ordinal);
        Assert.DoesNotContain("<mark", html[(closeOfLastMark + "</mark>".Length)..]);
    }

    [Fact]
    public void ByLine_AMovedLine_IsRemovedAndAddedWhole()
    {
        string html = TextDiffHighlighter.HighlightMergedLines(
            "iSearch Lookback Period\niSearch Lookback Period - Clean",
            "iSearch Lookback Period - Clean\niSearch Lookback Period").Value;

        Assert.Equal(
            $"{REMOVED_OPEN}iSearch Lookback Period\n{REMOVED_CLOSE}iSearch Lookback Period - Clean\n{ADDED_OPEN}iSearch Lookback Period{ADDED_CLOSE}",
            html);
    }
}
