using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Guards;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// Pins the retire-not-delete invariant structurally: no code path may hard-delete or soft-delete
/// a SharedComponent or SharedComponentVersion row. A soft-deleted component would vanish from
/// head lookups through the global IsDeleted filter while live template copies still link it, and
/// version rows are append-only lineage. Retiring flips State and must pass.
/// </summary>
public class SharedComponentDeletionGuardTests
{
    [Fact]
    public void HardDeletedComponent_Throws()
    {
        using CoreDbContext context = BuildContext();
        SharedComponent component = TrackComponent(context);

        context.Remove(component);

        _ = Assert.Throws<InvalidOperationException>(
            () => SharedComponentDeletionGuard.Validate(context.ChangeTracker));
    }

    [Fact]
    public void SoftDeletedComponent_Throws()
    {
        using CoreDbContext context = BuildContext();
        SharedComponent component = TrackComponent(context);

        component.IsDeleted = true;
        component.DeletedAt = DateTime.UtcNow;

        _ = Assert.Throws<InvalidOperationException>(
            () => SharedComponentDeletionGuard.Validate(context.ChangeTracker));
    }

    [Fact]
    public void HardDeletedVersion_Throws()
    {
        using CoreDbContext context = BuildContext();
        SharedComponentVersion version = TrackVersion(context);

        context.Remove(version);

        _ = Assert.Throws<InvalidOperationException>(
            () => SharedComponentDeletionGuard.Validate(context.ChangeTracker));
    }

    [Fact]
    public void SoftDeletedVersion_Throws()
    {
        using CoreDbContext context = BuildContext();
        SharedComponentVersion version = TrackVersion(context);

        version.IsDeleted = true;

        _ = Assert.Throws<InvalidOperationException>(
            () => SharedComponentDeletionGuard.Validate(context.ChangeTracker));
    }

    [Fact]
    public void RetiredTransition_Passes()
    {
        using CoreDbContext context = BuildContext();
        SharedComponent component = TrackComponent(context);

        component.State = SharedComponentState.Retired;

        SharedComponentDeletionGuard.Validate(context.ChangeTracker);
    }

    [Fact]
    public void Rename_Passes()
    {
        using CoreDbContext context = BuildContext();
        SharedComponent component = TrackComponent(context);

        component.Name = "Renamed";

        SharedComponentDeletionGuard.Validate(context.ChangeTracker);
    }

    [Fact]
    public void AddedRegistryRows_Pass()
    {
        using CoreDbContext context = BuildContext();
        _ = context.Add(NewComponent());
        _ = context.Add(NewVersion());

        SharedComponentDeletionGuard.Validate(context.ChangeTracker);
    }

    [Fact]
    public void UnrelatedEntityDeletion_Passes()
    {
        using CoreDbContext context = BuildContext();
        var workflowType = new WorkflowType("WT-1", "Report");
        context.Attach(workflowType).State = EntityState.Unchanged;

        context.Remove(workflowType);

        SharedComponentDeletionGuard.Validate(context.ChangeTracker);
    }

    [Fact]
    public async Task SaveChangesAsync_RefusesComponentDeletion_BeforeTouchingTheDatabase()
    {
        using CoreDbContext context = BuildContext();
        SharedComponent component = TrackComponent(context);

        context.Remove(component);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private static CoreDbContext BuildContext()
    {
        DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlServer("Server=unused")
            .Options;

        return new CoreDbContext(options);
    }

    private static SharedComponent NewComponent()
    {
        return new SharedComponent
        {
            Kind = SharedComponentKind.Block,
            Name = "Alpha",
            State = SharedComponentState.Active,
        };
    }

    private static SharedComponentVersion NewVersion()
    {
        return new SharedComponentVersion
        {
            SharedComponentId = "component-1",
            SequenceNumber = 1,
            ContentHash = new string('a', 64),
            CreatedByUserId = "user-test",
        };
    }

    private static SharedComponent TrackComponent(CoreDbContext context)
    {
        SharedComponent component = NewComponent();
        context.Attach(component).State = EntityState.Unchanged;

        return component;
    }

    private static SharedComponentVersion TrackVersion(CoreDbContext context)
    {
        SharedComponentVersion version = NewVersion();
        context.Attach(version).State = EntityState.Unchanged;

        return version;
    }
}
