using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static IniaAi.Domain.Tests.Data.Workflows.Properties.BlockPropertiesTestCodec;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class VerificationPropertiesTests
{
    private static VerificationProperties CreateTwoMemberProperties()
    {
        return new VerificationProperties
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
                new VerificationCriterion
                {
                    Id = "cross-member-consistency",
                    Label = "Members agree on shared figures",
                },
            ],
            WorkerSkills =
            [
                new AgentWorkerSkill
                {
                    Name = "rubric",
                    Description = "How to judge the group.",
                    Content = "Judge {{BalanceSheet}} against {ClientName} rules.",
                },
            ],
            PromptTemplate = "Verify the group for {{Quarter}}.",
            Model = "test-model-a",
            DeadlineMinutes = 15,
            MaxBudgetUsd = 2.5m,
        };
    }

    [Fact]
    public void Defaults_PassCapIsTwo_AndANewMemberRedoesByRevise()
    {
        var props = new VerificationProperties();

        Assert.Equal(BlockTypes.Verification, props.Type);
        Assert.Equal(2, props.PassCap);
        Assert.Equal(VerificationRedoMode.Revise, new VerificationMember().RedoMode);
    }

    [Fact]
    public void CreateDefaultProperties_ReturnsVerificationProperties()
    {
        BlockProperties props = Block.CreateDefaultProperties(BlockTypes.Verification);

        var verification = Assert.IsType<VerificationProperties>(props);
        Assert.Equal(BlockTypes.Verification, verification.Type);
    }

    [Fact]
    public void NewtonsoftRoundTrip_PreservesMembersCriteriaAndConfig()
    {
        VerificationProperties props = CreateTwoMemberProperties();

        BlockProperties? deserialized = DeserializeViaConverter(JsonConvert.SerializeObject(props));

        var verification = Assert.IsType<VerificationProperties>(deserialized);
        Assert.Equal(BlockTypes.Verification, verification.Type);
        Assert.Equal(2, verification.Members.Count);
        Assert.Equal(VerificationRedoMode.Revise, verification.Members[0].RedoMode);
        Assert.Equal(VerificationRedoMode.Scratch, verification.Members[1].RedoMode);
        Assert.Equal(2, verification.PassCap);
        Assert.Equal("test-model-a", verification.Model);
        Assert.Equal(15, verification.DeadlineMinutes);
        Assert.Equal(2.5m, verification.MaxBudgetUsd);
        VerificationCriterion groupCriterion = verification.Criteria.Single(c => c.Id == "cross-member-consistency");
        Assert.Empty(groupCriterion.AppliesTo);
        Assert.Equal(["BalanceSheet"], verification.Criteria.Single(c => c.Id == "totals-tie-out").AppliesTo);
    }

    [Fact]
    public void Newtonsoft_AMemberRowWithoutARedoMode_ReadsBackAsRevise()
    {
        VerificationProperties props = CreateTwoMemberProperties();
        var jo = JObject.Parse(JsonConvert.SerializeObject(props));
        var memberRow = (JObject)jo[nameof(VerificationProperties.Members)]![1]!;
        Assert.True(memberRow.Remove(nameof(VerificationMember.RedoMode)));

        var verification = Assert.IsType<VerificationProperties>(
            DeserializeViaConverter(jo.ToString()));

        Assert.Equal(VerificationRedoMode.Revise, verification.Members[1].RedoMode);
    }

    [Fact]
    public void Newtonsoft_APropertiesRowWithoutAPassCap_ReadsBackAsTwo()
    {
        VerificationProperties props = CreateTwoMemberProperties();
        var jo = JObject.Parse(JsonConvert.SerializeObject(props));
        Assert.True(jo.Remove(nameof(VerificationProperties.PassCap)));

        var verification = Assert.IsType<VerificationProperties>(
            DeserializeViaConverter(jo.ToString()));

        Assert.Equal(2, verification.PassCap);
    }

    [Fact]
    public void Clone_IsDeepAndIndependent()
    {
        VerificationProperties props = CreateTwoMemberProperties();
        props.PassCap = 3;

        var clone = Assert.IsType<VerificationProperties>(props.Clone());
        clone.Members[0].MemberTag = "Renamed";
        clone.Members[0].RedoMode = VerificationRedoMode.Scratch;
        clone.Criteria[0].AppliesTo.Add("CashFlow");
        clone.WorkerSkills[0].Content = "changed";

        Assert.Equal("BalanceSheet", props.Members[0].MemberTag);
        Assert.Equal(VerificationRedoMode.Revise, props.Members[0].RedoMode);
        Assert.Equal(["BalanceSheet"], props.Criteria[0].AppliesTo);
        Assert.Contains("{{BalanceSheet}}", props.WorkerSkills[0].Content);
        Assert.Equal("test-model-a", clone.Model);
        Assert.Equal(15, clone.DeadlineMinutes);
        Assert.Equal(3, clone.PassCap);
    }

    [Fact]
    public void GetAllPlaceholders_CarriesMemberTagsAndRubricEngineTags()
    {
        VerificationProperties props = CreateTwoMemberProperties();

        List<string> placeholders = props.GetAllPlaceholders();

        Assert.Contains("BalanceSheet", placeholders);
        Assert.Contains("CashFlow", placeholders);
        Assert.Contains("Quarter", placeholders);
        Assert.DoesNotContain("totals-tie-out", placeholders);
        Assert.DoesNotContain("ClientName", placeholders);
    }

    [Fact]
    public void GetAllPlaceholders_SkipsBlankMemberTags()
    {
        var props = new VerificationProperties
        {
            Members = [new VerificationMember(), new VerificationMember { MemberTag = "  " }],
        };

        Assert.Empty(props.GetAllPlaceholders());
    }

    [Fact]
    public void SetExecutionState_PreservesTheRunLedgerAcrossReResolution()
    {
        VerificationProperties props = CreateTwoMemberProperties();
        props.SetExecutionState(false, []);
        props.ExecutionState!.RunPasses.Add(new AgentRunPass
        {
            Round = 1,
            Pass = 1,
            ExecutionId = "wf_block_verifier_r1_1",
            Status = "Succeeded",
        });

        props.SetExecutionState(false, []);

        AgentRunPass survivor = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.Equal("wf_block_verifier_r1_1", survivor.ExecutionId);
    }

    [Fact]
    public void HasDeclaredMembers_AnswersFalseForBlankTagsOnly_AndTrueWithOneRealTag()
    {
        Assert.False(new VerificationProperties().HasDeclaredMembers());
        Assert.False(new VerificationProperties
        {
            Members = [new VerificationMember(), new VerificationMember { MemberTag = "  " }],
        }.HasDeclaredMembers());
        Assert.True(CreateTwoMemberProperties().HasDeclaredMembers());
    }

    [Fact]
    public void ConfiguredRedoModeFor_ReadsTheDeclaredMode_FirstDeclarationWins_NullForUnknown()
    {
        VerificationProperties props = CreateTwoMemberProperties();
        props.Members.Add(new VerificationMember { MemberTag = "CashFlow", RedoMode = VerificationRedoMode.Revise });

        Assert.Equal(VerificationRedoMode.Revise, props.ConfiguredRedoModeFor("BalanceSheet"));
        Assert.Equal(VerificationRedoMode.Scratch, props.ConfiguredRedoModeFor("CashFlow"));
        Assert.Null(props.ConfiguredRedoModeFor("NoSuchMember"));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(7, 3)]
    public void GetEffectivePassCap_ClampsTheDeclaredCapIntoTheRunnableRange(int declared, int effective)
    {
        Assert.Equal(effective, new VerificationProperties { PassCap = declared }.GetEffectivePassCap());
    }
}
