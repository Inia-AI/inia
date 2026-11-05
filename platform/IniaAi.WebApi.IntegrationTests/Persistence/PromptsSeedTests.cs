using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// The startup seed of a prompt set on a real database: a fresh database gets the code defaults, a
/// restart adds nothing, and a PromptsUpdateDate bump adds the new defaults as the newest row while
/// an older edited row is kept. The initializer asks the provider which tables exist, so it cannot
/// run on SQLite or InMemory.
/// </summary>
public abstract class PromptsSeedTests<TPrompts>
    where TPrompts : class
{
    private const string CREATED_AT = "CreatedAt";
    private const string EDITED_PROMPT = "EDITED BY A SUPERADMIN";

    private readonly ICoreDatabaseFixture _fixture;
    private readonly string _skipReason;

    protected PromptsSeedTests(ICoreDatabaseFixture fixture, string skipReason)
    {
        _fixture = fixture;
        _skipReason = skipReason;
    }

    protected abstract DateTime UpdateDate { get; }

    protected abstract string DefaultJudgePrompt { get; }

    [Fact]
    public async Task TheSeed_AddsTheDefaultsOnce_AndABumpAddsThemAgainAsTheNewestRow()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(_skipReason);
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        await ClearAsync(ct);

        Initialize();
        Initialize();

        (DateTime createdAt, string? judgePrompt) = Read(Assert.Single(await ReadAllAsync(ct)));
        Assert.Equal(UpdateDate, createdAt);
        Assert.Equal(DefaultJudgePrompt, judgePrompt);

        await ClearAsync(ct);
        await using (CoreDbContext context = _fixture.CreateContext())
        {
            _ = context.Set<TPrompts>().Add(EditedRow(UpdateDate.AddDays(-1), EDITED_PROMPT));
            _ = await context.SaveChangesAsync(ct);
        }

        Initialize();

        List<TPrompts> rows = await ReadAllAsync(ct);
        Assert.Equal(2, rows.Count);
        Assert.Equal(EDITED_PROMPT, Read(rows[1]).JudgePrompt);
        Assert.Equal(UpdateDate, Read(rows[0]).CreatedAt);
        Assert.Equal(DefaultJudgePrompt, Read(rows[0]).JudgePrompt);
    }

    protected abstract ICorePromptsSeed Seed();

    protected abstract TPrompts EditedRow(DateTime createdAt, string judgePrompt);

    protected abstract (DateTime CreatedAt, string? JudgePrompt) Read(TPrompts row);

    private void Initialize()
    {
        using CoreDbContext context = _fixture.CreateContext();
        new CoreDbInitializer(context, [Seed()]).Initialize();
    }

    private async Task ClearAsync(CancellationToken ct)
    {
        await using CoreDbContext context = _fixture.CreateContext();
        _ = await context.Set<TPrompts>().ExecuteDeleteAsync(ct);
    }

    private async Task<List<TPrompts>> ReadAllAsync(CancellationToken ct)
    {
        await using CoreDbContext context = _fixture.CreateContext();
        return await context.Set<TPrompts>()
            .AsNoTracking()
            .OrderByDescending(prompts => EF.Property<DateTime>(prompts, CREATED_AT))
            .ToListAsync(ct);
    }
}
