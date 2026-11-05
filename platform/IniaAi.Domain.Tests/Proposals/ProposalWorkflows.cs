using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Proposals;

/// <summary>
/// A real two-section workflow with a prompt block, its reader, a web call, a search, a
/// Library-linked block and a merge, plus the JSON helpers that edit its export the way the agent
/// edits workflow.json.
/// </summary>
internal static class ProposalWorkflows
{
    public const string PRODUCER = "revenue";
    public const string READER = "summary";
    public const string WEB_CALL = "notify";
    public const string SEARCH = "news";
    public const string LINKED = "library_rule";
    public const string MERGE = "report";
    public const string VARIABLE = "Currency";
    public const string DOCUMENT_CLASS = "AnnualReport";
    public const string PRODUCER_PROMPT = "Break revenue down by segment in {Currency}.";
    public const string READER_PROMPT = "Summarize: {revenue.answer}";

    public static Workflow Build()
    {
        var workflow = new Workflow("Credit report") { Title = "Credit report", WorkflowTypeId = "WT-Proposal" };
        _ = new WorkflowVariable(workflow, VARIABLE) { DisplayName = "Currency" };
        _ = new DocumentClass(workflow, DOCUMENT_CLASS) { DisplayName = "Annual report" };

        var analysis = new Section("Analysis", workflow) { Title = "Analysis" };
        var producer = new Block(BlockTypes.AiQuery, analysis, PRODUCER)
        {
            Name = "Revenue breakdown",
            Properties = new AiQueryProperties { UserPromptTemplate = PRODUCER_PROMPT, SystemPromptTemplate = "You are an analyst." },
        };
        _ = new RagSettings(producer);
        _ = new TextInput(producer, "Notes", "Use the latest year.");

        _ = new Block(BlockTypes.AiQuery, analysis, READER)
        {
            Name = "Summary",
            Properties = new AiQueryProperties { UserPromptTemplate = READER_PROMPT },
        };
        _ = new Block(BlockTypes.WebApiCall, analysis, WEB_CALL)
        {
            Name = "Notify",
            Properties = new WebApiCallProperties { UrlTemplate = "https://hooks.example.com/report", HttpMethod = "POST" },
        };
        _ = new Block(BlockTypes.IntelligentSearch, analysis, SEARCH)
        {
            Name = "News",
            Properties = new IntelligentSearchProperties { SearchTermTemplate = "{Counterparty} results" },
        };
        _ = new Block(BlockTypes.AiQuery, analysis, LINKED)
        {
            Name = "Library rule",
            Properties = new AiQueryProperties { UserPromptTemplate = "Apply the house rule." },
            SharedComponentId = "component-1",
            SharedComponentLinkState = SharedComponentLinkState.Linked,
            SharedComponentVersionHash = new string('a', 64),
        };

        var report = new Section("Report", workflow) { Title = "Report" };
        _ = new Block(BlockTypes.Merge, report, MERGE)
        {
            Name = "Report",
            Properties = new MergeProperties { TextTemplate = "{summary}" },
        };

        return workflow;
    }

    public static string Json(Workflow workflow)
    {
        return workflow.ToExportJson(includeRunTraces: true);
    }

    public static JObject Edit(string json)
    {
        return JObject.Parse(json);
    }

    public static JObject Block(JObject root, string tag)
    {
        return root["Sections"]!.SelectMany(section => section["Blocks"]!).OfType<JObject>()
            .Single(block => (string?)block["ReplacementTag"] == tag);
    }

    public static JArray Blocks(JObject root, int section)
    {
        return (JArray)root["Sections"]![section]!["Blocks"]!;
    }

    public static string StableId(Workflow workflow, string tag)
    {
        return workflow.Sections.SelectMany(section => section.Blocks).Single(block => block.ReplacementTag == tag).StableBlockId!;
    }

    public static Block LiveBlock(Workflow workflow, string tag)
    {
        return workflow.Sections.SelectMany(section => section.Blocks).Single(block => block.ReplacementTag == tag);
    }
}
