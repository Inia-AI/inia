using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// A workflow whose every field that carries an instruction holds a marker of its own: the block's
/// prompts, its name, the section's name, a variable's default and the page overview prompts. A test
/// that searches what a reader gets for <see cref="Instructions"/> finds any instruction the platform
/// hands out, and the marker it finds names the field it came from. The section's report heading is
/// <see cref="REPORT_HEADING"/>, and the block writes its answer into the report.
/// </summary>
public static class MarkerWorkflow
{
    public const string USER_PROMPT = "QUOKKA-USERPROMPT-51A7";
    public const string SYSTEM_PROMPT = "QUOKKA-SYSPROMPT-83D2";
    public const string BLOCK_NAME = "QUOKKA-BLOCKNAME-0E4C";
    public const string SECTION = "QUOKKA-SECTION-66B9";
    public const string VARIABLE_DEFAULT = "QUOKKA-VARDEFAULT-2F70";
    public const string OVERVIEW = "QUOKKA-OVERVIEW-9A13";

    public const string REPORT_HEADING = "Net assets";
    public const string READER_TAG = "reader";
    public const string PERIOD_TAG = "period";
    public const string PERIOD = "the year to December";

    public static IReadOnlyList<string> Instructions { get; } = [USER_PROMPT, SYSTEM_PROMPT, BLOCK_NAME, SECTION, VARIABLE_DEFAULT, OVERVIEW];

    public static Workflow Build()
    {
        var workflow = new Workflow("Credit report")
        {
            Configuration = new WorkflowConfiguration
            {
                PageOverviews = new PageOverviewPrompts
                {
                    Pages = new PageOverviewPrompt
                    {
                        SystemPrompt = PageOverviewPromptDefaults.PAGES_SYSTEM_PROMPT,
                        UserPrompt = $"{OVERVIEW}\n{PageOverviewPromptDefaults.PAGES_USER_PROMPT}",
                    },
                    SinglePage = new PageOverviewPrompt
                    {
                        SystemPrompt = PageOverviewPromptDefaults.SINGLE_PAGE_SYSTEM_PROMPT,
                        UserPrompt = $"{OVERVIEW}\n{PageOverviewPromptDefaults.SINGLE_PAGE_USER_PROMPT}",
                    },
                },
            },
        };
        _ = new WorkflowVariable(workflow, PERIOD_TAG) { DefaultValue = $"{VARIABLE_DEFAULT} {PERIOD}" };
        var section = new Section($"{SECTION} Analysis", workflow) { Title = REPORT_HEADING };
        var reader = new Block(BlockTypes.AiQuery, section)
        {
            Name = BLOCK_NAME,
            ReplacementTag = READER_TAG,
            IsOutputSource = true,
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = $"{USER_PROMPT}: summarise the net assets the documents report for {{{PERIOD_TAG}}}.",
                SystemPromptTemplate = $"{SYSTEM_PROMPT}: you are a credit analyst.",
                ShouldUsePageImages = false,
            },
        };
        _ = new RagSettings(reader) { Type = RagTypes.WholeDocument };
        return workflow;
    }
}
