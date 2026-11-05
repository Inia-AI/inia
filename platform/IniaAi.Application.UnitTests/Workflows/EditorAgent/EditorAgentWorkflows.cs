using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

/// <summary>
/// A small real workflow: a variable, a document class, and a reader block that reads a
/// producer block's answer. The reader is added first so its array position differs from its
/// execution order.
/// </summary>
internal static class EditorAgentWorkflows
{
    public const string TYPE = "WT-EditorAgent";
    public const string PRODUCER_TAG = "revenue";
    public const string READER_TAG = "summary";
    public const string PRODUCER_NAME = "Revenue breakdown";
    public const string READER_NAME = "Summary";
    public const string VARIABLE_TAG = "Currency";
    public const string DOCUMENT_CLASS = "AnnualReport";

    public static Workflow Build()
    {
        var workflow = new Workflow("Credit report") { Title = "Credit report", WorkflowTypeId = TYPE };
        _ = new WorkflowVariable(workflow, VARIABLE_TAG);
        _ = new DocumentClass(workflow, DOCUMENT_CLASS);

        var section = new Section("Analysis", workflow) { Title = "Analysis" };
        var reader = new Block(BlockTypes.AiQuery, section, READER_TAG)
        {
            Name = READER_NAME,
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarize: {revenue.answer}" },
        };
        var producer = new Block(BlockTypes.AiQuery, section, PRODUCER_TAG)
        {
            Name = PRODUCER_NAME,
            Properties = new AiQueryProperties { UserPromptTemplate = "Break revenue down by segment in {Currency}." },
        };

        producer.Order = 0;
        reader.Order = 1;

        return workflow;
    }

    public static Block Producer(Workflow workflow)
    {
        return workflow.Sections.Single().Blocks.Single(block => block.ReplacementTag == PRODUCER_TAG);
    }

    public static Block Reader(Workflow workflow)
    {
        return workflow.Sections.Single().Blocks.Single(block => block.ReplacementTag == READER_TAG);
    }
}
