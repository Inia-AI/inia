using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Extensions;
using IniaAi.Configuration.Enums.Anthropic;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// An AI Query block with a stored model shows on its log row the model id its call was sent to
/// when the connector reports it, so a stored id of another region shows the deployment's own
/// profile. A block without a stored model keeps its tier label.
/// </summary>
public sealed class AiQueryLogModelTests(WorkflowsFixture<AiQueryLogModelTests> fixture, ITestOutputHelper output)
    : IClassFixture<WorkflowsFixture<AiQueryLogModelTests>>
{
    private const string ORGANIZATION_ID = "log-model-org";
    private const string BLOCK_NAME = "PL_Revenue";
    private const string ADVANCED_MODEL_LABEL = "Advanced Model";
    private const string ANSWER = "<answer>1,200</answer>";

    private static readonly string _storedModel = AiModel.UsAwsSonnet5_5.GetEnumMember();

    [Fact]
    public async Task TheLogRow_ShowsTheIdTheCallWasSentTo_WhenTheConnectorReportsIt()
    {
        WorkflowExecutionLog log = await RunAndReadLogAsync(EuBedrockConnector(), _storedModel);

        Assert.Equal(AiModel.EuAwsSonnet5_5.GetEnumMember(), log.ModelUsed);
    }

    [Fact]
    public async Task TheLogRow_KeepsTheTierLabel_ForABlockWithNoStoredModel_OnABedrockStack()
    {
        WorkflowExecutionLog log = await RunAndReadLogAsync(EuBedrockConnector(), storedModel: null);

        Assert.Equal(ADVANCED_MODEL_LABEL, log.ModelUsed);
    }

    [Fact]
    public async Task TheLogRow_ShowsTheStoredModel_WhenTheConnectorReportsNoEndpoint()
    {
        WorkflowExecutionLog log = await RunAndReadLogAsync(ScriptedChatConnector.Answering(ANSWER), _storedModel);

        Assert.Equal(_storedModel, log.ModelUsed);
    }

    private static ScriptedChatConnector EuBedrockConnector()
    {
        return new ScriptedChatConnector(
            _ => ANSWER,
            endpointModelOf: model => ModelRouteResolver
                .ResolveInStackRegionOrThrow(model ?? AiModel.EuAwsSonnet5.GetEnumMember(), AiModel.EuAwsSonnet5)
                .GetEnumMember());
    }

    private async Task<WorkflowExecutionLog> RunAndReadLogAsync(ScriptedChatConnector connector, string? storedModel)
    {
        IServiceProvider serviceProvider = fixture.BuildTestServiceProvider(
            output,
            services => services.AddSingleton<IChatConnector>(connector));
        WorkflowHandler handler = serviceProvider.GetRequiredService<WorkflowHandler>();
        using IServiceScope scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork userData = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        var workflow = new Workflow();
        var section = new Section("Extraction", workflow);
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = BLOCK_NAME,
            ReplacementTag = "revenue",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Extract the revenue.",
                MaxTokens = 100,
                ModelIdOverride = storedModel,
            },
        };

        string userId = Guid.NewGuid().ToString();
        await handler.CreateRootOutputAsync(userData, null, workflow, ORGANIZATION_ID, userId);
        await handler.ExecuteWorkflowAsync(
            workflow, [], ORGANIZATION_ID, userId, userData, inputs: [], cancellationToken: TestContext.Current.CancellationToken);

        string executionId = handler.WorkflowExecutionId
            ?? throw new InvalidOperationException("The run has no execution.");

        return Assert.Single(
            await userData.Repository<WorkflowExecutionLog>().FindAsync(row => row.WorkflowExecutionId == executionId),
            row => row.BlockName == BLOCK_NAME);
    }
}
