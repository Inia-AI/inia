using System.Text.Json;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Graphs;

/// <summary>
/// Explore over a finished run of <see cref="MarkerWorkflow"/>: what explore never sends to its model it cannot repeat,
/// so no block prompt, page overview prompt or variable default is in a call, and the reader gets the answer alone.
/// </summary>
public sealed class GraphQuerySendsNoInstructionsTests : IClassFixture<GraphQueryServiceFixture>
{
    private const string REASON = "QUOKKA-REASON-7D21";
    private const string ANSWER = "From the annual report.";
    private const string TARGET_TAG = MarkerGraphRun.TARGET_TAG;

    private readonly GraphQueryServiceFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly ScriptedChatConnector _model = ScriptedChatConnector.Answering(
        $$"""<answer>{"action":"answer","reason":"{{REASON}}","answer":"{{ANSWER}}"}</answer>""");

    public GraphQuerySendsNoInstructionsTests(GraphQueryServiceFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task AQuestionAboutAFinishedRun_SendsNoPromptToTheModel_AndAnswersWithTheAnswerAlone()
    {
        IServiceProvider services = _fixture.BuildTestServiceProvider(_output, _model);
        using IServiceScope scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        string runId = await StoreFinishedRunAsync(scope.ServiceProvider.GetRequiredService<UserDataDbContext>());

        ProvenanceResult result = await scope.ServiceProvider.GetRequiredService<IGraphQueryService>()
            .QueryAsync(runId, TARGET_TAG, "Where does this figure come from?", cancellationToken: TestContext.Current.CancellationToken);

        string sent = string.Join("\n", _model.Calls.Select(call => $"{call.SystemPrompt}\n{call.Text}"));
        _output.WriteLine($"{_model.Calls.Count} call(s); block name sent: {sent.Contains(MarkerWorkflow.BLOCK_NAME, StringComparison.Ordinal)}; section name sent: {sent.Contains(MarkerWorkflow.SECTION, StringComparison.Ordinal)}");
        _output.WriteLine(sent);
        string[] instructions = [MarkerWorkflow.USER_PROMPT, MarkerWorkflow.SYSTEM_PROMPT, MarkerWorkflow.VARIABLE_DEFAULT, MarkerWorkflow.OVERVIEW];
        Assert.Contains("Net assets are 1,234,567.", sent, StringComparison.Ordinal);
        string[] carried = [.. instructions.Where(marker => sent.Contains(marker, StringComparison.Ordinal))];
        Assert.True(carried.Length == 0, $"The model was sent {string.Join(", ", carried)}");
        Assert.Equal(ANSWER, result.Answer);
        Assert.DoesNotContain(REASON, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    private Task<string> StoreFinishedRunAsync(UserDataDbContext db)
    {
        return MarkerGraphRun.StoreAsync(db, _fixture.GetOrganization().Id);
    }
}
