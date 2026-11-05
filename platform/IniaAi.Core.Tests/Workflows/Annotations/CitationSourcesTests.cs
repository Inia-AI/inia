using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class CitationSourcesTests
{
    private readonly Workflow _workflow = new();
    private readonly Section _filings;
    private readonly Section _report;

    public CitationSourcesTests()
    {
        _filings = new Section("Filings", parent: _workflow);
        _report = new Section("Report", parent: _workflow);
    }

    [Fact]
    public void OutputSourceBlock_IsASource()
    {
        Block paragraph = AiQuery(_report, "Performance", "Write the paragraph.", isOutputSource: true);

        Assert.True(CitationSources.Of(_workflow).Includes(paragraph));
    }

    [Fact]
    public void ReaderOfAReaderOfTheReport_IsASource()
    {
        Block notes = Reader(_filings, "RevenueNotes");
        _ = AiQuery(_filings, "RevenueDraft", "Summarise {RevenueNotes}.");
        _ = AiQuery(_report, "Performance", "Write from {RevenueDraft.answer}.", isOutputSource: true);

        Assert.True(CitationSources.Of(_workflow).Includes(notes));
    }

    [Fact]
    public void ReaderThatOnlyAGateReads_IsNotASource()
    {
        Block loader = Reader(_filings, "load_AR");
        Block gate = new(BlockTypes.SwitchCase, _filings, "verify_AR")
        {
            Name = "Verify Document",
            Properties = new SwitchCaseProperties { InputTemplate = "{load_AR}" },
        };
        _ = AiQuery(_report, "Performance", "Write the paragraph.", isOutputSource: true);

        CitationSources sources = CitationSources.Of(_workflow);

        Assert.False(sources.Includes(loader));
        Assert.False(sources.Includes(gate));
    }

    [Fact]
    public void BlockNothingReads_IsNotASource()
    {
        Block unread = Reader(_filings, "BankLines_Notes_AR");
        _ = AiQuery(_report, "Performance", "Write the paragraph.", isOutputSource: true);

        Assert.False(CitationSources.Of(_workflow).Includes(unread));
    }

    [Fact]
    public void PageReferenceListReadOnlyByAPageInput_IsNotASource()
    {
        Block pageReferences = AiQuery(_filings, "AR_source_page_ref", "List the note pages.");
        Block notes = Reader(_filings, "RevenueNotes_AR");
        _ = new RagSettings(notes)
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "{AR_source_page_ref.notes_revenue_pages}",
        };
        _ = AiQuery(_report, "Performance", "Write from {RevenueNotes_AR}.", isOutputSource: true);

        CitationSources sources = CitationSources.Of(_workflow);

        Assert.True(sources.Includes(notes));
        Assert.False(sources.Includes(pageReferences));
    }

    [Fact]
    public void ReadInContent_InputReadOnlyThroughAPageSetting_IsLeftOut()
    {
        Block pageReferences = AiQuery(_filings, "AR_source_page_ref", "List the note pages.");
        Block notes = Reader(_filings, "RevenueNotes_AR");
        Block paragraph = AiQuery(_report, "Performance", "Write from {RevenueNotes_AR}.", isOutputSource: true);
        _ = new RagSettings(paragraph)
        {
            Type = RagTypes.ExactPages,
            ExactIndexesInput = "{AR_source_page_ref.notes_revenue_pages}",
        };

        Assert.Equal([notes], CitationSources.ReadInContent(paragraph, [pageReferences, notes]));
    }

    [Fact]
    public void BlockReadThroughATextInputOfTheReport_IsASource()
    {
        Block notes = Reader(_filings, "DebtNotes");
        Block paragraph = AiQuery(_report, "Leverage", "Write the paragraph from {debt_context}.", isOutputSource: true);
        _ = new TextInput(paragraph, "debt_context", "Debt notes: {DebtNotes}");

        Assert.True(CitationSources.Of(_workflow).Includes(notes));
    }

    [Fact]
    public void BlockReadThroughTheOutputTitle_IsASource()
    {
        Block period = AiQuery(_filings, "PeriodEndDates", "Find the period end.");
        Block paragraph = AiQuery(_report, "Leverage", "Write the paragraph.", isOutputSource: true);
        paragraph.OutputTitleTemplate = "Leverage as of {PeriodEndDates.fy}";

        Assert.True(CitationSources.Of(_workflow).Includes(period));
    }

    [Fact]
    public void CopyWithTheTagOfAReachedBlock_IsASource()
    {
        _ = Reader(_filings, "FundLetter");
        _ = AiQuery(_report, "Summary", "Write from {FundLetter}.", isOutputSource: true);
        CitationSources sources = CitationSources.Of(_workflow);

        Block loopCopy = Reader(_filings, "FundLetter");

        Assert.True(sources.Includes(loopCopy));
    }

    [Fact]
    public void TagSharedByTwoBlocks_FollowsTheInputsOfBoth()
    {
        Block revenue = Reader(_filings, "RevenueNotes");
        Block debt = Reader(_filings, "DebtNotes");
        _ = AiQuery(_filings, "Analysis", "Analyse {RevenueNotes}.");
        _ = new Block(BlockTypes.Merge, _filings, "Analysis")
        {
            Name = "Analysis passthrough",
            Properties = new MergeProperties { TextTemplate = "{DebtNotes}" },
        };
        _ = AiQuery(_report, "Summary", "Write from {Analysis}.", isOutputSource: true);

        CitationSources sources = CitationSources.Of(_workflow);

        Assert.True(sources.Includes(revenue));
        Assert.True(sources.Includes(debt));
    }

    [Fact]
    public void BlockOfALoopSection_IsASource_WithTheInputsItReads()
    {
        var loop = new Section("LOOP - Funds", parent: _workflow);
        Block fundLetters = Reader(_filings, "FundLetters");
        _ = new Block(BlockTypes.LoopStart, loop, "loop_start") { Name = "Start" };
        Block copy = AiQuery(loop, "Funds_output_table_0", "Format the NAV from {FundLetters}.");
        _ = new Block(BlockTypes.LoopEnd, loop, "loop_end") { Name = "End" };
        _ = AiQuery(_report, "Summary", "Write the rows from {output_table}.", isOutputSource: true);

        CitationSources sources = CitationSources.Of(_workflow);

        Assert.True(sources.Includes(copy));
        Assert.True(sources.Includes(fundLetters));
    }

    [Fact]
    public void UntaggedBlockTheMapNeverSaw_IsASource()
    {
        _ = AiQuery(_report, "Summary", "Write the paragraph.", isOutputSource: true);
        CitationSources sources = CitationSources.Of(_workflow);

        Block addedAtRunTime = new(BlockTypes.AiQuery, _filings) { Name = "Runtime copy" };

        Assert.True(sources.Includes(addedAtRunTime));
    }

    [Fact]
    public void ReaderOfADataOutput_IsASource()
    {
        Block notes = Reader(_filings, "FacilityNotes");
        Block data = AiQuery(_filings, "facilities_json", "Extract the facilities from {FacilityNotes}.");
        data.DataOutputName = "facilities";
        Block unread = Reader(_filings, "BankLines_Notes_AR");
        _ = AiQuery(_report, "Performance", "Write the paragraph.", isOutputSource: true);

        CitationSources sources = CitationSources.Of(_workflow);

        Assert.True(sources.Includes(data));
        Assert.True(sources.Includes(notes));
        Assert.False(sources.Includes(unread));
    }

    [Fact]
    public void WorkflowWithOnlyADataOutput_FollowsItsInputsAlone()
    {
        Block notes = Reader(_filings, "FacilityNotes");
        Block data = AiQuery(_filings, "facilities_json", "Extract the facilities from {FacilityNotes}.");
        data.DataOutputName = "facilities";
        Block unread = Reader(_filings, "BankLines_Notes_AR");

        CitationSources sources = CitationSources.Of(_workflow);

        Assert.True(sources.Includes(notes));
        Assert.False(sources.Includes(unread));
    }

    [Fact]
    public void WorkflowWithoutAnOutputSource_KeepsEveryBlockASource()
    {
        Block notes = Reader(_filings, "RevenueNotes");
        Block draft = AiQuery(_filings, "RevenueDraft", "Summarise {RevenueNotes}.");

        CitationSources sources = CitationSources.Of(_workflow);

        Assert.True(sources.Includes(notes));
        Assert.True(sources.Includes(draft));
    }

    private static Block Reader(Section section, string tag)
    {
        return new Block(BlockTypes.SimpleRag, section, tag)
        {
            Name = tag,
            Properties = new RetrieveDataFromDocumentsProperties(),
        };
    }

    private static Block AiQuery(Section section, string tag, string prompt, bool isOutputSource = false)
    {
        return new Block(BlockTypes.AiQuery, section, tag)
        {
            Name = tag,
            IsOutputSource = isOutputSource,
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }
}
