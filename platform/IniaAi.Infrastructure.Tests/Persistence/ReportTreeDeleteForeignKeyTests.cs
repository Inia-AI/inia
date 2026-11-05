using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Infrastructure.Tests.Persistence;

public class ReportTreeDeleteForeignKeyTests
{
    // The WorkflowExecutionLog -> ReportParagraph FK is a nullable shadow key with no loaded
    // navigation, so EF cannot null it client-side when a paragraph (or its section's paragraphs)
    // is deleted. Unless the DB itself nulls it, the delete throws a FK violation (PG 23503 /
    // MSSQL 547). SetNull is the only behavior that survives a delete without loading every log.
    // The full runtime proof is the live PG execute-then-delete; this pins the model config so the
    // behavior cannot silently regress back to the convention default (ClientSetNull -> DB NoAction).
    [Fact]
    public void WorkflowExecutionLogToReportParagraph_ForeignKey_IsOptionalAndSetNullOnDelete()
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new UserDataDbContext(options);

        IForeignKey foreignKey = context.Model
            .FindEntityType(typeof(WorkflowExecutionLog))!
            .GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(ReportParagraph));

        Assert.False(foreignKey.IsRequired, "FK must be optional for SetNull to be valid.");
        Assert.Equal(DeleteBehavior.SetNull, foreignKey.DeleteBehavior);
    }
}
