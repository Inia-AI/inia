using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunSubtypesTests
{
    [Fact]
    public void Constants_MirrorTheSidecarWireStrings()
    {
        Assert.Equal("aborted", AgentRunSubtypes.ABORTED);
        Assert.Equal("budget_exhausted", AgentRunSubtypes.BUDGET_EXHAUSTED);
        Assert.Equal("checkpoint_failed", AgentRunSubtypes.CHECKPOINT_FAILED);
        Assert.Equal("checkpoint_missing", AgentRunSubtypes.CHECKPOINT_MISSING);
        Assert.Equal("checkpoint_restore_failed", AgentRunSubtypes.CHECKPOINT_RESTORE_FAILED);
        Assert.Equal("checkpoint_version_mismatch", AgentRunSubtypes.CHECKPOINT_VERSION_MISMATCH);
        Assert.Equal("deadline_exceeded", AgentRunSubtypes.DEADLINE_EXCEEDED);
        Assert.Equal("errored_result", AgentRunSubtypes.ERRORED_RESULT);
        Assert.Equal("internal_error", AgentRunSubtypes.INTERNAL_ERROR);
        Assert.Equal("malformed_pause", AgentRunSubtypes.MALFORMED_PAUSE);
        Assert.Equal("malformed_result", AgentRunSubtypes.MALFORMED_RESULT);
        Assert.Equal("mcp_connect_failed", AgentRunSubtypes.MCP_CONNECT_FAILED);
        Assert.Equal("missing_result", AgentRunSubtypes.MISSING_RESULT);
        Assert.Equal("pause_rejected", AgentRunSubtypes.PAUSE_REJECTED);
        Assert.Equal("pause_unsupported", AgentRunSubtypes.PAUSE_UNSUPPORTED);
        Assert.Equal("run_child_error", AgentRunSubtypes.RUN_CHILD_ERROR);
        Assert.Equal("runtime_shutdown", AgentRunSubtypes.RUNTIME_SHUTDOWN);
        Assert.Equal("session_id_mismatch", AgentRunSubtypes.SESSION_ID_MISMATCH);
        Assert.Equal("uid_pool_exhausted", AgentRunSubtypes.UID_POOL_EXHAUSTED);
        Assert.Equal("upstream_error", AgentRunSubtypes.UPSTREAM_ERROR);
    }

    [Fact]
    public void UpstreamError_IsAnOutageButNeverRetryableBackToBack()
    {
        Assert.True(AgentRunSubtypes.IsUpstreamOutage("upstream_error"));
        Assert.False(AgentRunSubtypes.IsRetryable("upstream_error"));
    }

    [Theory]
    [InlineData("run_child_error")]
    [InlineData("internal_error")]
    [InlineData("errored_result")]
    [InlineData("some_future_subtype")]
    [InlineData("")]
    public void IsUpstreamOutage_FalseForEverythingElse(string subtype)
    {
        Assert.False(AgentRunSubtypes.IsUpstreamOutage(subtype));
    }

    [Theory]
    [InlineData("uid_pool_exhausted")]
    [InlineData("internal_error")]
    [InlineData("runtime_shutdown")]
    public void IsRetryable_TrueForInfrastructureFailures(string subtype)
    {
        Assert.True(AgentRunSubtypes.IsRetryable(subtype));
    }

    [Theory]
    [InlineData("aborted")]
    [InlineData("budget_exhausted")]
    [InlineData("checkpoint_failed")]
    [InlineData("checkpoint_missing")]
    [InlineData("deadline_exceeded")]
    [InlineData("malformed_pause")]
    [InlineData("malformed_result")]
    [InlineData("missing_result")]
    [InlineData("pause_rejected")]
    [InlineData("pause_unsupported")]
    [InlineData("errored_result")]
    [InlineData("mcp_connect_failed")]
    public void IsRetryable_FalseForDeterministicOrDeliberateOutcomes(string subtype)
    {
        Assert.False(AgentRunSubtypes.IsRetryable(subtype));
    }

    [Fact]
    public void IsRetryable_FalseForTheSpendCarryingRunChildError()
    {
        Assert.False(AgentRunSubtypes.IsRetryable("run_child_error"));
    }

    [Theory]
    [InlineData("budget_exceeded")]
    [InlineData("some_future_subtype")]
    [InlineData("")]
    public void IsRetryable_FalseForUnknownSubtypes(string subtype)
    {
        Assert.False(AgentRunSubtypes.IsRetryable(subtype));
    }

    [Theory]
    [InlineData("checkpoint_restore_failed")]
    [InlineData("checkpoint_version_mismatch")]
    [InlineData("session_id_mismatch")]
    public void RestoreFailures_AreNeverRetryable(string subtype)
    {
        Assert.False(AgentRunSubtypes.IsRetryable(subtype));
        Assert.True(AgentRunSubtypes.IsContinuationRestoreFailure(subtype));
    }

    [Theory]
    [InlineData("checkpoint_failed")]
    [InlineData("checkpoint_missing")]
    [InlineData("run_child_error")]
    [InlineData("some_future_subtype")]
    public void IsContinuationRestoreFailure_FalseForEverythingElse(string subtype)
    {
        Assert.False(AgentRunSubtypes.IsContinuationRestoreFailure(subtype));
    }
}
