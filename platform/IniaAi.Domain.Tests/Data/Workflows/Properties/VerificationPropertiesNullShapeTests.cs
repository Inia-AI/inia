using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using static IniaAi.Domain.Tests.Data.Workflows.Properties.BlockPropertiesTestCodec;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class VerificationPropertiesNullShapeTests
{
    private const string NULL_MEMBERS =
    """
    {
        "Type": "Verification",
        "Members": null,
        "Criteria": [{ "Id": "totals-tie-out", "Label": "Totals tie out", "AppliesTo": ["BalanceSheet"] }]
    }
    """;

    private const string NULL_CRITERIA =
    """
    {
        "Type": "Verification",
        "Members": [{ "MemberTag": "BalanceSheet", "RedoMode": "Revise" }],
        "Criteria": null
    }
    """;

    private const string NULL_MEMBER_ROW =
    """
    {
        "Type": "Verification",
        "Members": [null, { "MemberTag": "BalanceSheet", "RedoMode": "Scratch" }]
    }
    """;

    private const string NULL_CRITERION_ROW =
    """
    {
        "Type": "Verification",
        "Criteria": [null, { "Id": "totals-tie-out", "Label": "Totals tie out" }]
    }
    """;

    private const string NULL_APPLIES_TO =
    """
    {
        "Type": "Verification",
        "Criteria": [{ "Id": "totals-tie-out", "Label": "Totals tie out", "AppliesTo": null }]
    }
    """;

    private const string WELL_FORMED =
    """
    {
        "Type": "Verification",
        "Members": [
            { "MemberTag": "BalanceSheet", "RedoMode": "Revise" },
            { "MemberTag": "CashFlow", "RedoMode": "Scratch" }
        ],
        "Criteria": [
            { "Id": "totals-tie-out", "Label": "Totals tie out", "AppliesTo": ["BalanceSheet", "CashFlow"] },
            { "Id": "cross-member-consistency", "Label": "Members agree", "AppliesTo": [] }
        ],
        "PassCap": 3,
        "Model": "test-model-a"
    }
    """;

    [Fact]
    public void Newtonsoft_ANullMembersList_ReadsBackAsAnEmptyList()
    {
        var verification = Assert.IsType<VerificationProperties>(DeserializeViaConverter(NULL_MEMBERS));

        Assert.Empty(verification.Members);
        Assert.Equal("totals-tie-out", Assert.Single(verification.Criteria).Id);
    }

    [Fact]
    public void Newtonsoft_ANullCriteriaList_ReadsBackAsAnEmptyList()
    {
        var verification = Assert.IsType<VerificationProperties>(DeserializeViaConverter(NULL_CRITERIA));

        Assert.Empty(verification.Criteria);
        Assert.Equal("BalanceSheet", Assert.Single(verification.Members).MemberTag);
    }

    [Fact]
    public void Newtonsoft_ANullMemberRow_IsDropped()
    {
        var verification = Assert.IsType<VerificationProperties>(DeserializeViaConverter(NULL_MEMBER_ROW));

        VerificationMember member = Assert.Single(verification.Members);
        Assert.Equal("BalanceSheet", member.MemberTag);
        Assert.Equal(VerificationRedoMode.Scratch, member.RedoMode);
    }

    [Fact]
    public void Newtonsoft_ANullCriterionRow_IsDropped()
    {
        var verification = Assert.IsType<VerificationProperties>(DeserializeViaConverter(NULL_CRITERION_ROW));

        Assert.Equal("totals-tie-out", Assert.Single(verification.Criteria).Id);
    }

    [Fact]
    public void Newtonsoft_ANullAppliesTo_ReadsBackAsAnEmptyList()
    {
        var verification = Assert.IsType<VerificationProperties>(DeserializeViaConverter(NULL_APPLIES_TO));

        Assert.Empty(Assert.Single(verification.Criteria).AppliesTo);
    }

    [Fact]
    public void Newtonsoft_AWellFormedPayload_IsUnchanged()
    {
        var verification = Assert.IsType<VerificationProperties>(DeserializeViaConverter(WELL_FORMED));

        Assert.Equal(BlockTypes.Verification, verification.Type);
        Assert.Equal(["BalanceSheet", "CashFlow"], verification.Members.Select(member => member.MemberTag));
        Assert.Equal(VerificationRedoMode.Revise, verification.Members[0].RedoMode);
        Assert.Equal(VerificationRedoMode.Scratch, verification.Members[1].RedoMode);
        Assert.Equal(["BalanceSheet", "CashFlow"], verification.Criteria[0].AppliesTo);
        Assert.Empty(verification.Criteria[1].AppliesTo);
        Assert.Equal(3, verification.PassCap);
        Assert.Equal("test-model-a", verification.Model);
    }

    [Fact]
    public void Assignment_ANullMembersList_CoercesToAnEmptyList()
    {
        var props = new VerificationProperties { Members = null! };

        Assert.Empty(props.Members);
    }

    [Fact]
    public void Assignment_ANullCriteriaList_CoercesToAnEmptyList()
    {
        var props = new VerificationProperties { Criteria = null! };

        Assert.Empty(props.Criteria);
    }

    [Fact]
    public void Assignment_ANullAppliesTo_CoercesToAnEmptyList()
    {
        var criterion = new VerificationCriterion { AppliesTo = null! };

        Assert.Empty(criterion.AppliesTo);
    }

    [Fact]
    public void Assignment_ANullRow_IsDropped()
    {
        var props = new VerificationProperties
        {
            Members = [null!, new VerificationMember { MemberTag = "BalanceSheet" }],
            Criteria = [null!, new VerificationCriterion { Id = "totals-tie-out" }],
        };

        Assert.Equal("BalanceSheet", Assert.Single(props.Members).MemberTag);
        Assert.Equal("totals-tie-out", Assert.Single(props.Criteria).Id);
    }
}
