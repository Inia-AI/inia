using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Enums;

namespace IniaAi.Core.Tests.Workflows.Outputs;

public class BlockCitedSourcesTests
{
    private const string RESEARCH_BLOCK_ID = "block-research";
    private const string SYNTHESIS_BLOCK_ID = "block-synthesis";
    private const string PROFILE_URL = "https://example.org/people/alex-rivera";
    private const string TEAM_URL = "https://example.org/team";
    private const string FUND_DOCUMENT = "Fund memorandum.pdf";

    private readonly WorkflowOutputBuilder _builder = new();

    [Fact]
    public async Task BlockOnlyCitation_ParagraphListsEveryPageAndLinkTheBlockChainCitedAsync()
    {
        WorkflowAnnotationsContext context = ResearchAndSynthesisChain();
        (Block block, BlockAnnotations annotations) = OutputCiting("Alex Rivera has been Partner since May 2011.", "May 2011");

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        BlockCitedSources synthesis = Assert.Single(paragraph.CitedSourcesByBlock, e => e.BlockId == SYNTHESIS_BLOCK_ID);
        Assert.Collection(
            synthesis.Sources,
            source => Assert.Equal(FUND_DOCUMENT, Assert.IsType<DocumentSource>(source).DocumentName),
            source => Assert.Equal(PROFILE_URL, Assert.IsType<UrlSource>(source).Url),
            source => Assert.Equal(TEAM_URL, Assert.IsType<UrlSource>(source).Url));
    }

    [Fact]
    public async Task BlockOnlyCitation_StillCarriesItsBlockSourceAsync()
    {
        WorkflowAnnotationsContext context = ResearchAndSynthesisChain();
        (Block block, BlockAnnotations annotations) = OutputCiting("Alex Rivera has been Partner since May 2011.", "May 2011");

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        Citation citation = Assert.Single(paragraph.Citations);
        BlockSource blockSource = Assert.IsType<BlockSource>(Assert.Single(citation.Sources));
        Assert.Equal(SYNTHESIS_BLOCK_ID, blockSource.BlockId);
    }

