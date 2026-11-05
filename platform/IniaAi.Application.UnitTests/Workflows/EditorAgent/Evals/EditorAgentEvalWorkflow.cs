using System.Security.Cryptography;
using System.Text;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent.Evals;

/// <summary>
/// The workflow every eval case opens: the platform's functionality-test workflow (23 sections,
/// web calls, a loop, a switch, a parameterized section, search blocks), with stable ids derived
/// from each tag so a grader can name a block, and one block given distinctive model settings.
/// </summary>
internal static class EditorAgentEvalWorkflow
{
    public const string TYPE = "WT-EditorAgentEval";
    public const string FIXTURE_PATH = "TestResources/WorkflowHandlerFunctionalityTest.workflow.json";

    public const string TUNED_MODEL_ID = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
    public const ModelEffort TUNED_EFFORT = ModelEffort.Low;
    public const int TUNED_THINKING_BUDGET = 4096;

    private const string SECTION_KEY_PREFIX = "section:";

    public static Workflow Build()
    {
        Workflow workflow = Workflow.FromExportJson(File.ReadAllText(FIXTURE_PATH))!;
        workflow.WorkflowTypeId = TYPE;
        foreach (Section section in workflow.Sections)
        {
            section.StableSectionId ??= SectionStableId(section.Title);
            foreach (Block block in section.Blocks)
            {
                block.StableBlockId ??= StableId(block.ReplacementTag);
            }
        }

        var tuned = (AiQueryProperties)Block(workflow, Tags.TOPICS_READER).Properties;
        tuned.ModelIdOverride = TUNED_MODEL_ID;
        tuned.Effort = TUNED_EFFORT;
        tuned.ShouldUseThinkingMode = true;
        tuned.ThinkingBudget = TUNED_THINKING_BUDGET;

        return workflow;
    }

    /// <summary>A past run of the workflow whose block <paramref name="blockTag"/> answered <paramref name="answer"/>.</summary>
    public static Workflow Executed(string executionId, string organizationId, string blockTag, string answer)
    {
        Workflow workflow = Build();
        workflow.Id = executionId;
        workflow.OrganizationId = organizationId;
        Block block = Block(workflow, blockTag);
        _ = new BlockResponse(block) { RawResponse = answer };
        block.ExecutionState = new BlockExecutionState(block) { Status = ExecutionStatus.Completed };

        return workflow;
    }

    public static Block Block(Workflow workflow, string tag)
    {
        return workflow.Sections.SelectMany(static section => section.Blocks).Single(block => block.ReplacementTag == tag);
    }

    /// <summary>The stable id <see cref="Build"/> gives the block with this tag.</summary>
    public static string StableId(string tag)
    {
        return Hash(tag);
    }

    /// <summary>The stable id <see cref="Build"/> gives the section with this title.</summary>
    public static string SectionStableId(string title)
    {
        return Hash(SECTION_KEY_PREFIX + title);
    }

    private static string Hash(string key)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
    }

    /// <summary>The fixture's tags and section titles the cases name.</summary>
    public static class Tags
    {
        public const string FACT_TRACKING = "section4SetupBlock1";
        public const string FACT_READER = "section4SetupBlock2";
        public const string FACT_READER_READER = "section4SetupBlock3";
        public const string NEXT_SECTION_READER = "section4Block1";
        public const string SEARCH_SUMMARY = "section4SetupBlock6";
        public const string TOPICS_READER = "useBuiltTopics";
        public const string SOURCES_CHECK = "exaSearchBlockSourcesCheck";
        public const string PERPLEXITY = "perplexitySearch";
        public const string PARALLEL = "parallelSearch";
        public const string DEEP_SEARCH_RESULT = "Market_DeepSearch_Result";
        public const string FAILING_MODEL = "failingAiBlock";
        public const string FAILING_MODEL_READER = "referencingErrorBlock";
        public const string IN_LOOP_FIRST = "inLoop1";
        public const string IN_LOOP_SECOND = "inLoop2";
        public const string SWITCH = "switch";
        public const string NUMBER_SOURCE = "numberSource";
        public const string DELAY = "delay";
        public const string OUTSIDE_FIRST = "outside1";
        public const string OUTSIDE_SECOND = "outside2";
        public const string OUTSIDE_LOOP_SECTION = "Outside Loop";
    }
}
