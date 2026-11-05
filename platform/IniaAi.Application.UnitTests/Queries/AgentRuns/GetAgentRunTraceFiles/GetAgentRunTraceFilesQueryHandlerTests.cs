using ErrorOr;
using IniaAi.Application.Queries.AgentRuns.GetAgentRunTraceFiles;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.AgentRuns.GetAgentRunTraceFiles;

public class GetAgentRunTraceFilesQueryHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-test";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly GetAgentRunTraceFilesQueryHandler _handler;

    public GetAgentRunTraceFilesQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new GetAgentRunTraceFilesQueryHandler(unitOfWork);
    }

    [Fact]
    public async Task Handle_ReturnsTheBlocksTraceRowsOrderedByPath()
    {
        Block block = await SeedExecutedBlockAsync(ORG_ID);
        await SeedTraceFileAsync(ORG_ID, block.Id, "trace/sub/agent-1.jsonl");
        await SeedTraceFileAsync(ORG_ID, block.Id, "trace/trace.ndjson");

        var query = new GetAgentRunTraceFilesQuery(ORG_ID, USER_ID, block.Id);

        ErrorOr<List<AgentRunTraceFile>> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("trace/sub/agent-1.jsonl", result.Value[0].Path);
        Assert.Equal("trace/trace.ndjson", result.Value[1].Path);
    }

    [Fact]
    public async Task Handle_CrossOrgBlock_ReturnsEmpty()
    {
        Block block = await SeedExecutedBlockAsync(OTHER_ORG_ID);
        await SeedTraceFileAsync(OTHER_ORG_ID, block.Id, "trace/trace.ndjson");

        var query = new GetAgentRunTraceFilesQuery(ORG_ID, USER_ID, block.Id);

        ErrorOr<List<AgentRunTraceFile>> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    private async Task<Block> SeedExecutedBlockAsync(string organizationId)
    {
        var workflow = new Workflow("Executed Agent Workflow") { OrganizationId = organizationId };
        var section = new Section("Section", workflow);
        var block = new Block(BlockTypes.Agent, section) { Name = "Agent" };

        _ = await _context.Workflows.AddAsync(workflow, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        return block;
    }

    private async Task SeedTraceFileAsync(string organizationId, string blockId, string path)
    {
        var file = new AgentRunTraceFile
        {
            OrganizationId = organizationId,
            BlockId = blockId,
            RunExecutionId = "exec-1",
            Attempt = 1,
            Path = path,
            Sha256 = new string('a', 64),
            SizeBytes = 3,
            Content = [1, 2, 3],
        };

        _ = await _context.AgentRunTraceFiles.AddAsync(file, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
