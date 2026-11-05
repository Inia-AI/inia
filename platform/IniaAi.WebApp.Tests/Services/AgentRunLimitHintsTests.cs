using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class AgentRunLimitHintsTests
{
    private const decimal DEADLINE_DEFAULT_MS = 900_000m;
    private const decimal DEADLINE_CEILING_MS = 1_800_000m;
    private const decimal BUDGET_DEFAULT_USD = 5m;
    private const decimal BUDGET_CEILING_USD = 12.5m;
    private const decimal TURNS_DEFAULT = 50m;
    private const decimal TURNS_CEILING = 300m;
    private const decimal RETAINED_DEFAULT_BYTES = 128m * 1024m * 1024m;
    private const decimal RETAINED_CEILING_BYTES = 264m * 1024m * 1024m;

    private sealed class FixedLimitsProvider : IAgentRunLimitsProvider
    {
        private readonly AgentRunLimits? _limits;

        public FixedLimitsProvider(AgentRunLimits? limits)
        {
            _limits = limits;
        }

        public AgentRunLimitsSnapshot? LastKnown =>
            _limits is null ? null : new AgentRunLimitsSnapshot(_limits, DateTimeOffset.UtcNow);

        public AgentRunLimits? Enforceable => _limits;
    }

    private static AgentRunLimitHints HintsFrom(AgentRunLimits? limits)
    {
        return AgentRunLimitHints.From(new FixedLimitsProvider(limits));
    }

    private static AgentRunLimits Limits()
    {
        return new AgentRunLimits(
            new AgentRunFuseLimit(DEADLINE_DEFAULT_MS, DEADLINE_CEILING_MS, "milliseconds"),
            new AgentRunFuseLimit(BUDGET_DEFAULT_USD, BUDGET_CEILING_USD, "usd"),
            new AgentRunFuseLimit(TURNS_DEFAULT, TURNS_CEILING, "turns"),
            new AgentRunFuseLimit(RETAINED_DEFAULT_BYTES, RETAINED_CEILING_BYTES, "bytes"));
    }

    /// <summary>
    /// Each placeholder carries the deployed default in the unit its own field is authored in —
    /// minutes, USD, model calls, MiB — never the runtime's milliseconds and bytes.
    /// </summary>
    [Fact]
    public void From_CarriesEachDeployedDefaultInTheFieldsOwnUnit()
    {
        AgentRunLimitHints hints = HintsFrom(Limits());

        Assert.Equal("runtime default 15", hints.DeadlinePlaceholder);
        Assert.Equal("runtime default 5.00", hints.BudgetPlaceholder);
        Assert.Equal("runtime default 50", hints.TurnsPlaceholder);
        Assert.Equal("runtime default 128", hints.RetainedPlaceholder);
    }

    [Fact]
    public void From_CapsEachFieldAtTheDeployedCeiling()
    {
        AgentRunLimitHints hints = HintsFrom(Limits());

        Assert.Equal(30, hints.DeadlineMaxMinutes);
        Assert.Equal(BUDGET_CEILING_USD, hints.BudgetMaxUsd);
        Assert.Equal(300, hints.TurnsMax);
        Assert.Equal(264, hints.RetainedMaxMib);
    }

    /// <summary>
    /// The snapshot is taken once at first render and never refreshed, so an editor opened
    /// against a cold cache stays cold for its whole life. The field must then read as a plain
    /// empty-state hint rather than as a figure that went missing mid-sentence.
    /// </summary>
    [Fact]
    public void From_FallsBackToTheBareEmptyState_WhenTheLimitsWereNeverFetched()
    {
        AgentRunLimitHints hints = HintsFrom(null);

        Assert.Same(AgentRunLimitHints.None, hints);
        Assert.Equal(AgentRunLimitHints.RUNTIME_DEFAULT, hints.DeadlinePlaceholder);
        Assert.Equal(AgentRunLimitHints.RUNTIME_DEFAULT, hints.BudgetPlaceholder);
        Assert.Equal(AgentRunLimitHints.RUNTIME_DEFAULT, hints.TurnsPlaceholder);
        Assert.Equal(AgentRunLimitHints.RUNTIME_DEFAULT, hints.RetainedPlaceholder);
    }

    [Fact]
    public void None_LeavesEveryFieldUncapped()
    {
        Assert.Equal(int.MaxValue, AgentRunLimitHints.None.DeadlineMaxMinutes);
        Assert.Equal(decimal.MaxValue, AgentRunLimitHints.None.BudgetMaxUsd);
        Assert.Equal(int.MaxValue, AgentRunLimitHints.None.TurnsMax);
        Assert.Equal(int.MaxValue, AgentRunLimitHints.None.RetainedMaxMib);
    }
}
