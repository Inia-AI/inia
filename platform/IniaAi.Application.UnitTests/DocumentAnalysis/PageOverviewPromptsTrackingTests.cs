using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.UnitTests.DocumentAnalysis;

public sealed class PageOverviewPromptsTrackingTests : IDisposable
{
    private readonly SharedComponentsTestHarness _harness = new();

    [Fact]
    public async Task EmptyPrompts_OnAClassStoredWithoutAny_AreNoChange()
    {
        DocumentClass stored = await StoreClassAsync(prompts: null);

        stored.PageOverviews = new PageOverviewPrompts();

        Assert.False(IsModified(stored));
    }

    [Fact]
    public async Task AnEditMadeInPlace_IsAChange()
    {
        DocumentClass stored = await StoreClassAsync(new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "Summarise page {0}." } });

        stored.PageOverviews!.Pages!.UserPrompt = "Summarise page {0} briefly.";

        Assert.True(IsModified(stored));
    }

    [Fact]
    public async Task TheSamePromptsInANewObject_AreNoChange()
    {
        DocumentClass stored = await StoreClassAsync(new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "Summarise page {0}." } });

        stored.PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "Summarise page {0}." } };

        Assert.False(IsModified(stored));
    }

    public void Dispose()
    {
        _harness.Dispose();
    }

    private async Task<DocumentClass> StoreClassAsync(PageOverviewPrompts? prompts)
    {
        var workflow = new Workflow("Tracked");
        _ = new DocumentClass(workflow, "report") { PageOverviews = prompts };
        _ = _harness.Context.Workflows.Add(workflow);
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _harness.Context.ChangeTracker.Clear();

        return await _harness.Context.DocumentClasses.SingleAsync(c => c.Key == "report", TestContext.Current.CancellationToken);
    }

    private bool IsModified(DocumentClass documentClass)
    {
        _harness.Context.ChangeTracker.DetectChanges();

        return _harness.Context.Entry(documentClass).Property(c => c.PageOverviews).IsModified;
    }
}
