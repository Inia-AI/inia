using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.Core.Services;
using IniaAi.Core.Tests.Workflows;
using IniaAi.Domain.Tools.Reconciliation;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IniaAi.Core.Tests.Services;

/// <summary>
/// The reconciliation prompts are edited in Tools Settings, so a match asks the model with the
/// newest stored row, not the code defaults.
/// </summary>
public class ReconciliationStoredPromptsTests : IClassFixture<WorkflowsFixture<ReconciliationStoredPromptsTests>>
{
    private const string EDITED_SYSTEM_PROMPT = "You reconcile names for an edited deployment.";
    private const string EDITED_FIND_MATCH_PROMPT = "Edited: which of {1} is {0}? <answer>name or NA</answer>";
    private const string EDITED_COMPARE_LISTS_PROMPT = "Edited: do {0} and {1} hold the same items? <answer>Yes or No</answer>";
    private const string IDENTIFIED_NAME = "Alpha Growth Fnd";

    private static readonly List<string> _candidates = ["Alpha Growth Fund", "Beta Income Fund"];

    private readonly WorkflowsFixture<ReconciliationStoredPromptsTests> _fixture;
    private readonly ITestOutputHelper _output;

    public ReconciliationStoredPromptsTests(WorkflowsFixture<ReconciliationStoredPromptsTests> fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task AMatch_AsksWithTheNewestStoredPrompts()
    {
        (ReconciliationService reconciliation, ScriptedChatConnector chat) = await BuildWithStoredRowsAsync();

        _ = await reconciliation.FindMatchFromListAsync(IDENTIFIED_NAME, _candidates, ModelFamily.Anthropic);

        ScriptedModelCall call = Assert.Single(chat.Calls);
        Assert.Equal(string.Format(EDITED_FIND_MATCH_PROMPT, IDENTIFIED_NAME, string.Join("\n", _candidates)), call.Text);
        Assert.Equal(EDITED_SYSTEM_PROMPT, call.SystemPrompt);
    }

    [Fact]
    public async Task AListComparison_AsksWithTheNewestStoredPrompts()
    {
        (ReconciliationService reconciliation, ScriptedChatConnector chat) = await BuildWithStoredRowsAsync();

        _ = await reconciliation.CompareListsAsync(_candidates, [IDENTIFIED_NAME], ModelFamily.Anthropic);

        ScriptedModelCall call = Assert.Single(chat.Calls);
        Assert.Equal(string.Format(EDITED_COMPARE_LISTS_PROMPT, string.Join("\n", _candidates), IDENTIFIED_NAME), call.Text);
        Assert.Equal(EDITED_SYSTEM_PROMPT, call.SystemPrompt);
    }

    [Fact]
    public async Task ACallerTemplate_StillWinsOverTheStoredPrompt()
    {
        const string callerTemplate = "Caller: {0} in {1}";
        (ReconciliationService reconciliation, ScriptedChatConnector chat) = await BuildWithStoredRowsAsync();

        _ = await reconciliation.FindMatchFromListAsync(IDENTIFIED_NAME, _candidates, ModelFamily.Anthropic, callerTemplate);

        Assert.Equal(string.Format(callerTemplate, IDENTIFIED_NAME, string.Join("\n", _candidates)), Assert.Single(chat.Calls).Text);
    }

    private async Task<(ReconciliationService Reconciliation, ScriptedChatConnector Chat)> BuildWithStoredRowsAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ScriptedChatConnector chat = ScriptedChatConnector.Answering("<answer>NA</answer>");
        IServiceProvider services = _fixture.BuildTestServiceProvider(
            _output,
            overrides => overrides.Replace(ServiceDescriptor.Singleton<IChatConnector>(chat)),
            withoutClosedModules: true);

        using (IServiceScope scope = services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            context.ReconciliationPrompts.RemoveRange(context.ReconciliationPrompts);
            context.ReconciliationPrompts.Add(new ReconciliationPrompts
            {
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SystemPrompt = "An older row nobody reads any more.",
                FindMatchPrompt = "Older: {0} {1}",
                CompareListsPrompt = "Older: {0} {1}",
            });
            context.ReconciliationPrompts.Add(new ReconciliationPrompts
            {
                CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                SystemPrompt = EDITED_SYSTEM_PROMPT,
                FindMatchPrompt = EDITED_FIND_MATCH_PROMPT,
                CompareListsPrompt = EDITED_COMPARE_LISTS_PROMPT,
            });
            _ = await context.SaveChangesAsync(ct);
        }

        return (services.GetRequiredService<ReconciliationService>(), chat);
    }
}
