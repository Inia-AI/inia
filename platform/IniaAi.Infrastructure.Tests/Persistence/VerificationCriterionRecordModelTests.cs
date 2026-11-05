using IniaAi.Domain.Entities.Verification;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// Guards the failure record's deliberate shape (spec §11): relationship-free — no FK could
/// cascade the longitudinal record away with its execution, and no navigation could derive the
/// tenant — with its query and write-path indexes declared up front, invisible to the template
/// context.
/// </summary>
public class VerificationCriterionRecordModelTests
{
    [Fact]
    public void UserDataModel_ContainsTheRecord_WithNoRelationships()
    {
        using UserDataDbContext context = SqliteModelContexts.CreateUserDataContext();

        IEntityType? entityType = context.Model.FindEntityType(typeof(VerificationCriterionRecord));

        Assert.NotNull(entityType);
        Assert.Empty(entityType.GetForeignKeys());
        Assert.Empty(entityType.GetNavigations());
        Assert.Empty(entityType.GetReferencingForeignKeys());
    }

    [Fact]
    public void UserDataModel_DeclaresTheQueryAndWritePathIndexes()
    {
        using UserDataDbContext context = SqliteModelContexts.CreateUserDataContext();

        IEntityType entityType = context.Model.FindEntityType(typeof(VerificationCriterionRecord))!;
        List<List<string>> indexes = [.. entityType.GetIndexes()
            .Select(static index => index.Properties.Select(static property => property.Name).ToList())];

        Assert.Equal(5, indexes.Count);
        Assert.Contains(indexes, static index => index.SequenceEqual(
            [nameof(VerificationCriterionRecord.OrganizationId), nameof(VerificationCriterionRecord.JudgedAtUtc)]));
        Assert.Contains(indexes, static index => index.SequenceEqual(
            [
                nameof(VerificationCriterionRecord.OrganizationId),
                nameof(VerificationCriterionRecord.WorkflowTypeId),
                nameof(VerificationCriterionRecord.CriterionId),
                nameof(VerificationCriterionRecord.JudgedAtUtc),
            ]));
        Assert.Contains(indexes, static index => index.SequenceEqual(
            [
                nameof(VerificationCriterionRecord.OrganizationId),
                nameof(VerificationCriterionRecord.TemplateVersionId),
                nameof(VerificationCriterionRecord.JudgedAtUtc),
            ]));
        Assert.Contains(indexes, static index => index.SequenceEqual(
            [nameof(VerificationCriterionRecord.WorkflowExecutionId)]));
        Assert.Contains(indexes, static index => index.SequenceEqual(
            [nameof(VerificationCriterionRecord.RunExecutionId)]));
    }

    [Fact]
    public void UserDataModel_RequiresTheWriterStampedTenant()
    {
        using UserDataDbContext context = SqliteModelContexts.CreateUserDataContext();

        IEntityType entityType = context.Model.FindEntityType(typeof(VerificationCriterionRecord))!;

        Assert.False(entityType.FindProperty(nameof(VerificationCriterionRecord.OrganizationId))!.IsNullable);
        Assert.True(entityType.FindProperty(nameof(VerificationCriterionRecord.Feedback))!.IsNullable);
        Assert.True(entityType.FindProperty(nameof(VerificationCriterionRecord.EvidenceSpans))!.IsNullable);
    }

    [Fact]
    public void CoreModel_DoesNotContainTheRecord()
    {
        using CoreDbContext context = SqliteModelContexts.CreateCoreContext();

        Assert.Null(context.Model.FindEntityType(typeof(VerificationCriterionRecord)));
    }
}
