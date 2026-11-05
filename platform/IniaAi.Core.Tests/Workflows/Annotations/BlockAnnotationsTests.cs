using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class BlockAnnotationsTests
{
    #region AddFact Tests

    [Fact]
    public void AddFact_WithNullFact_DoesNothing()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        // Act
        annotations.AddFact(null!);

        // Assert
        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void AddFact_WithNewFact_AddsFactToCollection()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var fact = new Fact("test value");
        fact.Sources.Add(new DocumentSource { CitedText = "cited", DocumentId = "doc", PageId = "page" });

        // Act
        annotations.AddFact(fact);

        // Assert
        _ = Assert.Single(annotations.Facts);
        Assert.Equal(fact, annotations.Facts[0]);
    }

    [Fact]
    public void AddFact_WithDuplicateValue_DoesNotAddAgain()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var fact1 = new Fact("same value");
        fact1.Sources.Add(new DocumentSource { CitedText = "cited 1", DocumentId = "doc1", PageId = "page1" });

        var fact2 = new Fact("same value"); // Same value = duplicate
        fact2.Sources.Add(new DocumentSource { CitedText = "cited 2", DocumentId = "doc2", PageId = "page2" });

        // Act
        annotations.AddFact(fact1);
        annotations.AddFact(fact2);

        // Assert - Only one fact added (equality is based on Value only)
        _ = Assert.Single(annotations.Facts);
    }

    #endregion

    #region AddFacts Tests

    [Fact]
    public void AddFacts_WithNullCollection_DoesNothing()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        // Act
        annotations.AddFacts(null!);

        // Assert
        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void AddFacts_WithFactCollection_AddsAllUniqueFacts()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var fact1 = new Fact("value 1");
        fact1.Sources.Add(new DocumentSource { CitedText = "cited", DocumentId = "doc", PageId = "page" });

        var fact2 = new Fact("value 2");
        fact2.Sources.Add(new DocumentSource { CitedText = "cited", DocumentId = "doc", PageId = "page" });

        var fact3 = new Fact("value 1"); // Duplicate of fact1

        var facts = new List<Fact> { fact1, fact2, fact3 };

        // Act
        annotations.AddFacts(facts);

        // Assert - Only 2 unique facts added
        Assert.Equal(2, annotations.Facts.Count);
        Assert.Contains(annotations.Facts, f => f.Value == "value 1");
        Assert.Contains(annotations.Facts, f => f.Value == "value 2");
    }

    [Fact]
    public void AddFacts_WithEmptyCollection_DoesNothing()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var facts = new List<Fact>();

        // Act
        annotations.AddFacts(facts);

        // Assert
        Assert.Empty(annotations.Facts);
    }

    #endregion

    #region AddConfidenceScore Tests

    [Fact]
    public void AddConfidenceScore_AddsScoreToCollection()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        // Act
        annotations.AddConfidenceScore(0.85);

        // Assert
        _ = Assert.Single(annotations.ConfidenceScores);
        Assert.Equal(0.85, annotations.ConfidenceScores[0]);
    }

    [Fact]
    public void AddConfidenceScores_WithNullCollection_DoesNothing()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        // Act
        annotations.AddConfidenceScores(null!);

        // Assert
        Assert.Empty(annotations.ConfidenceScores);
    }

    [Fact]
    public void AddConfidenceScores_WithScoreCollection_AddsAllScores()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var scores = new List<double> { 0.75, 0.85, 0.95 };

        // Act
        annotations.AddConfidenceScores(scores);

        // Assert
        Assert.Equal(3, annotations.ConfidenceScores.Count);
        Assert.Equal(scores, annotations.ConfidenceScores);
    }

    #endregion

    #region AddSources Tests

    [Fact]
    public void AddSources_WithNullCollection_DoesNothing()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        // Act
        annotations.AddSources(null!);

        // Assert
        Assert.Empty(annotations.Sources);
    }

    [Fact]
    public void AddSources_WithSourceCollection_AddsAllSources()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var sources = new List<string> { "Source 1", "Source 2", "Source 3" };

        // Act
        annotations.AddSources(sources);

        // Assert
        Assert.Equal(3, annotations.Sources.Count);
        Assert.Contains("Source 1", annotations.Sources);
        Assert.Contains("Source 2", annotations.Sources);
        Assert.Contains("Source 3", annotations.Sources);
    }

    [Fact]
    public void AddSources_WithDuplicateSources_AddsOnlyUnique()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var sources = new List<string> { "Source 1", "Source 2", "Source 1" };

        // Act
        annotations.AddSources(sources);

        // Assert
        Assert.Equal(2, annotations.Sources.Count);
    }

    [Fact]
    public void AddSources_WithWhitespaceSources_SkipsEmpty()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var sources = new List<string> { "Source 1", "", "  ", "Source 2" };

        // Act
        annotations.AddSources(sources);

        // Assert
        Assert.Equal(2, annotations.Sources.Count);
        Assert.Contains("Source 1", annotations.Sources);
        Assert.Contains("Source 2", annotations.Sources);
    }

    [Fact]
    public void AddSources_TrimsSources()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var sources = new List<string> { "  Source 1  ", "Source 2  " };

        // Act
        annotations.AddSources(sources);

        // Assert
        Assert.Contains("Source 1", annotations.Sources);
        Assert.Contains("Source 2", annotations.Sources);
    }

    [Fact]
    public void AddSources_WithTrimmedDuplicates_AddsOnlyUnique()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var sources = new List<string> { "Source 1", "  Source 1  " };

        // Act
        annotations.AddSources(sources);

        // Assert
        _ = Assert.Single(annotations.Sources);
    }

    #endregion

    #region Reference Sharing and Inherited Fact Problems

    [Fact]
    public void ExpandSourcesFromReferencedFact_RunsBeforeAddReferencedFacts_NoContamination()
    {
        // Arrange — Block A extracted a fact
        var blockAFact = new Fact("Revenue was $5M");
        blockAFact.Sources.Add(new BlockSource
        {
            CitedText = "revenue of $5M in Q4",
            BlockId = "earlier-block",
            BlockTitle = "Data Extraction"
        });

        var blockAAnnotations = new BlockAnnotations();
        blockAAnnotations.AddFact(blockAFact);

        // Block B has its own fact from FactExtractor
        var blockBOwnFact = new Fact("Revenue was $5M in Q4");
        blockBOwnFact.Sources.Add(new BlockSource
        {
            CitedText = "Revenue was $5M",
            BlockId = "block-a",
            BlockTitle = "Block A"
        });

        var blockBAnnotations = new BlockAnnotations();
        blockBAnnotations.AddFact(blockBOwnFact);

        var referencedFact = new Fact("$5M");
        referencedFact.Sources.Add(new DocumentSource
        {
            CitedText = "$5M from annual report",
            DocumentId = "doc-1",
            PageId = "page-1",
            DocumentName = "AnnualReport.pdf"
        });

        int originalSourceCount = blockAFact.Sources.Count; // 1

        // Act — pipeline order: expand FIRST, then add referenced facts
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(blockBAnnotations, referencedFact);
        string blockBResponse = "Revenue was $5M according to the analysis.";
        blockBAnnotations.AddReferencedFacts([blockAFact], blockBResponse);

        // Assert — Block A's fact is not contaminated because expand ran before add
        Assert.Equal(originalSourceCount, blockAAnnotations.Facts[0].Sources.Count);
        // Block B's own fact got expanded (has 2 sources: BlockSource + DocumentSource)
        Assert.Equal(2, blockBOwnFact.Sources.Count);
        // Block A's fact is shared by reference (same object in Block B's annotations)
        Assert.Same(blockAFact, blockBAnnotations.Facts.First(f => f.Value == "Revenue was $5M"));
    }

    [Fact]
    public void AddReferencedFacts_ShouldOnlyAddFactsWhoseValueAppearsInResponse()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        // Block's own fact (already extracted from AI response)
        var ownFact = new Fact("Revenue was $5M");
        ownFact.Sources.Add(new BlockSource
        {
            CitedText = "revenue $5M",
            BlockId = "block-A",
            BlockTitle = "Analysis"
        });
        annotations.AddFact(ownFact);

        string blockRawResponse = "Revenue was $5M in the last quarter.";

        // Referenced facts from upstream blocks — "$5M" appears in response, "15%" does not
        var relevantFact = new Fact("$5M");
        relevantFact.Sources.Add(new DocumentSource
        {
            CitedText = "$5M from report",
            DocumentId = "d1",
            PageId = "p1",
            DocumentName = "Report.pdf"
        });

        var irrelevantFact = new Fact("growth rate of 15%");
        irrelevantFact.Sources.Add(new DocumentSource
        {
            CitedText = "15% growth",
            DocumentId = "d2",
            PageId = "p2",
            DocumentName = "Other.pdf"
        });

        // Act — add referenced facts filtered by response
        annotations.AddReferencedFacts([relevantFact, irrelevantFact], blockRawResponse);

        // Assert — only the fact whose value appears in the response should be added
        Assert.DoesNotContain(annotations.Facts, f => f.Value == "growth rate of 15%");
        Assert.Contains(annotations.Facts, f => f.Value == "$5M");
    }

    [Fact]
    public void AddReferencedFacts_SharesByReference_SameObjectInBothBlocks()
    {
        // Arrange
        var originalFact = new Fact("$5M");
        originalFact.Sources.Add(new DocumentSource
        {
            CitedText = "$5M revenue",
            DocumentId = "d1",
            PageId = "p1",
            DocumentName = "Report.pdf"
        });

        var blockAAnnotations = new BlockAnnotations();
        blockAAnnotations.AddFact(originalFact);

        var blockBAnnotations = new BlockAnnotations();
        string blockBResponse = "The company reported $5M in revenue last year.";

        // Act — add referenced fact by reference (no cloning)
        blockBAnnotations.AddReferencedFacts([originalFact], blockBResponse);

        // Assert — same object in both blocks
        Fact factInB = blockBAnnotations.Facts.First(f => f.Value == "$5M");
        Assert.Same(blockAAnnotations.Facts[0], factInB);
    }

    [Fact]
    public void AddReferencedFacts_WithNullResponse_ShouldNotAddAnyFacts()
    {
        // Arrange
        var annotations = new BlockAnnotations();
        var fact = new Fact("$5M");
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "$5M",
            DocumentId = "d1",
            PageId = "p1",
            DocumentName = "Report.pdf"
        });

        // Act
        annotations.AddReferencedFacts([fact], null);

        // Assert — no facts added when response is null
        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void AddReferencedFacts_CaseInsensitive_MatchesRegardlessOfCasing()
    {
        var annotations = new BlockAnnotations();
        var fact = new Fact("Q3 2024");
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Q3 2024 results",
            DocumentId = "d1",
            PageId = "p1",
            DocumentName = "Report.pdf"
        });

        string response = "The company reported strong results in q3 2024.";

        annotations.AddReferencedFacts([fact], response);

        _ = Assert.Single(annotations.Facts);
        Assert.Equal("Q3 2024", annotations.Facts[0].Value);
    }

    [Fact]
    public void AddReferencedFacts_PropagatesViaQuantityEquivalence_WhenSubstringMisses()
    {
        // Tier 2/3: upstream fact value "$184 billion" doesn't appear literally
        // in the downstream rawResponse, but rawResponse has a quantity-equivalent
        // value ("$183,990 million" — Currency USD, ~0.005% drift). Pair chosen
        // tight enough to remain within any plausible Currency tolerance setting.
        // Pre-quantity propagation was pure substring and would drop this fact.
        var annotations = new BlockAnnotations();
        var fact = new Fact("$184 billion") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Total NAV $184 billion in fiscal year",
            DocumentId = "doc-revenue",
            PageId = "page1",
            DocumentName = "AnnualReport.pdf",
        });

        string downstreamResponse = "Total NAV was $183,990 million in fiscal 2024.";

        annotations.AddReferencedFacts([fact], downstreamResponse);

        _ = Assert.Single(annotations.Facts);
        Assert.Equal("$184 billion", annotations.Facts[0].Value);
    }

    [Fact]
    public void AddReferencedFacts_DoesNotPropagateAcrossKinds()
    {
        // Headline cross-kind case. Upstream fact "$300 billion" (Currency USD)
        // would have matched a downstream rawResponse that mentions
        // "300 billion Kelvin" under the pre-quantity 1% numeric-only tolerance.
        // The kind-aware matcher must reject the cross-kind comparison.
        var annotations = new BlockAnnotations();
        var fact = new Fact("$300 billion") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Total assets $300 billion across the portfolio",
            DocumentId = "doc-finance",
            PageId = "page1",
            DocumentName = "Balance.pdf",
        });

        string downstreamResponse = "Surface temperature reached 300 billion Kelvin during simulation.";

        annotations.AddReferencedFacts([fact], downstreamResponse);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void AddReferencedFacts_BarePlainInteger_DoesNotPropagateOnSubstringAlone()
    {
        // Path 4 from findings.md: bare Plain integer "712" (e.g. extracted
        // from upstream "712 employees") would propagate to any downstream
        // rawResponse containing "712" anywhere — including unrelated
        // contexts like "Page 712 of the report". Bare Plain integers carry
        // too little context to confidently propagate sources cross-block.
        var annotations = new BlockAnnotations();
        var fact = new Fact("712") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "712 employees as of fiscal year 2024",
            DocumentId = "doc-hr",
            PageId = "page1",
            DocumentName = "HR.pdf",
        });

        string downstreamResponse = "Page 712 of the report describes the audit findings.";

        annotations.AddReferencedFacts([fact], downstreamResponse);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void AddReferencedFacts_PlainWithMagnitude_PropagatesViaQuantityEquivalence()
    {
        // Plain Kind but with a magnitude word ("712 million") carries unit
        // context — the multiplier ≠ 1 discriminator distinguishes it from
        // the bare-integer false-positive case. Propagated normally.
        var annotations = new BlockAnnotations();
        var fact = new Fact("712 million") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "712 million in market capitalisation",
            DocumentId = "doc-finance",
            PageId = "page1",
            DocumentName = "Finance.pdf",
        });

        string downstreamResponse = "Market capitalisation reached 712 million in Q4.";

        annotations.AddReferencedFacts([fact], downstreamResponse);

        _ = Assert.Single(annotations.Facts);
    }

    [Fact]
    public void AddReferencedFacts_BarePlainDecimal_DoesNotPropagateOnSubstringAlone()
    {
        // Same shape as bare-integer: a plain decimal like "3.14" with no
        // unit context is too ambiguous to propagate from substring alone.
        var annotations = new BlockAnnotations();
        var fact = new Fact("3.14") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Constant pi = 3.14 in lab notes",
            DocumentId = "doc-math",
            PageId = "page1",
            DocumentName = "Math.pdf",
        });

        string downstreamResponse = "The discount rate dropped to 3.14 last month.";

        annotations.AddReferencedFacts([fact], downstreamResponse);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void AddReferencedFacts_BareYear_PropagatesWhereItsSourceSpeaksOfTheSameThing()
    {
        // A bare year is not Plain, but it says nothing on its own either: it
        // propagates only where the words around it match the words around it
        // in its source.
        var annotations = new BlockAnnotations();
        var fact = new Fact("2024") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Fiscal year 2024 results",
            DocumentId = "doc-fy",
            PageId = "page1",
            DocumentName = "FY24.pdf",
        });

        annotations.AddReferencedFacts([fact], "Revenue grew throughout 2024 across segments.");
        Assert.Empty(annotations.Facts);

        annotations.AddReferencedFacts([fact], "The fiscal 2024 results showed growth across segments.");
        _ = Assert.Single(annotations.Facts);
    }

    #endregion

    #region ExpandSourcesFromReferencedFact — Tightened Match

    [Fact]
    public void ExpandSourcesFromReferencedFact_WhenCitedTextMatchesButFactValueDoesNot_ShouldNotExpand()
    {
        // Arrange — Fact about revenue, but the BlockSource cites a long paragraph
        // that also mentions "9,300 employees"
        var annotations = new BlockAnnotations();

        var revenueFact = new Fact("Revenue was $5B");
        revenueFact.Sources.Add(new BlockSource
        {
            CitedText = "In 2024, revenue was $5B and the company had 9,300 employees",
            BlockId = "blockA"
        });
        annotations.AddFact(revenueFact);

        // Referenced fact about "9,300" — its value IS in the CitedText but NOT in fact.Value
        var referencedFact = new Fact("9,300");
        referencedFact.Sources.Add(new DocumentSource
        {
            CitedText = "employs 9,300 people worldwide",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert — Revenue fact should NOT get the DocumentSource about employees
        _ = Assert.Single(revenueFact.Sources);
        _ = Assert.IsType<BlockSource>(revenueFact.Sources[0]);
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_WhenBothCitedTextAndFactValueMatch_ShouldExpand()
    {
        // Arrange — Fact value contains the referenced fact's value AND CitedText matches
        var annotations = new BlockAnnotations();

        var compoundFact = new Fact("In 2024, the company had 9,300 employees");
        compoundFact.Sources.Add(new BlockSource
        {
            CitedText = "In 2024, the company had 9,300 employees and revenue of $5B",
            BlockId = "blockA"
        });
        annotations.AddFact(compoundFact);

        var referencedFact = new Fact("9,300");
        referencedFact.Sources.Add(new DocumentSource
        {
            CitedText = "employs 9,300 people worldwide",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert — Both conditions met, expansion should happen
        Assert.Equal(2, compoundFact.Sources.Count);
        Assert.Contains(compoundFact.Sources, s => s is DocumentSource);
    }

    #endregion

    #region Numeric Equivalence in ExpandSourcesFromReferencedFact

    [Fact]
    public void ExpandSourcesFromReferencedFact_NumericalFacts_WithCurrencyEquivalence_ShouldExpand()
    {
        // Arrange — Block A has "$183,990 million", Block B re-synthesized
        // as "$184 billion" (~0.005% drift, within any plausible Currency
        // tolerance setting).
        var annotations = new BlockAnnotations();

        var referencedFact = new Fact("$183,990 million") { IsNumerical = true };
        referencedFact.Sources.Add(new DocumentSource
        {
            CitedText = "Fund NAV $183,990 million",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "KIID.pdf"
        });

        var resynthesizedFact = new Fact("$184 billion") { IsNumerical = true };
        resynthesizedFact.Sources.Add(new BlockSource
        {
            CitedText = "The fund had a NAV of $183,990 million",
            BlockId = "block-a"
        });
        annotations.AddFact(resynthesizedFact);

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert — should expand because values are currency-equivalent
        Assert.Equal(2, resynthesizedFact.Sources.Count);
        Assert.Contains(resynthesizedFact.Sources, s => s is DocumentSource);
    }

    /// <summary>
    /// When both local and referenced facts are quantitied (have a structured
    /// <see cref="Quantities.Quantity"/>), relatedness must go through
    /// <c>QuantityMatcher.Equivalent</c> only — substring containment between
    /// the value strings is a false-positive trap. <c>"6.49%"</c> structurally
    /// contains <c>"49%"</c> but the BaseValues drift 86% (way outside the
    /// Concentration tolerance of 0.001). Same Path-2 shape that's already
    /// closed in <c>IsRelatedFact</c> (per findings.md); this test pins the
    /// behaviour for <c>LocalFactRelatedToReferenced</c>.
    /// </summary>
    [Fact]
    public void ExpandSourcesFromReferencedFact_BothQuantitied_SubstringContainmentDoesNotShortCircuitQuantityCheck()
    {
        // Arrange — local "6.49%" cites an upstream paragraph that mentions
        // both "6.49%" and "49%". The local fact's BlockSource quotes the
        // whole paragraph, so the CitedText-mention check passes for
        // referenced "49%" (literal substring). Without the bug fix,
        // LocalFactRelatedToReferenced returns true via "6.49%".Contains("49%")
        // and pulls upstream "49%"'s DocSource onto local "6.49%" — wrong.
        var annotations = new BlockAnnotations();

        var localFact = new Fact("6.49%") { IsNumerical = true };
        localFact.Sources.Add(new BlockSource
        {
            CitedText = "Retail shareholders own 6.49% and institutions own 49%.",
            BlockId = "upstream-block"
        });
        annotations.AddFact(localFact);

        var referencedFact = new Fact("49%") { IsNumerical = true };
        referencedFact.Sources.Add(new DocumentSource
        {
            CitedText = "institutions own 49%",
            DocumentId = "doc-institutional",
            PageId = "page-institutional",
            DocumentName = "Institutional.pdf"
        });

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert — local "6.49%" must NOT inherit the institutional DocSource.
        // BaseValues 0.0649 vs 0.49 are 86.7% apart, far outside the
        // Concentration tolerance (0.001). Quantity Equivalent is false; the
        // substring trap is the only path that could leak the source.
        _ = Assert.Single(localFact.Sources);
        _ = Assert.IsType<BlockSource>(localFact.Sources[0]);
    }

    /// <summary>
    /// The asymmetric quantitied-vs-broad case. A local numerical fact's value
    /// (e.g. <c>"$747.0 million"</c>) is structurally contained inside an
    /// upstream broad sentence (a long AI-text segment that mentions the
    /// number alongside many other claims). With the substring fallback
    /// active, the upstream broad fact's <em>entire</em> source set — every
    /// document Claude cited for that text segment — gets pulled onto the
    /// local numerical fact, even though most of those sources have nothing
    /// to do with this specific number. This is the production-trace
    /// $747.0M-with-16-unrelated-sources shape: the number was
    /// computed/synthesised by the AI from components on one page; the
    /// upstream broad sentence accumulated 16 sources for various adjacent
    /// claims; substring-fallback pulled all 16 onto the citation. The
    /// fix: when one side is quantitied and the other isn't, refuse the
    /// substring fallback. Path-2 rule extended to the asymmetric case.
    /// </summary>
    [Fact]
    public void ExpandSourcesFromReferencedFact_QuantitiedLocalVsBroadUpstream_DoesNotMatchViaSubstring()
    {
        // Arrange
        const string UPSTREAM_BROAD = "Reported EBITDA for FY2025 was $3,427.0 million versus $3,290.0 million in FY2024, " +
            "while adjusted EBITDA expanded to $4,174.0 million from $3,570.0 million, implying a total adjustment " +
            "quantum of $747.0 million in FY2025 against $280.0 million in FY2024.";

        var annotations = new BlockAnnotations();

        // Local numerical fact "$747.0 million" — Currency Quantity populated.
        // BlockSource quotes the upstream broad sentence (downstream's quote
        // of upstream's text); it mentions "$747.0 million" literally.
        var localFact = new Fact("$747.0 million") { IsNumerical = true };
        localFact.Sources.Add(new BlockSource
        {
            CitedText = UPSTREAM_BROAD,
            BlockId = "earnings-quality-block"
        });
        annotations.AddFact(localFact);

        // Upstream broad fact: long sentence, no Quantity. Sources are
        // pages Claude cited for the segment as a whole — none of their
        // CitedTexts contain "$747.0 million" or "747" (the value was
        // computed by the AI from components on a different page).
        var referencedFact = new Fact(UPSTREAM_BROAD);
        referencedFact.Sources.Add(new DocumentSource
        {
            CitedText = "Notes to Consolidated Financial Statements—page 175. New Accounting Pronouncements...",
            DocumentId = "doc-ar",
            PageId = "page-175",
            DocumentName = "AnnualReport.pdf"
        });
        referencedFact.Sources.Add(new DocumentSource
        {
            CitedText = "Long-Lived Assets table — geographic distribution of plant assets across regions.",
            DocumentId = "doc-ar",
            PageId = "page-180",
            DocumentName = "AnnualReport.pdf"
        });
        referencedFact.Sources.Add(new DocumentSource
        {
            CitedText = "Contract Balances — the timing of revenue recognition...",
            DocumentId = "doc-ar",
            PageId = "page-184",
            DocumentName = "AnnualReport.pdf"
        });

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert — local "$747.0 million" must NOT inherit any of the three
        // unrelated DocumentSources. None of them ground the value (no
        // CitedText contains "747"); the only thing that "related" them
        // before was the broad sentence containing the number's surface.
        _ = Assert.Single(localFact.Sources);
        _ = Assert.IsType<BlockSource>(localFact.Sources[0]);
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_NonNumericalFacts_NoCurrencyEquivalence()
    {
        // Arrange — non-numerical facts should NOT use currency equivalence
        var annotations = new BlockAnnotations();

        var referencedFact = new Fact("$183,866 million") { IsNumerical = false };
        referencedFact.Sources.Add(new DocumentSource
        {
            CitedText = "Fund NAV $183,866 million",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "KIID.pdf"
        });

        // Fact value does NOT contain "$183,866 million" — substring match fails
        var fact = new Fact("$184 billion") { IsNumerical = false };
        fact.Sources.Add(new BlockSource
        {
            CitedText = "The fund had a NAV of $183,866 million",
            BlockId = "block-a"
        });
        annotations.AddFact(fact);

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert — should NOT expand (non-numerical, no substring match)
        _ = Assert.Single(fact.Sources);
    }

    #endregion

    #region ExpandSourcesFromReferencedFact Tests

    [Fact]
    public void ExpandSourcesFromReferencedFact_WhenBlockSourceContainsReferencedFactValue_AddsDocumentSources()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var referencedFact = new Fact("Revenue was $5M");
        referencedFact.Sources.Add(new DocumentSource { CitedText = "Revenue was $5M", DocumentId = "doc1", PageId = "page1", DocumentName = "Annual Report.pdf" });

        // Fact value must contain referenced fact's value for expansion to happen
        var citingFact = new Fact("Revenue was $5M according to the report");
        citingFact.Sources.Add(new BlockSource { CitedText = "Revenue was $5M", BlockId = "blockA" });

        annotations.AddFact(citingFact);

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert
        Assert.Equal(2, citingFact.Sources.Count);
        Assert.Contains(citingFact.Sources, s => s is DocumentSource ds && ds.DocumentName == "Annual Report.pdf");
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_WhenBlockSourceDoesNotContainReferencedFactValue_DoesNotAddSources()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var referencedFact = new Fact("Revenue was $5M");
        referencedFact.Sources.Add(new DocumentSource { CitedText = "Revenue was $5M", DocumentId = "doc1", PageId = "page1" });

        var citingFact = new Fact("Unrelated fact");
        citingFact.Sources.Add(new BlockSource { CitedText = "Something completely different", BlockId = "blockA" });

        annotations.AddFact(citingFact);

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert
        _ = Assert.Single(citingFact.Sources);
        _ = Assert.IsType<BlockSource>(citingFact.Sources[0]);
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_WhenFactHasNonBlockSource_SkipsIt()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var referencedFact = new Fact("Revenue was $5M");
        referencedFact.Sources.Add(new DocumentSource { CitedText = "Revenue was $5M", DocumentId = "doc1", PageId = "page1" });

        var citingFact = new Fact("Some fact");
        citingFact.Sources.Add(new DocumentSource { CitedText = "Revenue was $5M", DocumentId = "doc2", PageId = "page2" }); // DocumentSource, not BlockSource

        annotations.AddFact(citingFact);

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert - No expansion because source is not a BlockSource
        _ = Assert.Single(citingFact.Sources);
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_DoesNotAddDuplicateSources()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var documentSource = new DocumentSource { CitedText = "Revenue was $5M", DocumentId = "doc1", PageId = "page1" };

        var referencedFact = new Fact("Revenue was $5M");
        referencedFact.Sources.Add(documentSource);

        var citingFact = new Fact("Revenue was $5M in Q4");
        citingFact.Sources.Add(new BlockSource { CitedText = "Revenue was $5M", BlockId = "blockA" });
        citingFact.Sources.Add(documentSource); // Already has this source

        annotations.AddFact(citingFact);

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert - Should not add duplicate
        Assert.Equal(2, citingFact.Sources.Count);
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_DoesNotAddBlockSourceFromReferencedFact()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var referencedFact = new Fact("Revenue was $5M");
        referencedFact.Sources.Add(new BlockSource { CitedText = "Some text", BlockId = "blockZ" }); // BlockSource in referenced fact
        referencedFact.Sources.Add(new DocumentSource { CitedText = "Revenue was $5M", DocumentId = "doc1", PageId = "page1" });

        var citingFact = new Fact("Revenue was $5M in the last quarter");
        citingFact.Sources.Add(new BlockSource { CitedText = "Revenue was $5M", BlockId = "blockA" });

        annotations.AddFact(citingFact);

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert - Only DocumentSource added, BlockSource from referenced fact is skipped
        Assert.Equal(2, citingFact.Sources.Count);
        _ = Assert.Single(citingFact.Sources.OfType<BlockSource>());
        _ = Assert.Single(citingFact.Sources.OfType<DocumentSource>());
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_WithEmptyFactsCollection_DoesNothing()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var referencedFact = new Fact("Revenue was $5M");
        referencedFact.Sources.Add(new DocumentSource { CitedText = "Revenue was $5M", DocumentId = "doc1", PageId = "page1" });

        // Act & Assert - Should not throw
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_WhenReferencedFactHasNoSources_DoesNothing()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var referencedFact = new Fact("Revenue was $5M"); // No sources

        var citingFact = new Fact("The company earned $5M");
        citingFact.Sources.Add(new BlockSource { CitedText = "Revenue was $5M", BlockId = "blockA" });

        annotations.AddFact(citingFact);

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert
        _ = Assert.Single(citingFact.Sources);
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_WithMultipleFacts_OnlyExpandsMatchingOnes()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var referencedFact = new Fact("Revenue was $5M");
        referencedFact.Sources.Add(new DocumentSource { CitedText = "Revenue was $5M", DocumentId = "doc1", PageId = "page1" });

        // Fact value contains referenced fact's value — should expand
        var matchingFact = new Fact("Revenue was $5M this year");
        matchingFact.Sources.Add(new BlockSource { CitedText = "Revenue was $5M", BlockId = "blockA" });

        // Fact value does NOT contain referenced fact's value — should NOT expand
        var nonMatchingFact = new Fact("Expenses were $3M");
        nonMatchingFact.Sources.Add(new BlockSource { CitedText = "Expenses were $3M", BlockId = "blockB" });

        annotations.AddFact(matchingFact);
        annotations.AddFact(nonMatchingFact);

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert
        Assert.Equal(2, matchingFact.Sources.Count); // Expanded
        _ = Assert.Single(nonMatchingFact.Sources); // Not expanded
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_WithMultipleSourcesInReferencedFact_AddsAllNonBlockSources()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var referencedFact = new Fact("Revenue was $5M");
        referencedFact.Sources.Add(new DocumentSource { CitedText = "Revenue was $5M", DocumentId = "doc1", PageId = "page1" });
        referencedFact.Sources.Add(new DocumentSource { CitedText = "Revenue was $5M", DocumentId = "doc2", PageId = "page2" });
        referencedFact.Sources.Add(new UrlSource { CitedText = "Revenue was $5M", Url = "https://example.com" });

        // Fact value contains referenced fact's value
        var citingFact = new Fact("Revenue was $5M in the annual report");
        citingFact.Sources.Add(new BlockSource { CitedText = "Revenue was $5M", BlockId = "blockA" });

        annotations.AddFact(citingFact);

        // Act
        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);

        // Assert - Original BlockSource + 3 sources from referenced fact
        Assert.Equal(4, citingFact.Sources.Count);
        Assert.Equal(2, citingFact.Sources.OfType<DocumentSource>().Count());
        _ = Assert.Single(citingFact.Sources.OfType<UrlSource>());
    }

    #endregion

    #region ExpandSourcesFromReferencedFact — Mixed Broad+Numerical Sequence

    /// <summary>
    /// Mirrors what <c>WorkflowHandler.UpdateAnnotationsFromReferencedBlocks</c>
    /// actually does — iterates the upstream block's facts (broad parents AND
    /// numerical sub-facts produced by <c>CreateNumericalFacts</c>) and calls
    /// <c>ExpandSourcesFromReferencedFact</c> per upstream fact. Verifies that
    /// after the full sequence each local fact ends up with the right sources
    /// and there's no cross-contamination between numerical sub-facts of
    /// different kinds (Currency $184B vs Concentration 25%).
    /// </summary>
    [Fact]
    public void ExpandSourcesFromReferencedFact_MixedBroadAndNumericalReferencedFactsSequence_AssignsSourcesPerSubFactWithoutCrossContamination()
    {
        // Arrange — upstream block had two AI citations:
        //   citation A → DocSource_revenue, broad fact "Revenue rose to $184 billion"
        //   citation B → DocSource_margin,  broad fact "operating margin grew 25%"
        // Plus the numerical sub-facts CreateNumericalFacts would have produced.
        var docSourceRevenue = new DocumentSource
        {
            CitedText = "Revenue rose to $184 billion",
            DocumentId = "doc-revenue",
            PageId = "page-revenue",
            DocumentName = "Revenue.pdf"
        };
        var docSourceMargin = new DocumentSource
        {
            CitedText = "operating margin grew 25%",
            DocumentId = "doc-margin",
            PageId = "page-margin",
            DocumentName = "Margin.pdf"
        };

        var upstreamBroadRevenue = new Fact("Revenue rose to $184 billion");
        upstreamBroadRevenue.Sources.Add(docSourceRevenue);

        var upstreamBroadMargin = new Fact("operating margin grew 25%");
        upstreamBroadMargin.Sources.Add(docSourceMargin);

        var upstreamRevenueValue = new Fact("$184 billion") { IsNumerical = true };
        upstreamRevenueValue.Sources.Add(docSourceRevenue);

        var upstreamMarginValue = new Fact("25%") { IsNumerical = true };
        upstreamMarginValue.Sources.Add(docSourceMargin);

        List<Fact> referencedFacts =
        [
            upstreamBroadRevenue,
            upstreamBroadMargin,
            upstreamRevenueValue,
            upstreamMarginValue,
        ];

        // Local block AI re-synthesised both upstream claims into one sentence
        // and cited the upstream block. CreateNumericalFacts has already split
        // the broad parent into the parent + two numerical sub-facts.
        const string LOCAL_SENTENCE = "Revenue rose to $184 billion alongside 25% margin growth";
        var localBlockSource = new BlockSource
        {
            CitedText = LOCAL_SENTENCE,
            BlockId = "upstream-block",
            BlockTitle = "Upstream"
        };

        var localBroad = new Fact(LOCAL_SENTENCE);
        localBroad.Sources.Add(localBlockSource);

        var localRevenueValue = new Fact("$184 billion") { IsNumerical = true };
        localRevenueValue.Sources.Add(localBlockSource);

        var localMarginValue = new Fact("25%") { IsNumerical = true };
        localMarginValue.Sources.Add(localBlockSource);

        var annotations = new BlockAnnotations();
        annotations.AddFact(localBroad);
        annotations.AddFact(localRevenueValue);
        annotations.AddFact(localMarginValue);

        // Act — same loop WorkflowHandler runs.
        foreach (Fact referencedFact in referencedFacts)
        {
            BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, referencedFact);
        }

        // Assert — broad parent picks up DocSource_revenue via broad↔broad
        // substring (LOCAL_SENTENCE contains upstream broad "Revenue rose to
        // $184 billion"). It does NOT pick up DocSource_margin: the upstream
        // broad "operating margin grew 25%" has different surface wording
        // (LOCAL_SENTENCE says "alongside 25% margin growth"), so the
        // BlockSource.CitedText mention check fails; and the asymmetric pulls
        // from upstream numerical "25%" / "$184 billion" (Quantitied) onto
        // the broad parent (no Quantity) are refused — substring fallback
        // only fires when BOTH sides are non-quantitied. DocSource_margin
        // still propagates to citations via local "25%" sub-fact (which
        // has it directly via equal-value match) or via ExpandUpstreamSources
        // at output time.
        Assert.Equal(2, localBroad.Sources.Count);
        Assert.Contains(localBroad.Sources, s => ReferenceEquals(s, docSourceRevenue));
        Assert.DoesNotContain(localBroad.Sources, s => ReferenceEquals(s, docSourceMargin));

        // Local "$184 billion" picks up DocSource_revenue (via the upstream
        // numerical "$184 billion" — Currency Quantity equivalence) and NOT
        // DocSource_margin (different kind: Currency vs Concentration).
        Assert.Equal(2, localRevenueValue.Sources.Count);
        Assert.Contains(localRevenueValue.Sources, s => ReferenceEquals(s, docSourceRevenue));
        Assert.DoesNotContain(localRevenueValue.Sources, s => ReferenceEquals(s, docSourceMargin));

        // Local "25%" picks up DocSource_margin (via the upstream numerical
        // "25%" — Concentration Quantity equivalence) and NOT DocSource_revenue.
        Assert.Equal(2, localMarginValue.Sources.Count);
        Assert.Contains(localMarginValue.Sources, s => ReferenceEquals(s, docSourceMargin));
        Assert.DoesNotContain(localMarginValue.Sources, s => ReferenceEquals(s, docSourceRevenue));
    }

    #endregion

    #region GetReferencedBlockIds Tests

    [Fact]
    public void GetReferencedBlockIds_WithNoFacts_ReturnsEmptyList()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        // Act
        List<string> result = annotations.GetReferencedBlockIds();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetReferencedBlockIds_WithFactsHavingNoBlockSources_ReturnsEmptyList()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var fact = new Fact("Some fact");
        fact.Sources.Add(new DocumentSource { CitedText = "cited", DocumentId = "doc1", PageId = "page1" });
        fact.Sources.Add(new UrlSource { CitedText = "cited", Url = "https://example.com" });

        annotations.AddFact(fact);

        // Act
        List<string> result = annotations.GetReferencedBlockIds();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetReferencedBlockIds_WithSingleBlockSource_ReturnsSingleBlockId()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var fact = new Fact("Some fact");
        fact.Sources.Add(new BlockSource { CitedText = "cited text", BlockId = "block-123" });

        annotations.AddFact(fact);

        // Act
        List<string> result = annotations.GetReferencedBlockIds();

        // Assert
        _ = Assert.Single(result);
        Assert.Equal("block-123", result[0]);
    }

    [Fact]
    public void GetReferencedBlockIds_WithMultipleFactsAndBlockSources_ReturnsAllUniqueBlockIds()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var fact1 = new Fact("Fact 1");
        fact1.Sources.Add(new BlockSource { CitedText = "cited", BlockId = "block-A" });

        var fact2 = new Fact("Fact 2");
        fact2.Sources.Add(new BlockSource { CitedText = "cited", BlockId = "block-B" });

        var fact3 = new Fact("Fact 3");
        fact3.Sources.Add(new BlockSource { CitedText = "cited", BlockId = "block-C" });

        annotations.AddFact(fact1);
        annotations.AddFact(fact2);
        annotations.AddFact(fact3);

        // Act
        List<string> result = annotations.GetReferencedBlockIds();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("block-A", result);
        Assert.Contains("block-B", result);
        Assert.Contains("block-C", result);
    }

    [Fact]
    public void GetReferencedBlockIds_WithDuplicateBlockReferences_ReturnsUniqueBlockIds()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var fact1 = new Fact("Fact 1");
        fact1.Sources.Add(new BlockSource { CitedText = "cited text 1", BlockId = "block-A" });

        var fact2 = new Fact("Fact 2");
        fact2.Sources.Add(new BlockSource { CitedText = "cited text 2", BlockId = "block-A" }); // Same block ID

        var fact3 = new Fact("Fact 3");
        fact3.Sources.Add(new BlockSource { CitedText = "cited text 3", BlockId = "block-B" });
        fact3.Sources.Add(new BlockSource { CitedText = "cited text 4", BlockId = "block-A" }); // Same block ID again

        annotations.AddFact(fact1);
        annotations.AddFact(fact2);
        annotations.AddFact(fact3);

        // Act
        List<string> result = annotations.GetReferencedBlockIds();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("block-A", result);
        Assert.Contains("block-B", result);
    }

    [Fact]
    public void GetReferencedBlockIds_WithMixedSourceTypes_ReturnsOnlyBlockSourceIds()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var fact = new Fact("Some fact");
        fact.Sources.Add(new DocumentSource { CitedText = "cited", DocumentId = "doc1", PageId = "page1" });
        fact.Sources.Add(new BlockSource { CitedText = "cited", BlockId = "block-X" });
        fact.Sources.Add(new UrlSource { CitedText = "cited", Url = "https://example.com" });
        fact.Sources.Add(new BlockSource { CitedText = "cited", BlockId = "block-Y" });

        annotations.AddFact(fact);

        // Act
        List<string> result = annotations.GetReferencedBlockIds();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("block-X", result);
        Assert.Contains("block-Y", result);
    }

    [Fact]
    public void GetReferencedBlockIds_WithFactHavingNoSources_ReturnsEmptyList()
    {
        // Arrange
        var annotations = new BlockAnnotations();

        var fact = new Fact("Fact with no sources");
        annotations.AddFact(fact);

        // Act
        List<string> result = annotations.GetReferencedBlockIds();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    [Fact]
    public void AddReferencedFacts_DateRestatedInAnotherForm_InheritsTheFact()
    {
        var annotations = new BlockAnnotations();
        var upstream = new Fact("2011-05-01") { IsNumerical = true };
        upstream.Sources.Add(new UrlSource { Url = "https://example.org/people/alex-rivera", CitedText = "start date: 2011-05-01" });

        annotations.AddReferencedFacts([upstream], "Alex Rivera has been Partner since 1 May 2011.");

        Assert.Contains(annotations.Facts, f => ReferenceEquals(f, upstream));
    }

    [Fact]
    public void AddReferencedFacts_DayFactAgainstMonthOnlyMention_IsNotInherited()
    {
        var annotations = new BlockAnnotations();
        var upstream = new Fact("2011-05-01") { IsNumerical = true };
        upstream.Sources.Add(new UrlSource { Url = "https://example.org/people/alex-rivera", CitedText = "start date: 2011-05-01" });

        annotations.AddReferencedFacts([upstream], "Alex Rivera has been Partner since May 2011.");

        Assert.Empty(annotations.Facts);
    }
}
