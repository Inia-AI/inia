using IniaAi.Core.Services;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using System.Globalization;

namespace IniaAi.Core.Tests.Workflows.Outputs;

/// <summary>
/// End-to-end integration tests for the full output pipeline:
/// annotation propagation → WorkflowOutputBuilder.BuildParagraphAsync → ReportParagraph.
///
/// Exercises both source expansion mechanisms:
/// 1. ExpandSourcesFromReferencedFact — adds Doc/Url sources to fact.Sources (tight matching)
/// 2. ExpandUpstreamSources — gives each citation's BlockSource its own UpstreamSources (looser matching)
///
/// Tests define CORRECT behavior. Failures indicate bugs to fix.
/// </summary>
public class OutputPipelineIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly WorkflowOutputBuilder _builder;
    private readonly CultureInfo _originalCulture;

    public OutputPipelineIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        _ = new MockExchangeRateService();
        _builder = new WorkflowOutputBuilder();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        CultureInfo.CurrentCulture = _originalCulture;
    }

    #region Test 1 — Full Chain: Citations + Fact Markers

    [Fact]
    public async Task ThreeBlockChain_OutputElement_HasCorrectCitationsAndFactMarkers()
    {
        // Block A: Document extraction — facts from KIID.pdf and awshx.pdf
        Fact factA1 = new("Fund NAV of $183,866 million as of April 30, 2025");
        factA1.Sources.Add(new DocumentSource
        {
            CitedText = "Fund net assets (in millions) $183,866 million reported April 30, 2025",
            DocumentId = "doc-kiid",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "KIID.pdf"
        });

        Fact factA2 = new("High Liquidity assets 94.6%");
        factA2.Sources.Add(new DocumentSource
        {
            CitedText = "High Liquidity 94.6% including U.S. Equities",
            DocumentId = "doc-awshx",
            PageId = "page-4",
            PageNumber = 4,
            DocumentName = "awshx.pdf"
        });

        BlockAnnotations blockA = CreateAnnotationsFromBroadFacts(factA1, factA2);
        WorkflowAnnotationsContext annotationsContext = new();
        annotationsContext.SetAnnotationsForBlock("block-a", blockA);

        LogBlock("Block A (Data Extraction)", blockA);

        // Block B: Cross-Check — references A with BlockSources
        Fact factB = new(
            "The fund had a NAV of $183,866 million with 94.6% in High Liquidity assets");
        factB.Sources.Add(new BlockSource
        {
            CitedText = "Fund NAV of $183,866 million as of April 30, 2025. " +
                        "High Liquidity assets 94.6%.",
            BlockId = "block-a",
            BlockTitle = "Data Extraction"
        });

        BlockAnnotations blockB = CreateAnnotationsFromBroadFacts(factB);
        SimulateUpdateAnnotationsFromReferencedBlocks(
            blockB, annotationsContext, ["block-a"],
            "The fund had a NAV of $183,866 million with 94.6% in High Liquidity assets.");

        annotationsContext.SetAnnotationsForBlock("block-b", blockB);
        LogBlock("Block B (Cross-Check) AFTER expansion", blockB);

        // Block C: Final Write Up — references B
        string rawResponse =
            "The fund had a NAV of $183,866 million as of April 30, 2025, " +
            "with 94.6% in High Liquidity assets.";

        Fact factC = new(rawResponse);
        factC.Sources.Add(new BlockSource
        {
            CitedText = "The fund had a NAV of $183,866 million with 94.6% in " +
                        "High Liquidity assets",
            BlockId = "block-b",
            BlockTitle = "Cross-Check"
        });

        BlockAnnotations blockC = CreateAnnotationsFromBroadFacts(factC);
        SimulateUpdateAnnotationsFromReferencedBlocks(
            blockC, annotationsContext, ["block-b"], rawResponse);

        LogBlock("Block C (Final Write Up) AFTER expansion", blockC);

        // Build the output element
        Block block = CreateBlock(rawResponse);
        ReportParagraph output = await BuildParagraph(
            block, new WorkflowOutputContext(), blockC, annotationsContext);

        LogOutputElement(output);

        // Assert: numerical facts have <fact id="cX"> markers in content
        Assert.Contains("<fact id=", output.Content);
        Assert.Contains("$183,866 million", output.Content);
        Assert.Contains("94.6%", output.Content);

        // Assert: citations exist for numerical facts
        Citation? navCitation = output.Citations
            .FirstOrDefault(c => c.IsNumerical && output.Content!.Contains(
                $"<fact id=\"{c.Id}\">$183,866 million</fact>"));
        Assert.NotNull(navCitation);

        Citation? liquidityCitation = output.Citations
            .FirstOrDefault(c => c.IsNumerical && output.Content!.Contains(
                $"<fact id=\"{c.Id}\">94.6%</fact>"));
        Assert.NotNull(liquidityCitation);

        // Assert: "$183,866 million" citation has KIID.pdf as direct source (rule 2: doc/url preferred)
        Assert.True(
            navCitation!.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf"),
            "$183,866 million citation should have KIID.pdf DocumentSource");

        // Assert: "94.6%" citation has awshx.pdf as direct source
        Assert.True(
            liquidityCitation!.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "awshx.pdf"),
            "94.6% citation should have awshx.pdf DocumentSource");

        // Assert: flattened — no BlockSources in citations that have doc/url (rule 2)
        Assert.DoesNotContain(navCitation.Sources, s => s is BlockSource);
        Assert.DoesNotContain(liquidityCitation!.Sources, s => s is BlockSource);

        // Assert: no broad citation — only numerical facts get citations
        Assert.True(output.Citations.All(c => c.IsNumerical),
            "All citations should be numerical (broad facts are not applied)");
    }

    [Fact]
    public async Task DownstreamCitations_ResolveAgainstTheReleasedAttempt_NotASupersededRound()
    {
        // A verified member's annotations are written once per attempt; the released attempt's
        // write replaces the superseded round's. A downstream block reading the member's tag
        // must build its citations from the released attempt's facts only.
        WorkflowAnnotationsContext annotationsContext = new();

        Fact supersededFact = new("Fund NAV of $999,999 million");
        supersededFact.Sources.Add(new DocumentSource
        {
            CitedText = "superseded round text $999,999 million",
            DocumentId = "doc-old",
            PageId = "page-1",
            PageNumber = 1,
            DocumentName = "old-round.pdf",
        });
        annotationsContext.SetAnnotationsForBlock("member-a", CreateAnnotationsFromBroadFacts(supersededFact));

        Fact releasedFact = new("Fund NAV of $183,866 million");
        releasedFact.Sources.Add(new DocumentSource
        {
            CitedText = "released round text $183,866 million",
            DocumentId = "doc-released",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "released-round.pdf",
        });
        annotationsContext.SetAnnotationsForBlock("member-a", CreateAnnotationsFromBroadFacts(releasedFact));

        string rawResponse = "The fund reports a NAV of $183,866 million.";
        Fact downstreamFact = new(rawResponse);
        downstreamFact.Sources.Add(new BlockSource
        {
            CitedText = "Fund NAV of $183,866 million",
            BlockId = "member-a",
            BlockTitle = "Member A",
        });
        BlockAnnotations downstream = CreateAnnotationsFromBroadFacts(downstreamFact);
        SimulateUpdateAnnotationsFromReferencedBlocks(
            downstream, annotationsContext, ["member-a"], rawResponse);

        Block block = CreateBlock(rawResponse);
        ReportParagraph output = await BuildParagraph(
            block, new WorkflowOutputContext(), downstream, annotationsContext);

        Citation? citation = output.Citations
            .FirstOrDefault(c => c.IsNumerical && output.Content!.Contains(
                $"<fact id=\"{c.Id}\">$183,866 million</fact>"));
        Assert.NotNull(citation);
        Assert.Contains(citation.Sources, static source =>
            source is DocumentSource document && document.DocumentName == "released-round.pdf");
        Assert.DoesNotContain(citation.Sources, static source =>
            source is DocumentSource document && document.DocumentName == "old-round.pdf");
    }

    #endregion

    #region Test 2 — ExpandUpstreamSources Fills UpstreamSources

    [Fact]
    public async Task ExpandUpstreamSources_FillsTheCitedBlockSourcesUpstreamSources()
    {
        // Block A — post-Phase-B state: broad parent + numerical sub-fact,
        // sharing the same DocumentSource. SplitNumericalFacts produces this
        // shape from the AI's broad text segment.
        var docSource = new DocumentSource
        {
            CitedText = "Revenue: $5M in the quarter",
            DocumentId = "doc1",
            PageId = "page-1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        };

        Fact factABroad = new("Revenue was $5M");
        factABroad.Sources.Add(docSource);

        Fact factANumerical = new("$5M") { IsNumerical = true };
        factANumerical.Sources.Add(docSource);

        BlockAnnotations blockA = new();
        blockA.AddFact(factABroad);
        blockA.AddFact(factANumerical);

        WorkflowAnnotationsContext annotationsContext = new();
        annotationsContext.SetAnnotationsForBlock("block-a", blockA);

        // Block B: references A with a BlockSource
        Fact factB = new("$5M") { IsNumerical = true };
        factB.Sources.Add(new BlockSource
        {
            CitedText = "Revenue was $5M",
            BlockId = "block-a",
            BlockTitle = "Data Extraction"
        });

        BlockAnnotations blockB = new();
        blockB.AddFact(factB);

        // NOTE: We intentionally do NOT run ExpandSourcesFromReferencedFact here
        // to isolate ExpandUpstreamSources behavior during output building.

        Block block = CreateBlock("Revenue was $5M.");
        ReportParagraph output = await BuildParagraph(
            block, new WorkflowOutputContext(), blockB, annotationsContext);

        LogOutputElement(output);

        // Assert: citation exists with doc source pulled up from upstream (rule 3)
        _ = Assert.Single(output.Citations);
        Citation citation = output.Citations[0];

        // Rule 3: only BlockSource existed → doc/url pulled up from upstream → BlockSource removed
        Assert.Contains(citation.Sources,
            s => s is DocumentSource ds && ds.DocumentName == "Report.pdf");
        Assert.DoesNotContain(citation.Sources, s => s is BlockSource);

        _output.WriteLine($"Citation sources (flattened): {citation.Sources.Count}");
        foreach (BaseSource s in citation.Sources)
        {
            _output.WriteLine($"  {s.GetType().Name}: key={s.SourceKey}");
        }
    }

    #endregion

    #region Test 3 — Long CitedText Matches Multiple Facts

    [Fact]
    public async Task LongCitedText_ExpandsUpstreamSources_ForAllMatchingFacts()
    {
        // Block A: two unrelated facts
        Fact factA1 = new("$5M") { IsNumerical = true };
        factA1.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue: $5M in the quarter",
            DocumentId = "doc1",
            PageId = "page-1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });

        Fact factA2 = new("9,300 employees") { IsNumerical = true };
        factA2.Sources.Add(new UrlSource
        {
            CitedText = "The company employs over 9,300 people worldwide",
            Url = "https://example.com/about"
        });

        BlockAnnotations blockA = new();
        blockA.AddFact(factA1);
        blockA.AddFact(factA2);

        WorkflowAnnotationsContext annotationsContext = new();
        annotationsContext.SetAnnotationsForBlock("block-a", blockA);

        // Block B: single fact with BlockSource whose CitedText contains BOTH values
        Fact factB = new("Revenue was $5M and the company has 9,300 employees");
        factB.Sources.Add(new BlockSource
        {
            CitedText = "Revenue was $5M in the quarter. The company has 9,300 employees.",
            BlockId = "block-a",
            BlockTitle = "Data Extraction"
        });

        BlockAnnotations blockB = CreateAnnotationsFromBroadFacts(factB);

        // Run annotation propagation — ExpandSourcesFromReferencedFact has TIGHT matching
        SimulateUpdateAnnotationsFromReferencedBlocks(
            blockB, annotationsContext, ["block-a"],
            "Revenue was $5M and the company has 9,300 employees.");

        LogBlock("Block B AFTER annotation propagation", blockB);

        Block block = CreateBlock(
            "Revenue was $5M and the company has 9,300 employees.");
        ReportParagraph output = await BuildParagraph(
            block, new WorkflowOutputContext(), blockB, annotationsContext);

        LogOutputElement(output);

        // Find the "$5M" numerical fact's citation
        Fact fiveMFact = blockB.Facts.First(f => f.IsNumerical && f.Value == "$5M");
        _output.WriteLine($"\n$5M fact sources ({fiveMFact.Sources.Count}):");
        foreach (BaseSource s in fiveMFact.Sources)
        {
            _output.WriteLine($"  {s.GetType().Name}: key={s.SourceKey}");
        }

        // Annotation propagation (tight matching): $5M fact should have DocumentSource
        // because its Value "$5M" is contained in referenced fact "$5M"'s value
        Assert.True(fiveMFact.Sources.Any(s => s is DocumentSource),
            "$5M fact should have DocumentSource via ExpandSourcesFromReferencedFact");

        // $5M fact should NOT have UrlSource about employees
        // (ExpandSourcesFromReferencedFact requires fact.Value.Contains(ref.Value))
        Assert.DoesNotContain(fiveMFact.Sources, s => s is UrlSource);

        // Assert: no broad citations (only numerical facts get citations)
        Assert.True(output.Citations.All(c => c.IsNumerical),
            "All citations should be numerical");

        // Assert: "$5M" citation has doc source directly, no BlockSource (rule 2)
        Citation? fiveMCitation = FindCitationForValueInContent(output, "$5M");
        if (fiveMCitation is not null)
        {
            Assert.Contains(fiveMCitation.Sources,
                s => s is DocumentSource ds && ds.DocumentName == "Report.pdf");
            Assert.DoesNotContain(fiveMCitation.Sources, s => s is BlockSource);
        }
    }

    #endregion

    #region Test 4 — Orphan Filtering

    [Fact]
    public async Task OrphanNumericalCitations_FilteredFromOutput_BroadCitationsKept()
    {
        // Set up annotations with a fact whose value doesn't appear in raw response
        BlockAnnotations annotations = new();

        // Fact 1: appears in content (kept)
        Fact matchingFact = new("$5M") { IsNumerical = true };
        matchingFact.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue: $5M",
            DocumentId = "doc1",
            PageId = "page-1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });
        annotations.AddFact(matchingFact);

        // Fact 2: numerical, does NOT appear in content (orphan — should be filtered)
        Fact orphanFact = new("€2.3 billion") { IsNumerical = true };
        orphanFact.Sources.Add(new DocumentSource
        {
            CitedText = "European revenue €2.3 billion",
            DocumentId = "doc2",
            PageId = "page-5",
            PageNumber = 5,
            DocumentName = "EuroReport.pdf"
        });
        annotations.AddFact(orphanFact);

        // Fact 3: broad, does NOT get <fact> tag but should always be kept
        Fact broadFact = new("The company showed strong financial performance") { IsNumerical = false };
        broadFact.Sources.Add(new DocumentSource
        {
            CitedText = "Strong financial performance noted",
            DocumentId = "doc1",
            PageId = "page-1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });
        annotations.AddFact(broadFact);

        Block block = CreateBlock("Revenue was $5M in the last quarter.");
        ReportParagraph output = await BuildParagraph(
            block, new WorkflowOutputContext(), annotations);

        LogOutputElement(output);

        // Assert: only 1 citation remains — "$5M" (matching numerical)
        // Broad facts no longer get citations; orphan "€2.3 billion" is filtered
        _ = Assert.Single(output.Citations);

        // The matching numerical citation should have a <fact> tag
        Citation numCitation = output.Citations[0];
        Assert.True(numCitation.IsNumerical);
        Assert.Contains($"<fact id=\"{numCitation.Id}\">$5M</fact>", output.Content);

        // The orphan "€2.3 billion" should be gone
        Assert.DoesNotContain(output.Citations,
            c => c.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "EuroReport.pdf"));
    }

    #endregion

    #region Test 5 — Full Pipeline: CreateNumericalFacts + Propagation + Output

    [Fact]
    public async Task BroadAndNumericalFacts_EndToEnd_CorrectContentMarkersAndCitationTypes()
    {
        // Block A: document extraction
        Fact factA = new(
            "The fund had a NAV of $183,866 million with 94.6% in High Liquidity " +
            "and 3.0% cash as of April 30, 2025.");
        factA.Sources.Add(new DocumentSource
        {
            CitedText = "Key fund statistics\n\nFund net assets (in millions) $183,866\n" +
                        "Information technology 20.04%",
            DocumentId = "doc-kiid",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "KIID.pdf"
        });
        factA.Sources.Add(new DocumentSource
        {
            CitedText = "Washington Mutual Investors Fund\nFinancial Statements\n" +
                        "for the year ended April 30, 2025",
            DocumentId = "doc-ar",
            PageId = "page-1",
            PageNumber = 1,
            DocumentName = "WM Annual Report.pdf"
        });

        // CreateNumericalFacts splits the broad fact into sub-facts
        BlockAnnotations blockA = CreateAnnotationsFromBroadFacts(factA);
        LogBlock("Block A (Data Extraction)", blockA);

        WorkflowAnnotationsContext annotationsContext = new();
        annotationsContext.SetAnnotationsForBlock("block-a", blockA);

        // Block B: Cross-Check referencing A
        string rawResponseB =
            "The fund has a NAV of $183,866 million with 94.6% high liquidity " +
            "and 3.0% cash as of April 30, 2025.";

        Fact factB = new(rawResponseB);
        factB.Sources.Add(new BlockSource
        {
            CitedText = "The fund had a NAV of $183,866 million with 94.6% in " +
                        "High Liquidity and 3.0% cash as of April 30, 2025.",
            BlockId = "block-a",
            BlockTitle = "Data Extraction"
        });

        BlockAnnotations blockB = CreateAnnotationsFromBroadFacts(factB);
        SimulateUpdateAnnotationsFromReferencedBlocks(
            blockB, annotationsContext, ["block-a"], rawResponseB);

        annotationsContext.SetAnnotationsForBlock("block-b", blockB);
        LogBlock("Block B (Cross-Check) AFTER expansion", blockB);

        // Build output for Block B
        Block block = CreateBlock(rawResponseB);
        ReportParagraph output = await BuildParagraph(
            block, new WorkflowOutputContext(), blockB, annotationsContext);

        LogOutputElement(output);

        // Assert: content has <fact> markers for numerical values
        Assert.Contains("<fact id=", output.Content);
        Assert.Contains("$183,866 million", output.Content);
        Assert.Contains("94.6%", output.Content);

        // Assert: all citations are numerical (broad facts no longer get citations)
        Assert.True(output.Citations.All(c => c.IsNumerical),
            "All citations should be numerical");
        Assert.True(output.Citations.Count >= 3,
            $"Expected at least 3 numerical citations, got {output.Citations.Count}");

        // Assert: "$183,866 million" citation has KIID.pdf as direct source
        // (via our Step 2 fix — strip multiplier suffix matching)
        Citation? navCitation = FindCitationForValueInContent(output, "$183,866 million");
        Assert.NotNull(navCitation);
        Assert.True(
            navCitation!.Sources.Any(s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf"),
            "$183,866 million citation should have KIID.pdf DocumentSource");

        // Assert: "94.6%" stays on Block A — neither page Block A cited states it, so a page
        // citation would send the reader to a page without the figure
        Citation? liquidityCitation = FindCitationForValueInContent(output, "94.6%");
        Assert.NotNull(liquidityCitation);
        BlockSource liquiditySource = Assert.IsType<BlockSource>(Assert.Single(liquidityCitation.Sources));
        Assert.Equal("block-a", liquiditySource.BlockId);

        // Assert: "April 30, 2025" citation should have WM Annual Report DocumentSource
        // (exact match — CitedText "for the year ended April 30, 2025" contains the value)
        Citation? dateCitation = FindCitationForValueInContent(output, "April 30, 2025");
        Assert.NotNull(dateCitation);
        Assert.True(
            dateCitation!.Sources.Any(s => s is DocumentSource ds &&
                ds.DocumentName == "WM Annual Report.pdf"),
            "April 30, 2025 citation should have WM Annual Report DocumentSource");

        // Assert: flattened — citations with doc sources have no BlockSources (rule 2)
        foreach (Citation c in output.Citations)
        {
            if (c.Sources.Any(s => s is DocumentSource or UrlSource))
            {
                Assert.DoesNotContain(c.Sources, s => s is BlockSource);
            }
        }
    }

    #endregion

    #region Test 6 — No Redundant Sources in Citation

    [Fact]
    public async Task Citation_ShouldNotHaveRedundantSources_WhenDocSourceAlsoInUpstreamSources()
    {
        // Block A: broad fact with DocumentSource
        Fact factA = new("Revenue was $5M in the quarter");
        factA.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue: $5M in the quarter",
            DocumentId = "doc1",
            PageId = "page-1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });

        BlockAnnotations blockA = CreateAnnotationsFromBroadFacts(factA);
        WorkflowAnnotationsContext annotationsContext = new();
        annotationsContext.SetAnnotationsForBlock("block-a", blockA);

        // Block B: references A with BlockSource
        string rawResponseB = "Revenue was $5M in the quarter.";
        Fact factB = new(rawResponseB);
        factB.Sources.Add(new BlockSource
        {
            CitedText = "Revenue was $5M in the quarter",
            BlockId = "block-a",
            BlockTitle = "Data Extraction"
        });

        BlockAnnotations blockB = CreateAnnotationsFromBroadFacts(factB);

        // Annotation propagation adds DocumentSource to "$5M" sub-fact
        SimulateUpdateAnnotationsFromReferencedBlocks(
            blockB, annotationsContext, ["block-a"], rawResponseB);

        // Verify precondition: "$5M" fact now has BOTH BlockSource AND DocumentSource
        Fact fiveMFact = blockB.Facts.First(f => f.IsNumerical && f.Value == "$5M");
        Assert.True(fiveMFact.Sources.Any(s => s is BlockSource),
            "Precondition: $5M fact should have BlockSource");
        Assert.True(fiveMFact.Sources.Any(s => s is DocumentSource),
            "Precondition: $5M fact should have DocumentSource from propagation");

        // Build output
        Block block = CreateBlock(rawResponseB);
        ReportParagraph output = await BuildParagraph(
            block, new WorkflowOutputContext(), blockB, annotationsContext);

        LogOutputElement(output);

        // Find the "$5M" citation
        Citation? fiveMCitation = FindCitationForValueInContent(output, "$5M");
        Assert.NotNull(fiveMCitation);

        // KEY ASSERTION: DocumentSource should appear directly in citation.Sources
        // (pulled up by annotation propagation — it's the authoritative source).
        Assert.Contains(fiveMCitation!.Sources,
            s => s is DocumentSource ds && ds.DocumentName == "Report.pdf");

        // Rule 2: doc/url exist directly → BlockSource is completely removed
        Assert.DoesNotContain(fiveMCitation.Sources, s => s is BlockSource);

        _output.WriteLine("Flattened — DocumentSource is direct, BlockSource removed entirely.");
    }

    #endregion

    #region Test 7 — UpstreamSources Should Only Contain Relevant Sources

    [Fact]
    public async Task NumericalCitation_UpstreamSources_ShouldOnlyContainSourcesForThatFact()
    {
        // Block A: two facts with DIFFERENT sources
        Fact factA1 = new("Fund NAV of $183,866 million");
        factA1.Sources.Add(new DocumentSource
        {
            CitedText = "Fund net assets (in millions) $183,866",
            DocumentId = "doc-kiid",
            PageId = "page-2",
            PageNumber = 2,
            DocumentName = "KIID.pdf"
        });

        Fact factA2 = new("High Liquidity assets 94.6%");
        factA2.Sources.Add(new DocumentSource
        {
            CitedText = "High Liquidity 94.6% including U.S. Equities",
            DocumentId = "doc-awshx",
            PageId = "page-4",
            PageNumber = 4,
            DocumentName = "awshx.pdf"
        });

        BlockAnnotations blockA = CreateAnnotationsFromBroadFacts(factA1, factA2);
        WorkflowAnnotationsContext annotationsContext = new();
        annotationsContext.SetAnnotationsForBlock("block-a", blockA);

        // Block B: two numerical facts, each with a BlockSource whose CitedText
        // is a FULL paragraph containing BOTH values (this is the real-world scenario)
        string fullParagraph =
            "Fund NAV of $183,866 million. High Liquidity assets 94.6%.";

        Fact factB1 = new("$183,866 million") { IsNumerical = true };
        factB1.Sources.Add(new BlockSource
        {
            CitedText = fullParagraph,
            BlockId = "block-a",
            BlockTitle = "Data Extraction"
        });

        Fact factB2 = new("94.6%") { IsNumerical = true };
        factB2.Sources.Add(new BlockSource
        {
            CitedText = fullParagraph,
            BlockId = "block-a",
            BlockTitle = "Data Extraction"
        });

        BlockAnnotations blockB = new();
        blockB.AddFact(factB1);
        blockB.AddFact(factB2);

        // Build output
        string rawResponse = "NAV was $183,866 million with 94.6% liquidity.";
        Block block = CreateBlock(rawResponse);
        ReportParagraph output = await BuildParagraph(
            block, new WorkflowOutputContext(), blockB, annotationsContext);

        LogOutputElement(output);

        // Find citations by their fact tags
        Citation? navCitation = FindCitationForValueInContent(output, "$183,866 million");
        Citation? liqCitation = FindCitationForValueInContent(output, "94.6%");
        Assert.NotNull(navCitation);
        Assert.NotNull(liqCitation);

        // Rule 3: only BlockSource existed → doc/url pulled up from upstream → BlockSource removed
        // KEY ASSERTION: "$183,866 million" should have KIID.pdf as direct source
        // but NOT awshx.pdf (which belongs to "94.6%")
        Assert.Contains(navCitation!.Sources,
            s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf");
        Assert.DoesNotContain(navCitation.Sources,
            s => s is DocumentSource ds && ds.DocumentName == "awshx.pdf");
        Assert.DoesNotContain(navCitation.Sources, s => s is BlockSource);

        // KEY ASSERTION: "94.6%" should have awshx.pdf as direct source
        // but NOT KIID.pdf (which belongs to "$183,866 million")
        Assert.Contains(liqCitation!.Sources,
            s => s is DocumentSource ds && ds.DocumentName == "awshx.pdf");
        Assert.DoesNotContain(liqCitation.Sources,
            s => s is DocumentSource ds && ds.DocumentName == "KIID.pdf");
        Assert.DoesNotContain(liqCitation.Sources, s => s is BlockSource);

        _output.WriteLine("Flattened — doc sources are direct, scoped to each fact.");
    }

    #endregion

    #region Test 8 — A Figure Computed In A Block Keeps Its Block Source

    [Theory]
    [InlineData("The manager's AUM is $91,150,000, made up of Fund One at $50,120,000 and Fund Two at $41,030,000.")]
    [InlineData("Fund One holds $50,120,000 and Fund Two $41,030,000, so the manager's AUM is $91,150,000.")]
    [InlineData("The manager's AUM is $91,150,000.")]
    public async Task ComputedTotal_CitedFromTheBlockThatAddedIt_StaysOnThatBlockAsync(string rawResponse)
    {
        WorkflowAnnotationsContext annotationsContext = new();

        Fact computation = new(
            "Fund One NAV is $50,120,000 and Fund Two NAV is $41,030,000, so AUM is $91,150,000");
        computation.Sources.Add(new DocumentSource
        {
            CitedText = "Net asset value at 30 June 2026: $50,120,000",
            DocumentId = "doc-fund-one",
            PageId = "page-fund-one-3",
            PageNumber = 3,
            DocumentName = "fund-one.pdf",
        });
        computation.Sources.Add(new DocumentSource
        {
            CitedText = "Net asset value at 30 June 2026: $41,030,000",
            DocumentId = "doc-fund-two",
            PageId = "page-fund-two-5",
            PageNumber = 5,
            DocumentName = "fund-two.pdf",
        });
        annotationsContext.SetAnnotationsForBlock("aum-block", CreateAnnotationsFromBroadFacts(computation));

        Fact summary = new(rawResponse);
        summary.Sources.Add(new BlockSource
        {
            CitedText = "Total AUM is $91,150,000: Fund One NAV $50,120,000 plus Fund Two NAV $41,030,000.",
            BlockId = "aum-block",
            BlockTitle = "AUM",
        });
        BlockAnnotations downstream = CreateAnnotationsFromBroadFacts(summary);
        SimulateUpdateAnnotationsFromReferencedBlocks(downstream, annotationsContext, ["aum-block"], rawResponse);

        ReportParagraph output = await BuildParagraph(
            CreateBlock(rawResponse), new WorkflowOutputContext(), downstream, annotationsContext);
        LogOutputElement(output);

        Citation? aumCitation = FindCitationForValueInContent(output, "$91,150,000");
        Assert.NotNull(aumCitation);
        BlockSource blockSource = Assert.IsType<BlockSource>(Assert.Single(aumCitation.Sources));
        Assert.Equal("aum-block", blockSource.BlockId);
    }

    #endregion

    #region Test 9 — Two Blocks Citing Different Values Through One Shared Source

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoBlocksCitingDifferentValuesThroughOneSharedSource_EachGetOnlyTheirOwnPageAsync(bool revenueBlockFirst)
    {
        WorkflowAnnotationsContext annotationsContext = new();

        Fact extraction = new("Revenue was $5,200,000 and the operating margin 12.5%");
        extraction.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue for the year: $5,200,000",
            DocumentId = "doc-income",
            PageId = "page-income-2",
            PageNumber = 2,
            DocumentName = "income.pdf",
        });
        extraction.Sources.Add(new DocumentSource
        {
            CitedText = "Operating margin: 12.5%",
            DocumentId = "doc-ratios",
            PageId = "page-ratios-4",
            PageNumber = 4,
            DocumentName = "ratios.pdf",
        });
        annotationsContext.SetAnnotationsForBlock("extraction", CreateAnnotationsFromBroadFacts(extraction));

        const string ANALYSIS = "Revenue reached $5,200,000 at a 12.5% operating margin.";
        Fact analysis = new(ANALYSIS);
        analysis.Sources.Add(new BlockSource
        {
            CitedText = "Revenue was $5,200,000 and the operating margin 12.5%",
            BlockId = "extraction",
            BlockTitle = "Extraction",
        });
        annotationsContext.SetAnnotationsForBlock("analysis", CreateAnnotationsFromBroadFacts(analysis));

        Task<ReportParagraph> Revenue() => BuildCitingAnalysis("Revenue: $5,200,000.", "$5,200,000");
        Task<ReportParagraph> Margin() => BuildCitingAnalysis("Operating margin: 12.5%.", "12.5%");
        ReportParagraph revenueParagraph;
        ReportParagraph marginParagraph;
        if (revenueBlockFirst)
        {
            revenueParagraph = await Revenue();
            marginParagraph = await Margin();
        }
        else
        {
            marginParagraph = await Margin();
            revenueParagraph = await Revenue();
        }

        DocumentSource revenuePage = Assert.IsType<DocumentSource>(Assert.Single(Assert.Single(revenueParagraph.Citations).Sources));
        Assert.Equal("income.pdf", revenuePage.DocumentName);
        DocumentSource marginPage = Assert.IsType<DocumentSource>(Assert.Single(Assert.Single(marginParagraph.Citations).Sources));
        Assert.Equal("ratios.pdf", marginPage.DocumentName);

        async Task<ReportParagraph> BuildCitingAnalysis(string rawResponse, string value)
        {
            Fact fact = new(value) { IsNumerical = true };
            fact.Sources.Add(new BlockSource { CitedText = ANALYSIS, BlockId = "analysis", BlockTitle = "Analysis" });
            BlockAnnotations annotations = new();
            annotations.AddFact(fact);
            return await BuildParagraph(CreateBlock(rawResponse), new WorkflowOutputContext(), annotations, annotationsContext);
        }
    }

    #endregion

    #region Helpers

    private static BlockAnnotations CreateAnnotationsFromBroadFacts(params Fact[] broadFacts)
    {
        BlockAnnotations annotations = new();
        foreach (Fact broadFact in broadFacts)
        {
            annotations.AddFacts(BlockAnnotationProcessor.CreateNumericalFacts(broadFact));
        }

        return annotations;
    }

    private static void SimulateUpdateAnnotationsFromReferencedBlocks(
        BlockAnnotations blockAnnotations,
        WorkflowAnnotationsContext context,
        string[] referencedBlockIds,
        string? rawResponse)
    {
        List<Fact> referencedFacts = [.. referencedBlockIds
            .Select(id => context.GetAnnotationsForBlock(id))
            .Where(a => a is not null)
            .SelectMany(a => a!.Facts)
            .Distinct()];

        foreach (Fact fact in referencedFacts)
        {
            BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(blockAnnotations, fact);
        }

        blockAnnotations.AddReferencedFacts(referencedFacts, rawResponse);
    }

    private static Block CreateBlock(string rawResponse)
    {
        Section section = new("output", new Workflow());
        Block block = new(BlockTypes.AiQuery, section)
        {
            Name = "TestBlock"
        };
        _ = new BlockResponse(block) { RawResponse = rawResponse };

        return block;
    }

    private async Task<ReportParagraph> BuildParagraph(
        Block block,
        WorkflowOutputContext context,
        BlockAnnotations? blockAnnotations,
        WorkflowAnnotationsContext? annotationsContext = null)
    {
        ReportParagraph? paragraph = await _builder.BuildParagraphAsync(
            block, context, blockAnnotations, annotationsContext);
        Assert.NotNull(paragraph);
        return paragraph;
    }

    /// <summary>
    /// Finds the citation whose fact tag wraps the given value in the output content.
    /// </summary>
    private static Citation? FindCitationForValueInContent(
        ReportParagraph output, string value)
    {
        return output.Content is null
            ? null
            : output.Citations
            .FirstOrDefault(c => output.Content.Contains($"<fact id=\"{c.Id}\">{value}</fact>"));
    }

    private void LogBlock(string blockName, BlockAnnotations annotations)
    {
        _output.WriteLine($"┌─ {blockName} ({annotations.Facts.Count} facts) ─┐");
        foreach (Fact fact in annotations.Facts)
        {
            LogFact(fact);
        }

        _output.WriteLine("└────────────────────────────────────┘\n");
    }

    private void LogFact(Fact fact)
    {
        string tag = fact.IsNumerical ? "[NUM]" : "[BROAD]";
        string value = fact.Value.Length > 80 ? fact.Value[..77] + "..." : fact.Value;
        _output.WriteLine($"  {tag} \"{value}\"");

        foreach (BaseSource source in fact.Sources)
        {
            string citedPreview = source.CitedText.Length > 60
                ? source.CitedText[..57] + "..."
                : source.CitedText;
            citedPreview = citedPreview.Replace("\n", "\\n");

            switch (source)
            {
                case DocumentSource ds:
                    _output.WriteLine($"    └─ DOC: {ds.DocumentName} p{ds.PageNumber} | \"{citedPreview}\"");
                    break;
                case UrlSource us:
                    _output.WriteLine($"    └─ URL: {us.Url} | \"{citedPreview}\"");
                    break;
                case BlockSource bs:
                    _output.WriteLine($"    └─ BLOCK: [{bs.BlockTitle}] | \"{citedPreview}\"");
                    if (bs.UpstreamSources.Count > 0)
                    {
                        foreach (BaseSource us in bs.UpstreamSources)
                        {
                            _output.WriteLine($"       └─ upstream: {us.GetType().Name} key={us.SourceKey}");
                        }
                    }

                    break;
            }
        }
    }

    private void LogOutputElement(ReportParagraph output)
    {
        _output.WriteLine("╔══ ReportParagraph ══╗");
        _output.WriteLine($"  Kind: {output.Kind}");

        string content = output.Content ?? "(null)";
        if (content.Length > 300)
        {
            content = content[..297] + "...";
        }

        _output.WriteLine($"  Content: {content}");
        _output.WriteLine($"  Citations: {output.Citations.Count}");

        foreach (Citation c in output.Citations)
        {
            string numTag = c.IsNumerical ? "[NUM]" : "[BROAD]";
            _output.WriteLine($"    {c.Id} {numTag} sources={c.Sources.Count}:");
            foreach (BaseSource s in c.Sources)
            {
                switch (s)
                {
                    case DocumentSource ds:
                        _output.WriteLine($"      DOC: {ds.DocumentName} p{ds.PageNumber} key={ds.SourceKey}");
                        break;
                    case UrlSource us:
                        _output.WriteLine($"      URL: {us.Url} key={us.SourceKey}");
                        break;
                    case BlockSource bs:
                        _output.WriteLine($"      BLOCK: [{bs.BlockTitle}] key={bs.SourceKey}");
                        _output.WriteLine($"        UpstreamSources: {bs.UpstreamSources.Count}");
                        foreach (BaseSource upstream in bs.UpstreamSources)
                        {
                            _output.WriteLine($"          {upstream.GetType().Name}: key={upstream.SourceKey}");
                        }

                        break;
                }
            }
        }

        _output.WriteLine("╚══════════════════════════╝\n");
    }

    #endregion
}
