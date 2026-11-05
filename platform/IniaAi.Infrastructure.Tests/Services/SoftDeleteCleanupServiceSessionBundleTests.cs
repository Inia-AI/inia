using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Tests.Helpers;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Pins that purging a block also releases the bytes of its session bundles: the pointer rows go
/// with the block by DB cascade, so without an explicit byte release every purge would strand a
/// blob no sweep can see — under the Database provider, an invisible row in the Files table.
/// Every "the bytes are gone" here is a read that came back empty, not a call that was observed.
/// </summary>
public class SoftDeleteCleanupServiceSessionBundleTests
{
    private const string ORG_A = "org-a";
    private const string ORG_B = "org-b";
    private const int RETENTION_DAYS = 7;

    private static readonly DateTime _pastCutoff = DateTime.UtcNow.AddDays(-(RETENTION_DAYS + 1));
    private static readonly byte[] _bundleBytes = [1, 2, 3];

    [Fact]
    public async Task PurgedWorkflow_ReleasesItsSessionBundleBytes_AndLeavesOtherBundlesAlone()
    {
        using var harness = new SqliteSessionBundleHarness();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Block purgedBlock = SeedWorkflowWithAgentBlock(harness.Context, ORG_A, deleted: true);
        Block liveBlock = SeedWorkflowWithAgentBlock(harness.Context, ORG_A, deleted: false);
        Block otherOrgBlock = SeedWorkflowWithAgentBlock(harness.Context, ORG_B, deleted: true);
        AgentRunSessionBundle purged = SeedBundle(harness, ORG_A, purgedBlock.Id);
        AgentRunSessionBundle live = SeedBundle(harness, ORG_A, liveBlock.Id);
        AgentRunSessionBundle otherOrg = SeedBundle(harness, ORG_B, otherOrgBlock.Id);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.CleanupAsync(ORG_A, RETENTION_DAYS, ct);

        Assert.Empty(result.Errors);
        Assert.Null(BytesOf(harness, ORG_A, purged));
        Assert.Equal(_bundleBytes, BytesOf(harness, ORG_A, live));
        Assert.Equal(_bundleBytes, BytesOf(harness, ORG_B, otherOrg));

        List<string> survivingKeys = await harness.Context.AgentRunSessionBundles
            .Select(row => row.StorageKey)
            .ToListAsync(ct);
        List<string> expectedKeys = [live.StorageKey, otherOrg.StorageKey];
        Assert.Equal(expectedKeys.Order(), survivingKeys.Order());
    }

    [Fact]
    public async Task SelfDeletedBlockPastCutoff_ReleasesItsSessionBundleBytes()
    {
        using var harness = new SqliteSessionBundleHarness();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Block liveBlock = SeedWorkflowWithAgentBlock(harness.Context, ORG_A, deleted: false);
        var deletedBlock = new Block(BlockTypes.Agent, liveBlock.Section!)
        {
            IsDeleted = true,
            DeletedAt = _pastCutoff,
        };
        AgentRunSessionBundle condemned = SeedBundle(harness, ORG_A, deletedBlock.Id);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.CleanupAsync(ORG_A, RETENTION_DAYS, ct);

        Assert.Empty(result.Errors);
        Assert.Null(BytesOf(harness, ORG_A, condemned));
        Assert.Empty(await harness.Context.AgentRunSessionBundles.ToListAsync(ct));
    }

    /// <summary>
    /// A stranded blob is reported, not thrown: the rows are already purged when the byte
    /// release runs, and a sweep that stops at the first unreachable blob leaves everything
    /// behind it stranded too.
    /// </summary>
    [Fact]
    public async Task BytesDeleteFails_ReportsEachStrandedBundleAndFinishesTheSweep()
    {
        using var harness = new SqliteSessionBundleHarness(new FileStorageManager(
            new FixedFileStorageResolver(new ThrowingFileStorageService()),
            Options.Create(new FileStorageSettings { DefaultProvider = "Database" })));
        CancellationToken ct = TestContext.Current.CancellationToken;

        Block firstBlock = SeedWorkflowWithAgentBlock(harness.Context, ORG_A, deleted: true);
        Block secondBlock = SeedWorkflowWithAgentBlock(harness.Context, ORG_A, deleted: true);
        _ = SeedBundle(harness, ORG_A, firstBlock.Id);
        _ = SeedBundle(harness, ORG_A, secondBlock.Id);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.CleanupAsync(ORG_A, RETENTION_DAYS, ct);

        Assert.Equal(2, result.Errors.Count(error => error.Contains("session bundle bytes")));
        Assert.Empty(await harness.Context.AgentRunSessionBundles.ToListAsync(ct));
    }

    private static byte[]? BytesOf(
        SqliteSessionBundleHarness harness, string organizationId, AgentRunSessionBundle bundle)
    {
        return harness.Storage.GetTestFile(
            organizationId, StorageCategories.AgentSessionBundles, bundle.StorageKey);
    }

    private static Block SeedWorkflowWithAgentBlock(UserDataDbContext context, string organizationId, bool deleted)
    {
        var workflow = new Workflow
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{organizationId}-{(deleted ? "deleted" : "live")}",
            OrganizationId = organizationId,
            IsDeleted = deleted,
            DeletedAt = deleted ? _pastCutoff : null,
        };
        var section = new Section("main", workflow) { Id = Guid.NewGuid().ToString() };
        var block = new Block(BlockTypes.Agent, section);
        _ = context.Workflows.Add(workflow);

        return block;
    }

    private static AgentRunSessionBundle SeedBundle(
        SqliteSessionBundleHarness harness, string organizationId, string blockId)
    {
        var bundle = new AgentRunSessionBundle
        {
            OrganizationId = organizationId,
            BlockId = blockId,
            Round = AgentRunIds.FIRST_ROUND,
            RunExecutionId = AgentRunIds.For(
                "exec", blockId, AgentRunIds.WORKER_ROLE, AgentRunIds.FIRST_ROUND, AgentRunIds.FIRST_PASS),
            Attempt = AgentRunIds.FIRST_PASS,
            Segment = AgentRunCheckpoint.TERMINAL_SEGMENT,
            Sha256 = new string('a', 64),
            SizeBytes = _bundleBytes.Length,
            StorageProvider = StorageProvider.Database.ToString(),
            StorageKey = AgentRunSessionBundleStorage.NewStorageKey(),
        };
        _ = harness.Context.AgentRunSessionBundles.Add(bundle);
        harness.Storage.AddTestFile(
            organizationId, StorageCategories.AgentSessionBundles, bundle.StorageKey, _bundleBytes);

        return bundle;
    }

    private sealed class SqliteSessionBundleHarness : IDisposable
    {
        public SqliteUserDataDbContext Context { get; }
        public SoftDeleteCleanupService Service { get; }
        public TestFileStorageService Storage { get; } = new();

        private readonly SqliteConnection _connection;

        public SqliteSessionBundleHarness(IFileStorageManager? fileStorageManager = null)
        {
            _connection = SqliteUserDataSchema.OpenInMemoryDatabase();

            DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
                .UseSqlite(_connection)
                .Options;
            Context = new SqliteUserDataDbContext(options);
            Context.Organizations.AddRange(
                new Organization("Org A", ORG_A),
                new Organization("Org B", ORG_B));
            _ = Context.SaveChanges();

            Service = new SoftDeleteCleanupService(
                Context,
                fileStorageManager ?? new FileStorageManager(
                    new TestFileStorageResolver(Storage),
                    Options.Create(new FileStorageSettings { DefaultProvider = "Database" })),
                [],
                NullLogger<SoftDeleteCleanupService>.Instance);
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
