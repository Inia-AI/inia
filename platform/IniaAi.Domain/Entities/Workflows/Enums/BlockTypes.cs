using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>What a block does when the workflow runs; each type has its own settings.</summary>
public enum BlockTypes
{
    /// <summary>Sends a prompt, with the documents its retrieval settings pick, to a model and keeps the answer. The workhorse block: extraction, analysis and drafting.</summary>
    [EnumMember(Value = nameof(AiQuery))]
    AiQuery = 0,

    /// <summary>Joins text from earlier blocks and variables into one text, with no model call. Its output is the resolved template.</summary>
    [EnumMember(Value = nameof(Merge))]
    Merge = 3,

    /// <summary>Works out a number from a formula over earlier blocks' values, with no model call.</summary>
    [EnumMember(Value = nameof(CalculateNumber))]
    CalculateNumber = 6,

    /// <summary>Retrieves the document pages or passages its retrieval settings select and outputs them as text for a later block to read.</summary>
    [EnumMember(Value = nameof(SimpleRag))]
    SimpleRag = 9,

    /// <summary>Starts a loop: the blocks up to the matching LoopEnd run once for each item of a list.</summary>
    [EnumMember(Value = nameof(LoopStart))]
    LoopStart = 10,

    /// <summary>Marks where the loop its LoopStart opened ends.</summary>
    [EnumMember(Value = nameof(LoopEnd))]
    LoopEnd = 11,

    /// <summary>Has a model group the uploaded documents' content into topics, each with the pages it covers.</summary>
    [EnumMember(Value = nameof(BuildTopicsForAllDocuments))]
    BuildTopicsForAllDocuments = 15,

    /// <summary>Searches the web (Exa) and returns the matching pages' content, optionally crawling subpages. Sends its query to an outside service.</summary>
    [EnumMember(Value = nameof(IntelligentSearch))]
    IntelligentSearch = 19,

    /// <summary>Fetches today's exchange rates from one currency to one or more others from an outside rate service.</summary>
    [EnumMember(Value = nameof(ExchangeRate))]
    ExchangeRate = 20,

    /// <summary>Calls an outside web address with an HTTP request and keeps the response. Sends data outside the platform; internal addresses are refused.</summary>
    [EnumMember(Value = nameof(WebApiCall))]
    WebApiCall = 21,

    /// <summary>Draws a chart from data that earlier blocks produce.</summary>
    [EnumMember(Value = nameof(DynamicChart))]
    DynamicChart = 23,

    /// <summary>Writes JSON into an Excel or Word template stored for the organization (Inject), or reads an uploaded Excel back into JSON (Extract). Paid build only.</summary>
    [EnumMember(Value = nameof(Json2Xlsx))]
    Json2Xlsx = 24,

    /// <summary>Maps an input value to one of several fixed outputs, like a lookup table. It does not branch the workflow; later blocks read the matched value.</summary>
    [EnumMember(Value = nameof(SwitchCase))]
    SwitchCase = 25,

    /// <summary>Combines JSON from several blocks into one JSON value, optionally pulling snippets out by tag or pattern first. Input that is not JSON is skipped with a warning.</summary>
    [EnumMember(Value = nameof(MergeJson))]
    MergeJson = 26,

    /// <summary>Applies one text operation (replace, substring, trim, split, join, change case, escape, distinct) to an input, with no model call.</summary>
    [EnumMember(Value = nameof(StringManipulation))]
    StringManipulation = 27,

    /// <summary>Runs an agent with skills on the agent runtime: it can read files and documents, run tools and work in several steps, then return its answer. Only where the deployment enables the agent runtime.</summary>
    [EnumMember(Value = nameof(Agent))]
    Agent = 28,

    /// <summary>Lines up stored peer companies' figures with the target company's period, currency and units, and outputs one JSON for later blocks, with no model call. Paid build only.</summary>
    [EnumMember(Value = nameof(PeerFinancials))]
    PeerFinancials = 29,

    /// <summary>Groups Agent blocks under a verifier that checks their output against stated criteria and sends failing ones back to redo their work. Paid build only.</summary>
    [EnumMember(Value = nameof(Verification))]
    Verification = 30,
}