    [Fact]
    public async Task BlockOnlyCitation_DateStatedInAnotherForm_MarksTheExcerptThatStatesItAsync()
    {
        WorkflowAnnotationsContext context = ResearchAndSynthesisChain();
        (Block block, BlockAnnotations annotations) = OutputCiting("Alex Rivera has been Partner since May 2011.", "May 2011");

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        BlockSource blockSource = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(paragraph.Citations).Sources));
        UrlSource match = Assert.IsType<UrlSource>(Assert.Single(blockSource.MatchingSources));
        Assert.Equal(PROFILE_URL, match.Url);
    }

    [Fact]
    public async Task BlockOnlyCitation_SecondExcerptOfALinkStatesTheFact_MarksThatExcerptAsync()
    {
        WorkflowAnnotationsContext context = ResearchAndSynthesisChain();
        (Block block, BlockAnnotations annotations) = OutputCiting("Alex Rivera joined as Analyst in June 2005.", "June 2005");

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        BlockSource blockSource = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(paragraph.Citations).Sources));
        UrlSource match = Assert.IsType<UrlSource>(Assert.Single(blockSource.MatchingSources));
        Assert.Equal(PROFILE_URL, match.Url);
        Assert.Contains("2005-06-01", match.CitedText);
    }

    [Fact]
    public async Task BlockOnlyCitation_QuantityStatedInAnotherForm_MarksTheExcerptThatStatesItAsync()
    {
        WorkflowAnnotationsContext context = ResearchAndSynthesisChain();
        (Block block, BlockAnnotations annotations) = OutputCiting("The fund holds $89.9 million in assets.", "$89.9 million");

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        BlockSource blockSource = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(paragraph.Citations).Sources));
        DocumentSource match = Assert.IsType<DocumentSource>(Assert.Single(blockSource.MatchingSources));
        Assert.Equal(FUND_DOCUMENT, match.DocumentName);
    }

    [Fact]
    public async Task BlockOnlyCitation_ParaphrasedFact_MarksNothingButStillListsTheChainAsync()
    {
        WorkflowAnnotationsContext context = ResearchAndSynthesisChain();
        (Block block, BlockAnnotations annotations) = OutputCiting("The team has 12 partners.", "12 partners");

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        BlockSource blockSource = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(paragraph.Citations).Sources));
        Assert.Empty(blockSource.MatchingSources);
        Assert.Equal(3, Assert.Single(paragraph.CitedSourcesByBlock, e => e.BlockId == SYNTHESIS_BLOCK_ID).Sources.Count);
    }

    [Fact]
    public async Task BlockOnlyCitation_LeavesTheSharedSourceGraphAsItWasAsync()
    {
        WorkflowAnnotationsContext context = ResearchAndSynthesisChain();
        (Block block, BlockAnnotations annotations) = OutputCiting("Alex Rivera has been Partner since May 2011.", "May 2011");
        BlockSource sharedBlockSource = annotations.Facts.SelectMany(f => f.Sources).OfType<BlockSource>().Single();
        int researchSourceCount = context.GetAnnotationsForBlock(RESEARCH_BLOCK_ID)!.Facts.Sum(f => f.Sources.Count);

        _ = await BuildAsync(block, annotations, context);

        Assert.Empty(sharedBlockSource.MatchingSources);
        Assert.Equal(researchSourceCount, context.GetAnnotationsForBlock(RESEARCH_BLOCK_ID)!.Facts.Sum(f => f.Sources.Count));
    }

    [Fact]
    public async Task CitationWithAPageOfItsOwn_AddsNoBlockListAsync()
    {
        WorkflowAnnotationsContext context = ResearchAndSynthesisChain();
        (Block block, BlockAnnotations annotations) = OutputCiting("Alex Rivera has been Partner since May 2011.", "May 2011");
        annotations.Facts.Single().Sources.Add(new DocumentSource
        {
            CitedText = "Partner since May 2011",
            DocumentId = "doc-own",
            PageId = "page-own",
            PageNumber = 2,
            DocumentName = "Own.pdf",
        });

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        Assert.Empty(paragraph.CitedSourcesByBlock);
    }

    [Fact]
    public async Task BlockOnlyChain_EveryBlockItReachesGetsItsOwnListAsync()
    {
        WorkflowAnnotationsContext context = ResearchAndSynthesisChain();
        var restatedDate = new Fact("May 2011") { IsNumerical = true };
        restatedDate.Sources.Add(new BlockSource { BlockId = RESEARCH_BLOCK_ID, BlockTitle = "Research", CitedText = "Partner since 2011-05-01" });
        context.GetAnnotationsForBlock(SYNTHESIS_BLOCK_ID)!.AddFact(restatedDate);
        (Block block, BlockAnnotations annotations) = OutputCiting(
            "Alex Rivera has been Partner since May 2011.", "May 2011", citedText: "Alex Rivera, Partner since May 2011");

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        BlockCitedSources research = Assert.Single(paragraph.CitedSourcesByBlock, e => e.BlockId == RESEARCH_BLOCK_ID);
        Assert.Equal([PROFILE_URL, TEAM_URL], research.Sources.OfType<UrlSource>().Select(s => s.Url));
        Assert.DoesNotContain(research.Sources, s => s is DocumentSource);
    }

    [Fact]
    public async Task BlocksThatCiteEachOther_ListEachSourceOnceAsync()
    {
        WorkflowAnnotationsContext context = ResearchAndSynthesisChain();
        var backReference = new Fact("synthesis") { IsNumerical = false };
        backReference.Sources.Add(new BlockSource { BlockId = SYNTHESIS_BLOCK_ID, CitedText = "synthesis" });
        context.GetAnnotationsForBlock(RESEARCH_BLOCK_ID)!.AddFact(backReference);
        (Block block, BlockAnnotations annotations) = OutputCiting("Alex Rivera has been Partner since May 2011.", "May 2011");

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        Assert.Equal(3, Assert.Single(paragraph.CitedSourcesByBlock, e => e.BlockId == SYNTHESIS_BLOCK_ID).Sources.Count);
    }

    [Fact]
    public async Task LongExcerpt_ListKeepsItsStartAndTheMatchKeepsTheFactAsync()
    {
        string filler = new('x', BlockAnnotationProcessor.MAX_CITED_TEXT_LENGTH * 3);
        var context = new WorkflowAnnotationsContext();
        var research = new BlockAnnotations();
        var researchFact = new Fact("profile") { IsNumerical = false };
        researchFact.Sources.Add(new UrlSource { Url = PROFILE_URL, CitedText = $"Profile. {filler} start date: 2011-05-01" });
        research.AddFact(researchFact);
        context.SetAnnotationsForBlock(RESEARCH_BLOCK_ID, research);
        (Block block, BlockAnnotations annotations) = OutputCiting("Alex Rivera has been Partner since May 2011.", "May 2011", RESEARCH_BLOCK_ID);

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        BaseSource listed = Assert.Single(Assert.Single(paragraph.CitedSourcesByBlock).Sources);
        Assert.True(listed.CitedText.Length <= BlockAnnotationProcessor.MAX_CITED_TEXT_LENGTH + 1);
        Assert.StartsWith("Profile.", listed.CitedText);
        BlockSource blockSource = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(paragraph.Citations).Sources));
        string match = Assert.Single(blockSource.MatchingSources).CitedText;
        Assert.Contains("2011-05-01", match);
        Assert.True(match.Length <= BlockAnnotationProcessor.MAX_CITED_TEXT_LENGTH + 2);
    }

    [Fact]
    public async Task ManyExcerptsStateTheFact_EveryOneIsMarkedAsync()
    {
        const int MATCHING_LINKS = 8;
        var context = new WorkflowAnnotationsContext();
        var research = new BlockAnnotations();
        var researchFact = new Fact("profiles") { IsNumerical = false };
        for (int index = 0; index < MATCHING_LINKS; index++)
        {
            researchFact.Sources.Add(new UrlSource { Url = $"https://example.org/p/{index}", CitedText = $"joined in May 2011, profile {index}" });
        }

        researchFact.Sources.Add(new UrlSource { Url = TEAM_URL, CitedText = "Our team" });
        research.AddFact(researchFact);
        context.SetAnnotationsForBlock(RESEARCH_BLOCK_ID, research);
        (Block block, BlockAnnotations annotations) = OutputCiting("Alex Rivera has been Partner since May 2011.", "May 2011", RESEARCH_BLOCK_ID);

        ReportParagraph paragraph = await BuildAsync(block, annotations, context);

        BlockSource blockSource = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(paragraph.Citations).Sources));
        Assert.Equal(MATCHING_LINKS, blockSource.MatchingSources.Count);
        Assert.Equal(MATCHING_LINKS + 1, Assert.Single(paragraph.CitedSourcesByBlock).Sources.Count);
    }

    [Fact]
    public async Task NoAnnotationsContext_BlockOnlyCitationKeepsTodaysShapeAsync()
    {
        (Block block, BlockAnnotations annotations) = OutputCiting("Alex Rivera has been Partner since May 2011.", "May 2011");

        ReportParagraph paragraph = await BuildAsync(block, annotations, null);

        Assert.Empty(paragraph.CitedSourcesByBlock);
        BlockSource blockSource = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(paragraph.Citations).Sources));
        Assert.Empty(blockSource.MatchingSources);
    }

    /// <summary>
    /// A research block that cited two web pages (the profile twice) and a synthesis block that
    /// cited one document page and the research block.
    /// </summary>
    private static WorkflowAnnotationsContext ResearchAndSynthesisChain()
    {
        var context = new WorkflowAnnotationsContext();

        var research = new BlockAnnotations();
        var profileFact = new Fact("Alex Rivera, Partner") { IsNumerical = false };
        profileFact.Sources.Add(new UrlSource { Url = PROFILE_URL, CitedText = "start date: 2011-05-01 Title: Partner" });
        profileFact.Sources.Add(new UrlSource { Url = PROFILE_URL, CitedText = "start date: 2005-06-01 Title: Analyst" });
        profileFact.Sources.Add(new UrlSource { Url = TEAM_URL, CitedText = "Our team: Alex Rivera, Morgan Lee" });
        research.AddFact(profileFact);
        context.SetAnnotationsForBlock(RESEARCH_BLOCK_ID, research);

        var synthesis = new BlockAnnotations();
        var synthesisFact = new Fact("Fund assets and principals") { IsNumerical = false };
        synthesisFact.Sources.Add(new DocumentSource
        {
            CitedText = "Net assets of 89,900,000 as at year end",
            DocumentId = "doc-fund",
            PageId = "page-fund-12",
            PageNumber = 12,
            DocumentName = FUND_DOCUMENT,
        });
        synthesisFact.Sources.Add(new BlockSource { BlockId = RESEARCH_BLOCK_ID, BlockTitle = "Research", CitedText = "Alex Rivera, Partner" });
        synthesis.AddFact(synthesisFact);
        context.SetAnnotationsForBlock(SYNTHESIS_BLOCK_ID, synthesis);

        return context;
    }

    private static (Block Block, BlockAnnotations Annotations) OutputCiting(
        string response,
        string factValue,
        string citedBlockId = SYNTHESIS_BLOCK_ID,
        string citedText = "a paraphrase with no figures")
    {
        var block = new Block(BlockTypes.AiQuery, new Section("output", new Workflow())) { Name = "Executive Summary" };
        _ = new BlockResponse(block) { RawResponse = response };

        var annotations = new BlockAnnotations();
        var fact = new Fact(factValue) { IsNumerical = true };
        fact.Sources.Add(new BlockSource { BlockId = citedBlockId, BlockTitle = "Key Principals", CitedText = citedText });
        annotations.AddFact(fact);

        return (block, annotations);
    }

    private async Task<ReportParagraph> BuildAsync(
        Block block,
        BlockAnnotations annotations,
        WorkflowAnnotationsContext? context)
    {
        ReportParagraph? paragraph = await _builder.BuildParagraphAsync(block, new WorkflowOutputContext(), annotations, context);
        Assert.NotNull(paragraph);
        return paragraph;
    }
}
