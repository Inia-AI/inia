using IniaAi.Common.Interfaces.Services.AgentRuntime;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// The TryGet accessors encode the sidecar state machine's status→payload invariants, so the
/// fixtures here are exactly the shapes it can emit: one payload per status, never error and
/// result together, requiredInput only on awaiting_input.
/// </summary>
public class AgentRunMonitorAccessorsTests
{
    [Fact]
    public void SucceededMonitor_ExposesOnlyTheResult()
    {
        AgentRunMonitor monitor = Monitor(AgentRunStatus.Succeeded) with
        {
            Result = new AgentRunResult("the report", "sess-9"),
        };

        Assert.True(monitor.TryGetResult(out AgentRunResult? result));
        Assert.Equal("the report", result.Output);
        Assert.Equal("sess-9", result.SessionId);
        Assert.False(monitor.TryGetError(out _));
        Assert.False(monitor.TryGetRequiredInput(out _));
    }

    [Fact]
    public void FailedMonitor_ExposesOnlyTheError()
    {
        AgentRunMonitor monitor = Monitor(AgentRunStatus.Failed) with
        {
            Error = new AgentRunError(AgentRunSubtypes.BUDGET_EXHAUSTED),
        };

        Assert.True(monitor.TryGetError(out AgentRunError? error));
        Assert.Equal("budget_exhausted", error.Subtype);
        Assert.False(monitor.TryGetResult(out _));
        Assert.False(monitor.TryGetRequiredInput(out _));
    }

    [Fact]
    public void AwaitingInputMonitor_ExposesOnlyTheRequiredInput()
    {
        using JsonDocument input = JsonDocument.Parse("""{ "draft": "Fund A" }""");
        AgentRunMonitor monitor = Monitor(AgentRunStatus.AwaitingInput) with
        {
            RequiredInput = new AgentRunRequiredInput(
                "resume-abc",
                new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero),
                new AgentRunQuestion("q1", "select_fund", "Which fund?", input.RootElement.Clone())),
            PausedAt = new DateTimeOffset(2026, 8, 3, 10, 3, 0, TimeSpan.Zero),
            ResumedAt = null,
        };

        Assert.True(monitor.TryGetRequiredInput(out AgentRunRequiredInput? requiredInput));
        Assert.Equal("resume-abc", requiredInput.ResumeToken);
        Assert.Equal("q1", requiredInput.Question.Id);
        Assert.False(monitor.TryGetResult(out _));
        Assert.False(monitor.TryGetError(out _));
    }

    [Fact]
    public void RunningMonitor_ExposesNoPayloadEvenIfAFieldSneaksIn()
    {
        AgentRunMonitor monitor = Monitor(AgentRunStatus.Running) with
        {
            Result = new AgentRunResult("stray", "sess-9"),
            Error = new AgentRunError(AgentRunSubtypes.INTERNAL_ERROR),
        };

        Assert.False(monitor.TryGetResult(out _));
        Assert.False(monitor.TryGetError(out _));
        Assert.False(monitor.TryGetRequiredInput(out _));
    }

    [Fact]
    public void PayloadStatusWithoutItsPayload_IsFalseNotAThrow()
    {
        Assert.False(Monitor(AgentRunStatus.Succeeded).TryGetResult(out _));
        Assert.False(Monitor(AgentRunStatus.Failed).TryGetError(out _));
        Assert.False(Monitor(AgentRunStatus.AwaitingInput).TryGetRequiredInput(out _));
    }

    private static AgentRunMonitor Monitor(AgentRunStatus status)
    {
        return new AgentRunMonitor
        {
            ExecutionId = "exec-1",
            Attempt = 1,
            Segment = 0,
            Status = status,
            CreatedAt = new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 3, 10, 5, 0, TimeSpan.Zero),
            EstimatedCostUsd = 0.25m,
        };
    }
}
