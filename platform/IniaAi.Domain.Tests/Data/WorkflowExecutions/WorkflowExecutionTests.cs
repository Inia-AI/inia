using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Tests.Data.WorkflowExecutions;

public class WorkflowExecutionTests
{
    [Fact]
    public void Duration_IsNull_WhenStartedAtIsNull()
    {
        var execution = new WorkflowExecution
        {
            StartedAt = null,
            CompletedAt = DateTime.UtcNow,
        };

        Assert.Null(execution.Duration);
    }

    [Fact]
    public void Duration_IsNull_WhenCompletedAtIsNull()
    {
        var execution = new WorkflowExecution
        {
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
        };

        Assert.Null(execution.Duration);
    }

    [Fact]
    public void Duration_IsNull_WhenBothAreNull()
    {
        var execution = new WorkflowExecution();

        Assert.Null(execution.Duration);
    }

    [Fact]
    public void Duration_ReturnsDifference_WhenBothSet()
    {
        DateTime startedAt = new(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc);
        DateTime completedAt = new(2026, 4, 29, 10, 1, 30, DateTimeKind.Utc);

        var execution = new WorkflowExecution
        {
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };

        Assert.Equal(TimeSpan.FromSeconds(90), execution.Duration);
    }

    [Fact]
    public void Duration_IsNegative_WhenCompletedBeforeStarted()
    {
        DateTime startedAt = new(2026, 4, 29, 10, 1, 30, DateTimeKind.Utc);
        DateTime completedAt = new(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc);

        var execution = new WorkflowExecution
        {
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };

        Assert.Equal(TimeSpan.FromSeconds(-90), execution.Duration);
    }
}
