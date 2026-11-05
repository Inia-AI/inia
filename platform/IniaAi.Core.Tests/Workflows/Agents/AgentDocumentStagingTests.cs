using IniaAi.Application.Documents;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows.Agents;

/// <summary>
/// The Agent block is open, so it stages a document through open code. A deployment without the
/// paid modules still hands the agent the document's bytes from file storage.
/// </summary>
public class AgentDocumentStagingTests : IClassFixture<WorkflowsFixture<AgentDocumentStagingTests>>
{
    private const string ORGANIZATION_ID = "org-staging";
    private const string DOCUMENT_NAME = "figures.txt";

    private readonly IServiceProvider _sp;

    public AgentDocumentStagingTests(
        WorkflowsFixture<AgentDocumentStagingTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper, withoutClosedModules: true);
    }

    [Fact]
    public async Task StageDocument_LoadsAStoredDocumentsBytes_InTheOpenBuild()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        byte[] content = "quarterly figures"u8.ToArray();

        using IServiceScope scope = _sp.CreateScope();
        IFileStorageManager storage = scope.ServiceProvider.GetRequiredService<IFileStorageManager>();
        StorageProvider provider = storage.GetProviderForTenant(ORGANIZATION_ID);
        var metadata = new FileMetadata(ORGANIZATION_ID, DOCUMENT_NAME, content.Length, provider);
        var document = new Document(metadata, MediaType.TextPlain) { Name = DOCUMENT_NAME };
        _ = await DocumentStorage.StoreBytesAsync(storage, document, content);

        Workflow root = new() { Id = "staging-root", Title = "staging" };
        Section section = new("S", root) { Id = "staging-sec" };
        var block = new Block(BlockTypes.Agent, section) { Id = "staging-blk", Name = "Agent" };

        BlockExecutionContext context = scope.ServiceProvider.GetRequiredService<BlockExecutionContext>();

        AgentRunDocument staged = await AgentRunBlockSupport.StageDocumentAsync(
            document, DOCUMENT_NAME, block, context, ct);

        Assert.Equal(content, staged.Content.ToArray());
    }
}
