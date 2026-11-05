using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class AiQueryCitationDefaultTests
{
    private const string CONTRACT = "Contract";
    private const string INVOICE = "Invoice";

    private static Block NewBlock(int filterRuleCount, params string[] documentClasses)
    {
        return NewBlock(BlockTypes.AiQuery, filterRuleCount, documentClasses);
    }

    private static Block NewBlock(BlockTypes type, int filterRuleCount, params string[] documentClasses)
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        var section = new Section("section", workflow);
        var block = new Block(type, section, "tag");
        block.SupportedDocumentClasses.AddRange(documentClasses);
        for (int i = 0; i < filterRuleCount; i++)
        {
            _ = new RagSettings(block) { Type = RagTypes.WholeDocument };
        }

        return block;
    }

    private static bool CitationsTicked(Block block)
    {
        return ((AiQueryProperties)block.Properties).ShouldGetCitationsFromModel;
    }

    [Fact]
    public void ApplyToAddedFilterRule_FirstRuleOnAny_TicksCitations()
    {
        Block block = NewBlock(1, DocumentClass.KEY_ANY);

        AiQueryCitationDefault.ApplyToAddedFilterRule(block);

        Assert.True(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToAddedFilterRule_FirstRuleOnConcreteClass_TicksCitations()
    {
        Block block = NewBlock(1, CONTRACT);

        AiQueryCitationDefault.ApplyToAddedFilterRule(block);

        Assert.True(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToAddedFilterRule_FirstRuleOnNone_LeavesCitationsUnticked()
    {
        Block block = NewBlock(1, DocumentClass.KEY_NONE);

        AiQueryCitationDefault.ApplyToAddedFilterRule(block);

        Assert.False(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToAddedFilterRule_FirstRuleOnUnsetClasses_LeavesCitationsUnticked()
    {
        Block block = NewBlock(1);

        AiQueryCitationDefault.ApplyToAddedFilterRule(block);

        Assert.False(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToAddedFilterRule_SecondRule_KeepsUntickedCitations()
    {
        Block block = NewBlock(2, DocumentClass.KEY_ANY);

        AiQueryCitationDefault.ApplyToAddedFilterRule(block);

        Assert.False(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToClassChange_FromUnsetToAnyWithFilterRule_TicksCitations()
    {
        Block block = NewBlock(1, DocumentClass.KEY_ANY);

        AiQueryCitationDefault.ApplyToClassChange(block, []);

        Assert.True(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToClassChange_FromNoneToConcreteClassesWithFilterRule_TicksCitations()
    {
        Block block = NewBlock(1, CONTRACT, INVOICE);

        AiQueryCitationDefault.ApplyToClassChange(block, [DocumentClass.KEY_NONE]);

        Assert.True(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToClassChange_FromNoneToAnyWithoutFilterRule_LeavesCitationsUnticked()
    {
        Block block = NewBlock(0, DocumentClass.KEY_ANY);

        AiQueryCitationDefault.ApplyToClassChange(block, [DocumentClass.KEY_NONE]);

        Assert.False(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToClassChange_FromAnyToConcreteClass_KeepsUntickedCitations()
    {
        Block block = NewBlock(1, CONTRACT);

        AiQueryCitationDefault.ApplyToClassChange(block, [DocumentClass.KEY_ANY]);

        Assert.False(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToClassChange_FromConcreteClassToAny_KeepsUntickedCitations()
    {
        Block block = NewBlock(1, DocumentClass.KEY_ANY);

        AiQueryCitationDefault.ApplyToClassChange(block, [CONTRACT]);

        Assert.False(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToClassChange_FromAnyToNone_LeavesTickedCitations()
    {
        Block block = NewBlock(1, DocumentClass.KEY_NONE);
        ((AiQueryProperties)block.Properties).ShouldGetCitationsFromModel = true;

        AiQueryCitationDefault.ApplyToClassChange(block, [DocumentClass.KEY_ANY]);

        Assert.True(CitationsTicked(block));
    }

    [Theory]
    [InlineData(DocumentClass.KEY_ANY)]
    [InlineData(CONTRACT)]
    public void ApplyToNewProperties_FilterRuleAndSelectedClasses_TicksCitations(string documentClass)
    {
        Block block = NewBlock(1, documentClass);

        AiQueryCitationDefault.ApplyToNewProperties(block);

        Assert.True(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToNewProperties_AnyWithoutFilterRule_LeavesCitationsUnticked()
    {
        Block block = NewBlock(0, DocumentClass.KEY_ANY);

        AiQueryCitationDefault.ApplyToNewProperties(block);

        Assert.False(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToNewProperties_FilterRuleOnNone_LeavesCitationsUnticked()
    {
        Block block = NewBlock(1, DocumentClass.KEY_NONE);

        AiQueryCitationDefault.ApplyToNewProperties(block);

        Assert.False(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToNewProperties_AgentBlockWithFilterRuleOnAny_LeavesTheBlockAlone()
    {
        Block block = NewBlock(BlockTypes.Agent, 1, DocumentClass.KEY_ANY);
        string blockBefore = block.ToJson(pretty: false);

        AiQueryCitationDefault.ApplyToNewProperties(block);

        Assert.Equal(blockBefore, block.ToJson(pretty: false));
    }

    [Fact]
    public void ApplyToContentEdit_PromptStartsReadingADocumentReader_TicksCitations()
    {
        Block block = BlockBesideInputs();

        AiQueryCitationDefault.ApplyToContentEdit(block, () => Prompt(block).UserPromptTemplate = "Summarise {AnnualReport}.");

        Assert.True(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToContentEdit_AsManyDocumentReadersAsAiInputs_TicksCitations()
    {
        Block block = BlockBesideInputs();

        AiQueryCitationDefault.ApplyToContentEdit(block, () => Prompt(block).UserPromptTemplate = "Check {Draft.answer} against {AnnualReport}.");

        Assert.True(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToContentEdit_MoreAiInputsThanDocumentReaders_LeavesCitationsUnticked()
    {
        Block block = BlockBesideInputs();

        AiQueryCitationDefault.ApplyToContentEdit(block, () => Prompt(block).UserPromptTemplate = "Merge {Draft}, {Plan} and {AnnualReport}.");

        Assert.False(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToContentEdit_ReaderOfOverviews_LeavesCitationsUnticked()
    {
        Block block = BlockBesideInputs();

        AiQueryCitationDefault.ApplyToContentEdit(block, () => Prompt(block).UserPromptTemplate = "Classify {Overviews}.");

        Assert.False(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToContentEdit_TextInputStartsReadingADocumentReader_TicksCitations()
    {
        Block block = BlockBesideInputs();
        var textInput = new TextInput(block, "filings");

        AiQueryCitationDefault.ApplyToContentEdit(block, () => textInput.Value = "{AnnualReport}");

        Assert.True(CitationsTicked(block));
    }

    [Fact]
    public void ApplyToContentEdit_BlockThatAlreadyReadADocumentReader_KeepsUntickedCitations()
    {
        Block block = BlockBesideInputs();
        Prompt(block).UserPromptTemplate = "Summarise {AnnualReport}.";

        AiQueryCitationDefault.ApplyToContentEdit(block, () => Prompt(block).UserPromptTemplate = "Summarise {AnnualReport} briefly.");

        Assert.False(CitationsTicked(block));
    }

    private static Block BlockBesideInputs()
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        var section = new Section("section", workflow);
        _ = new Block(BlockTypes.SimpleRag, section, "AnnualReport") { Properties = new RetrieveDataFromDocumentsProperties() };
        _ = new Block(BlockTypes.SimpleRag, section, "Overviews") { Properties = new RetrieveDataFromDocumentsProperties { ShouldUseOverviews = true } };
        _ = new Block(BlockTypes.AiQuery, section, "Draft") { Properties = new AiQueryProperties() };
        _ = new Block(BlockTypes.Agent, section, "Plan") { Properties = new AgentProperties() };

        return new Block(BlockTypes.AiQuery, section, "Summary") { Properties = new AiQueryProperties() };
    }

    private static AiQueryProperties Prompt(Block block)
    {
        return (AiQueryProperties)block.Properties;
    }
}
