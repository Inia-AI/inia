using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// Regression tests for the EF Core model configuration of <see cref="Block.Properties"/>.
/// The column is persisted via <c>HasConversion</c> with a JSON (de)serialization pair.
/// For reference-typed converted columns EF Core falls back to reference equality for
/// change detection unless a <see cref="ValueComparer"/> is explicitly configured — in
/// which case in-place mutations (like setting <c>Block.Properties.ExecutionState</c>
/// after a block executes) are invisible to the change tracker and never persisted.
/// </summary>
public class UserDataDbContextBlockPropertiesTests
{
    [Fact]
    public void BlockProperties_ValueComparer_DetectsInPlaceExecutionStateMutation()
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new UserDataDbContext(options);
        IEntityType blockEntity = context.Model.FindEntityType(typeof(Block))!;
        IProperty propertiesProperty = blockEntity.FindProperty(nameof(Block.Properties))!;
        ValueComparer comparer = propertiesProperty.GetValueComparer();

        var original = new AiQueryProperties
        {
            UserPromptTemplate = "Hello {tag}",
            SystemPromptTemplate = "You are {role}",
        };

        // EF snapshots the value when the entity is loaded/inserted. The snapshot is what
        // the change tracker compares against at SaveChanges time.
        object snapshot = comparer.Snapshot(original);

        // Simulate block execution: the WorkflowHandler mutates Block.Properties.ExecutionState
        // in place on the already-tracked entity.
        original.ExecutionState = new AiQueryPropertiesExecutionState(original)
        {
            UserPrompt = "Hello world",
            SystemPrompt = "You are helpful",
        };

        // If the comparer correctly deep-clones on Snapshot and compares by value, then
        // the pre-mutation snapshot must NOT equal the post-mutation live instance — which
        // is what tells EF to emit an UPDATE for the JSON column.
        bool equal = comparer.Equals(snapshot, original);

        Assert.False(
            equal,
            "Block.Properties requires a ValueComparer that deep-clones on Snapshot and compares by value. "
            + "Without it, in-place mutations to Block.Properties.ExecutionState during workflow execution "
            + "are invisible to the EF change tracker and the executed-workflow row is saved with a stale "
            + "(null) ExecutionState.");
    }

    [Fact]
    public void BlockProperties_ValueComparer_Snapshot_ProducesDeepClone()
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new UserDataDbContext(options);
        IProperty propertiesProperty = context.Model
            .FindEntityType(typeof(Block))!
            .FindProperty(nameof(Block.Properties))!;
        ValueComparer comparer = propertiesProperty.GetValueComparer();

        var original = new AiQueryProperties { UserPromptTemplate = "Hello" };

        var snapshot = (BlockProperties)comparer.Snapshot(original);

        // Mutate the original after snapshotting — a deep clone must not reflect the change.
        original.UserPromptTemplate = "Goodbye";

        Assert.IsType<AiQueryProperties>(snapshot);
        Assert.Equal("Hello", ((AiQueryProperties)snapshot).UserPromptTemplate);
    }
}
