using IniaAi.Common;
using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

/// <summary>
/// Replacement tags resolve case-sensitively (Ordinal) — the default comparison threaded through
/// substitution and dependency detection must match the case-sensitive block-output lookup and the
/// validators. These pin the consolidated policy so it can't drift back to case-insensitive.
/// </summary>
public class ReplacementTagCaseSensitivityTests
{
    [Fact]
    public void HasPlaceholder_ExactCase_Matches()
    {
        var workflow = new Workflow("wf");
        var section = new Section("s", workflow);
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "b",
            Properties = new AiQueryProperties { UserPromptTemplate = "Use {block_0.answer}." },
        };

        Assert.True(block.HasPlaceholder("block_0"));
    }

    [Fact]
    public void HasPlaceholder_WrongCase_DoesNotMatch()
    {
        var workflow = new Workflow("wf");
        var section = new Section("s", workflow);
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "b",
            Properties = new AiQueryProperties { UserPromptTemplate = "Use {block_0.answer}." },
        };

        // Case-sensitive: a mis-cased reference must NOT be treated as a dependency.
        Assert.False(block.HasPlaceholder("BLOCK_0"));
    }

    [Fact]
    public void ReplaceAll_WrongCaseKey_DoesNotSubstitute()
    {
        var props = new MergeProperties { TextTemplate = "Use {tone} here." };

        // Default comparison is case-sensitive, so the upper-cased key does not match {tone}.
        props.ReplaceAll(new Dictionary<string, string?> { ["TONE"] = "warm" });

        Assert.Equal("Use {tone} here.", props.TextTemplate);
    }

    [Fact]
    public void ReplaceAll_ExactCaseKey_Substitutes()
    {
        var props = new MergeProperties { TextTemplate = "Use {tone} here." };

        props.ReplaceAll(new Dictionary<string, string?> { ["tone"] = "warm" });

        Assert.Equal("Use {warm} here.", props.TextTemplate);
    }

    // The brace-substitution path (asPlaceHolder: true) is what runtime tag resolution uses;
    // pin it directly against the shared comparison policy.
    [Fact]
    public void BraceSubstitution_WrongCase_DoesNotReplace()
    {
        string? result = "Use {tone}.".ReplacePlaceholders(
            asPlaceHolder: true,
            new Dictionary<string, string?> { ["TONE"] = "warm" },
            ReplacementTags.Comparison);

        Assert.Equal("Use {tone}.", result);
    }

    [Fact]
    public void BraceSubstitution_ExactCase_Replaces()
    {
        string? result = "Use {tone}.".ReplacePlaceholders(
            asPlaceHolder: true,
            new Dictionary<string, string?> { ["tone"] = "warm" },
            ReplacementTags.Comparison);

        Assert.Equal("Use warm.", result);
    }
}
