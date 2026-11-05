using ErrorOr;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Custody against a real UserData context and the house in-memory storage service, so a test
/// that says "the bytes are stored" means the bytes came back out of storage.
/// </summary>
public class AgentRunSessionBundleCustodianTests : IDisposable
{
    private const string ORG_ID = "org-custody";
    private const string BLOCK_ID = "blk-custody";
    private const string RUN_ID = "exec_blk_w_1_1";
    private const int ROUND = 1;

    private readonly DbContextOptions<UserDataDbContext> _options;
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly TestFileStorageService _storageService = new();
    private readonly StubAgentRuntimeClient _client = new();
    private readonly IFileStorageManager _storage;
    private readonly AgentRunSessionBundleCustodian _custodian;

    public AgentRunSessionBundleCustodianTests()
    {
        _options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"AgentRunSessionBundleCustodianTests-{Guid.NewGuid()}")
            .Options;

        var services = new ServiceCollection();
        _ = services.AddScoped(_ => new UserDataDbContext(_options));
        _serviceProvider = services.BuildServiceProvider();

        _storage = new FileStorageManager(
            new TestFileStorageResolver(_storageService),
            Options.Create(new FileStorageSettings { DefaultProvider = "Database" }));

        _custodian = new AgentRunSessionBundleCustodian(
            _client,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _storage,
            NullLogger<AgentRunSessionBundleCustodian>.Instance);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceProvider.Dispose();
    }

    private static AgentRunSessionBundleAnchor Anchor(int round = ROUND)
    {
        return AgentRunSessionBundleAnchor.ForBlock(ORG_ID, BLOCK_ID, round);
    }

    private static AgentRunCheckpoint Checkpoint(byte[] bundle)
    {
        return new AgentRunCheckpoint(bundle, Convert.ToHexStringLower(SHA256.HashData(bundle)));
    }

    private void RuntimeHolds(byte[] bundle)
    {
        _client.OnGetRunCheckpoint = _ => Checkpoint(bundle);
    }

    private async Task<List<AgentRunSessionBundle>> RowsAsync()
    {
        using var context = new UserDataDbContext(_options);

        return await context.Set<AgentRunSessionBundle>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TakeCustodyAsync_RecordsTheRowAndStoresTheBytes()
    {
        byte[] bundle = AgentRunSessionBundleContentTests.Bundle(AgentRunSessionBundleContentTests.ManifestJson());
        RuntimeHolds(bundle);

        bool held = await _custodian.TakeCustodyAsync(
            Anchor(), RUN_ID, attempt: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(held);
        AgentRunSessionBundle row = Assert.Single(await RowsAsync());
        Assert.Equal(ORG_ID, row.OrganizationId);
        Assert.Equal(BLOCK_ID, row.BlockId);
        Assert.Equal(ROUND, row.Round);
        Assert.Equal(RUN_ID, row.RunExecutionId);
        Assert.Equal(1, row.Attempt);
        Assert.Equal(AgentRunCheckpoint.TERMINAL_SEGMENT, row.Segment);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bundle)), row.Sha256);
        Assert.Equal(bundle.LongLength, row.SizeBytes);
        Assert.Equal(StorageProvider.Database.ToString(), row.StorageProvider);
        Assert.NotEmpty(row.StorageKey);
        Assert.Contains("bundleFormat", row.VersionStamp);

        Assert.Equal(
            bundle,
            _storageService.GetTestFile(ORG_ID, StorageCategories.AgentSessionBundles, row.StorageKey));
    }

    /// <summary>
    /// A leg that died before its first model turn published nothing. That is the ordinary
    /// answer, not a fault: no row, no bytes, and the run keeps its acknowledgement.
    /// </summary>
    [Fact]
    public async Task TakeCustodyAsync_RunPublishedNoBundle_RecordsNothing()
    {
        _client.OnGetRunCheckpoint = _ => Error.NotFound(AgentRunLifecycleErrors.NOT_FOUND, "no checkpoint");

        bool held = await _custodian.TakeCustodyAsync(
            Anchor(), RUN_ID, attempt: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(held);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task TakeCustodyAsync_FetchFails_RecordsNothing()
    {
        _client.OnGetRunCheckpoint = _ => Error.Failure("AgentRun.Unreachable", "the sidecar went away");

        bool held = await _custodian.TakeCustodyAsync(
            Anchor(), RUN_ID, attempt: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(held);
        Assert.Empty(await RowsAsync());
    }

    /// <summary>
    /// A row exists to say "these exact bytes are held". Bytes that do not hash to the digest
    /// the runtime declared are not those bytes, so nothing is stored and nothing is recorded.
    /// </summary>
    [Fact]
    public async Task TakeCustodyAsync_BytesDoNotMatchTheDeclaredDigest_RefusesTheBundle()
    {
        byte[] bundle = AgentRunSessionBundleContentTests.Bundle(AgentRunSessionBundleContentTests.ManifestJson());
        _client.OnGetRunCheckpoint = _ => new AgentRunCheckpoint(bundle, new string('a', 64));

        bool held = await _custodian.TakeCustodyAsync(
            Anchor(), RUN_ID, attempt: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(held);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task TakeCustodyAsync_StoringTheBytesFails_RecordsNothing()
    {
        var custodian = new AgentRunSessionBundleCustodian(
            _client,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new FileStorageManager(
                new FixedFileStorageResolver(new ThrowingFileStorageService()),
                Options.Create(new FileStorageSettings { DefaultProvider = "Database" })),
            NullLogger<AgentRunSessionBundleCustodian>.Instance);
        RuntimeHolds(AgentRunSessionBundleContentTests.Bundle(AgentRunSessionBundleContentTests.ManifestJson()));

        bool held = await custodian.TakeCustodyAsync(
            Anchor(), RUN_ID, attempt: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(held);
        Assert.Empty(await RowsAsync());
    }

    /// <summary>
    /// The runtime caps what it writes, so a payload past that cap is a sidecar breaking its own
    /// contract. It is refused unread rather than stored: under the Database provider it would
    /// otherwise become one unbounded row in the Files table.
    /// </summary>
    [Fact]
    public async Task TakeCustodyAsync_BundlePastTheCap_RefusesItWithoutStoringAnything()
    {
        byte[] oversized = new byte[AgentRunSessionBundleContent.MAX_BUNDLE_BYTES + 1];
        RuntimeHolds(oversized);

        bool held = await _custodian.TakeCustodyAsync(
            Anchor(), RUN_ID, attempt: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(held);
        Assert.Empty(await RowsAsync());
    }

    /// <summary>
    /// Re-running the same leg mints the same run id, so the round's row is replaced rather than
    /// doubled — and the bytes it used to name go, or every re-run would leak a bundle.
    /// </summary>
    [Fact]
    public async Task TakeCustodyAsync_SecondBundleAtTheSameRound_SupersedesTheRowAndItsBytes()
    {
        RuntimeHolds(AgentRunSessionBundleContentTests.Bundle(AgentRunSessionBundleContentTests.ManifestJson()));
        _ = await _custodian.TakeCustodyAsync(Anchor(), RUN_ID, 1, cancellationToken: TestContext.Current.CancellationToken);
        AgentRunSessionBundle first = Assert.Single(await RowsAsync());

        byte[] second = AgentRunSessionBundleContentTests.Bundle(
            AgentRunSessionBundleContentTests.ManifestJson(
                stamp: """{"bundleFormat":"1","sidecar":{"version":"0.4.0","revision":"later"},"sdk":"2.0.44","model":"claude-opus-5"}"""));
        RuntimeHolds(second);
        _ = await _custodian.TakeCustodyAsync(Anchor(), RUN_ID, 1, cancellationToken: TestContext.Current.CancellationToken);

        AgentRunSessionBundle row = Assert.Single(await RowsAsync());
        Assert.Equal(first.Id, row.Id);
        Assert.NotEqual(first.StorageKey, row.StorageKey);
        Assert.Contains("0.4.0", row.VersionStamp);
        Assert.Null(_storageService.GetTestFile(ORG_ID, StorageCategories.AgentSessionBundles, first.StorageKey));
        Assert.Equal(
            second,
            _storageService.GetTestFile(ORG_ID, StorageCategories.AgentSessionBundles, row.StorageKey));
    }

    [Fact]
    public async Task ResolveNewestBeforeAsync_AfterSupersede_AnswersWithTheStandingBundleOnly()
    {
        RuntimeHolds(AgentRunSessionBundleContentTests.Bundle(AgentRunSessionBundleContentTests.ManifestJson()));
        _ = await _custodian.TakeCustodyAsync(Anchor(), RUN_ID, 1, cancellationToken: TestContext.Current.CancellationToken);

        byte[] second = AgentRunSessionBundleContentTests.Bundle(
            AgentRunSessionBundleContentTests.ManifestJson(
                stamp: """{"bundleFormat":"1","sidecar":{"version":"0.4.0","revision":"later"},"sdk":"2.0.44","model":"claude-opus-5"}"""));
        RuntimeHolds(second);
        _ = await _custodian.TakeCustodyAsync(Anchor(), RUN_ID, 1, cancellationToken: TestContext.Current.CancellationToken);

        AgentRunSessionBundle? resolved = await _custodian.ResolveNewestBeforeAsync(
            ORG_ID, BLOCK_ID, ROUND + 1, TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Contains("0.4.0", resolved.VersionStamp);
    }

    /// <summary>
    /// The descending walk is custody's own: among the rounds that left a bundle below the
    /// dispatch round, the newest wins, whatever any leg's ledger status says.
    /// </summary>
    [Fact]
    public async Task ResolveNewestBeforeAsync_TwoRoundsHeld_AnswersTheNewestBelowTheDispatchRound()
    {
        RuntimeHolds(AgentRunSessionBundleContentTests.Bundle(AgentRunSessionBundleContentTests.ManifestJson()));
        _ = await _custodian.TakeCustodyAsync(Anchor(round: 1), RUN_ID, 1, cancellationToken: TestContext.Current.CancellationToken);
        _ = await _custodian.TakeCustodyAsync(Anchor(round: 2), RUN_ID, 1, cancellationToken: TestContext.Current.CancellationToken);

        AgentRunSessionBundle? resolved = await _custodian.ResolveNewestBeforeAsync(
            ORG_ID, BLOCK_ID, 3, TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.Round);
    }

    /// <summary>
    /// An absent row is the whole "nothing to resume from" answer — there is no availability
    /// flag and no marker to read instead. Another tenant's read is one of the absences.
    /// </summary>
    [Fact]
    public async Task ResolveNewestBeforeAsync_NothingHeldForThatLeg_AnswersNull()
    {
        RuntimeHolds(AgentRunSessionBundleContentTests.Bundle(AgentRunSessionBundleContentTests.ManifestJson()));
        _ = await _custodian.TakeCustodyAsync(Anchor(round: 1), RUN_ID, 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(await _custodian.ResolveNewestBeforeAsync(ORG_ID, BLOCK_ID, 1, TestContext.Current.CancellationToken));
        Assert.Null(await _custodian.ResolveNewestBeforeAsync(ORG_ID, "blk-other", 2, TestContext.Current.CancellationToken));
        Assert.Null(await _custodian.ResolveNewestBeforeAsync(
            "org-someone-else", BLOCK_ID, ROUND + 1, TestContext.Current.CancellationToken));
    }
}
