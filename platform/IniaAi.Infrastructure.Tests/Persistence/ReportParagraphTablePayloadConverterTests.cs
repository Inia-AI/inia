using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IniaAi.Infrastructure.Tests.Persistence;

public class ReportParagraphTablePayloadConverterTests
{
    [Fact]
    public void TablePayload_ValueComparer_DetectsInPlaceMutation()
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new UserDataDbContext(options);
        IProperty tablePayloadProperty = context.Model
            .FindEntityType(typeof(ReportParagraph))!
            .FindProperty(nameof(ReportParagraph.TablePayload))!;
        ValueComparer comparer = tablePayloadProperty.GetValueComparer();

        var original = new TableOutput
        {
            TableName = "Revenue",
            TableData = [["2024", "100"]],
        };

        object snapshot = comparer.Snapshot(original);

        original.TableData.Add(["2025", "120"]);

        Assert.False(
            comparer.Equals(snapshot, original),
            "ReportParagraph.TablePayload requires a ValueComparer that deep-clones on Snapshot and "
            + "compares by value; otherwise in-place table edits are invisible to the EF change tracker.");
    }
}
