using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.StoredKeys;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.StoredKeys;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class WebApiCallStrategyStoredKeyTests
    : IClassFixture<WorkflowsFixture<WebApiCallStrategyStoredKeyTests>>, IClassFixture<LocalTestHttpServer>
{
    private const string ORGANIZATION_ID = "org-stored-keys";
    private const string OTHER_ORGANIZATION_ID = "org-without-keys";
    private const string UNREADABLE_KEY_ORGANIZATION_ID = "org-unreadable-key";
    private const string KEY_NAME = "ParallelAi";
    private const string KEY_VALUE = "fake test value";
    private const string FORMER_TENANT_KEY_VALUE = "fake former tenant value";
    private const string DEPLOYMENT_KEY_NAME = "PerplexityApiKey";
    private const string DEPLOYMENT_KEY_VALUE = "fake deployment value";
    private const string ORGANIZATION_KEY_VALUE = "fake organization value";

    private static readonly string _keyToken = StoredKeyPlaceholders.Token(KEY_NAME);

    private readonly IServiceProvider _testServiceProvider;
    private readonly LocalTestHttpServer _server;

    public WebApiCallStrategyStoredKeyTests(
        WorkflowsFixture<WebApiCallStrategyStoredKeyTests> fixture,
        LocalTestHttpServer server,
        ITestOutputHelper outputHelper)
    {
        fixture.PlatformSettings.OutboundAccess.AllowedInternalHosts = ["127.0.0.1"];
        fixture.PlatformSettings.StoredKeys.DeploymentKeys[DEPLOYMENT_KEY_NAME] = DEPLOYMENT_KEY_VALUE;
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
        _server = server;
    }

    [Fact]
    public async Task ExecuteBlockAsync_AStoredKeyInsideAHeaderValue_SendsTheValueInItsPlace()
    {
        await StoreKeyAsync(ORGANIZATION_ID, KEY_NAME, KEY_VALUE);
        (Block block, BlockExecutionContext context) = Arrange(
            ORGANIZATION_ID,
            "/headers",
            new() { ["Authorization"] = $"Bearer {_keyToken}" });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse?.RawResponse);
        Assert.Contains($"Bearer {KEY_VALUE}", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_AStoredKey_LeavesOnlyItsPlaceholderInTheBlock()
    {
        await StoreKeyAsync(ORGANIZATION_ID, KEY_NAME, KEY_VALUE);
        (Block block, BlockExecutionContext context) = Arrange(ORGANIZATION_ID, "/headers", new() { ["x-api-key"] = _keyToken });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        string savedProperties = JsonConvert.SerializeObject(block.Properties);
        Assert.Contains(_keyToken, savedProperties);
        Assert.DoesNotContain(KEY_VALUE, savedProperties);
    }

    [Fact]
    public async Task ExecuteBlockAsync_AFormerTenantKeyPlaceholder_SendsTheStoredKeyOfThatName()
    {
        await StoreKeyAsync(ORGANIZATION_ID, "ParallelApiKey", FORMER_TENANT_KEY_VALUE);
        (Block block, BlockExecutionContext context) = Arrange(
            ORGANIZATION_ID,
            "/headers",
            new() { ["x-api-key"] = "{Organization.ParallelApiKey}" });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse?.RawResponse);
        Assert.Contains(FORMER_TENANT_KEY_VALUE, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_AStoredKeyTheOrganizationDoesNotHave_FailsNamingItAndSendsNothing()
    {
        await StoreKeyAsync(ORGANIZATION_ID, KEY_NAME, KEY_VALUE);
        (Block block, BlockExecutionContext context) = Arrange(
            OTHER_ORGANIZATION_ID,
            "/never-reached-missing-key",
            new() { ["x-api-key"] = _keyToken });

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains($"no stored key named '{KEY_NAME}'", failure.Message);
        Assert.Equal(0, _server.PathHits("/never-reached-missing-key"));
    }

    [Fact]
    public async Task ExecuteBlockAsync_AFormerTenantKeyTheOrganizationDoesNotHave_SendsTheDeploymentValue()
    {
        (Block block, BlockExecutionContext context) = Arrange(
            OTHER_ORGANIZATION_ID,
            "/headers",
            new() { ["Authorization"] = $"Bearer {{Organization.{DEPLOYMENT_KEY_NAME}}}" });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse?.RawResponse);
        Assert.Contains($"Bearer {DEPLOYMENT_KEY_VALUE}", block.BlockResponse.RawResponse);
        Assert.DoesNotContain(DEPLOYMENT_KEY_VALUE, JsonConvert.SerializeObject(block.Properties));
    }

    [Fact]
    public async Task ExecuteBlockAsync_AnOrganizationWithItsOwnKey_SendsItsOwnValueOverTheDeploymentValue()
    {
        await StoreKeyAsync(ORGANIZATION_ID, DEPLOYMENT_KEY_NAME, ORGANIZATION_KEY_VALUE);
        (Block block, BlockExecutionContext context) = Arrange(
            ORGANIZATION_ID,
            "/headers",
            new() { ["Authorization"] = $"Bearer {StoredKeyPlaceholders.Token(DEPLOYMENT_KEY_NAME)}" });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse?.RawResponse);
        Assert.Contains($"Bearer {ORGANIZATION_KEY_VALUE}", block.BlockResponse.RawResponse);
        Assert.DoesNotContain(DEPLOYMENT_KEY_VALUE, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_AStoredKeyTheKeyRingCannotDecrypt_FailsNamingItAndSendsNothing()
    {
        await StoreProtectedValueAsync(ORGANIZATION_ID, KEY_NAME, "not-a-data-protection-payload");
        (Block block, BlockExecutionContext context) = Arrange(
            ORGANIZATION_ID,
            "/never-reached-unreadable-key",
            new() { ["x-api-key"] = _keyToken });

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains($"The stored key '{KEY_NAME}' cannot be decrypted", failure.Message);
        Assert.Equal(0, _server.PathHits("/never-reached-unreadable-key"));
    }

    [Fact]
    public async Task ExecuteBlockAsync_AnOrganizationKeyTheKeyRingCannotDecrypt_FailsAlthoughTheDeploymentHasThatName()
    {
        await StoreProtectedValueAsync(UNREADABLE_KEY_ORGANIZATION_ID, DEPLOYMENT_KEY_NAME, "not-a-data-protection-payload");
        (Block block, BlockExecutionContext context) = Arrange(
            UNREADABLE_KEY_ORGANIZATION_ID,
            "/never-reached-unreadable-deployment-name",
            new() { ["Authorization"] = $"Bearer {StoredKeyPlaceholders.Token(DEPLOYMENT_KEY_NAME)}" });

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains($"The stored key '{DEPLOYMENT_KEY_NAME}' cannot be decrypted", failure.Message);
        Assert.Equal(0, _server.PathHits("/never-reached-unreadable-deployment-name"));
    }

    [Fact]
    public void DeploymentValue_MatchesANameWrittenInAnyCase()
    {
        var settings = new StoredKeySettings();
        settings.DeploymentKeys["perplexityapikey"] = DEPLOYMENT_KEY_VALUE;
        settings.DeploymentKeys["ParallelApiKey"] = "";

        Assert.Equal(DEPLOYMENT_KEY_VALUE, settings.DeploymentValue(DEPLOYMENT_KEY_NAME));
        Assert.Null(settings.DeploymentValue("ParallelApiKey"));
        Assert.Null(settings.DeploymentValue("FmpApiKey"));
    }

    [Fact]
    public async Task ExecuteBlockAsync_AStoredKeyOnARunWithNoOrganization_FailsAndSendsNothing()
    {
        (Block block, BlockExecutionContext context) = Arrange(
            null,
            "/never-reached-no-organization",
            new() { ["x-api-key"] = _keyToken });

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(0, _server.PathHits("/never-reached-no-organization"));
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_StoredKeyPlaceholders_StayInTheRunAndReachOnlyTheRequest()
    {
        await StoreKeyAsync(ORGANIZATION_ID, KEY_NAME, KEY_VALUE);
        await StoreKeyAsync(ORGANIZATION_ID, "ParallelApiKey", FORMER_TENANT_KEY_VALUE);
        var workflow = new Workflow();
        var block = new Block(BlockTypes.WebApiCall, new Section("section", workflow))
        {
            Name = "Call",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = $"{_server.BaseUrl}/headers",
                HttpMethod = "GET",
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {_keyToken}",
                    ["x-api-key"] = "{Organization.ParallelApiKey}",
                },
            },
        };
        WorkflowHandler handler = _testServiceProvider.GetRequiredService<WorkflowHandler>();
        using IServiceScope scope = _testServiceProvider.CreateScope();

        _ = await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: [],
            organizationId: ORGANIZATION_ID,
            jobId: Guid.NewGuid().ToString(),
            userDataUnitOfWork: scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            inputs: [],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse?.RawResponse);
        Assert.Contains($"Bearer {KEY_VALUE}", block.BlockResponse.RawResponse);
        Assert.Contains(FORMER_TENANT_KEY_VALUE, block.BlockResponse.RawResponse);
        WebApiCallProperties properties = Assert.IsType<WebApiCallProperties>(block.Properties);
        Assert.Equal($"Bearer {_keyToken}", properties.ExecutionState?.AdditionalHeaders["Authorization"]);
        Assert.Equal("{Organization.ParallelApiKey}", properties.ExecutionState?.AdditionalHeaders["x-api-key"]);
    }

    private (Block Block, BlockExecutionContext Context) Arrange(
        string? organizationId,
        string path,
        Dictionary<string, string> headers)
    {
        var block = new Block(BlockTypes.WebApiCall, new Section("test", new Workflow("test")))
        {
            Name = "Test",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = $"{_server.BaseUrl}{path}",
                HttpMethod = "GET",
                AdditionalHeaders = headers,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        context.OrganizationId = organizationId;
        block.SetExecutionState(false, []);

        return (block, context);
    }

    // The class shares one database, and each test's service provider has its own key ring, so
    // every test writes the value again with its own ring.
    private Task StoreKeyAsync(string organizationId, string name, string value)
    {
        IStoredKeyCipher cipher = _testServiceProvider.GetRequiredService<IStoredKeyCipher>();
        return StoreProtectedValueAsync(organizationId, name, cipher.Protect(value));
    }

    private async Task StoreProtectedValueAsync(string organizationId, string name, string protectedValue)
    {
        using IServiceScope scope = _testServiceProvider.CreateScope();
        ICoreUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();

        OrganizationStoredKey? storedKey = unitOfWork.Repository<OrganizationStoredKey>()
            .GetAll()
            .SingleOrDefault(key => key.OrganizationId == organizationId && key.Name == name);

        if (storedKey is null)
        {
            storedKey = new OrganizationStoredKey { OrganizationId = organizationId, Name = name };
            unitOfWork.Repository<OrganizationStoredKey>().Add(storedKey);
        }

        storedKey.ProtectedValue = protectedValue;
        _ = await unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
    }
}
