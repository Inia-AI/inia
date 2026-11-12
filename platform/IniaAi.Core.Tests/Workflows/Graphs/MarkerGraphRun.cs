using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Graphs;

/// <summary>
/// A finished run of <see cref="MarkerWorkflow"/> as the engine leaves it, stored for explore: its copy of the workflow,
/// each block's answer, and the reader's prompts as sent. Explore is asked about the second block, which reads the reader.
/// </summary>
internal static class MarkerGraphRun
{
    public const string TARGET_TAG = "opinion";
    public const string READER_ANSWER = "<answer>Net assets are 1,234,567.</answer>";
    public const string OPINION_ANSWER = "Net assets are 1,234,567.";

    public static async Task<string> StoreAsync(UserDataDbContext db, string organizationId, string opinionAnswer = OPINION_ANSWER)
    {
        Workflow workflow = MarkerWorkflow.Build();
        Section section = workflow.Sections.Single();
        Block reader = section.Blocks.Single();
        var opinion = new Block(BlockTypes.Merge, section)
        {
            Name = $"{MarkerWorkflow.BLOCK_NAME} opinion",
            ReplacementTag = TARGET_TAG,
            Order = 2,
            Properties = new MergeProperties { TextTemplate = $"{{{MarkerWorkflow.READER_TAG}}}" },
        };
        reader.SetExecutionState(false, []);
        AiQueryPropertiesExecutionState sentPrompts = ((AiQueryProperties)reader.Properties!).ExecutionState!;
        sentPrompts.UserPrompt = $"{MarkerWorkflow.USER_PROMPT}: summarise the net assets the documents report for {MarkerWorkflow.VARIABLE_DEFAULT} {MarkerWorkflow.PERIOD}.";
        sentPrompts.SystemPrompt = $"{MarkerWorkflow.SYSTEM_PROMPT}: you are a credit analyst.";
        _ = new BlockResponse(reader) { RawResponse = READER_ANSWER };
        _ = new BlockResponse(opinion) { RawResponse = opinionAnswer };
        _ = db.Workflows.Add(workflow);

        var execution = new WorkflowExecution
        {
            OrganizationId = organizationId,
            WorkflowId = workflow.Id,
            JobId = Guid.NewGuid().ToString(),
            Title = "Credit report",
        };
        _ = db.WorkflowExecutions.Add(execution);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return execution.Id;
    }
}
