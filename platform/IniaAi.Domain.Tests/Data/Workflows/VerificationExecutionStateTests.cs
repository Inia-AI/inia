using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class VerificationExecutionStateTests
{
    private static VerificationProperties CreateProperties()
    {
        return new VerificationProperties
        {
            PromptTemplate = "Verify {member}.",
            SystemPromptTemplate = "You verify.",
        };
    }

    [Fact]
    public void ReResolution_CarriesTheExecutedRecordOver()
    {
        VerificationProperties props = CreateProperties();
        var previous = new VerificationPropertiesExecutionState(props)
        {
            RunPasses = [new AgentRunPass { ExecutionId = "run-1" }],
            Judgments = [new VerificationJudgment { Round = 1, UncheckedReason = "capacity overflow" }],
            ReleaseMarker = new VerificationReleaseMarker
            {
                Provenance = VerificationProvenance.UNVERIFIED_UNCHECKED,
                UncheckedReason = "capacity overflow",
            },
        };

        var resolved = new VerificationPropertiesExecutionState(props, previous);

        AgentRunPass pass = Assert.Single(resolved.RunPasses);
        Assert.Equal("run-1", pass.ExecutionId);
        Assert.Same(previous.Judgments[0], Assert.Single(resolved.Judgments));
        Assert.Same(previous.ReleaseMarker, resolved.ReleaseMarker);
    }

    [Fact]
    public void ReResolution_CarriesEveryRoundOfTheJudgmentLedgerOver()
    {
        VerificationProperties props = CreateProperties();
        var roundOne = new VerificationJudgment { Round = 1 };
        var roundTwo = new VerificationJudgment { Round = 2 };
        var previous = new VerificationPropertiesExecutionState(props)
        {
            Judgments = [roundOne, roundTwo],
        };

        var resolved = new VerificationPropertiesExecutionState(props, previous);

        Assert.Equal(2, resolved.Judgments.Count);
        Assert.Same(roundOne, resolved.Judgments[0]);
        Assert.Same(roundTwo, resolved.Judgments[1]);
        Assert.Same(roundTwo, resolved.LatestJudgment);
    }

    [Fact]
    public void LatestJudgment_PicksTheHighestRound_NotTheAppendOrder()
    {
        VerificationProperties props = CreateProperties();
        var state = new VerificationPropertiesExecutionState(props)
        {
            Judgments =
            [
                new VerificationJudgment { Round = 2 },
                new VerificationJudgment { Round = 1 },
            ],
        };

        Assert.Equal(2, state.LatestJudgment!.Round);
    }

    [Fact]
    public void Newtonsoft_RoundTripsTheMultiRoundJudgmentLedger()
    {
        VerificationProperties props = CreateProperties();
        var state = new VerificationPropertiesExecutionState(props)
        {
            Judgments =
            [
                new VerificationJudgment
                {
                    Round = 1,
                    Verdict = new VerificationVerdict { Outcome = VerificationOutcomes.FAIL_REVISABLE },
                },
                new VerificationJudgment
                {
                    Round = 2,
                    Verdict = new VerificationVerdict { Outcome = VerificationOutcomes.PASS },
                },
            ],
        };

        string json = JsonConvert.SerializeObject(state);
        var restored = JsonConvert.DeserializeObject<VerificationPropertiesExecutionState>(json);

        Assert.NotNull(restored);
        Assert.Equal(2, restored.Judgments.Count);
        Assert.Equal(VerificationOutcomes.FAIL_REVISABLE, restored.Judgments[0].Verdict!.Outcome);
        Assert.Equal(VerificationOutcomes.PASS, restored.Judgments[1].Verdict!.Outcome);
        Assert.Equal(2, restored.LatestJudgment!.Round);
    }

    [Fact]
    public void ReResolution_WithoutAPreviousState_StartsEmpty()
    {
        VerificationProperties props = CreateProperties();

        var resolved = new VerificationPropertiesExecutionState(props, null);

        Assert.Empty(resolved.RunPasses);
        Assert.Empty(resolved.Judgments);
        Assert.Null(resolved.LatestJudgment);
        Assert.Null(resolved.ReleaseMarker);
    }
}
