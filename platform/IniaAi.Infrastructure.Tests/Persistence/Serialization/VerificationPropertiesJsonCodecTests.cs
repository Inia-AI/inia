using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Persistence.Serialization;

public class VerificationPropertiesJsonCodecTests
{
    private static VerificationProperties CreateProperties()
    {
        var props = new VerificationProperties
        {
            Members =
            [
                new VerificationMember { MemberTag = "BalanceSheet" },
                new VerificationMember { MemberTag = "CashFlow", RedoMode = VerificationRedoMode.Scratch },
            ],
            Criteria =
            [
                new VerificationCriterion
                {
                    Id = "totals-tie-out",
                    Label = "Totals tie out to the source",
                    AppliesTo = ["BalanceSheet"],
                },
            ],
            WorkerSkills =
            [
                new AgentWorkerSkill
                {
                    Name = "rubric",
                    Description = "How to judge the group.",
                    Content = "Judge {{BalanceSheet}} against the source.",
                },
            ],
            PromptTemplate = "Verify the group.",
            Model = "test-model-a",
            DeadlineMinutes = 15,
            MaxBudgetUsd = 2.5m,
        };
        props.SetExecutionState(true, []);
        props.ExecutionState!.RunPasses.Add(new AgentRunPass
        {
            Round = 1,
            Pass = 1,
            ExecutionId = "wf_block_verifier_r1_1",
        });
        return props;
    }

    [Fact]
    public void UserDataOptions_RoundTripsMembersCriteriaAndExecutionState()
    {
        VerificationProperties props = CreateProperties();

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        BlockProperties? deserialized = JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options);

