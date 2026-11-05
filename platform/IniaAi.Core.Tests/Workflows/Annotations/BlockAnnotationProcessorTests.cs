using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Enums;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public partial class BlockAnnotationProcessorTests
{
    private const int LONG_FILLER_LENGTH = BlockAnnotationProcessor.MAX_CITED_TEXT_LENGTH * 5;
    private const string VALUE_PHRASE = "$183,866 million";
    private const string VALUE_DIGITS = "183,866";

    private readonly ITestOutputHelper _testOutputHelper;
    private readonly FactExtractor _factExtractor;
    private readonly ConfidenceScoreExtractor _confidenceScoreExtractor;
    private readonly SourceExtractor _sourceExtractor;
    private readonly BlockAnnotationProcessor _processor;

    public BlockAnnotationProcessorTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _factExtractor = new FactExtractor();
        _confidenceScoreExtractor = new ConfidenceScoreExtractor();
        _sourceExtractor = new SourceExtractor();

        _processor = new BlockAnnotationProcessor(
            _factExtractor,
            _confidenceScoreExtractor,
            _sourceExtractor);
    }

    [Fact]
    public void ProcessFacts_WithNullRawResponse_DoesNothing()
    {
        // Arrange
        ResponseContentSequence rawResponse = [];
        var pages = new Dictionary<int, Page>();
        var blockAnnotations = new BlockAnnotations();

        // Act
        _processor.ProcessFacts(rawResponse, pages, blockAnnotations);

        // Assert
        Assert.Empty(blockAnnotations.Facts);
    }

    [Fact]
    public void ProcessConfidenceScore_WithValidResponse_AddsScoreToAnnotations()
    {
        // Arrange
        string rawResponse = "This is a response <confidence>0.85</confidence>";
        var blockAnnotations = new BlockAnnotations();

        // Act
        _processor.ProcessConfidenceScore(rawResponse, blockAnnotations);

        // Assert
        Assert.Equal(0.85, blockAnnotations.ConfidenceScores.FirstOrDefault());
    }

    [Fact]
    public void ProcessConfidenceScore_WithNullResponse_DoesNothing()
    {
        // Arrange
        string? rawResponse = null;
        var blockAnnotations = new BlockAnnotations();

        // Act
        _processor.ProcessConfidenceScore(rawResponse!, blockAnnotations);

        // Assert
        Assert.Empty(blockAnnotations.ConfidenceScores);
    }

    [Fact]
    public void ProcessConfidenceScore_WithNullAnnotations_DoesNotThrow()
    {
        // Arrange
        string rawResponse = "This is a response <confidence>0.85</confidence>";

        // Act & Assert
        Exception? exception = Record.Exception(() => _processor.ProcessConfidenceScore(rawResponse, null!));
        Assert.Null(exception);
    }

    [Fact]
    public void ClearConfidenceTags_RemovesConfidenceTags()
    {
        // Arrange
        string rawResponse =
        """
        Response with confidence

        <confidence>0.85</confidence>
        """;

        // Act
        string result = BlockAnnotationProcessor.ClearConfidenceTags(rawResponse);

        // Assert
        Assert.Equal("Response with confidence", result);
    }

    [Fact]
    public void ClearFactTags_ShouldRemoveAllFactTags()
    {
        // Arrange
        string input = "This is a <fact>test fact<fact_source>page 1</fact_source></fact> in a document.";
        string expected = "This is a test fact in a document.";

        // Act
        string? result = BlockAnnotationProcessor.ClearFactTags(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClearFactTags_ShouldRemoveAttributedFactTags()
    {
        // Arrange - the actual generated format wraps the value in an id-bearing tag
        string input = "Boeing paid <fact id=\"c10\">$2.5 billion</fact> in <fact id=\"c0\">2024</fact>.";
        string expected = "Boeing paid $2.5 billion in 2024.";

        // Act
        string? result = BlockAnnotationProcessor.ClearFactTags(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClearFactTags_ShouldHandleMultipleFacts()
    {
        // Arrange
        string input = "<fact>Fact 1<fact_source>page 1</fact_source></fact> and <fact>Fact 2<fact_source>page 2; page 3</fact_source></fact>";
        string expected = "Fact 1 and Fact 2";

        // Act
        string? result = BlockAnnotationProcessor.ClearFactTags(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClearFactTags_ShouldHandleEmptyInput()
    {
        // Arrange
        string input = "";

        // Act
        string? result = BlockAnnotationProcessor.ClearFactTags(input);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void ClearFactTags_ShouldHandleInputWithoutTags()
    {
        // Arrange
        string input = "This is a regular text with no tags.";

        // Act
        string? result = BlockAnnotationProcessor.ClearFactTags(input);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void ClearFactTags_ShouldHandleNestedTags()
    {
        // Arrange
        string input = "This <fact>contains <fact>nested<fact_source>page 5</fact_source></fact> tags<fact_source>page 1</fact_source></fact>";
        string expected = "This contains nested tags";

        // Act
        string? result = BlockAnnotationProcessor.ClearFactTags(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClearFactTags_ShouldRemoveFactSourceWithAttributes()
    {
        // Arrange - uses ACTUAL tag format with attributes (as generated by CreateFormattedSourceEntries)
        string input = """This is a <fact>test fact<fact_source type="document" page-id="123" label="Doc - page 1"><cited-text>some cited text</cited-text></fact_source></fact> in a document.""";
        string expected = "This is a test fact in a document.";

        // Act
        string? result = BlockAnnotationProcessor.ClearFactTags(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClearFactTags_ShouldRemoveWebSourceWithAttributes()
    {
        // Arrange - web source with type and label attributes
        string input = """<fact>15.5%<fact_source type="web" label="https://example.com"><cited-text>growth rate</cited-text></fact_source></fact>""";
        string expected = "15.5%";

        // Act
        string? result = BlockAnnotationProcessor.ClearFactTags(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClearFactTags_ShouldRemoveMultipleAttributedSources()
    {
        // Arrange - multiple fact_source tags with attributes
        string input = """<fact>100<fact_source type="document" page-id="p1" label="Doc1 - page 1"><cited-text>cited 1</cited-text></fact_source><fact_source type="document" page-id="p2" label="Doc2 - page 3"><cited-text>cited 2</cited-text></fact_source></fact>""";
        string expected = "100";

        // Act
        string? result = BlockAnnotationProcessor.ClearFactTags(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClearSourceTags_ShouldRemoveAllSourceTags()
    {
        // Arrange
        string input =
        """
        This is a source

        <source>
        page 1
        </source>
        """;
        string expected =
        """
        This is a source
        """;

        // Act
        string result = BlockAnnotationProcessor.ClearSourceTags(input);

        // Assert
        Assert.Equal(expected, result, ignoreCase: true, ignoreWhiteSpaceDifferences: true);
    }

    #region ProcessFacts Tests

    [Fact]
    public void ProcessFacts_WithNullPages_DoesNothing()
    {
        // Arrange
        var responseContent = new ResponseContentSequence();
        responseContent.Add(new ResponseCommonContent("some text"));
        var blockAnnotations = new BlockAnnotations();

        // Act
        _processor.ProcessFacts(responseContent, null!, blockAnnotations);

        // Assert
        Assert.Empty(blockAnnotations.Facts);
    }

    [Fact]
    public void ProcessFacts_WithNullBlockAnnotations_DoesNotThrow()
    {
        // Arrange
        var responseContent = new ResponseContentSequence();
        responseContent.Add(new ResponseCommonContent("some text"));
        var pages = new Dictionary<int, Page>();

        // Act & Assert
        Exception? exception = Record.Exception(() => _processor.ProcessFacts(responseContent, pages, null!));
        Assert.Null(exception);
    }

    #endregion

    #region ExpandUpstreamSources Tests

    [Fact]
    public void ExpandUpstreamSources_DoesNotAddDuplicateSourcesToUpstreamSources()
    {
        // Arrange - Create a scenario where multiple facts match the cited text
        // This simulates a real workflow where a BlockSource cites text that matches multiple facts
        var blockAnnotations = new BlockAnnotations();

        // Create multiple facts that will ALL match the cited text (they're all substrings)
        // This is the snowball scenario: if cited text = "revenue was $5M in Q4"
        // And we have facts for "revenue", "$5M", "Q4", "revenue was $5M", etc.
        // All of them will add their sources, potentially duplicating the same DocumentSource

        var docSource1 = new DocumentSource
        {
            CitedText = "revenue: $5M",
            DocumentId = "doc1",
            PageId = "page1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        };

        var docSource2 = new DocumentSource
        {
            CitedText = "Q4 results",
            DocumentId = "doc1",
            PageId = "page2",
            PageNumber = 2,
            DocumentName = "Report.pdf"
        };

        // Fact 1: "revenue"
        var fact1 = new Fact("revenue");
        fact1.Sources.Add(docSource1);
        blockAnnotations.AddFact(fact1);

        // Fact 2: "$5M" - shares the same document source
        var fact2 = new Fact("$5M");
        fact2.Sources.Add(docSource1); // Same source as fact1!
        blockAnnotations.AddFact(fact2);

        // Fact 3: "Q4" - different source
        var fact3 = new Fact("Q4");
        fact3.Sources.Add(docSource2);
        blockAnnotations.AddFact(fact3);

        // Fact 4: "revenue was $5M" - also shares docSource1
        var fact4 = new Fact("revenue was $5M");
        fact4.Sources.Add(docSource1); // Same source again!
        blockAnnotations.AddFact(fact4);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", blockAnnotations);

        // BlockSource that cites text matching ALL four facts
        var blockSource = new BlockSource
        {
            CitedText = "revenue was $5M in Q4",
            BlockId = "block-A",
            BlockTitle = "Revenue Block"
        };

        _testOutputHelper.WriteLine($"Before expansion:");
        _testOutputHelper.WriteLine($"  BlockSource.UpstreamSources.Count = {blockSource.UpstreamSources.Count}");

        // Act
        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context);

        // Log results for debugging
        _testOutputHelper.WriteLine($"After expansion:");
        _testOutputHelper.WriteLine($"  BlockSource.UpstreamSources.Count = {expanded.UpstreamSources.Count}");

        foreach (BaseSource source in expanded.UpstreamSources)
        {
            if (source is DocumentSource ds)
            {
                _testOutputHelper.WriteLine($"  - DocumentSource: {ds.DocumentName} page {ds.PageNumber}, CitedText: {ds.CitedText}");
            }
        }

        // Count how many times docSource1 appears (should be 1, not 3)
        int docSource1Count = expanded.UpstreamSources
            .OfType<DocumentSource>()
            .Count(s => s.PageId == "page1");

        int docSource2Count = expanded.UpstreamSources
            .OfType<DocumentSource>()
            .Count(s => s.PageId == "page2");

        _testOutputHelper.WriteLine($"docSource1 (page1) appears {docSource1Count} times");
        _testOutputHelper.WriteLine($"docSource2 (page2) appears {docSource2Count} times");

        // Assert - Each unique source should appear only once
        Assert.Equal(1, docSource1Count); // Should be 1, not 3
        Assert.Equal(1, docSource2Count); // Should be 1
        Assert.Equal(2, expanded.UpstreamSources.Count); // Total unique sources
    }

    [Fact]
    public void ExpandUpstreamSources_WithNestedBlockSources_DoesNotExplodeSources()
    {
        // Arrange - Create a chain: BlockC -> BlockB -> BlockA -> Document
        // Each block has multiple facts that match, testing for exponential growth

        var docSource = new DocumentSource
        {
            CitedText = "Original document text",
            DocumentId = "doc1",
            PageId = "page1",
            PageNumber = 1,
            DocumentName = "Source.pdf"
        };

        // Block A has facts pointing to the document
        var blockAAnnotations = new BlockAnnotations();
        var factA1 = new Fact("value is 100");
        factA1.Sources.Add(docSource);
        blockAAnnotations.AddFact(factA1);

        var factA2 = new Fact("100");
        factA2.Sources.Add(docSource); // Same source
        blockAAnnotations.AddFact(factA2);

        // Block B has facts pointing to Block A
        var blockBAnnotations = new BlockAnnotations();
        var blockSourceToA = new BlockSource
        {
            CitedText = "value is 100",
            BlockId = "block-A",
            BlockTitle = "Block A"
        };
        var factB1 = new Fact("value is 100");
        factB1.Sources.Add(blockSourceToA);
        blockBAnnotations.AddFact(factB1);

        var factB2 = new Fact("100"); // Another fact with same block source
        factB2.Sources.Add(blockSourceToA);
        blockBAnnotations.AddFact(factB2);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", blockAAnnotations);
        context.SetAnnotationsForBlock("block-B", blockBAnnotations);

        // Block C cites Block B
        var blockSourceToB = new BlockSource
        {
            CitedText = "value is 100",
            BlockId = "block-B",
            BlockTitle = "Block B"
        };

        _testOutputHelper.WriteLine("Before expansion:");
        _testOutputHelper.WriteLine($"  blockSourceToB.UpstreamSources.Count = {blockSourceToB.UpstreamSources.Count}");

        // Act
        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSourceToB, context);

        // Log results
        _testOutputHelper.WriteLine("After expansion:");
        _testOutputHelper.WriteLine($"  expanded.UpstreamSources.Count = {expanded.UpstreamSources.Count}");

        int totalSources = CountAllSourcesRecursively(expanded);
        _testOutputHelper.WriteLine($"  Total sources in entire chain = {totalSources}");

        LogSourceTree(expanded, 0);

        // Assert - Should not have exponential growth
        // Expected: BlockB (1) -> BlockA (1) -> Document (1) = ~3 total sources, not exponential
        Assert.True(totalSources < 10, $"Expected fewer than 10 sources but got {totalSources} - snowball detected!");
    }

    private static int CountAllSourcesRecursively(BlockSource blockSource)
    {
        int count = blockSource.UpstreamSources.Count;
        foreach (BaseSource source in blockSource.UpstreamSources)
        {
            if (source is BlockSource nested)
            {
                count += CountAllSourcesRecursively(nested);
            }
        }

        return count;
    }

    private void LogSourceTree(BaseSource source, int depth)
    {
        string indent = new(' ', depth * 2);
        if (source is BlockSource bs)
        {
            _testOutputHelper.WriteLine($"{indent}BlockSource: {bs.BlockTitle} (id: {bs.BlockId})");
            _testOutputHelper.WriteLine($"{indent}  UpstreamSources.Count = {bs.UpstreamSources.Count}");
            foreach (BaseSource us in bs.UpstreamSources)
            {
                LogSourceTree(us, depth + 1);
            }
        }
        else if (source is DocumentSource ds)
        {
            _testOutputHelper.WriteLine($"{indent}DocumentSource: {ds.DocumentName} page {ds.PageNumber}");
        }
        else if (source is UrlSource us)
        {
            _testOutputHelper.WriteLine($"{indent}UrlSource: {us.Url}");
        }
    }

    [Fact]
    public void ExpandUpstreamSources_SourcesTheGraphBlockSourceCarries_AreNotCarriedIntoTheCopy()
    {
        var blockAnnotations = new BlockAnnotations();
        var fact = new Fact("value");
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "some text",
            DocumentId = "doc1",
            PageId = "page1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });
        blockAnnotations.AddFact(fact);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", blockAnnotations);

        var blockSource = new BlockSource
        {
            CitedText = "value",
            BlockId = "block-A",
            BlockTitle = "Block A"
        };
        blockSource.UpstreamSources.Add(new DocumentSource
        {
            CitedText = "written for another fact",
            DocumentId = "doc2",
            PageId = "page7",
            PageNumber = 7,
            DocumentName = "Other.pdf"
        });

        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context);

        DocumentSource upstream = Assert.IsType<DocumentSource>(Assert.Single(expanded.UpstreamSources));
        Assert.Equal("page1", upstream.PageId);
        Assert.Equal("page7", Assert.IsType<DocumentSource>(Assert.Single(blockSource.UpstreamSources)).PageId);
    }

    [Fact]
    public void ExpandUpstreamSources_WithFactValue_OnlyIncludesRelatedUpstreamFacts()
    {
        // Arrange — Block A has two facts: "$183,866 million" and "94.6%"
        // Both appear in the same paragraph CitedText. Without factValue filtering,
        // "$183,866 million" would pick up "94.6%"'s document source too.
        var blockAAnnotations = new BlockAnnotations();

        var docSourceKIID = new DocumentSource
        {
            CitedText = "Fund NAV $183,866 million",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "KIID.pdf"
        };
        var docSourceAwshx = new DocumentSource
        {
            CitedText = "High Liquidity 94.6%",
            DocumentId = "doc2",
            PageId = "page2",
            DocumentName = "awshx.pdf"
        };

        var factNav = new Fact("$183,866 million") { IsNumerical = true };
        factNav.Sources.Add(docSourceKIID);
        blockAAnnotations.AddFact(factNav);

        var factLiquidity = new Fact("94.6%") { IsNumerical = true };
        factLiquidity.Sources.Add(docSourceAwshx);
        blockAAnnotations.AddFact(factLiquidity);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", blockAAnnotations);

        // Block B cites Block A with a paragraph containing both values
        var blockSource = new BlockSource
        {
            CitedText = "The fund had NAV of $183,866 million with 94.6% in high liquidity",
            BlockId = "block-A"
        };

        // Act — expand with factValue = "$183,866 million"
        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context, "$183,866 million");

        // Assert — should only get KIID.pdf, NOT awshx.pdf
        _ = Assert.Single(expanded.UpstreamSources);
        DocumentSource upstreamDoc = Assert.IsType<DocumentSource>(expanded.UpstreamSources[0]);
        Assert.Equal("KIID.pdf", upstreamDoc.DocumentName);
    }

    [Fact]
    public void ExpandUpstreamSources_WithoutFactValue_IncludesAllMatchingUpstreamFacts()
    {
        // Arrange — same setup as above but WITHOUT factValue filtering
        var blockAAnnotations = new BlockAnnotations();

        var docSourceKIID = new DocumentSource
        {
            CitedText = "Fund NAV $183,866 million",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "KIID.pdf"
        };
        var docSourceAwshx = new DocumentSource
        {
            CitedText = "High Liquidity 94.6%",
            DocumentId = "doc2",
            PageId = "page2",
            DocumentName = "awshx.pdf"
        };

        var factNav = new Fact("$183,866 million");
        factNav.Sources.Add(docSourceKIID);
        blockAAnnotations.AddFact(factNav);

        var factLiquidity = new Fact("94.6%");
        factLiquidity.Sources.Add(docSourceAwshx);
        blockAAnnotations.AddFact(factLiquidity);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", blockAAnnotations);

        var blockSource = new BlockSource
        {
            CitedText = "The fund had NAV of $183,866 million with 94.6% in high liquidity",
            BlockId = "block-A"
        };

        // Act — expand WITHOUT factValue (null)
        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context);

        // Assert — gets BOTH doc sources (no filtering)
        Assert.Equal(2, expanded.UpstreamSources.Count);
    }

    [Fact]
    public void ExpandUpstreamSources_WithQuantitiedFactValue_AsymmetricContainment_DoesNotIncludeUpstream()
    {
        // Arrange — factValue "$183,866" parses as a Currency quantity
        // (scope is quantitied). Upstream fact "$183,866 million" has no
        // IsNumerical → no Quantity (broad). Asymmetric pair.
        //
        // Pre-fix: the substring fallback in AreRelatedFacts fired
        // ("$183,866 million".Contains("$183,866") = true), pulling the
        // upstream DocumentSource into UpstreamSources. That's the same
        // shape as the production-trace $747.0M-with-16-unrelated-sources
        // leak: a quantitied scope substring-matching a non-quantitied
        // upstream pulls every co-occurring source on the upstream's text.
        //
        // Post-fix: when one side is quantitied and the other isn't, the
        // substring fallback is refused. No sources pulled. The realistic
        // pipeline state would have the upstream marked numerical too,
        // which would route through QuantityMatcher.Equivalent ($183,866
        // vs $183,866 million ≠ equivalent → also refused).
        var blockAAnnotations = new BlockAnnotations();

        var docSource = new DocumentSource
        {
            CitedText = "NAV $183,866 million",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        };

        var upstreamFact = new Fact("$183,866 million");
        upstreamFact.Sources.Add(docSource);
        blockAAnnotations.AddFact(upstreamFact);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", blockAAnnotations);

        var blockSource = new BlockSource
        {
            CitedText = "Fund had $183,866 million in assets",
            BlockId = "block-A"
        };

        // Act — quantitied scope vs non-quantitied upstream
        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context, "$183,866");

        // Assert — asymmetric substring fallback refused; nothing pulled.
        Assert.Empty(expanded.UpstreamSources);
    }

    [Fact]
    public void ExpandUpstreamSources_WithFactValue_PropagatesThroughNestedBlocks()
    {
        // Arrange — 3-block chain: C -> B -> A -> Doc
        // factValue should filter at every level
        var blockAAnnotations = new BlockAnnotations();
        var docSource = new DocumentSource
        {
            CitedText = "Revenue $5M",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        };
        var unrelatedDocSource = new DocumentSource
        {
            CitedText = "Employee count 9,300",
            DocumentId = "doc2",
            PageId = "page2",
            DocumentName = "HR.pdf"
        };

        var factRevenue = new Fact("$5M");
        factRevenue.Sources.Add(docSource);
        blockAAnnotations.AddFact(factRevenue);

        var factEmployees = new Fact("9,300");
        factEmployees.Sources.Add(unrelatedDocSource);
        blockAAnnotations.AddFact(factEmployees);

        var blockBAnnotations = new BlockAnnotations();
        var blockSourceToA = new BlockSource
        {
            CitedText = "Revenue $5M and 9,300 employees",
            BlockId = "block-A"
        };
        var factB = new Fact("$5M");
        factB.Sources.Add(blockSourceToA);
        blockBAnnotations.AddFact(factB);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", blockAAnnotations);
        context.SetAnnotationsForBlock("block-B", blockBAnnotations);

        // Block C cites Block B
        var blockSourceToB = new BlockSource
        {
            CitedText = "Revenue $5M and 9,300 employees",
            BlockId = "block-B"
        };

        // Act — filter for "$5M" through the whole chain
        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSourceToB, context, "$5M");

        // Assert — should get BlockSource(block-A) in upstream, and within that, only Report.pdf (not HR.pdf)
        _ = Assert.Single(expanded.UpstreamSources);
        BlockSource nestedBlockSource = Assert.IsType<BlockSource>(expanded.UpstreamSources[0]);
        Assert.Equal("block-A", nestedBlockSource.BlockId);
        _ = Assert.Single(nestedBlockSource.UpstreamSources);
        DocumentSource finalDoc = Assert.IsType<DocumentSource>(nestedBlockSource.UpstreamSources[0]);
        Assert.Equal("Report.pdf", finalDoc.DocumentName);
    }

    [Fact]
    public void ExpandUpstreamSources_BlocksThatCiteEachOther_StopsAtTheFirstBlockReachedTwice()
    {
        var blockAAnnotations = new BlockAnnotations();
        var factA = new Fact("$5M");
        factA.Sources.Add(new BlockSource { CitedText = "Revenue $5M", BlockId = "block-B" });
        blockAAnnotations.AddFact(factA);

        var blockBAnnotations = new BlockAnnotations();
        var factB = new Fact("$5M");
        factB.Sources.Add(new BlockSource { CitedText = "Revenue $5M", BlockId = "block-A" });
        blockBAnnotations.AddFact(factB);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", blockAAnnotations);
        context.SetAnnotationsForBlock("block-B", blockBAnnotations);

        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(
            new BlockSource { CitedText = "Revenue $5M", BlockId = "block-A" }, context, "$5M");

        BlockSource toB = Assert.IsType<BlockSource>(Assert.Single(expanded.UpstreamSources));
        Assert.Equal("block-B", toB.BlockId);
        BlockSource backToA = Assert.IsType<BlockSource>(Assert.Single(toB.UpstreamSources));
        Assert.Equal("block-A", backToA.BlockId);
        Assert.Empty(backToA.UpstreamSources);
    }

    [Fact]
    public void ExpandUpstreamSources_DoesNotMatchAcrossKinds_DollarVsKelvin()
    {
        // Headline false-positive case. The pre-quantity matcher used a 1%
        // numeric tolerance with no unit awareness, so a Currency fact like
        // "$300 billion" matched a CitedText mentioning "300 billion Kelvin".
        // The quantity-aware matcher must reject the cross-kind comparison.
        var upstreamAnnotations = new BlockAnnotations();
        var unrelatedDoc = new DocumentSource
        {
            CitedText = "Surface temperature reached 300 billion Kelvin during simulation",
            DocumentId = "doc-physics",
            PageId = "page1",
            DocumentName = "Physics.pdf",
        };
        var thermalFact = new Fact("300 billion Kelvin") { IsNumerical = true };
        thermalFact.Sources.Add(unrelatedDoc);
        upstreamAnnotations.AddFact(thermalFact);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-physics", upstreamAnnotations);

        // Block downstream cites the upstream paragraph but the citation's fact
        // is a financial value — completely different kind.
        var blockSource = new BlockSource
        {
            CitedText = "Surface temperature reached 300 billion Kelvin during simulation",
            BlockId = "block-physics",
        };

        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context, "$300 billion");

        // Currency $300B and Temperature 300B Kelvin must NOT collide. The
        // pre-quantity flat-tolerance matcher would let Physics.pdf bleed up
        // into the financial citation; the kind-aware matcher rejects it.
        Assert.Empty(expanded.UpstreamSources);
    }

    [Fact]
    public void ExpandUpstreamSources_PlainWildcardMatchesTypedFact()
    {
        // Plain wildcard tier: scope is "$184 billion" (Currency USD),
        // upstream fact is a bare-magnitude Plain ("183.99 billion") because
        // the upstream AI dropped the unit symbol during re-synthesis. Plain
        // matches Currency by value via Tier 3 wildcard. Pair drift ~0.005%,
        // robust to plausible future tolerance retuning.
        var upstreamAnnotations = new BlockAnnotations();
        var revenueDoc = new DocumentSource
        {
            CitedText = "Net assets 183.99 billion in fiscal 2024",
            DocumentId = "doc-revenue",
            PageId = "page1",
            DocumentName = "Filings.pdf",
        };
        var rephrasedFact = new Fact("183.99 billion") { IsNumerical = true };
        rephrasedFact.Sources.Add(revenueDoc);
        upstreamAnnotations.AddFact(rephrasedFact);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-revenue", upstreamAnnotations);

        var blockSource = new BlockSource
        {
            CitedText = "Net assets 183.99 billion in fiscal 2024",
            BlockId = "block-revenue",
        };

        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context, "$184 billion");

        // Plain (183.99 billion) ~ Currency USD (184 billion) at the wildcard
        // tolerance → match. Filings.pdf bleeds up.
        _ = Assert.Single(expanded.UpstreamSources);
        DocumentSource doc = Assert.IsType<DocumentSource>(expanded.UpstreamSources[0]);
        Assert.Equal("Filings.pdf", doc.DocumentName);
    }

    [Fact]
    public void ExpandUpstreamSources_PreservesNumericRoundingMatch_DollarBillionVsMillion()
    {
        // Regression check: "$184 billion" ≈ "$183,990 million" (Currency USD,
        // ~0.005% drift) keeps working under the new matcher. Pair chosen
        // tight enough to remain within any plausible Currency tolerance
        // setting so the test isn't sensitive to the constant.
        var upstreamAnnotations = new BlockAnnotations();
        var navDoc = new DocumentSource
        {
            CitedText = "Total NAV $183,990 million across the portfolio",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "KIID.pdf",
        };
        var navFact = new Fact("$183,990 million") { IsNumerical = true };
        navFact.Sources.Add(navDoc);
        upstreamAnnotations.AddFact(navFact);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", upstreamAnnotations);

        var blockSource = new BlockSource
        {
            CitedText = "Total NAV $183,990 million across the portfolio",
            BlockId = "block-A",
        };

        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context, "$184 billion");

        _ = Assert.Single(expanded.UpstreamSources);
        DocumentSource doc = Assert.IsType<DocumentSource>(expanded.UpstreamSources[0]);
        Assert.Equal("KIID.pdf", doc.DocumentName);
    }

    [Fact]
    public void ExpandUpstreamSources_WithFactValue_PicksClosestQuantity_NotAllWithinTolerance()
    {
        // Path 3 residual from findings.md: two upstream facts both fall
        // inside Concentration tolerance against scope "85%" ("85.02%" and
        // "85%" both within ~0.024% drift). Pre-best-match behaviour pulled
        // BOTH sources because `Equivalent` admits anything within tolerance.
        // Best-match-wins drops the further candidate so only the upstream
        // fact whose Quantity is closest to scope contributes.
        var upstreamAnnotations = new BlockAnnotations();

        var ownershipDoc = new DocumentSource
        {
            CitedText = "Institutional shareholders 85.02%",
            DocumentId = "doc-ownership",
            PageId = "page1",
            DocumentName = "Ownership.pdf",
        };
        var utilizationDoc = new DocumentSource
        {
            CitedText = "Server utilization 85%",
            DocumentId = "doc-utilization",
            PageId = "page2",
            DocumentName = "Utilization.pdf",
        };

        var ownershipFact = new Fact("85.02%") { IsNumerical = true };
        ownershipFact.Sources.Add(ownershipDoc);
        upstreamAnnotations.AddFact(ownershipFact);

        var utilizationFact = new Fact("85%") { IsNumerical = true };
        utilizationFact.Sources.Add(utilizationDoc);
        upstreamAnnotations.AddFact(utilizationFact);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", upstreamAnnotations);

        var blockSource = new BlockSource
        {
            CitedText = "Owners hold 85.02% and server utilization is 85% in production",
            BlockId = "block-A",
        };

        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context, "85%");

        // Scope "85%" exactly equals utilizationFact (delta 0); ownershipFact
        // is 0.02pp away. Both inside tolerance — best-match-wins keeps only
        // the closer candidate.
        _ = Assert.Single(expanded.UpstreamSources);
        DocumentSource doc = Assert.IsType<DocumentSource>(expanded.UpstreamSources[0]);
        Assert.Equal("Utilization.pdf", doc.DocumentName);
    }

    [Fact]
    public void ExpandUpstreamSources_WithFactValue_TiedQuantities_KeepsAllAtMinimumDelta()
    {
        // Two upstream facts equidistant from scope. Best-match-wins does
        // not arbitrarily pick one — both stay (genuine ambiguity, the
        // matcher surfaces both sources rather than guess). Pair drifts
        // chosen tight enough to remain inside any plausible Concentration
        // tolerance setting.
        var upstreamAnnotations = new BlockAnnotations();

        var docAbove = new DocumentSource
        {
            CitedText = "Reading was 85.02%",
            DocumentId = "doc-above",
            PageId = "page1",
            DocumentName = "Above.pdf",
        };
        var docBelow = new DocumentSource
        {
            CitedText = "Reading was 84.98%",
            DocumentId = "doc-below",
            PageId = "page2",
            DocumentName = "Below.pdf",
        };

        var above = new Fact("85.02%") { IsNumerical = true };
        above.Sources.Add(docAbove);
        upstreamAnnotations.AddFact(above);

        var below = new Fact("84.98%") { IsNumerical = true };
        below.Sources.Add(docBelow);
        upstreamAnnotations.AddFact(below);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock("block-A", upstreamAnnotations);

        var blockSource = new BlockSource
        {
            CitedText = "Two adjacent readings: 85.02% and 84.98% across runs",
            BlockId = "block-A",
        };

        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context, "85%");

        Assert.Equal(2, expanded.UpstreamSources.Count);
    }

    #endregion

    #region ApplyFactsWithIds Word Boundary Tests

    [Fact]
    public void ApplyFactsWithIds_NumberInLargerNumber_ShouldNotMatch()
    {
        // Arrange - "3" should NOT match within "3.5x"
        string rawResponse = "The growth rate was 3.5x this quarter.";
        var facts = new List<Fact> { new("3") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "3" inside "3.5x"
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_PercentageInLargerNumber_ShouldNotMatch()
    {
        // Arrange - "5.1%" should NOT match within "95.1%"
        string rawResponse = "The growth rate was 95.1% this quarter.";
        var facts = new List<Fact> { new("5.1%") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "5.1%" inside "95.1%"
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_PercentageStandalone_ShouldMatch()
    {
        // Arrange - "5.1%" SHOULD match when standalone
        string rawResponse = "The growth rate was 5.1% this quarter.";
        var facts = new List<Fact> { new("5.1%") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "5.1%"
        Assert.Contains("<fact id=\"c0\">5.1%</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_DecimalInLargerDecimal_ShouldNotMatch()
    {
        // Arrange - "1.5" should NOT match within "21.5"
        string rawResponse = "The value increased to 21.5 million.";
        var facts = new List<Fact> { new("1.5") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "1.5" inside "21.5"
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_DecimalStandalone_ShouldMatch()
    {
        // Arrange - "1.5" SHOULD match when standalone
        string rawResponse = "The value increased to 1.5 million.";
        var facts = new List<Fact> { new("1.5") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "1.5"
        Assert.Contains("<fact id=\"c0\">1.5</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_IntegerInLargerNumber_ShouldNotMatch()
    {
        // Arrange - "25" should NOT match within "125"
        string rawResponse = "The total was 125 units.";
        var facts = new List<Fact> { new("25") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "25" inside "125"
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_IntegerStandalone_ShouldMatch()
    {
        // Arrange - "25" SHOULD match when standalone
        string rawResponse = "The total was 25 units.";
        var facts = new List<Fact> { new("25") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "25"
        Assert.Contains("<fact id=\"c0\">25</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_NumberWithCommas_ShouldNotMatchPartial()
    {
        // Arrange - "1,000" should NOT match within "21,000"
        string rawResponse = "Revenue was $21,000 million.";
        var facts = new List<Fact> { new("1,000") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "1,000" inside "21,000"
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_NumberWithCommasStandalone_ShouldMatch()
    {
        // Arrange - "1,000" SHOULD match when standalone
        string rawResponse = "Revenue was $1,000 million.";
        var facts = new List<Fact> { new("1,000") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "1,000"
        Assert.Contains("<fact id=\"c0\">1,000</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_BareIntegerInsideThousandsLeading_ShouldNotMatch()
    {
        // Fact "13" must not match the leading "13" inside the thousands-grouped
        // number "13,751" — the comma-followed-by-digit is part of the same
        // number, not a list separator.
        string rawResponse = "The headcount reached 13,751 employees.";
        var facts = new List<Fact> { new("13") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_BareIntegerInsideThousandsTrailing_ShouldNotMatch()
    {
        // Fact "751" must not match the trailing group inside "13,751" —
        // the digit-comma immediately before is part of the same number.
        string rawResponse = "The headcount reached 13,751 employees.";
        var facts = new List<Fact> { new("751") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_BareIntegerInListWithComma_StillMatches()
    {
        // Fact "13" SHOULD still match inside a true list "13, 14, 15" — the
        // comma is followed by a space (list separator), not a digit
        // (thousands grouping).
        string rawResponse = "Top counts: 13, 14, 15 in that order.";
        var facts = new List<Fact> { new("13") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.Contains("<fact id=\"c0\">13</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_FactValueWithCommaThousandsGrouping_StillMatches()
    {
        // The exact thousands-grouped fact value still matches its own
        // surface — only partial digit-runs across the comma are blocked.
        string rawResponse = "The headcount reached 13,751 employees.";
        var facts = new List<Fact> { new("13,751") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.Contains("<fact id=\"c0\">13,751</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_CurrencyAmount_ShouldNotMatchPartial()
    {
        // Arrange - "$5.00" should NOT match within "$15.00"
        string rawResponse = "The price was $15.00 per unit.";
        var facts = new List<Fact> { new("$5.00") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have matched
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_CurrencyAmountStandalone_ShouldMatch()
    {
        // Arrange - "$5.00" SHOULD match when standalone
        string rawResponse = "The price was $5.00 per unit.";
        var facts = new List<Fact> { new("$5.00") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "$5.00"
        Assert.Contains("<fact id=\"c0\">$5.00</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_WordInLargerWord_ShouldNotMatch()
    {
        // Arrange - "cat" should NOT match within "concatenate"
        string rawResponse = "We need to concatenate the strings.";
        var facts = new List<Fact> { new("cat") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "cat" inside "concatenate"
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_WordStandalone_ShouldMatch()
    {
        // Arrange - "cat" SHOULD match when standalone
        string rawResponse = "The cat sat on the mat.";
        var facts = new List<Fact> { new("cat") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "cat"
        Assert.Contains("<fact id=\"c0\">cat</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_PercentageAtEndOfSentence_ShouldMatch()
    {
        // Arrange - "5.1%" at end of sentence (followed by period)
        string rawResponse = "Growth was 5.1%.";
        var facts = new List<Fact> { new("5.1%") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "5.1%"
        Assert.Contains("<fact id=\"c0\">5.1%</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_PercentageAtStartOfSentence_ShouldMatch()
    {
        // Arrange - "5.1%" at start (after newline/start)
        string rawResponse = "5.1% was the growth rate.";
        var facts = new List<Fact> { new("5.1%") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "5.1%"
        Assert.Contains("<fact id=\"c0\">5.1%</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_NumberInParentheses_ShouldMatch()
    {
        // Arrange - Number in parentheses should match
        string rawResponse = "The rate (5.1%) was impressive.";
        var facts = new List<Fact> { new("5.1%") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "5.1%"
        Assert.Contains("<fact id=\"c0\">5.1%</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_MultipleOccurrencesWithMixedContext_ShouldMatchOnlyStandalone()
    {
        // Arrange - Both "5.1%" standalone and "95.1%" in same text
        string rawResponse = "Last year was 95.1%, this year is 5.1%.";
        var facts = new List<Fact> { new("5.1%") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped only the standalone "5.1%", not the one in "95.1%"
        Assert.Contains("<fact id=\"c0\">5.1%</fact>", result);
        Assert.Contains("95.1%", result); // Original "95.1%" should remain unwrapped
        Assert.DoesNotContain("9<fact", result); // Should not have "9<fact id="c0">5.1%</fact>"

        // Count occurrences of <fact> - should be exactly 1
        int factCount = GenerateFactTagRegex().Count(result);
        Assert.Equal(1, factCount);
    }

    [Fact]
    public void ApplyFactsWithIds_NegativeNumber_ShouldMatchStandalone()
    {
        // Arrange - Negative number should match when standalone
        string rawResponse = "The decline was -5.1% this quarter.";
        var facts = new List<Fact> { new("-5.1%") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "-5.1%"
        Assert.Contains("<fact id=\"c0\">-5.1%</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_YearInLargerNumber_ShouldNotMatch()
    {
        // Arrange - "2024" should NOT match within "12024" (edge case)
        string rawResponse = "The ID was 12024.";
        var facts = new List<Fact> { new("2024") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "2024" inside "12024"
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_YearStandalone_ShouldMatch()
    {
        // Arrange - "2024" SHOULD match when standalone
        string rawResponse = "In 2024, revenue grew.";
        var facts = new List<Fact> { new("2024") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "2024"
        Assert.Contains("<fact id=\"c0\">2024</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_PhraseWithNumbers_ShouldMatchExactPhrase()
    {
        // Arrange - Phrase "grew 5.1%" should match exactly
        string rawResponse = "Revenue grew 5.1% in Q4.";
        var facts = new List<Fact> { new("grew 5.1%") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped the phrase
        Assert.Contains("<fact id=\"c0\">grew 5.1%</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_NumberFollowedByUnit_ShouldMatchWithUnit()
    {
        // Arrange - "5.1 million" should match as complete unit
        string rawResponse = "Revenue was 5.1 million dollars.";
        var facts = new List<Fact> { new("5.1 million") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "5.1 million"
        Assert.Contains("<fact id=\"c0\">5.1 million</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_PercentageWithSpace_ShouldMatch()
    {
        // Arrange - Some locales use "5.1 %" with space
        string rawResponse = "The rate was 5.1 % this year.";
        var facts = new List<Fact> { new("5.1 %") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "5.1 %"
        Assert.Contains("<fact id=\"c0\">5.1 %</fact>", result);
    }

    [Fact]
    public void DatePipeline_ExtractThenApplyFactsWithIds_DateAnnotatesAsSingleUnit()
    {
        // End-to-end through the in-block side of the pipeline:
        // NumericalDataExtractor.Extract → build facts → ApplyFactsWithIds.
        // Pins that the day-month-year regex (extractor) and longest-first
        // alternation ordering (annotator) agree: "31 December 2024" is one
        // annotated unit, and a separate "page 31" gets its own annotation
        // without disrupting the date span.
        const string responseText = "Net assets as of 31 December 2024 totalled $5B, reported on page 31.";

        List<string> extracted = NumericalDataExtractor.Extract(responseText);

        // Sanity-check the extraction contract this test relies on.
        Assert.Contains("31 December 2024", extracted);
        Assert.Contains("$5B", extracted);
        Assert.Contains("31", extracted);
        Assert.DoesNotContain("December 2024", extracted);

        var facts = extracted.Select(v => new Fact(v) { IsNumerical = true }).ToList();
        var citations = facts
            .Select((_, i) => new Citation
            {
                Id = $"c{i}",
                Sources = [new DocumentSource { PageId = $"p{i}" }]
            })
            .ToList();

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(responseText, facts, citations);

        // Pull the citation IDs assigned in extraction order so the assertions
        // don't depend on which `cN` the dictionary handed each value.
        string dateId = citations[extracted.IndexOf("31 December 2024")].Id;
        string currencyId = citations[extracted.IndexOf("$5B")].Id;
        string standaloneDayId = citations[extracted.IndexOf("31")].Id;

        // Date wraps as a single unit.
        Assert.Contains($"<fact id=\"{dateId}\">31 December 2024</fact>", result);

        // Currency wraps independently.
        Assert.Contains($"<fact id=\"{currencyId}\">$5B</fact>", result);

        // The standalone "page 31" gets its own annotation.
        Assert.Contains($"page <fact id=\"{standaloneDayId}\">31</fact>.", result);

        // The "31" inside the date span is NOT wrapped a second time —
        // longest-first ordering ensures the date alternative wins at that
        // position and no nested <fact> tags appear.
        Assert.DoesNotContain($"<fact id=\"{standaloneDayId}\">31</fact> December 2024", result);

        // Exactly three opening and three closing <fact> tags — one per
        // distinct value, no duplicates from substring matches.
        int openTagCount = result.Split("<fact id=\"").Length - 1;
        int closeTagCount = result.Split("</fact>").Length - 1;
        Assert.Equal(3, openTagCount);
        Assert.Equal(3, closeTagCount);
    }

    [Fact]
    public void ApplyFactsWithIds_DateAndStandaloneDay_LongerFactWinsAtSharedPrefix()
    {
        // Regression: when both "31 December 2024" and "31" are facts, the longer
        // multi-token date must wrap as one annotation. With the previous
        // shortest-first ordering, "\b31\b" matched at position 0 of "31 December
        // 2024" before the longer alternative was tried, producing a stray
        // standalone-day citation inside the date span.
        string rawResponse = "Net assets as of 31 December 2024 totalled $5B, reported on page 31.";
        var facts = new List<Fact>
        {
            new("31 December 2024") { IsNumerical = true },
            new("31") { IsNumerical = true }
        };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
            new() { Id = "c1", Sources = [new DocumentSource { PageId = "p2" }] }
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // The date is wrapped as a single annotation, not split by a stray "31".
        Assert.Contains("<fact id=\"c0\">31 December 2024</fact>", result);
        Assert.DoesNotContain("<fact id=\"c1\">31</fact> December 2024", result);

        // The standalone "page 31" still gets its own annotation.
        Assert.Contains("page <fact id=\"c1\">31</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_OverlappingFacts_LongestMatchAtPositionWins()
    {
        // "$25M" and "25" both extracted; at "$25M" the digits "25" alone must not match
        // (word boundary handles that), and where bare "25" stands alone it should.
        // This locks in the longest-first ordering for any overlap case.
        string rawResponse = "Revenue $25M with a multiplier of 25.";
        var facts = new List<Fact>
        {
            new("$25M") { IsNumerical = true },
            new("25") { IsNumerical = true }
        };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
            new() { Id = "c1", Sources = [new DocumentSource { PageId = "p2" }] }
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.Contains("<fact id=\"c0\">$25M</fact>", result);
        Assert.Contains("of <fact id=\"c1\">25</fact>.", result);
    }

    #endregion

    #region ApplyFactsWithIds Percentage and Date Edge Cases

    [Fact]
    public void ApplyFactsWithIds_NumberBeforePercent_ShouldNotMatch()
    {
        // Arrange - "100" should NOT match within "100%"
        // The fact "100" is legitimate from elsewhere, but shouldn't match the number in "100%"
        string rawResponse = "The completion rate was 100% this quarter.";
        var facts = new List<Fact> { new("100") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "100" inside "100%"
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_NumberBeforePercent_StandaloneMatches()
    {
        // Arrange - "100" SHOULD match when standalone, not followed by %
        string rawResponse = "There were 100 participants in the study.";
        var facts = new List<Fact> { new("100") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "100"
        Assert.Contains("<fact id=\"c0\">100</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_NumberInDateDash_ShouldNotMatch()
    {
        // Arrange - "15" should NOT match within date "2024-01-15"
        string rawResponse = "The report was published on 2024-01-15.";
        var facts = new List<Fact> { new("15") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "15" inside the date
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_NumberInDateSlash_ShouldNotMatch()
    {
        // Arrange - "15" should NOT match within date "01/15/2024"
        string rawResponse = "The deadline is 01/15/2024.";
        var facts = new List<Fact> { new("15") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "15" inside the date
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_YearInDate_ShouldNotMatch()
    {
        // Arrange - "2024" should NOT match within date "2024-01-15"
        string rawResponse = "The report was published on 2024-01-15.";
        var facts = new List<Fact> { new("2024") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "2024" inside the date
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_MonthInDate_ShouldNotMatch()
    {
        // Arrange - "01" should NOT match within date "2024-01-15"
        string rawResponse = "The report was published on 2024-01-15.";
        var facts = new List<Fact> { new("01") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "01" inside the date
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_NumberStandalone_ShouldMatchNotDate()
    {
        // Arrange - "15" SHOULD match when standalone, not in a date
        string rawResponse = "There were 15 items in the list.";
        var facts = new List<Fact> { new("15") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "15"
        Assert.Contains("<fact id=\"c0\">15</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_YearStandalone_ShouldMatchNotDate()
    {
        // Arrange - "2024" SHOULD match when standalone (not in date format)
        string rawResponse = "In 2024, revenue grew significantly.";
        var facts = new List<Fact> { new("2024") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped "2024"
        Assert.Contains("<fact id=\"c0\">2024</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_MixedDateAndStandalone_ShouldMatchOnlyStandalone()
    {
        // Arrange - "15" appears both in a date and standalone
        string rawResponse = "On 2024-01-15, we counted 15 items.";
        var facts = new List<Fact> { new("15") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should have wrapped only the standalone "15", not the one in the date
        Assert.Contains("<fact id=\"c0\">15</fact>", result);
        Assert.Contains("2024-01-15", result); // Date should remain intact

        // Count occurrences of <fact> - should be exactly 1
        int factCount = GenerateFactTagRegex().Count(result);
        Assert.Equal(1, factCount);
    }

    [Fact]
    public void ApplyFactsWithIds_DecimalBeforePercent_ShouldNotMatch()
    {
        // Arrange - "5.5" should NOT match within "5.5%"
        string rawResponse = "Growth was 5.5% this year.";
        var facts = new List<Fact> { new("5.5") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Should NOT have wrapped "5.5" inside "5.5%"
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    #endregion

    #region CreateFactsFromPages Tests

    [Fact]
    public void CreateFactsFromPages_WithEmptyPages_DoesNotAddFacts()
    {
        // Arrange
        var pages = new Dictionary<int, Page>();
        var blockAnnotations = new BlockAnnotations();

        // Act
        BlockAnnotationProcessor.CreateFactsFromPages(pages, blockAnnotations, shouldUseOverviews: false);

        // Assert
        Assert.Empty(blockAnnotations.Facts);
    }

    [Fact]
    public void CreateFactsFromPages_WithPagesWithText_CreatesFactsWithDocumentSource()
    {
        // Arrange
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page1 = new Page(doc, 1, 1) { RawText = "Revenue was $5M in Q4" };
        var page2 = new Page(doc, 2, 2) { RawText = "Expenses were $3M" };

        var pages = new Dictionary<int, Page>
        {
            { 1, page1 },
            { 2, page2 }
        };
        var blockAnnotations = new BlockAnnotations();

        // Act — Phase B order: CreateFactsFromPages emits broad-only,
        // SplitNumericalFacts produces numerical sub-facts with page-text
        // verification.
        BlockAnnotationProcessor.CreateFactsFromPages(pages, blockAnnotations, shouldUseOverviews: false);
        BlockAnnotationProcessor.SplitNumericalFacts(blockAnnotations, [.. pages.Values]);

        // Assert - Each page produces a broad fact + numerical sub-facts
        // Page 1: "Revenue was $5M in Q4" → broad + "$5M" = 2 facts
        // Page 2: "Expenses were $3M" → broad + "$3M" = 2 facts
        Assert.Equal(4, blockAnnotations.Facts.Count);

        // Verify broad facts have correct sources
        Fact broadFact1 = blockAnnotations.Facts.First(f => f.Value.Contains("Revenue") && !f.IsNumerical);
        _ = Assert.Single(broadFact1.Sources);
        DocumentSource source1 = Assert.IsType<DocumentSource>(broadFact1.Sources.First());
        Assert.Equal(page1.DocumentId, source1.DocumentId);
        Assert.Equal(page1.Id, source1.PageId);
        Assert.Equal(1, source1.PageNumber);

        Fact broadFact2 = blockAnnotations.Facts.First(f => f.Value.Contains("Expenses") && !f.IsNumerical);
        _ = Assert.Single(broadFact2.Sources);
        DocumentSource source2 = Assert.IsType<DocumentSource>(broadFact2.Sources.First());
        Assert.Equal(2, source2.PageNumber);

        // Verify numerical sub-facts exist
        var numericalFacts = blockAnnotations.Facts.Where(f => f.IsNumerical).ToList();
        Assert.Equal(2, numericalFacts.Count);
        Assert.Contains(numericalFacts, f => f.Value == "$5M");
        Assert.Contains(numericalFacts, f => f.Value == "$3M");
    }

    [Fact]
    public void CreateFactsFromPages_WithPagesWithOverviews_CreatesFactsFromOverviews()
    {
        // Arrange
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc, 1, 1)
        {
            RawText = "Detailed page content here",
            Overview = "Summary: Revenue highlights"
        };

        var pages = new Dictionary<int, Page> { { 1, page } };
        var blockAnnotations = new BlockAnnotations();

        // Act
        BlockAnnotationProcessor.CreateFactsFromPages(pages, blockAnnotations, shouldUseOverviews: true);

        // Assert
        _ = Assert.Single(blockAnnotations.Facts);
        Fact fact = blockAnnotations.Facts.First();
        Assert.Equal("Summary: Revenue highlights", fact.Value);
        _ = Assert.IsType<DocumentSource>(fact.Sources.First());
    }

    [Fact]
    public void CreateFactsFromPages_WithPageWithSourceUrl_CreatesFactsWithUrlSource()
    {
        // Arrange
        var doc = new Document(new FileMetadata("IniaAi", "WebContent.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc, 1, 1)
        {
            RawText = "Web content about AI",
            SourceUrl = "https://example.com/article"
        };

        var pages = new Dictionary<int, Page> { { 1, page } };
        var blockAnnotations = new BlockAnnotations();

        // Act
        BlockAnnotationProcessor.CreateFactsFromPages(pages, blockAnnotations, shouldUseOverviews: false);

        // Assert
        _ = Assert.Single(blockAnnotations.Facts);
        Fact fact = blockAnnotations.Facts.First();
        Assert.Equal("Web content about AI", fact.Value);
        _ = Assert.Single(fact.Sources);

        UrlSource urlSource = Assert.IsType<UrlSource>(fact.Sources.First());
        Assert.Equal("https://example.com/article", urlSource.Url);
        Assert.Equal("Web content about AI", urlSource.CitedText);
    }

    [Fact]
    public void CreateFactsFromPages_WithEmptyPageContent_SkipsPage()
    {
        // Arrange
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var pageWithContent = new Page(doc, 1, 1) { RawText = "Valid content" };
        var pageWithEmptyText = new Page(doc, 2, 2) { RawText = "" };
        var pageWithWhitespace = new Page(doc, 3, 3) { RawText = "   " };
        var pageWithNullText = new Page(doc, 4, 4) { RawText = null };

        var pages = new Dictionary<int, Page>
        {
            { 1, pageWithContent },
            { 2, pageWithEmptyText },
            { 3, pageWithWhitespace },
            { 4, pageWithNullText }
        };
        var blockAnnotations = new BlockAnnotations();

        // Act
        BlockAnnotationProcessor.CreateFactsFromPages(pages, blockAnnotations, shouldUseOverviews: false);

        // Assert
        _ = Assert.Single(blockAnnotations.Facts);
        Assert.Equal("Valid content", blockAnnotations.Facts.First().Value);
    }

    [Fact]
    public void CreateFactsFromPages_WithMixedSources_CreatesCorrectSourceTypes()
    {
        // Arrange
        var doc = new Document(new FileMetadata("IniaAi", "MixedDoc.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var documentPage = new Page(doc, 1, 1) { RawText = "Document page content" };
        var webPage = new Page(doc, 2, 2)
        {
            RawText = "Web page content",
            SourceUrl = "https://example.com/page"
        };

        var pages = new Dictionary<int, Page>
        {
            { 1, documentPage },
            { 2, webPage }
        };
        var blockAnnotations = new BlockAnnotations();

        // Act
        BlockAnnotationProcessor.CreateFactsFromPages(pages, blockAnnotations, shouldUseOverviews: false);

        // Assert
        Assert.Equal(2, blockAnnotations.Facts.Count);

        Fact docFact = blockAnnotations.Facts.First(f => f.Value == "Document page content");
        _ = Assert.IsType<DocumentSource>(docFact.Sources.First());

        Fact webFact = blockAnnotations.Facts.First(f => f.Value == "Web page content");
        _ = Assert.IsType<UrlSource>(webFact.Sources.First());
    }

    [Fact]
    public void CreateFactsFromPages_WithOverviewsButEmptyOverview_SkipsPage()
    {
        // Arrange
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var pageWithOverview = new Page(doc, 1, 1)
        {
            RawText = "Detailed content",
            Overview = "Valid overview"
        };
        var pageWithoutOverview = new Page(doc, 2, 2)
        {
            RawText = "Detailed content without overview",
            Overview = null
        };
        var pageWithEmptyOverview = new Page(doc, 3, 3)
        {
            RawText = "More content",
            Overview = "   "
        };

        var pages = new Dictionary<int, Page>
        {
            { 1, pageWithOverview },
            { 2, pageWithoutOverview },
            { 3, pageWithEmptyOverview }
        };
        var blockAnnotations = new BlockAnnotations();

        // Act
        BlockAnnotationProcessor.CreateFactsFromPages(pages, blockAnnotations, shouldUseOverviews: true);

        // Assert
        _ = Assert.Single(blockAnnotations.Facts);
        Assert.Equal("Valid overview", blockAnnotations.Facts.First().Value);
    }

    [Fact]
    public void CreateFactsFromPages_TrimsPageContent()
    {
        // Arrange
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc, 1, 1) { RawText = "  Content with whitespace  " };

        var pages = new Dictionary<int, Page> { { 1, page } };
        var blockAnnotations = new BlockAnnotations();

        // Act
        BlockAnnotationProcessor.CreateFactsFromPages(pages, blockAnnotations, shouldUseOverviews: false);

        // Assert
        _ = Assert.Single(blockAnnotations.Facts);
        Assert.Equal("Content with whitespace", blockAnnotations.Facts.First().Value);
    }

    [Fact]
    public void CreateFactsFromPages_SetsDocumentSourcePropertiesCorrectly()
    {
        // Arrange
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc, 5, 7) { RawText = "Page content" };

        var pages = new Dictionary<int, Page> { { 1, page } };
        var blockAnnotations = new BlockAnnotations();

        // Act
        BlockAnnotationProcessor.CreateFactsFromPages(pages, blockAnnotations, shouldUseOverviews: false);

        // Assert
        Fact fact = blockAnnotations.Facts.First();
        DocumentSource docSource = Assert.IsType<DocumentSource>(fact.Sources.First());

        Assert.Equal(page.DocumentId, docSource.DocumentId);
        Assert.Equal(page.Id, docSource.PageId);
        Assert.Equal(5, docSource.PageNumber);
        Assert.Equal(7, docSource.FilePageNumber);
        Assert.Equal("Report.pdf", docSource.DocumentName);
        Assert.Equal("Page content", docSource.CitedText);
    }

    #endregion

    #region ApplyFactsWithIds Numerical Filtering Tests

    [Fact]
    public void ApplyFactsWithIds_WithBroadAndNumericalFacts_OnlyWrapsNumericalFacts()
    {
        // Arrange - Broad fact (non-numerical) and numerical sub-facts
        string rawResponse = "Revenue grew 15.3% to $2.4 billion in Q3 2024.";

        var broadFact = new Fact("Revenue grew 15.3% to $2.4 billion in Q3 2024") { IsNumerical = false };
        broadFact.Sources.Add(new DocumentSource { PageId = "p1" });

        var numFact1 = new Fact("15.3%") { IsNumerical = true };
        numFact1.Sources.Add(new DocumentSource { PageId = "p1" });

        var numFact2 = new Fact("$2.4 billion") { IsNumerical = true };
        numFact2.Sources.Add(new DocumentSource { PageId = "p1" });

        var numFact3 = new Fact("Q3 2024") { IsNumerical = true };
        numFact3.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { broadFact, numFact1, numFact2, numFact3 };
        // Citations only contain numerical facts (broad skipped in BuildCitations)
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
            new() { Id = "c1", Sources = [new DocumentSource { PageId = "p1" }] },
            new() { Id = "c2", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        _testOutputHelper.WriteLine($"Result: {result}");

        // Assert - Numerical facts wrapped with c0/c1/c2, broad fact NOT wrapped
        Assert.Contains("<fact id=\"c0\">15.3%</fact>", result);
        Assert.Contains("<fact id=\"c1\">$2.4 billion</fact>", result);
        Assert.Contains("<fact id=\"c2\">Q3 2024</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_WithOnlyBroadFacts_NoFactTagsApplied()
    {
        // Arrange - Only non-numerical broad facts
        string rawResponse = "The company expanded into new markets.";
        var broadFact = new Fact("The company expanded into new markets") { IsNumerical = false };
        broadFact.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { broadFact };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - No fact tags at all
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_WithOnlyNumericalFacts_AllWrapped()
    {
        // Arrange - Only numerical facts
        string rawResponse = "Revenue was $5M and growth was 15%.";
        var fact1 = new Fact("$5M") { IsNumerical = true };
        fact1.Sources.Add(new DocumentSource { PageId = "p1" });
        var fact2 = new Fact("15%") { IsNumerical = true };
        fact2.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { fact1, fact2 };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
            new() { Id = "c1", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - Both wrapped
        Assert.Contains("<fact id=\"c0\">$5M</fact>", result);
        Assert.Contains("<fact id=\"c1\">15%</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_IndexAlignmentPreserved_CorrectCitationIds()
    {
        // Arrange - Verify that citation IDs align correctly when broad facts are skipped
        // Facts: [broad, numerical($5M), numerical(2024)]
        // ApplyFactsWithIds only considers numerical facts for mapping
        string rawResponse = "The value was $5M in 2024.";

        var broadFact = new Fact("The value was $5M in 2024") { IsNumerical = false };
        broadFact.Sources.Add(new DocumentSource { PageId = "p1" });

        var numFact1 = new Fact("$5M") { IsNumerical = true };
        numFact1.Sources.Add(new DocumentSource { PageId = "p1" });

        var numFact2 = new Fact("2024") { IsNumerical = true };
        numFact2.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { broadFact, numFact1, numFact2 };
        // Citations only contain numerical facts (broad skipped)
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
            new() { Id = "c1", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        _testOutputHelper.WriteLine($"Result: {result}");

        // Assert — numerical facts mapped to c0 and c1 (broad skipped)
        Assert.Contains("<fact id=\"c0\">$5M</fact>", result);
        Assert.Contains("<fact id=\"c1\">2024</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_SingleDigitNumericalFact_NotWrapped()
    {
        string rawResponse = "There are 5 subsidiaries in the group.";
        var fact = new Fact("5") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { fact };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_OrderedListOrdinals_NotWrapped()
    {
        string rawResponse = "1. First point\n2. Second point\n3. Third point";

        var ordinal1 = new Fact("1") { IsNumerical = true };
        ordinal1.Sources.Add(new DocumentSource { PageId = "p1" });
        var ordinal2 = new Fact("2") { IsNumerical = true };
        ordinal2.Sources.Add(new DocumentSource { PageId = "p1" });
        var ordinal3 = new Fact("3") { IsNumerical = true };
        ordinal3.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { ordinal1, ordinal2, ordinal3 };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_SingleDigitSkipped_RemainingFactsKeepCitationAlignment()
    {
        string rawResponse = "Item 5 reported $2.4 billion in revenue.";

        var singleDigit = new Fact("5") { IsNumerical = true };
        singleDigit.Sources.Add(new DocumentSource { PageId = "p1" });

        var numFact = new Fact("$2.4 billion") { IsNumerical = true };
        numFact.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { singleDigit, numFact };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.DoesNotContain("<fact id=\"c0\">5</fact>", result);
        Assert.Contains("<fact id=\"c0\">$2.4 billion</fact>", result);
    }

    #endregion

    #region CreateFactsFromPages Numerical Tests

    [Fact]
    public void CreateFactsFromPages_WithNumericalContent_CreatesNumericalSubFacts()
    {
        // Arrange
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc, 1, 1) { RawText = "Revenue was $5M in Q4 2024" };

        var pages = new Dictionary<int, Page> { { 1, page } };
        var blockAnnotations = new BlockAnnotations();

        // Act — Phase B: CreateFactsFromPages emits broad-only,
        // SplitNumericalFacts produces numerical sub-facts.
        BlockAnnotationProcessor.CreateFactsFromPages(pages, blockAnnotations, shouldUseOverviews: false);
        BlockAnnotationProcessor.SplitNumericalFacts(blockAnnotations, [.. pages.Values]);

        // Assert - Should have broad fact + numerical sub-facts
        Assert.True(blockAnnotations.Facts.Count > 1);

        Fact? broadFact = blockAnnotations.Facts.FirstOrDefault(f => !f.IsNumerical);
        Assert.NotNull(broadFact);
        Assert.Contains("Revenue was $5M in Q4 2024", broadFact.Value);

        var numericalFacts = blockAnnotations.Facts.Where(f => f.IsNumerical).ToList();
        Assert.True(numericalFacts.Count >= 2); // $5M and Q4 2024
        var values = numericalFacts.Select(f => f.Value).ToList();
        Assert.Contains("$5M", values);
        Assert.Contains("Q4 2024", values);
    }

    #endregion

    #region ApplyFactsWithIds Quantity Fallback Tests

    [Fact]
    public void ApplyFactsWithIds_WrapsLocalSurface_WhenFactValueIsRephrasedAsEquivalentQuantity()
    {
        // The fact's Value ("$184 billion") doesn't appear literally in
        // rawResponse — the AI rendered it as "$183,990 million" (~0.005%
        // drift, within any plausible Currency tolerance setting). The
        // legacy regex pass would leave the fact orphan-free of any marker.
        // The Quantity fallback extracts "$183,990 million" as a Currency
        // USD quantity, matches it against fact.Quantity, and wraps the
        // local surface with the citation marker.
        string rawResponse = "Total NAV was $183,990 million in fiscal 2024.";

        var fact = new Fact("$184 billion") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { fact };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.Contains("<fact id=\"c0\">$183,990 million</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_QuantityFallback_WrapsEveryOccurrenceOfTheSurfaceItMatched()
    {
        // The literal tier marks every occurrence of a figure; a figure found by quantity must
        // be marked the same way, or its second mention reads as unsourced.
        string rawResponse = "Total drawn debt is $4.60mn. Cash fell to $4.60mn by year end.";

        var fact = new Fact("$4.6 million") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource { PageId = "p1" });

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(
            rawResponse,
            [fact],
            [new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }]);

        Assert.Equal(
            "Total drawn debt is <fact id=\"c0\">$4.60mn</fact>. Cash fell to <fact id=\"c0\">$4.60mn</fact> by year end.",
            result);
    }

    [Fact]
    public void ApplyFactsWithIds_QuantityFallback_DoesNotWrapBareSingleDigitSurface()
    {
        string rawResponse = "Per the guidelines, a Rating of 0 is assigned.\n\n<rating>0</rating>";

        var fact = new Fact("0%") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { fact };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.DoesNotContain("<fact", result);
    }

    [Fact]
    public void ApplyFactsWithIds_PreservesLiteralMatch_WhenBothLiteralAndQuantityAvailable()
    {
        // Tier 1 substring still wins. The fact appears literally; no Quantity
        // fallback should trigger and re-wrap the same span.
        string rawResponse = "Net income was $60.9 billion in fiscal 2024.";

        var fact = new Fact("$60.9 billion") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { fact };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Literal $60.9 billion wrapped, no nested or duplicate marker.
        Assert.Contains("<fact id=\"c0\">$60.9 billion</fact>", result);
        // Sanity: only one fact tag in the output.
        int firstOpen = result.IndexOf("<fact ", StringComparison.Ordinal);
        int secondOpen = result.IndexOf("<fact ", firstOpen + 1, StringComparison.Ordinal);
        Assert.Equal(-1, secondOpen);
    }

    [Fact]
    public void ApplyFactsWithIds_QuantityFallback_DoesNotDisplaceLiteralClaim()
    {
        // Two facts: F1 "$60.9 billion" appears literally; F2 "$61 billion"
        // is the rephrased form. Today F1 wraps the literal span. F2 also
        // appears in the rawResponse (different sentence, literal). Both
        // should wrap their respective literal spans; quantity-fallback must
        // not steal F1's span by matching F2 there.
        string rawResponse = "Q3 income $60.9 billion. Q4 income $61 billion.";

        var f1 = new Fact("$60.9 billion") { IsNumerical = true };
        f1.Sources.Add(new DocumentSource { PageId = "p1" });
        var f2 = new Fact("$61 billion") { IsNumerical = true };
        f2.Sources.Add(new DocumentSource { PageId = "p2" });

        var facts = new List<Fact> { f1, f2 };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
            new() { Id = "c1", Sources = [new DocumentSource { PageId = "p2" }] },
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        Assert.Contains("<fact id=\"c0\">$60.9 billion</fact>", result);
        Assert.Contains("<fact id=\"c1\">$61 billion</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_QuantityFallback_DoesNotMatchAcrossKinds()
    {
        // Currency fact "$300 billion" must NOT wrap a span like "300 billion"
        // when the local context types it differently (here: Temperature). The
        // matcher rejects cross-kind comparisons.
        string rawResponse = "Surface temperature reached 300 billion Kelvin during simulation.";

        var fact = new Fact("$300 billion") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { fact };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // No fact marker should be inserted — Currency vs Temperature is a
        // cross-kind mismatch the matcher refuses.
        Assert.DoesNotContain("<fact id=\"c0\">", result);
    }

    [System.Text.RegularExpressions.GeneratedRegex("<fact")]
    private static partial System.Text.RegularExpressions.Regex GenerateFactTagRegex();

    #endregion

    #region ApplyFactsWithIds HTML Tag-Region Guard Tests

    // A numeric fact must never be wrapped inside an HTML tag/attribute, since
    // the emitted <fact id="..."> contains a quote that corrupts the enclosing
    // attribute (the merge-block HTML-table corruption). Occurrences in body
    // text must still wrap. Plain-text responses must be unaffected.

    [Fact]
    public void ApplyFactsWithIds_NumberInsideStyleAttribute_NotWrapped()
    {
        // Arrange - "600" lives only inside style="font-weight:600", never in body text.
        string rawResponse = "<th style=\"font-weight:600\">data</th>";
        var facts = new List<Fact> { new("600") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - no marker injected into the attribute; output unchanged.
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_NumberInsideCssVariable_NotWrapped()
    {
        // Arrange - "1" appears only inside the CSS variable var(--bg-1).
        string rawResponse = "<div style=\"color:var(--bg-1)\">text</div>";
        var facts = new List<Fact> { new("1") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - the CSS variable is untouched.
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_SameNumberInBodyAndAttribute_OnlyBodyWrapped()
    {
        // Arrange - "600" appears both inside an attribute and as visible body text.
        string rawResponse = "<span data-n=\"600\">600</span>";
        var facts = new List<Fact> { new("600") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - the attribute is intact and only the body occurrence is wrapped.
        Assert.Contains("data-n=\"600\"", result);
        Assert.Contains("<fact id=\"c0\">600</fact>", result);
        Assert.Equal(1, GenerateFactTagRegex().Count(result));
    }

    [Fact]
    public void ApplyFactsWithIds_RealMergeTable_StructurePreservedAndScoresWrapped()
    {
        // Arrange - the real merge-block rating table (placeholders resolved to
        // scores). Facts: rating scores 5/6/7 (visible body cells) plus "1"
        // which only ever appears inside var(--bg-1)/var(--bg-2) attributes.
        const string rawResponse =
            """
            <table style="border-collapse:collapse;font-family:Arial,sans-serif;width:100%;background:var(--bg-1);border:1px solid var(--stroke);"><thead><tr><th style="width:50%;padding:10px 12px;border:1px solid var(--stroke);text-align:left;font-size:12px;font-weight:600;color:var(--text-primary);background:var(--bg-2);">Component</th><th style="width:50%;padding:10px 12px;border:1px solid var(--stroke);text-align:left;font-size:12px;font-weight:600;color:var(--text-primary);background:var(--bg-2);">Rating</th></tr></thead><tbody><tr><td style="padding:8px 12px;border:1px solid var(--stroke);font-size:12px;font-weight:600;color:var(--text-primary);background:var(--bg-2);vertical-align:top;">Tier Rating</td><td style="padding:8px 12px;border:1px solid var(--stroke);font-size:12px;font-family:monospace;color:var(--accent-indigo-to);background:var(--bg-2);vertical-align:top;">15</td></tr><tr><td style="padding:8px 12px;border:1px solid var(--stroke);font-size:12px;font-weight:600;color:var(--text-primary);background:var(--bg-1);vertical-align:top;">Industry Risk Rating</td><td style="padding:8px 12px;border:1px solid var(--stroke);font-size:12px;font-family:monospace;color:var(--accent-indigo-to);background:var(--bg-1);vertical-align:top;">26</td></tr><tr><td style="padding:8px 12px;border:1px solid var(--stroke);font-size:12px;font-weight:600;color:var(--text-primary);background:var(--bg-2);vertical-align:top;">Strategy Rating</td><td style="padding:8px 12px;border:1px solid var(--stroke);font-size:12px;font-family:monospace;color:var(--accent-indigo-to);background:var(--bg-2);vertical-align:top;">37</td></tr></tbody></table>
            """;
        var facts = new List<Fact>
        {
            new("15") { IsNumerical = true },
            new("26") { IsNumerical = true },
            new("37") { IsNumerical = true },
            new("10") { IsNumerical = true }
        };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
            new() { Id = "c1", Sources = [new DocumentSource { PageId = "p2" }] },
            new() { Id = "c2", Sources = [new DocumentSource { PageId = "p3" }] },
            new() { Id = "c3", Sources = [new DocumentSource { PageId = "p4" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - every styled tag survives byte-for-byte.
        Assert.Contains(
            "<table style=\"border-collapse:collapse;font-family:Arial,sans-serif;width:100%;background:var(--bg-1);border:1px solid var(--stroke);\">",
            result);
        Assert.Contains(
            "<th style=\"width:50%;padding:10px 12px;border:1px solid var(--stroke);text-align:left;font-size:12px;font-weight:600;color:var(--text-primary);background:var(--bg-2);\">",
            result);
        Assert.Contains("background:var(--bg-1);vertical-align:top;", result);

        // No marker ever injected into a CSS variable or any other attribute.
        Assert.DoesNotContain("--bg-<fact", result);
        Assert.DoesNotContain("<fact id=\"c3\"", result);

        // The visible body scores are wrapped.
        Assert.Contains("<fact id=\"c0\">15</fact>", result);
        Assert.Contains("<fact id=\"c1\">26</fact>", result);
        Assert.Contains("<fact id=\"c2\">37</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_QuantityFallbackIntoAttribute_NotWrapped()
    {
        // Arrange - the only quantity-equivalent surface ("$183,990 million"
        // ~ "$184 billion") sits inside the data-note attribute. The Tier 2/3
        // quantity fallback must not wrap inside the tag. (Mirror of the
        // existing passing fallback test, but with the surface in an attribute.)
        string rawResponse = "<div data-note=\"$183,990 million\">Total NAV.</div>";

        var fact = new Fact("$184 billion") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { fact };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - attribute left intact, no marker injected.
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_PlainTextNoHtml_UnchangedBehavior()
    {
        // Arrange - no HTML: the guard must be a no-op (regression).
        string rawResponse = "The rate was 25 today.";
        var facts = new List<Fact> { new("25") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - wraps exactly as before the guard existed.
        Assert.Contains("<fact id=\"c0\">25</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_NumberInTagContent_Wrapped()
    {
        // Arrange - "25" is visible body text between the tags.
        string rawResponse = "<td>25</td>";
        var facts = new List<Fact> { new("25") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - body content is annotated, tags preserved.
        Assert.Contains("<td><fact id=\"c0\">25</fact></td>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_AttributeValueEqualsFactExactly_NotWrapped()
    {
        // Arrange - "5" appears only as an attribute value, not in body text.
        string rawResponse = "<a data-score=\"5\">link</a>";
        var facts = new List<Fact> { new("5") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - attribute untouched, nothing wrapped.
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_VoidSelfClosingTagAttribute_NotWrapped()
    {
        // Arrange - "5" inside a self-closing tag's attribute.
        string rawResponse = "<img width=\"5\"/>";
        var facts = new List<Fact> { new("5") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - not wrapped.
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_AngleBracketsInProse_StillWraps()
    {
        // Arrange - bare angle brackets in prose ("x < 25", "y > 13") are NOT
        // HTML tags (strict tag regex requires a letter / "!" / "/" after '<'),
        // so the standalone "25" must still wrap.
        string rawResponse = "if x < 25 and y > 13 then 25 wins";
        var facts = new List<Fact> { new("25") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - the prose number is annotated despite the angle brackets.
        Assert.Contains("<fact id=\"c0\">25</fact>", result);
    }

    [Fact]
    public void ApplyFactsWithIds_MultilineHtmlAttribute_NotWrapped()
    {
        // Arrange - an attribute value spanning a newline (the tag region must
        // cross newlines, since [^>] matches them).
        const string rawResponse = "<div data-x=\"line1\n600 here\">body</div>";
        var facts = new List<Fact> { new("600") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - the in-attribute number is not wrapped.
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_GreaterThanInsideQuotedAttribute_NotWrapped()
    {
        // Arrange - a literal '>' inside a quoted attribute value must not end
        // the tag region early and leave a later in-tag number wrappable.
        string rawResponse = "<div title=\"x > 5\" data-n=\"600\">body</div>";
        var facts = new List<Fact>
        {
            new("600") { IsNumerical = true },
            new("5") { IsNumerical = true }
        };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] },
            new() { Id = "c1", Sources = [new DocumentSource { PageId = "p2" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - the whole tag is treated as one region; neither attribute
        // number is wrapped and the markup is unchanged.
        Assert.DoesNotContain("<fact", result);
        Assert.Equal(rawResponse, result);
    }

    [Fact]
    public void ApplyFactsWithIds_TierFallbackStillWrapsBody_WhenLiteralOnlyInAttribute()
    {
        // Arrange - the fact's literal ("$184 billion") appears ONLY inside an
        // attribute, so the Tier 1 match there is skipped. That skip must NOT
        // mark the value consumed, so the Tier 2/3 fallback can still recover
        // the quantity-equivalent body surface ("$183,990 million"). This pins
        // the "add to consumedValues AFTER the tag-region skip" ordering.
        string rawResponse = "<div data-note=\"$184 billion\">Total NAV was $183,990 million.</div>";

        var fact = new Fact("$184 billion") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource { PageId = "p1" });

        var facts = new List<Fact> { fact };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new DocumentSource { PageId = "p1" }] }
        };

        // Act
        string result = BlockAnnotationProcessor.ApplyFactsWithIds(rawResponse, facts, citations);

        // Assert - body surface wrapped via the fallback; the in-attribute
        // literal is left untouched.
        Assert.Contains("<fact id=\"c0\">$183,990 million</fact>", result);
        Assert.DoesNotContain("data-note=\"<fact", result);
        Assert.DoesNotContain("$184 billion</fact>", result);
    }

    #endregion

    [Fact]
    public void SplitNumericalFacts_DocumentSourceWithInScopePageThatLacksValue_RemovesSource()
    {
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc, 1, 1) { RawText = "This page discusses unrelated qualitative topics only." };

        var fact = new Fact("$5M") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue was $5M",
            DocumentId = page.DocumentId,
            PageId = page.Id,
            PageNumber = 1,
            FilePageNumber = 1,
            DocumentName = "Report.pdf",
        });

        var annotations = new BlockAnnotations();
        annotations.AddFact(fact);

        BlockAnnotationProcessor.SplitNumericalFacts(annotations, [page]);

        Assert.Empty(annotations.Facts.SelectMany(f => f.Sources));
    }

    [Fact]
    public void SplitNumericalFacts_FactInheritedFromAnEarlierBlock_IsLeftAsTheEarlierBlockMadeIt()
    {
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc, 1, 1) { RawText = "This page discusses unrelated qualitative topics only." };

        var inherited = new Fact("$5M") { IsNumerical = true };
        inherited.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue was $5M",
            DocumentId = page.DocumentId,
            PageId = page.Id,
            PageNumber = 1,
            FilePageNumber = 1,
            DocumentName = "Report.pdf",
        });

        var annotations = new BlockAnnotations();
        BlockAnnotationProcessor.InheritReferencedFacts(annotations, [inherited], "Revenue reached $5M.");

        BlockAnnotationProcessor.SplitNumericalFacts(annotations, [page]);

        Assert.Same(inherited, Assert.Single(annotations.Facts));
        _ = Assert.Single(inherited.Sources);
    }

    private static int SurvivingSourcesAfterVerification(string claimedValue, string pageText)
    {
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc, 1, 1) { RawText = pageText };

        var fact = new Fact(claimedValue) { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = $"the report states {claimedValue}",
            DocumentId = page.DocumentId,
            PageId = page.Id,
            PageNumber = 1,
            FilePageNumber = 1,
            DocumentName = "Report.pdf",
        });

        var annotations = new BlockAnnotations();
        annotations.AddFact(fact);

        BlockAnnotationProcessor.SplitNumericalFacts(annotations, [page]);

        return annotations.Facts.SelectMany(f => f.Sources).Count();
    }

    [Fact]
    public void SplitNumericalFacts_ValueOnlyPresentInsideALargerNumber_RemovesSource()
    {
        Assert.Equal(0, SurvivingSourcesAfterVerification("750", "Net assets were 1,204,750 at month end."));
    }

    [Fact]
    public void SplitNumericalFacts_ValueOnlyPresentInsideADate_RemovesSource()
    {
        Assert.Equal(0, SurvivingSourcesAfterVerification("15", "Report as of 2024-01-15 for the quarter."));
    }

    [Fact]
    public void SplitNumericalFacts_EmptyValue_RemovesSource()
    {
        Assert.Equal(0, SurvivingSourcesAfterVerification(string.Empty, "Net assets were 1,204,750 at month end."));
    }

    [Theory]
    [InlineData("1,204,750", "Net assets were 1,204,750 at month end.")]
    [InlineData("1,204,750", "Net assets were 1,204,750.")]
    [InlineData("$183,866", "Segment revenue of $183,866 million was reported.")]
    [InlineData("183,866", "Segment revenue of $183,866 million was reported.")]
    [InlineData("12.5", "The expense ratio is 12.5% per annum.")]
    [InlineData("102.35", "Class A closed at 102.35 per share.")]
    [InlineData("1.2 million", "Total assets under management were 1,200,000 USD.")]
    [InlineData("$183,866 million", "Segment revenue reached $183,866 in the period.")]
    public void SplitNumericalFacts_ValueGenuinelyStatedOnThePage_KeepsSource(string claimedValue, string pageText)
    {
        Assert.Equal(1, SurvivingSourcesAfterVerification(claimedValue, pageText));
    }

    [Fact]
    public void SplitNumericalFacts_UrlSourceThatLacksValue_RemovesSource()
    {
        var fact = new Fact("$5M") { IsNumerical = true };
        fact.Sources.Add(new UrlSource
        {
            Url = "https://example.com/article",
            CitedText = "This article discusses unrelated qualitative topics only.",
        });

        var annotations = new BlockAnnotations();
        annotations.AddFact(fact);

        BlockAnnotationProcessor.SplitNumericalFacts(annotations, []);

        Assert.Empty(annotations.Facts.SelectMany(f => f.Sources));
    }

    [Fact]
    public void SplitNumericalFacts_DocumentSourceWithPageOutOfScope_RemovesSource()
    {
        var doc = new Document(new FileMetadata("IniaAi", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc, 1, 1) { RawText = "This page discusses unrelated qualitative topics only." };

        var fact = new Fact("$5M") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "This document discusses unrelated qualitative topics only.",
            DocumentId = page.DocumentId,
            PageId = page.Id,
            PageNumber = 1,
            FilePageNumber = 1,
            DocumentName = "Report.pdf",
        });

        var annotations = new BlockAnnotations();
        annotations.AddFact(fact);

        BlockAnnotationProcessor.SplitNumericalFacts(annotations, []);

        Assert.Empty(annotations.Facts.SelectMany(f => f.Sources));
    }

    [Fact]
    public void BoundCitationSources_LongSourceWithLiteralValue_WindowsAroundValue()
    {
        string filler = new('x', LONG_FILLER_LENGTH);
        string citedText = $"{filler} fund net assets were {VALUE_PHRASE} as reported {filler}";

        var citation = new Citation
        {
            Id = "c0",
            IsNumerical = true,
            Sources = [new UrlSource { Url = "https://example.com", CitedText = citedText }],
        };

        BlockAnnotationProcessor.BoundCitationSources(citation, VALUE_PHRASE);

        BaseSource windowed = Assert.Single(citation.Sources);
        Assert.Contains(VALUE_DIGITS, windowed.CitedText);
        Assert.True(windowed.CitedText.Length < citedText.Length);
        Assert.StartsWith(BlockAnnotationProcessor.CITED_TEXT_ELLIPSIS, windowed.CitedText);
        Assert.EndsWith(BlockAnnotationProcessor.CITED_TEXT_ELLIPSIS, windowed.CitedText);
    }

    [Fact]
    public void BoundCitationSources_LongDocumentSource_WindowsPreservingType()
    {
        string filler = new('y', LONG_FILLER_LENGTH);
        string citedText = $"{filler} revenue of {VALUE_PHRASE} was reported {filler}";

        var citation = new Citation
        {
            Id = "c0",
            IsNumerical = true,
            Sources =
            [
                new DocumentSource
                {
                    CitedText = citedText,
                    DocumentId = "d1",
                    PageId = "p1",
                    PageNumber = 1,
                    FilePageNumber = 1,
                    DocumentName = "Report.pdf",
                }
            ],
        };

        BlockAnnotationProcessor.BoundCitationSources(citation, VALUE_PHRASE);

        BaseSource windowed = Assert.Single(citation.Sources);
        _ = Assert.IsType<DocumentSource>(windowed);
        Assert.Contains(VALUE_DIGITS, windowed.CitedText);
        Assert.True(windowed.CitedText.Length < citedText.Length);
    }

    [Fact]
    public void BoundCitationSources_LongSourceWithEquivalentValue_WindowsAroundTheEquivalent()
    {
        string filler = new('z', LONG_FILLER_LENGTH);
        string citedText = $"{filler} total net assets of $183,990 million were reported {filler}";

        var citation = new Citation
        {
            Id = "c0",
            IsNumerical = true,
            Sources = [new UrlSource { Url = "https://example.com", CitedText = citedText }],
        };

        BlockAnnotationProcessor.BoundCitationSources(citation, "$184 billion");

        BaseSource windowed = Assert.Single(citation.Sources);
        Assert.Contains("$183,990 million", windowed.CitedText);
        Assert.True(windowed.CitedText.Length < citedText.Length);
    }

    [Fact]
    public void BoundCitationSources_LongSourceValueNotLocatable_KeptToPreserveEvidence()
    {
        string filler = new('z', LONG_FILLER_LENGTH);
        string citedText = $"{filler} total net assets were reported {filler}";

        var citation = new Citation
        {
            Id = "c0",
            IsNumerical = true,
            Sources = [new UrlSource { Url = "https://example.com", CitedText = citedText }],
        };

        BlockAnnotationProcessor.BoundCitationSources(citation, "$184 billion");

        BaseSource kept = Assert.Single(citation.Sources);
        Assert.Equal(citedText, kept.CitedText);
    }

    [Fact]
    public void BoundCitationSources_ShortSource_KeptVerbatim()
    {
        string citedText = $"Net assets were {VALUE_PHRASE} as of April 30.";

        var citation = new Citation
        {
            Id = "c0",
            IsNumerical = true,
            Sources = [new UrlSource { Url = "https://example.com", CitedText = citedText }],
        };

        BlockAnnotationProcessor.BoundCitationSources(citation, VALUE_PHRASE);

        Assert.Equal(citedText, Assert.Single(citation.Sources).CitedText);
    }

    [Fact]
    public void BoundCitationSources_SurrogatePairsAtWindowBoundary_StaysWellFormed()
    {
        string pad = string.Concat(Enumerable.Repeat("😀", LONG_FILLER_LENGTH));

        for (int shift = 0; shift < 4; shift++)
        {
            string gap = new('x', shift);
            string citedText = pad + gap + VALUE_PHRASE + gap + pad;

            var citation = new Citation
            {
                Id = "c0",
                IsNumerical = true,
                Sources = [new UrlSource { Url = "https://example.com", CitedText = citedText }],
            };

            BlockAnnotationProcessor.BoundCitationSources(citation, VALUE_PHRASE);

            string bounded = Assert.Single(citation.Sources).CitedText;
            AssertWellFormedUtf16(bounded);
            Assert.Contains(VALUE_DIGITS, bounded);
        }
    }

    private static void AssertWellFormedUtf16(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                Assert.True(i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]));
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(text[i]));
            }
        }
    }

    [Fact]
    public void CreateNumericalFacts_DateStatedInProse_GroundedByIsoDateInSource()
    {
        var broad = new Fact("Vice President from January 2010 through May 2011, and Partner/Managing Director from May 2011 onward");
        broad.Sources.Add(new UrlSource
        {
            Url = "https://linkedin.com/in/example",
            CitedText = "start date: 2010-01-01 end date: 2011-05-01 Title: Vice President",
        });

        List<Fact> facts = BlockAnnotationProcessor.CreateNumericalFacts(broad);

        List<Fact> promotions = [.. facts.Where(f => f.IsNumerical && f.Value == "May 2011")];
        Assert.NotEmpty(promotions);
        Assert.All(promotions, promotion => Assert.Single(promotion.Sources, s => s is UrlSource));
    }

    [Fact]
    public void SplitNumericalFacts_DateFactWithIsoDateUrlSource_KeepsTheSource()
    {
        var annotations = new BlockAnnotations();
        var dateFact = new Fact("May 2011") { IsNumerical = true };
        dateFact.Sources.Add(new UrlSource
        {
            Url = "https://linkedin.com/in/example",
            CitedText = "start date: 2011-05-01 Title: Partner / Managing Director",
        });
        annotations.AddFact(dateFact);

        BlockAnnotationProcessor.SplitNumericalFacts(annotations, []);

        Fact kept = Assert.Single(annotations.Facts, f => f.Value == "May 2011");
        Assert.Single(kept.Sources, s => s is UrlSource);
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_DateRestatedInAnotherForm_AttachesUpstreamSource()
    {
        const string URL = "https://example.org/people/alex-rivera";
        var annotations = new BlockAnnotations();
        var local = new Fact("May 2011") { IsNumerical = true };
        local.Sources.Add(new BlockSource
        {
            BlockId = "block-a",
            BlockTitle = "Extraction",
            CitedText = "Partner / Managing Director since 2011-05-01",
        });
        annotations.AddFact(local);

        var upstream = new Fact("2011-05-01") { IsNumerical = true };
        upstream.Sources.Add(new UrlSource { Url = URL, CitedText = "start date: 2011-05-01 Title: Partner" });

        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, upstream);

        Assert.Contains(local.Sources, s => s is UrlSource url && url.Url == URL);
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_CitedTextRestatesUpstreamDate_AttachesUpstreamSource()
    {
        const string URL = "https://example.org/people/alex-rivera";
        var annotations = new BlockAnnotations();
        var local = new Fact("May 2011") { IsNumerical = true };
        local.Sources.Add(new BlockSource
        {
            BlockId = "block-a",
            BlockTitle = "Extraction",
            CitedText = "Partner / Managing Director since 1 May 2011",
        });
        annotations.AddFact(local);

        var upstream = new Fact("2011-05-01") { IsNumerical = true };
        upstream.Sources.Add(new UrlSource { Url = URL, CitedText = "start date: 2011-05-01 Title: Partner" });

        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, upstream);

        Assert.Contains(local.Sources, s => s is UrlSource url && url.Url == URL);
    }

    [Fact]
    public void ExpandUpstreamSources_CitedTextIsMonthOnlyRestatementOfUpstreamDay_PullsUpstreamSource()
    {
        const string URL = "https://example.org/people/alex-rivera";
        var context = new WorkflowAnnotationsContext();
        var upstreamAnnotations = new BlockAnnotations();
        var upstream = new Fact("2011-05-01") { IsNumerical = true };
        upstream.Sources.Add(new UrlSource { Url = URL, CitedText = "start date: 2011-05-01 Title: Partner" });
        upstreamAnnotations.AddFact(upstream);
        context.SetAnnotationsForBlock("block-a", upstreamAnnotations);

        var blockSource = new BlockSource { BlockId = "block-a", CitedText = "May 2011" };

        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context, "May 2011");

        Assert.Contains(expanded.UpstreamSources, s => s is UrlSource url && url.Url == URL);
    }

    [Fact]
    public void ExpandSourcesFromReferencedFact_DifferentDatesInOneCitedText_DoesNotAttachUpstreamSource()
    {
        var annotations = new BlockAnnotations();
        var local = new Fact("June 2012") { IsNumerical = true };
        local.Sources.Add(new BlockSource
        {
            BlockId = "block-a",
            BlockTitle = "Extraction",
            CitedText = "Partner since 2011-05-01; Managing Director since 2012-06-01",
        });
        annotations.AddFact(local);

        var upstream = new Fact("2011-05-01") { IsNumerical = true };
        upstream.Sources.Add(new UrlSource { Url = "https://example.org/people/alex-rivera", CitedText = "start date: 2011-05-01" });

        BlockAnnotationProcessor.ExpandSourcesFromReferencedFact(annotations, upstream);

        Assert.DoesNotContain(local.Sources, s => s is UrlSource);
    }

    [Fact]
    public void ExpandUpstreamSources_WithDateFactValueRestatedInAnotherForm_PullsUpstreamSource()
    {
        const string URL = "https://example.org/people/alex-rivera";
        var context = new WorkflowAnnotationsContext();
        var upstreamAnnotations = new BlockAnnotations();
        var upstream = new Fact("2011-05-01") { IsNumerical = true };
        upstream.Sources.Add(new UrlSource { Url = URL, CitedText = "start date: 2011-05-01 Title: Partner" });
        upstreamAnnotations.AddFact(upstream);
        context.SetAnnotationsForBlock("block-a", upstreamAnnotations);

        var blockSource = new BlockSource
        {
            BlockId = "block-a",
            CitedText = "Partner / Managing Director since 2011-05-01",
        };

        BlockSource expanded = BlockAnnotationProcessor.ExpandUpstreamSources(blockSource, context, "May 2011");

        Assert.Contains(expanded.UpstreamSources, s => s is UrlSource url && url.Url == URL);
    }

    [Fact]
    public void ApplyFactsWithIds_DateFactRestatedInAnotherForm_WrapsTheRestatedSpan()
    {
        const string RAW = "Alex Rivera has been Partner since 1 May 2011.";
        var facts = new List<Fact> { new("2011-05-01") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new UrlSource { Url = "https://example.org/people/alex-rivera" }] },
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(RAW, facts, citations);

        Assert.Equal("Alex Rivera has been Partner since <fact id=\"c0\">1 May 2011</fact>.", result);
    }

    [Fact]
    public void ApplyFactsWithIds_DateFactRestatedTwice_WrapsEveryRestatement()
    {
        const string RAW = "Alex Rivera joined on 1 May 2011. Since 1 May 2011 Alex has led the fund.";
        var facts = new List<Fact> { new("2011-05-01") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new UrlSource { Url = "https://example.org/people/alex-rivera" }] },
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(RAW, facts, citations);

        Assert.Equal(
            "Alex Rivera joined on <fact id=\"c0\">1 May 2011</fact>. Since <fact id=\"c0\">1 May 2011</fact> Alex has led the fund.",
            result);
    }

    [Fact]
    public void ApplyFactsWithIds_DayFactAgainstMonthOnlyMention_DoesNotWrap()
    {
        const string RAW = "Alex Rivera has been Partner since May 2011.";
        var facts = new List<Fact> { new("2011-05-01") { IsNumerical = true } };
        var citations = new List<Citation>
        {
            new() { Id = "c0", Sources = [new UrlSource { Url = "https://example.org/people/alex-rivera" }] },
        };

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(RAW, facts, citations);

        Assert.Equal(RAW, result);
    }
}
