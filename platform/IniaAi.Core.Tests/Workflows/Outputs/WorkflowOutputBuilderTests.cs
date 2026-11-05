using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Enums;
using System.Globalization;

namespace IniaAi.Core.Tests.Workflows.Outputs;

public class WorkflowOutputBuilderTests : IDisposable
{
    private const int LONG_FILLER_LENGTH = BlockAnnotationProcessor.MAX_CITED_TEXT_LENGTH * 5;

    private readonly CultureInfo _originalCulture;
    private readonly WorkflowOutputBuilder _builder;
    private readonly WorkflowOutputContext _context;

    public WorkflowOutputBuilderTests()
    {
        _originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        _builder = new WorkflowOutputBuilder();
        _context = new WorkflowOutputContext();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        CultureInfo.CurrentCulture = _originalCulture;
    }

    [Fact]
    public async Task AiQueryBlock_NoContent_ProducesEmptyParagraph()
    {
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "NoContentParagraph",
            BlockResponse = null
        };

        ReportParagraph output = await BuildParagraph(block, _context, null);
        Assert.Equal(ReportParagraphKind.Paragraph, output.Kind);
        Assert.Null(output.Content);
    }

    [Fact]
    public async Task AiQueryBlock_HasRawText_PreservesContentAsIs()
    {
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "PlainTextParagraph" };
        SetBlockResponse(block, "Hello <content> & test");

        ReportParagraph output = await BuildParagraph(block, _context, null);

        Assert.Equal(ReportParagraphKind.Paragraph, output.Kind);
        Assert.Equal("Hello <content> & test", output.Content);
    }

    [Fact]
    public async Task AiQueryBlock_HasMarkdownContent_PreservesMarkdown()
    {
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "MarkdownParagraph" };
        SetBlockResponse(block, "# Heading\n\nSome **bold** text.");

        ReportParagraph output = await BuildParagraph(block, _context, null);

        Assert.Equal(ReportParagraphKind.Paragraph, output.Kind);
        Assert.Equal("# Heading\n\nSome **bold** text.", output.Content);
    }

    [Fact]
    public async Task AiQueryBlock_HasHtmlContent_PreservesHtml()
    {
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "HtmlParagraph" };
        SetBlockResponse(block, "<h1>Title</h1><p>body</p>");

        ReportParagraph output = await BuildParagraph(block, _context, null);

        Assert.Equal(ReportParagraphKind.Paragraph, output.Kind);
        Assert.Equal("<h1>Title</h1><p>body</p>", output.Content);
    }

    private static void SetBlockResponse(Block block, string raw)
    {
        _ = new BlockResponse(block)
        {
            RawResponse = raw
        };
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

    #region Citation Building Tests

    [Fact]
    public async Task BuildFromBlockAsync_WithFacts_PopulatesCitations()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "CitationTest"
        };
        SetBlockResponse(block, "The revenue was $5M last quarter.");

        var annotations = new BlockAnnotations();
        var fact = new Fact("$5M") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue: $5M",
            DocumentId = "doc1",
            PageId = "page1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });
        annotations.AddFact(fact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert
        Assert.NotEmpty(output.Citations);
        _ = Assert.Single(output.Citations);
        Assert.Equal("c0", output.Citations[0].Id);
        _ = Assert.Single(output.Citations[0].Sources);
        _ = Assert.IsType<DocumentSource>(output.Citations[0].Sources[0]);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithDuplicateFacts_SharesSameCitationId()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "DuplicateCitationTest"
        };
        SetBlockResponse(block, "The value is $5M. Again, $5M was reported.");

        var annotations = new BlockAnnotations();

        // Two facts with the same value
        var fact1 = new Fact("$5M") { IsNumerical = true };
        fact1.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue: $5M",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report1.pdf"
        });

        var fact2 = new Fact("$5M") { IsNumerical = true }; // Same value - should deduplicate
        fact2.Sources.Add(new DocumentSource
        {
            CitedText = "$5M confirmed",
            DocumentId = "doc2",
            PageId = "page2",
            DocumentName = "Report2.pdf"
        });

        annotations.AddFact(fact1);
        annotations.AddFact(fact2);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert - Should have single citation (deduplicated by value)
        // Note: BlockAnnotations.AddFact already deduplicates, so only one fact is added
        _ = Assert.Single(output.Citations);
        Assert.Equal("c0", output.Citations[0].Id);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithBlockSource_KeepsBlockSourceInCitation()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "BlockSourceTest"
        };
        SetBlockResponse(block, "Based on analysis, the value is 42.");

        var annotations = new BlockAnnotations();
        var fact = new Fact("42") { IsNumerical = true };
        fact.Sources.Add(new BlockSource
        {
            CitedText = "the value is 42",
            BlockId = "block-123",
            BlockTitle = "Analysis Block"
        });
        annotations.AddFact(fact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert - BlockSource should be preserved in citations
        _ = Assert.Single(output.Citations);
        _ = Assert.Single(output.Citations[0].Sources);
        _ = Assert.IsType<BlockSource>(output.Citations[0].Sources[0]);

        var blockSource = (BlockSource)output.Citations[0].Sources[0];
        Assert.Equal("block-123", blockSource.BlockId);
        Assert.Equal("Analysis Block", blockSource.BlockTitle);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithMixedSources_IncludesAllSourceTypes()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "MixedSourcesTest"
        };
        SetBlockResponse(block, "The metric is 100.");

        var annotations = new BlockAnnotations();
        var fact = new Fact("100") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "metric: 100",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });
        fact.Sources.Add(new UrlSource
        {
            CitedText = "100 reported",
            Url = "https://example.com/data"
        });
        fact.Sources.Add(new BlockSource
        {
            CitedText = "calculated 100",
            BlockId = "calc-block",
            BlockTitle = "Calculation"
        });
        annotations.AddFact(fact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert — rule 2: doc/url exist → BlockSource dropped
        _ = Assert.Single(output.Citations);
        Assert.Equal(2, output.Citations[0].Sources.Count);
        Assert.Contains(output.Citations[0].Sources, s => s is DocumentSource);
        Assert.Contains(output.Citations[0].Sources, s => s is UrlSource);
        Assert.DoesNotContain(output.Citations[0].Sources, s => s is BlockSource);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithFacts_InjectsFactIdMarkers()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "FactMarkerTest"
        };
        SetBlockResponse(block, "The revenue was $5M last quarter.");

        var annotations = new BlockAnnotations();
        var fact = new Fact("$5M") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue: $5M",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });
        annotations.AddFact(fact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert - Content should contain fact ID markers (numerical facts get tags)
        Assert.Contains("<fact id=\"c0\">", output.Content);
        Assert.Contains("$5M", output.Content);
        Assert.Contains("</fact>", output.Content);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithNoAnnotations_HasEmptyCitations()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "NoCitationsTest"
        };
        SetBlockResponse(block, "Plain text without citations.");

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, null);

        // Assert
        Assert.Empty(output.Citations);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithEmptyAnnotations_HasEmptyCitations()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "EmptyAnnotationsTest"
        };
        SetBlockResponse(block, "Plain text without citations.");

        var annotations = new BlockAnnotations(); // Empty annotations

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert
        Assert.Empty(output.Citations);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithDuplicateSourcesAcrossFacts_DeduplicatesSources()
    {
        // Arrange - Multiple facts pointing to the same document source
        // This tests the deduplication in BuildCitations
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "DuplicateSourcesTest"
        };
        SetBlockResponse(block, "The revenue was $5M and profit was $2M.");

        var annotations = new BlockAnnotations();

        // Same document source used by multiple facts
        var sharedDocSource = new DocumentSource
        {
            CitedText = "Financial data",
            DocumentId = "doc1",
            PageId = "page1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        };

        // Fact 1: "$5M" with the shared source
        var fact1 = new Fact("$5M") { IsNumerical = true };
        fact1.Sources.Add(sharedDocSource);
        annotations.AddFact(fact1);

        // Fact 2: "$2M" with the same shared source (different fact, same source)
        var fact2 = new Fact("$2M") { IsNumerical = true };
        fact2.Sources.Add(sharedDocSource);
        annotations.AddFact(fact2);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert - Should have 2 citations (one per fact value)
        Assert.Equal(2, output.Citations.Count);

        // Each citation should have exactly 1 source (deduplicated)
        foreach (Citation citation in output.Citations)
        {
            _ = Assert.Single(citation.Sources);
            DocumentSource docSource = Assert.IsType<DocumentSource>(citation.Sources[0]);
            Assert.Equal("page1", docSource.PageId);
        }
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithMultipleSourcesSamePageId_DeduplicatesWithinCitation()
    {
        // Arrange - Same fact value appearing multiple times with same source
        // Tests deduplication when same source is added multiple times to same citation
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "SameFactDuplicateSourceTest"
        };
        SetBlockResponse(block, "The value is 100.");

        var annotations = new BlockAnnotations();

        // Create a fact with multiple sources pointing to the same page
        var fact = new Fact("100") { IsNumerical = true };
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "First citation",
            DocumentId = "doc1",
            PageId = "page1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Second citation same page",
            DocumentId = "doc1",
            PageId = "page1", // Same PageId - should be deduplicated
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Different page",
            DocumentId = "doc1",
            PageId = "page2", // Different PageId - should be kept
            PageNumber = 2,
            DocumentName = "Report.pdf"
        });
        annotations.AddFact(fact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert - Should have 1 citation with 2 unique sources (page1 and page2)
        _ = Assert.Single(output.Citations);
        Assert.Equal(2, output.Citations[0].Sources.Count);

        var pageIds = output.Citations[0].Sources
            .OfType<DocumentSource>()
            .Select(s => s.PageId)
            .ToList();

        Assert.Contains("page1", pageIds);
        Assert.Contains("page2", pageIds);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithSameFactValueMultipleTimes_DeduplicatesSourcesAcrossFactInstances()
    {
        // Arrange - Two facts with the SAME value (which get merged into one citation)
        // each having sources pointing to the same page
        // This tests deduplication when sources accumulate across multiple fact instances
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "MergedFactsTest"
        };
        SetBlockResponse(block, "The value is 100.");

        var annotations = new BlockAnnotations();

        // First fact with value "100"
        var fact1 = new Fact("100") { IsNumerical = true };
        fact1.Sources.Add(new DocumentSource
        {
            CitedText = "First fact source",
            DocumentId = "doc1",
            PageId = "page1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });

        // Second fact with SAME value "100" - will be merged into same citation
        // Note: BlockAnnotations.AddFact may dedupe, so we add sources directly
        var fact2 = new Fact("100") { IsNumerical = true };
        fact2.Sources.Add(new DocumentSource
        {
            CitedText = "Second fact source same page",
            DocumentId = "doc1",
            PageId = "page1", // Same PageId - should be deduplicated
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });

        // Manually add both facts (bypassing BlockAnnotations deduplication)
        // by adding sources to the same fact
        fact1.Sources.Add(fact2.Sources[0]);
        annotations.AddFact(fact1);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert - Should have 1 citation with only 1 source (deduplicated)
        _ = Assert.Single(output.Citations);
        _ = Assert.Single(output.Citations[0].Sources); // Only 1 unique source
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithBlockSourceAndDocumentSource_KeepsBothSources()
    {
        // Arrange - A fact has both a BlockSource and a DocumentSource
        // Both sources should be kept in the citation
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "MixedSourcesTest"
        };
        SetBlockResponse(block, "Information from upstream.");

        var annotations = new BlockAnnotations();

        var blockSource = new BlockSource
        {
            CitedText = "Block cited text",
            BlockId = "upstream-block",
            BlockTitle = "Upstream Block",
            UpstreamSources =
            [
                new DocumentSource
                {
                    CitedText = "Upstream document text",
                    DocumentId = "doc1",
                    PageId = "page1",
                    PageNumber = 1,
                    DocumentName = "Report.pdf"
                }
            ]
        };

        var fact = new Fact("Information from upstream") { IsNumerical = true };
        fact.Sources.Add(blockSource);
        fact.Sources.Add(new DocumentSource
        {
            CitedText = "Direct document citation",
            DocumentId = "doc1",
            PageId = "page1",
            PageNumber = 1,
            DocumentName = "Report.pdf"
        });
        annotations.AddFact(fact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert — rule 2: doc/url exist → only doc kept, BlockSource dropped
        _ = Assert.Single(output.Citations);
        _ = Assert.Single(output.Citations[0].Sources);
        Assert.Contains(output.Citations[0].Sources, s => s is DocumentSource);
        Assert.DoesNotContain(output.Citations[0].Sources, s => s is BlockSource);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithBlockSourceNoUpstream_KeepsBlockSource()
    {
        // Arrange - A BlockSource with no upstreamSources
        // Should be kept as-is
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "BlockSourceTest"
        };
        SetBlockResponse(block, "Information from upstream.");

        var annotations = new BlockAnnotations();

        var blockSource = new BlockSource
        {
            CitedText = "Block cited text",
            BlockId = "upstream-block",
            BlockTitle = "Upstream Block",
            UpstreamSources = []
        };

        var fact = new Fact("Information from upstream") { IsNumerical = true };
        fact.Sources.Add(blockSource);
        annotations.AddFact(fact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert — rule 4: no doc/url anywhere → keep BlockSource as fallback
        _ = Assert.Single(output.Citations);
        _ = Assert.Single(output.Citations[0].Sources);

        BlockSource resultBlockSource = Assert.IsType<BlockSource>(output.Citations[0].Sources[0]);
        Assert.Equal("upstream-block", resultBlockSource.BlockId);
        Assert.Empty(resultBlockSource.UpstreamSources);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithOnlyBlockSources_PullsDocUrlFromUpstream()
    {
        // Arrange — Rule 3: only block sources → pull doc/url from upstream tree
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "Rule3Test"
        };
        SetBlockResponse(block, "Revenue was $5M last year.");

        var annotations = new BlockAnnotations();

        var blockSource = new BlockSource
        {
            CitedText = "Revenue was $5M",
            BlockId = "upstream-block",
            BlockTitle = "Upstream Block",
            UpstreamSources =
            [
                new DocumentSource
                {
                    CitedText = "Revenue $5M from annual report",
                    DocumentId = "doc1",
                    PageId = "page1",
                    PageNumber = 3,
                    DocumentName = "AnnualReport.pdf"
                },
                new UrlSource
                {
                    CitedText = "Revenue $5M per filing",
                    Url = "https://example.com/filing"
                }
            ]
        };

        var fact = new Fact("$5M") { IsNumerical = true };
        fact.Sources.Add(blockSource); // Only a block source, no direct doc/url

        annotations.AddFact(fact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert — Rule 3: doc/url pulled from upstream, block source dropped
        _ = Assert.Single(output.Citations);
        Assert.Equal(2, output.Citations[0].Sources.Count);
        Assert.Contains(output.Citations[0].Sources, s => s is DocumentSource ds && ds.DocumentName == "AnnualReport.pdf");
        Assert.Contains(output.Citations[0].Sources, s => s is UrlSource us && us.Url == "https://example.com/filing");
        Assert.DoesNotContain(output.Citations[0].Sources, s => s is BlockSource);
    }

    [Fact]
    public async Task BuildFromBlockAsync_OnlyBlockSources_UpstreamPageThatDoesNotStateTheFigure_KeepsTheBlock()
    {
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "Rule3GroundingTest" };
        SetBlockResponse(block, "Revenue fell to £13.19 million in the half.");

        var annotations = new BlockAnnotations();
        var blockSource = new BlockSource
        {
            CitedText = "The half-year results show total revenues of £13.19 million.",
            BlockId = "upstream-block",
            BlockTitle = "Revenue Growth & Segments",
            UpstreamSources =
            [
                new DocumentSource
                {
                    CitedText = "Notes to the consolidated financial statements. 1) General information.",
                    DocumentId = "doc-interim",
                    PageId = "page-16",
                    PageNumber = 16,
                    DocumentName = "Interim.pdf",
                },
            ],
        };
        var fact = new Fact("£13.19 million") { IsNumerical = true };
        fact.Sources.Add(blockSource);
        annotations.AddFact(fact);

        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        BlockSource kept = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(output.Citations).Sources));
        Assert.Equal("upstream-block", kept.BlockId);
    }

    [Theory]
    [InlineData(
        "In the half-year ended 30 June 2026, performance deteriorated sharply: revenue fell 42.38% to $17.5mn, concentrated in the Television segment.",
        "42.38%",
        "services the Group was paid for but was partially frustrated from performing by the customer. Once this impact is stripped out the like for like margin for FY24 is 42.4%.")]
    [InlineData(
        "Free-operating-cash-flow-to-debt turned sharply negative at -35.5% from 13.6%, and principal coverage on an accrual basis stood at -1.42x.",
        "13.6%",
        "| |Tangible Net Worth|--|(7.44)|(5.63)|(1.42)|(0.71)|0.00|32.24%|(8.45)|(5.55)|52.38%|13.59%|\n| |Retained Earnings|--|(11.51)|(8.99)|(5.51)|(3.65)|0.00|28.05%|")]
    public async Task BuildFromBlockAsync_OnlyBlockSources_UpstreamPageStatesOnlyARoundingAboutSomethingElse_KeepsTheBlock(
        string paragraph, string figure, string pageExcerpt)
    {
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "Rule3MeaningTest" };
        SetBlockResponse(block, paragraph);

        var annotations = new BlockAnnotations();
        var blockSource = new BlockSource
        {
            CitedText = paragraph,
            BlockId = "upstream-block",
            BlockTitle = "Operating Performance Plan",
            UpstreamSources =
            [
                new DocumentSource { CitedText = pageExcerpt, DocumentId = "doc", PageId = "page", PageNumber = 30, DocumentName = "Report.pdf" },
            ],
        };
        var fact = new Fact(figure) { IsNumerical = true };
        fact.Sources.Add(blockSource);
        annotations.AddFact(fact);

        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        Assert.IsType<BlockSource>(Assert.Single(Assert.Single(output.Citations).Sources));
    }

    [Fact]
    public async Task BuildFromBlockAsync_OnlyBlockSources_UpstreamPageStatesARoundingOfTheSameThing_PullsThePage()
    {
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "Rule3RoundingTest" };
        SetBlockResponse(block, "Herald Investment Management holds 33.2% of the shares.");

        var annotations = new BlockAnnotations();
        var blockSource = new BlockSource
        {
            CitedText = "Herald Investment Management holds 33.2%.",
            BlockId = "upstream-block",
            BlockTitle = "Ownership",
            UpstreamSources =
            [
                new DocumentSource { CitedText = "Substantial shareholdings: Herald Investment Management 8,358,726 shares, 33.19% of the issued share capital.", DocumentId = "doc", PageId = "page-48", PageNumber = 48, DocumentName = "Report.pdf" },
            ],
        };
        var fact = new Fact("33.2%") { IsNumerical = true };
        fact.Sources.Add(blockSource);
        annotations.AddFact(fact);

        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        DocumentSource page = Assert.IsType<DocumentSource>(Assert.Single(Assert.Single(output.Citations).Sources));
        Assert.Equal("page-48", page.PageId);
    }

    [Fact]
    public async Task BuildFromBlockAsync_BlockSourcePullUp_LongUpstreamCitedText_IsWindowed()
    {
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "PullUpBoundingTest" };
        SetBlockResponse(block, "Revenue was $5M last year.");

        string filler = new('x', LONG_FILLER_LENGTH);
        string longUpstreamCitedText = $"{filler} revenue $5M from annual report {filler}";

        var blockSource = new BlockSource
        {
            CitedText = "Revenue was $5M",
            BlockId = "upstream-block",
            BlockTitle = "Upstream Block",
            UpstreamSources =
            [
                new UrlSource
                {
                    CitedText = longUpstreamCitedText,
                    Url = "https://example.com/filing"
                }
            ]
        };

        var fact = new Fact("$5M") { IsNumerical = true };
        fact.Sources.Add(blockSource);

        var annotations = new BlockAnnotations();
        annotations.AddFact(fact);

        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        _ = Assert.Single(output.Citations);
        BaseSource pulledUp = Assert.Single(output.Citations[0].Sources);
        Assert.Contains(fact.Value, pulledUp.CitedText);
        Assert.True(pulledUp.CitedText.Length < longUpstreamCitedText.Length);
    }

    [Fact]
    public async Task BuildFromBlockAsync_QuantityEquivalentSource_KeepsSupportingFigure()
    {
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "EquivalenceEvidenceTest" };
        SetBlockResponse(block, "Total net assets were $184 billion overall.");

        string filler = new('z', LONG_FILLER_LENGTH);
        string longCitedText = $"{filler} total net assets of $183,990 million were reported {filler}";

        var fact = new Fact("$184 billion") { IsNumerical = true };
        fact.Sources.Add(new UrlSource { CitedText = longCitedText, Url = "https://example.com/filing" });

        var annotations = new BlockAnnotations();
        annotations.AddFact(fact);

        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        _ = Assert.Single(output.Citations);
        BaseSource source = Assert.Single(output.Citations[0].Sources);
        Assert.Contains("$183,990 million", source.CitedText);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithNestedBlockSources_PullsDocUrlFromDeepUpstream()
    {
        // Arrange — Rule 3 with nested blocks: C -> B -> A -> Doc
        // Only block sources at the top, doc buried 2 levels deep
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "DeepRule3Test"
        };
        SetBlockResponse(block, "NAV was $183,866 million.");

        var annotations = new BlockAnnotations();

        var blockSource = new BlockSource
        {
            CitedText = "NAV was $183,866 million",
            BlockId = "block-B",
            BlockTitle = "Cross-Check",
            UpstreamSources =
            [
                new BlockSource
                {
                    CitedText = "Fund NAV $183,866 million",
                    BlockId = "block-A",
                    BlockTitle = "Data Extraction",
                    UpstreamSources =
                    [
                        new DocumentSource
                        {
                            CitedText = "Fund net assets $183,866 million",
                            DocumentId = "doc1",
                            PageId = "page1",
                            PageNumber = 2,
                            DocumentName = "KIID.pdf"
                        }
                    ]
                }
            ]
        };

        var fact = new Fact("$183,866 million") { IsNumerical = true };
        fact.Sources.Add(blockSource);

        annotations.AddFact(fact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert — Rule 3: doc pulled from 2 levels deep, all block sources dropped
        _ = Assert.Single(output.Citations);
        _ = Assert.Single(output.Citations[0].Sources);
        DocumentSource docSource = Assert.IsType<DocumentSource>(output.Citations[0].Sources[0]);
        Assert.Equal("KIID.pdf", docSource.DocumentName);
    }

    [Fact]
    public async Task BuildFromBlockAsync_WithSmallAndLargeFacts_PreservesGranularCitations()
    {
        // Arrange - Content has both small numerical facts and a large broad encompassing fact
        // Small numerical facts get <fact> tags; large broad fact stays in citations but not in content
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow)
        {
            Name = "GranularCitationsTest"
        };
        SetBlockResponse(block, "Cats are cute. Dogs are loyal. Birds can fly.");

        var annotations = new BlockAnnotations();

        // Small granular facts (numerical)
        var fact1 = new Fact("Cats are cute.") { IsNumerical = true };
        fact1.Sources.Add(new DocumentSource
        {
            CitedText = "About cats",
            DocumentId = "doc1",
            PageId = "page1",
            PageNumber = 1,
            DocumentName = "Animals.pdf"
        });

        var fact2 = new Fact("Dogs are loyal.") { IsNumerical = true };
        fact2.Sources.Add(new DocumentSource
        {
            CitedText = "About dogs",
            DocumentId = "doc1",
            PageId = "page2",
            PageNumber = 2,
            DocumentName = "Animals.pdf"
        });

        var fact3 = new Fact("Birds can fly.") { IsNumerical = true };
        fact3.Sources.Add(new DocumentSource
        {
            CitedText = "About birds",
            DocumentId = "doc1",
            PageId = "page3",
            PageNumber = 3,
            DocumentName = "Animals.pdf"
        });

        // Large encompassing fact (broad, non-numerical — kept in array but no <fact> tag)
        var largeFact = new Fact("Cats are cute. Dogs are loyal. Birds can fly.") { IsNumerical = false };
        largeFact.Sources.Add(new DocumentSource
        {
            CitedText = "About all animals",
            DocumentId = "doc1",
            PageId = "page4",
            PageNumber = 4,
            DocumentName = "Animals.pdf"
        });

        annotations.AddFact(fact1);
        annotations.AddFact(fact2);
        annotations.AddFact(fact3);
        annotations.AddFact(largeFact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert — only 3 numerical citations (broad fact skipped)
        Assert.Equal(3, output.Citations.Count);
        Assert.True(output.Citations.All(c => c.IsNumerical));

        // Content should have 3 separate <fact> tags for numerical facts
        Assert.Contains("<fact id=\"c0\">Cats are cute.</fact>", output.Content);
        Assert.Contains("<fact id=\"c1\">Dogs are loyal.</fact>", output.Content);
        Assert.Contains("<fact id=\"c2\">Birds can fly.</fact>", output.Content);
    }

    #endregion

    #region Numerical Citation Tests

    [Fact]
    public async Task BuildCitations_OnlyCreatesNumericalCitations()
    {
        // Arrange
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "NumericalTest" };
        SetBlockResponse(block, "Revenue was $5M in 2024.");

        var annotations = new BlockAnnotations();

        // Broad fact (non-numerical) — should NOT produce a citation
        var broadFact = new Fact("Revenue was $5M in 2024") { IsNumerical = false };
        broadFact.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue was $5M",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });
        annotations.AddFact(broadFact);

        // Numerical sub-fact — should produce a citation
        var numFact = new Fact("$5M") { IsNumerical = true };
        numFact.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue was $5M",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });
        annotations.AddFact(numFact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert — only numerical citation, no broad
        _ = Assert.Single(output.Citations);
        Assert.True(output.Citations[0].IsNumerical);
    }

    [Fact]
    public async Task FilterOrphanCitations_OnlyNumericalCitations_BroadSkipped()
    {
        // Arrange - Broad facts no longer produce citations
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "BroadSkipTest" };
        SetBlockResponse(block, "Revenue grew 15% to $5M.");

        var annotations = new BlockAnnotations();

        // Broad fact — should NOT produce a citation
        var broadFact = new Fact("Revenue grew 15% to $5M") { IsNumerical = false };
        broadFact.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue grew",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });
        annotations.AddFact(broadFact);

        // Numerical fact — will get <fact> tag
        var numFact = new Fact("15%") { IsNumerical = true };
        numFact.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue grew 15%",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });
        annotations.AddFact(numFact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert — only numerical citation
        _ = Assert.Single(output.Citations);
        Assert.True(output.Citations[0].IsNumerical);
        Assert.Contains($"<fact id=\"{output.Citations[0].Id}\">15%</fact>", output.Content);
    }

    [Fact]
    public async Task FilterOrphanCitations_RemovesOrphanNumericalCitations()
    {
        // Arrange - A numerical fact that doesn't appear in content should be filtered
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "OrphanTest" };
        // Content has "dogs" but the numerical fact "42" won't match anything
        SetBlockResponse(block, "Cats are cute. Dogs are loyal.");

        var annotations = new BlockAnnotations();

        // Fact 1 - matches content, numerical
        var matchFact = new Fact("Cats are cute.") { IsNumerical = true };
        matchFact.Sources.Add(new DocumentSource
        {
            CitedText = "About cats",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Animals.pdf"
        });
        annotations.AddFact(matchFact);

        // Fact 2 - doesn't match content, numerical (orphan)
        var orphanFact = new Fact("42 widgets") { IsNumerical = true };
        orphanFact.Sources.Add(new DocumentSource
        {
            CitedText = "Widget count",
            DocumentId = "doc1",
            PageId = "page2",
            DocumentName = "Animals.pdf"
        });
        annotations.AddFact(orphanFact);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert - Only the matching citation should remain
        // The orphan numerical citation is filtered
        _ = Assert.Single(output.Citations);
        Assert.Equal("c0", output.Citations[0].Id);
    }

    [Fact]
    public async Task EndToEnd_NumericalValuesGetTags_BroadCitationsRemainInArray()
    {
        // Arrange - Full end-to-end: broad text with numerical sub-facts
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.AiQuery, workflow) { Name = "EndToEndTest" };
        SetBlockResponse(block, "Revenue grew 15.3% to $2.4 billion in Q3 2024.");

        var annotations = new BlockAnnotations();

        // Simulate FactExtractor output: broad + numerical sub-facts
        var broadFact = new Fact("Revenue grew 15.3% to $2.4 billion in Q3 2024") { IsNumerical = false };
        broadFact.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue grew 15.3% to $2.4 billion",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });

        var numFact1 = new Fact("15.3%") { IsNumerical = true };
        numFact1.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue grew 15.3% to $2.4 billion",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });

        var numFact2 = new Fact("$2.4 billion") { IsNumerical = true };
        numFact2.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue grew 15.3% to $2.4 billion",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });

        var numFact3 = new Fact("Q3 2024") { IsNumerical = true };
        numFact3.Sources.Add(new DocumentSource
        {
            CitedText = "Revenue grew 15.3% to $2.4 billion",
            DocumentId = "doc1",
            PageId = "page1",
            DocumentName = "Report.pdf"
        });

        annotations.AddFact(broadFact);
        annotations.AddFact(numFact1);
        annotations.AddFact(numFact2);
        annotations.AddFact(numFact3);

        // Act
        ReportParagraph output = await BuildParagraph(block, _context, annotations);

        // Assert — 3 numerical citations only (broad fact skipped)
        Assert.Equal(3, output.Citations.Count);
        Assert.True(output.Citations.All(c => c.IsNumerical));

        // Numerical citations have <fact> tags
        Assert.Contains("<fact", output.Content);
        Assert.Contains("15.3%", output.Content);
        Assert.Contains("$2.4 billion", output.Content);
        Assert.Contains("Q3 2024", output.Content);
    }

    #endregion
}