        var verification = Assert.IsType<VerificationProperties>(deserialized);
        Assert.Equal(BlockTypes.Verification, verification.Type);
        Assert.Equal(VerificationRedoMode.Revise, verification.Members[0].RedoMode);
        Assert.Equal(VerificationRedoMode.Scratch, verification.Members[1].RedoMode);
        Assert.Equal("totals-tie-out", Assert.Single(verification.Criteria).Id);
        Assert.Equal("test-model-a", verification.Model);
        Assert.Equal(2.5m, verification.MaxBudgetUsd);
        AgentRunPass row = Assert.Single(verification.ExecutionState!.RunPasses);
        Assert.Equal("wf_block_verifier_r1_1", row.ExecutionId);
    }

    [Fact]
    public void UserDataOptions_AMemberRowWithoutARedoMode_ReadsBackAsRevise()
    {
        VerificationProperties props = CreateProperties();

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options)
            .Replace(",\"redoMode\":\"scratch\"", string.Empty)
            .Replace(",\"redoMode\":\"revise\"", string.Empty);
        Assert.DoesNotContain("redoMode", json, StringComparison.OrdinalIgnoreCase);

        var verification = Assert.IsType<VerificationProperties>(JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options));

        Assert.All(
            verification.Members,
            static member => Assert.Equal(VerificationRedoMode.Revise, member.RedoMode));
    }

    [Fact]
    public void UserDataOptions_AJudgmentWithADerivedVerdictRoundTrips()
    {
        VerificationProperties props = CreateProperties();
        var judgedAt = new DateTime(2026, 8, 24, 10, 30, 0, DateTimeKind.Utc);
        props.ExecutionState!.Judgments.Add(new VerificationJudgment
        {
            Round = 2,
            JudgedAtUtc = judgedAt,
            AdvisoryOutcome = VerificationOutcomes.PASS,
            SuspiciouslyFast = true,
            VerifierSeconds = 4.2,
            VerifierCostUsd = 0.31m,
            ParseWarnings = ["duplicate entry for 'totals-tie-out' collapsed to not-met"],
            Verdict = new VerificationVerdict
            {
                Outcome = VerificationOutcomes.FAIL_REVISABLE,
                RedoMemberTags = ["BalanceSheet"],
                AssessedCriteriaCount = 2,
                CannotAssessCount = 1,
                Members =
                [
                    new VerificationMemberVerdict
                    {
                        MemberTag = "BalanceSheet",
                        DerivedVerdict = VerificationMemberVerdicts.FAIL,
                        AdvisoryVerdict = VerificationMemberVerdicts.PASS,
                        Feedback = "The totals do not tie out.",
                        Criteria =
                        [
                            new VerificationCriterionVerdict
                            {
                                CriterionId = "totals-tie-out",
                                Label = "Totals tie out to the source",
                                State = VerificationCriterionStates.NOT_MET,
                                Feedback = "Assets total 120, components sum to 118.",
                                Evidence =
                                [
                                    new VerificationEvidence
                                    {
                                        Kind = VerificationEvidenceKinds.QUOTE,
                                        Text = "Total assets: 120",
                                        QuoteVerified = false,
                                    },
                                    new VerificationEvidence
                                    {
                                        Kind = VerificationEvidenceKinds.COMPUTATION,
                                        Text = "50 + 68 = 118 != 120",
                                    },
                                ],
                            },
                        ],
                    },
                ],
                GroupCriteria =
                [
                    new VerificationCriterionVerdict
                    {
                        CriterionId = "statements-agree",
                        Label = "The statements agree with each other",
                        State = VerificationCriterionStates.CANNOT_ASSESS,
                        Feedback = "The cash flow statement is not in scope this round.",
                        PlatformGenerated = true,
                        ImplicatedMembers = ["BalanceSheet", "CashFlow"],
                    },
                ],
            },
        });

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        var verification = Assert.IsType<VerificationProperties>(JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options));

        VerificationJudgment judgment = Assert.Single(verification.ExecutionState!.Judgments);
        Assert.Equal(2, judgment.Round);
        Assert.Equal(judgedAt, judgment.JudgedAtUtc);
        Assert.Null(judgment.UncheckedReason);
        Assert.Equal(VerificationOutcomes.PASS, judgment.AdvisoryOutcome);
        Assert.True(judgment.SuspiciouslyFast);
        Assert.False(judgment.SuspiciouslyCheap);
        Assert.Equal(4.2, judgment.VerifierSeconds);
        Assert.Equal(0.31m, judgment.VerifierCostUsd);
        Assert.Single(judgment.ParseWarnings);

        Assert.NotNull(judgment.Verdict);
        VerificationVerdict verdict = judgment.Verdict;
        Assert.Equal(VerificationOutcomes.FAIL_REVISABLE, verdict.Outcome);
        Assert.Equal(["BalanceSheet"], verdict.RedoMemberTags);
        Assert.Equal(2, verdict.AssessedCriteriaCount);
        Assert.Equal(1, verdict.CannotAssessCount);

        VerificationMemberVerdict member = Assert.Single(verdict.Members);
        Assert.Equal(VerificationMemberVerdicts.FAIL, member.DerivedVerdict);
        Assert.Equal(VerificationMemberVerdicts.PASS, member.AdvisoryVerdict);

        VerificationCriterionVerdict criterion = Assert.Single(member.Criteria);
        Assert.Equal("Totals tie out to the source", criterion.Label);
        Assert.Equal(VerificationCriterionStates.NOT_MET, criterion.State);
        Assert.Equal(2, criterion.Evidence.Count);
        Assert.False(criterion.Evidence[0].QuoteVerified);
        Assert.Null(criterion.Evidence[1].QuoteVerified);

        VerificationCriterionVerdict group = Assert.Single(verdict.GroupCriteria);
        Assert.True(group.PlatformGenerated);
        Assert.Equal(["BalanceSheet", "CashFlow"], group.ImplicatedMembers);
    }

    [Fact]
    public void UserDataOptions_AnUncheckedJudgmentRoundTrips()
    {
        VerificationProperties props = CreateProperties();
        props.ExecutionState!.Judgments.Add(new VerificationJudgment
        {
            UncheckedReason = "The verifier run failed with subtype 'deadline_exceeded'.",
            VerifierCostUsd = 1.05m,
        });

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        var verification = Assert.IsType<VerificationProperties>(JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options));

        VerificationJudgment judgment = Assert.Single(verification.ExecutionState!.Judgments);
        Assert.Equal("The verifier run failed with subtype 'deadline_exceeded'.", judgment.UncheckedReason);
        Assert.Null(judgment.Verdict);
        Assert.Equal(1.05m, judgment.VerifierCostUsd);
    }

    [Fact]
    public void UserDataOptions_ATwoRoundJudgmentLedgerRoundTrips()
    {
        VerificationProperties props = CreateProperties();
        props.ExecutionState!.Judgments.Add(new VerificationJudgment
        {
            Round = 1,
            Verdict = new VerificationVerdict
            {
                Outcome = VerificationOutcomes.FAIL_REVISABLE,
                RedoMemberTags = ["BalanceSheet"],
            },
        });
        props.ExecutionState.Judgments.Add(new VerificationJudgment
        {
            Round = 2,
            Verdict = new VerificationVerdict { Outcome = VerificationOutcomes.PASS },
        });

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        var verification = Assert.IsType<VerificationProperties>(JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options));

        Assert.Equal(2, verification.ExecutionState!.Judgments.Count);
        Assert.Equal(VerificationOutcomes.FAIL_REVISABLE, verification.ExecutionState.Judgments[0].Verdict!.Outcome);
        Assert.Equal(["BalanceSheet"], verification.ExecutionState.Judgments[0].Verdict!.RedoMemberTags);
        Assert.Equal(VerificationOutcomes.PASS, verification.ExecutionState.Judgments[1].Verdict!.Outcome);
        Assert.Equal(2, verification.ExecutionState.LatestJudgment!.Round);
    }

    [Fact]
    public void UserDataOptions_AMixedRoundMarkerRoundTrips()
    {
        VerificationProperties props = CreateProperties();
        props.ExecutionState!.ReleaseMarker = new VerificationReleaseMarker
        {
            Provenance = VerificationProvenance.VERIFIED,
            MixedRoundStatement = "The released set mixes attempts from rounds 1 and 2.",
            NoProgressCriterionIds = ["totals-tie-out"],
            FailedCriteria =
            [
                new VerificationMarkerNote
                {
                    Label = "Totals tie out to the source",
                    Note = "Assets total 120, components sum to 118.",
                    Round = 1,
                },
            ],
        };

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        var verification = Assert.IsType<VerificationProperties>(JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options));

        VerificationReleaseMarker marker = verification.ExecutionState!.ReleaseMarker!;
        Assert.Equal(
            "The released set mixes attempts from rounds 1 and 2.",
            marker.MixedRoundStatement);
        Assert.Equal(["totals-tie-out"], marker.NoProgressCriterionIds);
        Assert.Equal(1, Assert.Single(marker.FailedCriteria).Round);
        Assert.True(marker.HasAnalystNote());
    }

    [Fact]
    public void UserDataOptions_AReleaseMarkerRoundTrips()
    {
        VerificationProperties props = CreateProperties();
        props.ExecutionState!.ReleaseMarker = new VerificationReleaseMarker
        {
            Provenance = VerificationProvenance.UNVERIFIED_EXHAUSTED,
            FailedCriteria =
            [
                new VerificationMarkerNote
                {
                    Label = "Totals tie out to the source",
                    Note = "Assets total 120, components sum to 118.",
                },
            ],
            CannotAssessCriteria =
            [
                new VerificationMarkerNote
                {
                    Label = "The statements agree with each other",
                    Note = "The cash flow statement is not in scope this round.",
                },
            ],
        };

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);
        var verification = Assert.IsType<VerificationProperties>(JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options));

        Assert.NotNull(verification.ExecutionState!.ReleaseMarker);
        VerificationReleaseMarker marker = verification.ExecutionState.ReleaseMarker;
        Assert.Equal(VerificationProvenance.UNVERIFIED_EXHAUSTED, marker.Provenance);
        Assert.True(marker.IsUnverified());
        Assert.Null(marker.UncheckedReason);
        VerificationMarkerNote failed = Assert.Single(marker.FailedCriteria);
        Assert.Equal("Totals tie out to the source", failed.Label);
        Assert.Equal("Assets total 120, components sum to 118.", failed.Note);
        VerificationMarkerNote abstained = Assert.Single(marker.CannotAssessCriteria);
        Assert.Equal("The statements agree with each other", abstained.Label);
    }

    [Fact]
    public void UserDataOptions_AStateWithoutAMarker_OmitsTheProperty()
    {
        VerificationProperties props = CreateProperties();

        string json = JsonSerializer.Serialize<BlockProperties>(props, BlockPropertiesJsonDefaults.Options);

        Assert.DoesNotContain("releaseMarker", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"members\":null")]
    [InlineData("\"criteria\":null")]
    [InlineData("\"members\":[null]")]
    [InlineData("\"criteria\":[null]")]
    [InlineData("\"criteria\":[{\"id\":\"x\",\"appliesTo\":null}]")]
    public void UserDataOptions_NullShapedCollections_ReadBackEmptyInsteadOfNull(string nullShapedMember)
    {
        string json = "{\"type\":\"Verification\","
            + nullShapedMember
            + ",\"model\":\"test-model-a\"}";

        var verification = Assert.IsType<VerificationProperties>(JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options));

        Assert.NotNull(verification.Members);
        Assert.NotNull(verification.Criteria);
        Assert.DoesNotContain(verification.Members, static member => member is null);
        Assert.DoesNotContain(verification.Criteria, static criterion => criterion is null);
        Assert.All(verification.Criteria, static criterion => Assert.NotNull(criterion.AppliesTo));
    }

    /// <summary>
    /// The field was called <c>WindowStopReason</c> when it shipped, days before it was widened
    /// to every reason a loop stops. A marker persisted under the old name still reads back with
    /// its reason, and the alias never writes the old name again.
    /// </summary>
    [Fact]
    public void AMarkerWrittenUnderTheOldFieldName_StillReadsItsStopReason()
    {
        const string reason = "No further repair round was bought: the window would not hold it.";
        string json = "{\"type\":\"Verification\",\"model\":\"test-model-a\","
            + "\"executionState\":{\"releaseMarker\":{\"provenance\":\"unverified-exhausted\","
            + "\"WindowStopReason\":\"" + reason + "\"}}}";

        var verification = Assert.IsType<VerificationProperties>(JsonSerializer.Deserialize<BlockProperties>(
            json, BlockPropertiesJsonDefaults.Options));

        Assert.Equal(reason, verification.ExecutionState?.ReleaseMarker?.StopReason);

        string rewritten = JsonSerializer.Serialize<BlockProperties>(
            verification, BlockPropertiesJsonDefaults.Options);

        Assert.DoesNotContain("WindowStopReason", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(reason, rewritten, StringComparison.Ordinal);
    }
}
