using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Tests.Workflows.Annotations;

/// <summary>
/// Integration tests focused on numerical fact source expansion through block chains.
/// Models production data from regulated fund credit analysis workflows.
///
/// Goal: Numerical facts from Document/URL sources in early blocks should retain
/// those original sources as they propagate through downstream blocks.
///
/// These tests define CORRECT behavior. Failures indicate bugs to fix.
/// </summary>
public class AnnotationPipelineIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public AnnotationPipelineIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Simple Chain — Number Propagates Unchanged

    [Fact]
    public void ThreeBlockChain_NumericalFact_RetainsDocumentSource()
    {
        // Models: AUM extraction → Consolidation → Final Write Up
        // "$183,990 million" from KIID.pdf where CitedText contains the number.
        var factA = new Fact("As of April 30, 2025, the fund had a NAV of $183,990 million");
        factA.Sources.Add(new DocumentSource
        {
            CitedText = "Fund net assets (in millions) $183,990 million as reported",
            DocumentId = "doc-kiid",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "KIID.pdf"
        });

        BlockAnnotations blockA = CreateAnnotationsFromBroadFacts(factA);
        LogBlock("Block A (AUM Extraction)", blockA);

        // Verify: numerical "$183,990 million" should have DocSource at origin
        Fact aNav = blockA.Facts.First(f => f.IsNumerical && f.Value == "$183,990 million");
        Assert.Contains(aNav.Sources, s => s is DocumentSource);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-a", blockA);

        var factB = new Fact("The fund had a NAV of $183,990 million as of April 2025");
        factB.Sources.Add(new BlockSource
        {
            CitedText = "As of April 30, 2025, the fund had a NAV of $183,990 million",
            BlockId = "block-a",
            BlockTitle = "AUM"
        });

        BlockAnnotations blockB = CreateAnnotationsFromBroadFacts(factB);
        _output.WriteLine("── Block B BEFORE expansion ──");
        LogBlock("Block B (Consolidation)", blockB);

        InheritFactsFromBlocks(
            blockB, context, ["block-a"],
            "The fund had a NAV of $183,990 million as of April 2025.");

        _output.WriteLine("── Block B AFTER expansion ──");
        LogBlock("Block B (Consolidation)", blockB);

        Fact bNav = blockB.Facts.First(f => f.IsNumerical && f.Value == "$183,990 million");
        Assert.True(bNav.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf"),
            "Block B's '$183,990 million' should have DocumentSource from Block A");

        context.SetAnnotationsForBlock("block-b", blockB);

        var factC = new Fact("Fund NAV is $183,990 million");
        factC.Sources.Add(new BlockSource
        {
            CitedText = "The fund had a NAV of $183,990 million as of April 2025",
            BlockId = "block-b",
            BlockTitle = "Consolidation"
        });

        BlockAnnotations blockC = CreateAnnotationsFromBroadFacts(factC);
        InheritFactsFromBlocks(
            blockC, context, ["block-b"],
            "Fund NAV is $183,990 million.");

        _output.WriteLine("── Block C AFTER expansion ──");
        LogBlock("Block C (Final Write Up)", blockC);

        Fact cNav = blockC.Facts.First(f => f.IsNumerical && f.Value == "$183,990 million");
        Assert.True(cNav.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf"),
            "Final Write Up '$183,990 million' should trace to KIID.pdf through the chain");
    }

    #endregion

    #region Page Header CitedText — Production Bug

    [Fact]
    public void NumericalSubFact_WhenDocSourceCitedTextLacksExactNumber_ShouldRetainDocSource()
    {
        // Production case from Final Write Up block:
        // Broad fact "As of April 30, 2025, the fund had a NAV of $183,990 million USD"
        // has DocumentSource from KIID.pdf page 2, but the CitedText is the page header
        // "Key fund statistics...Fund net assets (in millions) $183,990" — note "$183,990"
        // WITHOUT "million" suffix. NumericalDataExtractor extracts "$183,990 million" as a
        // unit (currency + multiplier), so CitedText.Contains("$183,990 million") is FALSE.
        //
        // The Document IS the source. The sub-fact should retain it.

        var broadFact = new Fact("As of April 30, 2025, the fund had a NAV of $183,990 million USD");
        broadFact.Sources.Add(new BlockSource
        {
            CitedText = "As of April 30, 2025, the fund had a NAV of $183,990 million USD",
            BlockId = "block-crosscheck",
            BlockTitle = "Cross-Check Against Fact Registry"
        });
        broadFact.Sources.Add(new DocumentSource
        {
            CitedText = "Key fund statistics\n\nFund net assets (in millions)               $183,990",
            DocumentId = "doc-kiid",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "KIID.pdf"
        });
        broadFact.Sources.Add(new DocumentSource
        {
            CitedText = "Washington\n     Mutual Investors\n     Fund\n\n\n     Financial Statements and Other Information\n     N-CSR Items 7-11\n     for the year ended April 30, 2025",
            DocumentId = "doc-annual-report",
            PageId = "page-1",
            PageNumber = 1,
            DocumentName = "WM Annual Report.pdf"
        });

        _output.WriteLine("── Broad fact sources BEFORE CreateNumericalFacts ──");
        LogFact(broadFact);

        List<Fact> result = BlockAnnotationProcessor.CreateNumericalFacts(broadFact);

        _output.WriteLine("── ALL facts AFTER CreateNumericalFacts ──");
        foreach (Fact fact in result)
        {
            LogFact(fact);
        }

        Fact navFact = result.First(f => f.IsNumerical && f.Value == "$183,990 million");

        // BlockSource CitedText DOES contain "$183,990 million" — should keep it
        Assert.Contains(navFact.Sources, s => s is BlockSource);

        // KIID.pdf CitedText has "$183,990" but NOT "$183,990 million" as extracted.
        // The document IS the source of the number — should be retained.
        Assert.True(navFact.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf"),
            "Numerical sub-fact should retain KIID.pdf DocumentSource even when " +
            "CitedText '$183,990' doesn't match extracted value '$183,990 million'");
    }

    [Fact]
    public void NumericalSubFact_Percentage_WhenDocSourceCitedTextLacksValue_DoesNotInheritDocSource()
    {
        // Production case: broad fact "...94.6% in High Liquidity assets and 3.0% cash..."
        // has DocumentSources whose CitedTexts don't contain "94.6%" or "3.0%". Under the
        // grounding invariant, those percentages keep only the BlockSource that actually
        // mentions them — unrelated DocumentSources are NOT inherited. Numbers that DO
        // appear in some DocumentSource's CitedText (e.g. "April 30, 2025" in WM Annual
        // Report) are still attached to that source via Step 1.

        var broadFact = new Fact(
            "The fund demonstrates an exceptionally strong liquidity profile " +
            "with 94.6% in High Liquidity assets and 3.0% cash & equivalents, " +
            "with 100% of investments classified as Level 1 as of April 30, 2025.");
        broadFact.Sources.Add(new BlockSource
        {
            CitedText = "The fund demonstrates an exceptionally strong liquidity profile " +
                        "with 94.6% in High Liquidity assets and 3.0% cash & equivalents, " +
                        "with 100% of investments classified as Level 1 as of April 30, 2025.",
            BlockId = "block-crosscheck",
            BlockTitle = "Cross-Check Against Fact Registry"
        });
        broadFact.Sources.Add(new DocumentSource
        {
            // Production: KIID.pdf page 2 — page header with fund statistics table.
            // Contains "$183,990" but NOT "94.6%" or "3.0%" or "100%" or "April 30, 2025".
            CitedText = "Key fund statistics\n\nFund net assets (in millions)               $183,990\n" +
                        "Information technology 20.04%\nHealth care 15.33%",
            DocumentId = "doc-kiid",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "KIID.pdf"
        });
        broadFact.Sources.Add(new DocumentSource
        {
            // Production: WM Annual Report cover page — contains "April 30, 2025".
            CitedText = "Washington Mutual Investors Fund\n\nFinancial Statements\nfor the year ended April 30, 2025",
            DocumentId = "doc-annual-report",
            PageId = "page-1",
            PageNumber = 1,
            DocumentName = "WM Annual Report.pdf"
        });

        _output.WriteLine("── Broad fact sources BEFORE CreateNumericalFacts ──");
        LogFact(broadFact);

        List<Fact> result = BlockAnnotationProcessor.CreateNumericalFacts(broadFact);

        _output.WriteLine("── ALL facts AFTER CreateNumericalFacts ──");
        foreach (Fact fact in result)
        {
            LogFact(fact);
        }

        // "94.6%" — only BlockSource contains it. No DocumentSource should be inherited.
        Fact liquidity = result.First(f => f.IsNumerical && f.Value == "94.6%");
        _ = Assert.Single(liquidity.Sources);
        _ = Assert.IsType<BlockSource>(liquidity.Sources[0]);

        // "3.0%" — same: BlockSource only.
        Fact cash = result.First(f => f.IsNumerical && f.Value == "3.0%");
        _ = Assert.Single(cash.Sources);
        _ = Assert.IsType<BlockSource>(cash.Sources[0]);

        // "100%" — same: BlockSource only.
        Fact level1 = result.First(f => f.IsNumerical && f.Value == "100%");
        _ = Assert.Single(level1.Sources);
        _ = Assert.IsType<BlockSource>(level1.Sources[0]);

        // "April 30, 2025" — appears in BlockSource AND WM Annual Report's CitedText.
        // Both are attached via Step 1; KIID is NOT inherited.
        Fact date = result.First(f => f.IsNumerical && f.Value == "April 30, 2025");
        Assert.Contains(date.Sources, s => s is BlockSource);
        Assert.Contains(date.Sources, s => s is DocumentSource ds && ds.DocumentName == "WM Annual Report.pdf");
        Assert.DoesNotContain(date.Sources, s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf");
    }

    #endregion

    #region Multiple Numbers — No Source Contamination

    [Fact]
    public void MultipleNumbers_EachRetainsOnlyItsOwnSource()
    {
        // Two numbers from different source types in Block A.
        // Each numerical sub-fact in Block B should only get its relevant source.

        var factA1 = new Fact("Fund net assets of $183,990 million");
        factA1.Sources.Add(new DocumentSource
        {
            CitedText = "Fund net assets (in millions) $183,990 million",
            DocumentId = "doc-kiid",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "KIID.pdf"
        });

        var factA2 = new Fact("Expense ratio of 0.55%");
        factA2.Sources.Add(new UrlSource
        {
            CitedText = "The fund has an expense ratio of 0.55%",
            Url = "https://www.capitalgroup.com/individual/investments/fund/awshx"
        });

        BlockAnnotations blockA = CreateAnnotationsFromBroadFacts(factA1, factA2);
        LogBlock("Block A (Data Extraction)", blockA);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-a", blockA);

        // Block B combines both numbers, cites A with a single BlockSource
        var factB = new Fact("The fund has $183,990 million in net assets with 0.55% expense ratio");
        factB.Sources.Add(new BlockSource
        {
            CitedText = "Fund net assets of $183,990 million. Expense ratio of 0.55%.",
            BlockId = "block-a",
            BlockTitle = "AUM Extraction"
        });

        BlockAnnotations blockB = CreateAnnotationsFromBroadFacts(factB);
        InheritFactsFromBlocks(
            blockB, context, ["block-a"],
            "The fund has $183,990 million in net assets with 0.55% expense ratio.");

        _output.WriteLine("── Block B AFTER expansion ──");
        LogBlock("Block B", blockB);

        // "$183,990 million" should have DocumentSource, NOT UrlSource
        Fact bNav = blockB.Facts.First(f => f.IsNumerical && f.Value == "$183,990 million");
        Assert.True(bNav.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf"),
            "$183,990 million should have DocumentSource from KIID.pdf");
        Assert.DoesNotContain(bNav.Sources, s => s is UrlSource);

        // "0.55%" should have UrlSource, NOT DocumentSource
        Fact bExpense = blockB.Facts.First(f => f.IsNumerical && f.Value == "0.55%");
        Assert.True(bExpense.Sources.Any(s => s is UrlSource),
            "0.55% should have UrlSource from capitalgroup.com");
        Assert.DoesNotContain(bExpense.Sources, s => s is DocumentSource);
    }

    #endregion

    #region Number Re-Synthesis — Shape Change

    [Fact]
    public void ResynthesizedNumber_ShouldTraceToOriginalSource()
    {
        // "$183,990 million" is re-synthesized as "$184 billion" in a downstream block.
        // The BlockSource CitedText from upstream still contains "$183,990 million".
        // The numerical sub-fact "$184 billion" should trace back to the original
        // DocumentSource via 1% relative numeric equivalence: at the within-block
        // grounding (FactExtractor Step 3) it matches the BlockSource CitedText
        // ("$183,990 million" ≈ "$184 billion" within tolerance), and at cross-block
        // expansion (BlockAnnotationProcessor) it inherits the upstream DocumentSource.
        var factA = new Fact("Fund net assets of $183,990 million");
        factA.Sources.Add(new DocumentSource
        {
            CitedText = "Fund net assets (in millions) $183,990 million",
            DocumentId = "doc-kiid",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "KIID.pdf"
        });

        BlockAnnotations blockA = CreateAnnotationsFromBroadFacts(factA);
        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-a", blockA);

        var factB = new Fact("The fund has net assets of $183,990 million");
        factB.Sources.Add(new BlockSource
        {
            CitedText = "Fund net assets of $183,990 million",
            BlockId = "block-a",
            BlockTitle = "AUM"
        });

        BlockAnnotations blockB = CreateAnnotationsFromBroadFacts(factB);
        InheritFactsFromBlocks(
            blockB, context, ["block-a"],
            "The fund has net assets of $183,990 million.");
        context.SetAnnotationsForBlock("block-b", blockB);

        _output.WriteLine("── Block B AFTER expansion ──");
        LogBlock("Block B (Unchanged)", blockB);

        var factC = new Fact("The fund manages approximately $184 billion in assets");
        factC.Sources.Add(new BlockSource
        {
            CitedText = "The fund has net assets of $183,990 million",
            BlockId = "block-b",
            BlockTitle = "Consolidation"
        });

        BlockAnnotations blockC = CreateAnnotationsFromBroadFacts(factC);
        _output.WriteLine("── Block C BEFORE expansion ──");
        LogBlock("Block C (Re-synthesis)", blockC);

        InheritFactsFromBlocks(
            blockC, context, ["block-b"],
            "The fund manages approximately $184 billion in assets.");

        _output.WriteLine("── Block C AFTER expansion ──");
        LogBlock("Block C (Re-synthesis)", blockC);

        Fact cResynthesized = blockC.Facts.First(f => f.IsNumerical && f.Value == "$184 billion");
        Assert.True(cResynthesized.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf"),
            "Re-synthesized '$184 billion' should trace back to KIID.pdf " +
            "from original '$183,990 million' via numeric equivalence");
    }

    #endregion

    #region Full Realistic Chain — Production Scenario

    [Fact]
    public void FourBlockChain_MultipleNumbers_AllTraceToOriginalSources()
    {
        // Models the production chain:
        // AUM & Liquidity (docs) → Consolidation → Cross-Check → Final Write Up
        //
        // Numbers: $183,990 million (KIID.pdf), 29% (KIID.pdf), 94.6% (awshx.pdf)
        // Each should trace to its original document at every stage.

        var factA1 = new Fact("As of April 30, 2025, the fund had a NAV of $183,990 million");
        factA1.Sources.Add(new DocumentSource
        {
            CitedText = "Fund net assets (in millions) $183,990 million reported as of April 30, 2025",
            DocumentId = "doc-kiid",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "KIID.pdf"
        });

        var factA2 = new Fact("Portfolio turnover rate 29%");
        factA2.Sources.Add(new DocumentSource
        {
            CitedText = "Portfolio turnover rate 29%",
            DocumentId = "doc-kiid",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "KIID.pdf"
        });

        var factA3 = new Fact("The fund has 94.6% in High Liquidity assets");
        factA3.Sources.Add(new DocumentSource
        {
            CitedText = "High Liquidity 94.6% including U.S. Equities 91.6%",
            DocumentId = "doc-awshx",
            PageId = "page-4",
            PageNumber = 4,
            DocumentName = "awshx.pdf"
        });

        BlockAnnotations blockA = CreateAnnotationsFromBroadFacts(factA1, factA2, factA3);
        LogBlock("Block A (Data Extraction)", blockA);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-a", blockA);

        var factB = new Fact("NAV of $183,990 million, turnover 29%, with 94.6% high liquidity");
        factB.Sources.Add(new BlockSource
        {
            CitedText = "As of April 30, 2025, the fund had a NAV of $183,990 million. " +
                        "Portfolio turnover rate 29%. The fund has 94.6% in High Liquidity assets.",
            BlockId = "block-a",
            BlockTitle = "Data Extraction"
        });

        BlockAnnotations blockB = CreateAnnotationsFromBroadFacts(factB);
        InheritFactsFromBlocks(
            blockB, context, ["block-a"],
            "NAV of $183,990 million, turnover 29%, with 94.6% high liquidity.");

        _output.WriteLine("── Block B AFTER expansion ──");
        LogBlock("Block B (Consolidation)", blockB);

        context.SetAnnotationsForBlock("block-b", blockB);

        var factC = new Fact("Fund NAV $183,990 million, turnover 29%, 94.6% high liquidity confirmed");
        factC.Sources.Add(new BlockSource
        {
            CitedText = "NAV of $183,990 million, turnover 29%, with 94.6% high liquidity",
            BlockId = "block-b",
            BlockTitle = "Master Consolidation"
        });

        BlockAnnotations blockC = CreateAnnotationsFromBroadFacts(factC);
        InheritFactsFromBlocks(
            blockC, context, ["block-b"],
            "Fund NAV $183,990 million, turnover 29%, 94.6% high liquidity confirmed.");

        _output.WriteLine("── Block C AFTER expansion ──");
        LogBlock("Block C (Cross-Check)", blockC);

        context.SetAnnotationsForBlock("block-c", blockC);

        var factD = new Fact("The fund had a NAV of $183,990 million with turnover of 29% and 94.6% high liquidity assets");
        factD.Sources.Add(new BlockSource
        {
            CitedText = "Fund NAV $183,990 million, turnover 29%, 94.6% high liquidity confirmed",
            BlockId = "block-c",
            BlockTitle = "Cross-Check"
        });

        BlockAnnotations blockD = CreateAnnotationsFromBroadFacts(factD);
        InheritFactsFromBlocks(
            blockD, context, ["block-c"],
            "The fund had a NAV of $183,990 million with turnover of 29% and 94.6% high liquidity assets.");

        _output.WriteLine("── Block D AFTER expansion (FINAL) ──");
        LogBlock("Block D (Final Write Up)", blockD);

        Fact dNav = blockD.Facts.First(f => f.IsNumerical && f.Value == "$183,990 million");
        Assert.True(dNav.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf"),
            "$183,990 million in Final Write Up should trace to KIID.pdf");

        Fact dTurnover = blockD.Facts.First(f => f.IsNumerical && f.Value == "29%");
        Assert.True(dTurnover.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf"),
            "29% in Final Write Up should trace to KIID.pdf");

        Fact dLiquidity = blockD.Facts.First(f => f.IsNumerical && f.Value == "94.6%");
        Assert.True(dLiquidity.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "awshx.pdf"),
            "94.6% in Final Write Up should trace to awshx.pdf");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates block annotations from broad facts, running CreateNumericalFacts on each
    /// to split into numerical sub-facts. Mirrors the real pipeline where ProcessFacts
    /// calls ExtractFacts which calls CreateNumericalFacts.
    /// </summary>
    private static BlockAnnotations CreateAnnotationsFromBroadFacts(params Fact[] broadFacts)
    {
        var annotations = new BlockAnnotations();
        foreach (Fact broadFact in broadFacts)
        {
            annotations.AddFacts(BlockAnnotationProcessor.CreateNumericalFacts(broadFact));
        }

        return annotations;
    }

    /// <summary>
    /// Runs the handler's real propagation step over the facts the context holds for the
    /// referenced blocks, without WorkflowHandler, Block entities, or DI.
    /// </summary>
    private static void InheritFactsFromBlocks(
        BlockAnnotations blockAnnotations,
        WorkflowAnnotationsContext context,
        string[] referencedBlockIds,
        string? rawResponse)
    {
        IEnumerable<Fact> referencedFacts = referencedBlockIds
            .Select(id => context.GetAnnotationsForBlock(id))
            .Where(a => a is not null)
            .SelectMany(a => a!.Facts);

        BlockAnnotationProcessor.InheritReferencedFacts(blockAnnotations, referencedFacts, rawResponse);
    }

    private void LogBlock(string blockName, BlockAnnotations annotations)
    {
        _output.WriteLine($"┌─ {blockName} ({annotations.Facts.Count} facts) ─┐");
        foreach (Fact fact in annotations.Facts)
        {
            LogFact(fact);
        }

        _output.WriteLine("└────────────────────────────────────┘");
        _output.WriteLine("");
    }

    private void LogFact(Fact fact)
    {
        string tag = fact.IsNumerical ? "[NUM]" : "[BROAD]";
        string value = fact.Value.Length > 80 ? fact.Value[..77] + "..." : fact.Value;
        _output.WriteLine($"  {tag} \"{value}\"");

        if (fact.Sources.Count == 0)
        {
            _output.WriteLine("    └─ (no sources)");
            return;
        }

        foreach (BaseSource source in fact.Sources)
        {
            string citedPreview = source.CitedText.Length > 60
                ? source.CitedText[..57] + "..."
                : source.CitedText;
            citedPreview = citedPreview.Replace("\n", "\\n");

            switch (source)
            {
                case DocumentSource ds:
                    _output.WriteLine($"    └─ DocumentSource: {ds.DocumentName} p{ds.PageNumber} | cited: \"{citedPreview}\"");
                    break;
                case UrlSource us:
                    _output.WriteLine($"    └─ UrlSource: {us.Url} | cited: \"{citedPreview}\"");
                    break;
                case BlockSource bs:
                    _output.WriteLine($"    └─ BlockSource: [{bs.BlockTitle}] | cited: \"{citedPreview}\"");
                    break;
                default:
                    _output.WriteLine($"    └─ {source.GetType().Name}: \"{citedPreview}\"");
                    break;
            }
        }
    }

    #endregion
}
