using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

public class JsonFieldPlaceholderTests : IClassFixture<WorkflowsFixture<WorkflowHandler>>
{
    private const string SOURCE_TAG = "baseline";
    private const string CLEAN_JSON = "{\"company\": \"Acme\", \"debt\": {\"total\": 42}}";
    private const string FENCED_JSON = "```json\n" + CLEAN_JSON + "\n```";
    private const string TAGGED_JSON = "Reasoning first.\n<answer>" + CLEAN_JSON + "</answer>";
    private const string NO_DATA = "No data available";

    private readonly WorkflowHandler _workflowHandler;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public JsonFieldPlaceholderTests(
        WorkflowsFixture<WorkflowHandler> fixture,
        ITestOutputHelper testOutputHelper)
    {
        IServiceProvider serviceProvider = fixture.BuildTestServiceProvider(testOutputHelper);
        _workflowHandler = serviceProvider.GetRequiredService<WorkflowHandler>();
        IServiceScope scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    [Fact]
    public async Task FieldPlaceholder_JsonFormatWithFencedAnswer_ReadsTheValidatedJson()
    {
        string resolved = await ResolveAsync(
            OutputFormatType.Json,
            FENCED_JSON,
            "{baseline.company}|{baseline.debt.total}");

        Assert.Equal("Acme|42", resolved);
    }

    [Fact]
    public async Task FieldPlaceholder_JsonInTagFormat_ReadsTheValidatedJson()
    {
        string resolved = await ResolveAsync(
            OutputFormatType.JsonInTag,
            TAGGED_JSON,
            "{baseline.company}");

        Assert.Equal("Acme", resolved);
    }

    [Fact]
    public async Task TagPlaceholder_JsonInTagFormat_StillReadsTheTagContent()
    {
        string resolved = await ResolveAsync(
            OutputFormatType.JsonInTag,
            TAGGED_JSON,
            "{baseline.answer}");

        Assert.Equal(CLEAN_JSON, resolved);
    }

    [Fact]
    public async Task JsonAndWholePlaceholders_JsonFormatWithFencedAnswer_BehaveAsBefore()
    {
        string resolved = await ResolveAsync(
            OutputFormatType.Json,
            FENCED_JSON,
            "{baseline.Json.company}|{baseline}");

        Assert.Equal("Acme|" + FENCED_JSON, resolved);
    }

    [Fact]
    public async Task FieldPlaceholder_NoOutputFormatWithFencedAnswer_StillFindsNothing()
    {
        string resolved = await ResolveAsync(
            OutputFormatType.None,
            FENCED_JSON,
            "{baseline.company}");

        Assert.Equal(NO_DATA, resolved);
    }

    private async Task<string> ResolveAsync(
        OutputFormatType outputFormat,
        string rawAnswer,
        string template)
    {
        var workflow = new Workflow("Json field placeholders");
        var section = new Section("Json field placeholders", workflow);

        var source = new Block(BlockTypes.AiQuery, section, SOURCE_TAG)
        {
            Name = "Baseline",
            Properties = new AiQueryProperties
            {
                OutputFormatType = outputFormat,
                OutputFormatTagName = outputFormat == OutputFormatType.JsonInTag ? "answer" : null,
            },
        };

        _ = new BlockResponse(source)
        {
            RawResponse = rawAnswer,
            JsonResponse = CLEAN_JSON,
        };

        var reader = new Block(BlockTypes.Merge, section, "reader")
        {
            Name = "Reader",
            Properties = new MergeProperties
            {
                TextTemplate = template,
            },
        };

        _ = await _workflowHandler.ExecuteBlockAsync(
            section,
            1,
            [],
            _userDataUnitOfWork,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(reader.BlockResponse);
        return reader.BlockResponse.RawResponse!;
    }
}
