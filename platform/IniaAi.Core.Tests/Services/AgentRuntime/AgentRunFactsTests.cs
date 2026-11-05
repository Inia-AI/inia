using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using System.Text.Json.Nodes;

namespace IniaAi.Core.Tests.Services.AgentRuntime;

public class AgentRunFactsTests
{
    private const string EXECUTION_ID = "exec-1";
    private const string PROMPT = "Extract the figures";
    private const string AUTHORED_SYSTEM_PROMPT = "You extract balance sheets.";

    private static AgentRunStartRequest Request(
        IReadOnlyList<AgentRunDocument>? documents = null,
        IReadOnlyList<AgentRunSkillFile>? skills = null,
        JsonObject? outputSchema = null,
        string? systemPrompt = null)
    {
        return new AgentRunStartRequest(EXECUTION_ID, PROMPT, RunToken: null)
        {
            Documents = documents ?? [],
            SkillFiles = skills ?? [],
            OutputSchema = outputSchema,
            SystemPrompt = systemPrompt,
        };
    }

    private static AgentRunDocument Document(string name, string? documentClass = null)
    {
        return new AgentRunDocument(name, new byte[] { 1 }, "application/pdf", documentClass);
    }

    [Fact]
    public void ForRequest_EveryItem_RendersTheWholeSectionInOrder()
    {
        AgentRunStartRequest request = Request(
            [Document("Annual Report 2025.pdf", "Annual Report")],
            [new AgentRunSkillFile("balance-sheet", "content", "Standardizes a balance sheet.")
            {
                Assets = [new AgentRunSkillAsset("check.py", "print(1)")],
            }],
            new JsonObject { ["type"] = "object" });

        Assert.Equal(
            """
            <task_setup>
            Added automatically by the system.
            - Skills: skills:balance-sheet
            - Skill files: $SKILL_ASSETS_DIR/balance-sheet/check.py
            - The final answer is checked against an enforced output schema.
            </task_setup>
            """.ReplaceLineEndings("\n"),
            AgentRunFacts.ForRequest(request));
    }

    [Fact]
    public void ForRequest_StagedDocuments_AreLeftToTheRuntimesUserTurn()
    {
        Assert.Null(AgentRunFacts.ForRequest(Request([Document("Annual Report 2025.pdf", "Annual Report")])));
    }

    [Fact]
    public void ForRequest_NoAssetsAndNoSchema_LeavesThoseItemsOut()
    {
        string? facts = AgentRunFacts.ForRequest(Request(
            [Document("a.pdf")], [new AgentRunSkillFile("work", "c", "Does work.")]));

        Assert.NotNull(facts);
        Assert.DoesNotContain(AgentRunFacts.SKILL_FILES_LABEL, facts, StringComparison.Ordinal);
        Assert.DoesNotContain(AgentRunFacts.OUTPUT_SCHEMA_LINE, facts, StringComparison.Ordinal);
    }

    [Fact]
    public void ForRequest_Skill_NamesOnlyTheInvocationWithoutItsDescription()
    {
        string? facts = AgentRunFacts.ForRequest(Request(skills: [new AgentRunSkillFile("work", "c", "Does work.")]));

        Assert.NotNull(facts);
        Assert.Contains("\n- Skills: skills:work\n", facts, StringComparison.Ordinal);
        Assert.DoesNotContain("Does work.", facts, StringComparison.Ordinal);
    }

    [Fact]
    public void ForRequest_NothingToState_IsNull()
    {
        Assert.Null(AgentRunFacts.ForRequest(Request()));
    }

    [Fact]
    public void WithRunFacts_PutsTheTaskSetupBeforeTheAuthoredPromptWrappedInInstructions()
    {
        AgentRunStartRequest request = AgentRunFacts.WithRunFacts(Request(
            skills: [new AgentRunSkillFile("work", "c", "Does work.")], systemPrompt: AUTHORED_SYSTEM_PROMPT));

        Assert.Equal(
            """
            <task_setup>
            Added automatically by the system.
            - Skills: skills:work
            </task_setup>

            <instructions>
            You extract balance sheets.
            </instructions>
            """.ReplaceLineEndings("\n"),
            request.SystemPrompt);
    }

    [Fact]
    public void WithRunFacts_MarkdownHeadingsInTheAuthoredPrompt_StayUnchangedInsideInstructions()
    {
        const string AUTHORED = "# Role\nYou extract.\n\n## Output\nJSON only.";

        AgentRunStartRequest request = AgentRunFacts.WithRunFacts(Request(systemPrompt: AUTHORED));

        Assert.Equal("<instructions>\n" + AUTHORED + "\n</instructions>", request.SystemPrompt);
    }

    [Fact]
    public void ForRequest_SeveralSkills_ListsThemOnOneLine()
    {
        string? facts = AgentRunFacts.ForRequest(Request(skills:
        [
            new AgentRunSkillFile("balance-sheet", "c", "d"),
            new AgentRunSkillFile("verify-bs", "c", "d"),
        ]));

        Assert.NotNull(facts);
        Assert.Contains("\n- Skills: skills:balance-sheet, skills:verify-bs\n", facts, StringComparison.Ordinal);
    }

    [Fact]
    public void ForRequest_ManySkillFiles_ListsThemAsSubBullets()
    {
        string? facts = AgentRunFacts.ForRequest(Request(skills:
        [
            new AgentRunSkillFile("bs", "c", "d")
            {
                Assets =
                [
                    new AgentRunSkillAsset("a.py", "1"),
                    new AgentRunSkillAsset("b.py", "1"),
                    new AgentRunSkillAsset("c.py", "1"),
                    new AgentRunSkillAsset("d.py", "1"),
                ],
            },
        ]));

        Assert.Equal(
            """
            <task_setup>
            Added automatically by the system.
            - Skills: skills:bs
            - Skill files:
              - $SKILL_ASSETS_DIR/bs/a.py
              - $SKILL_ASSETS_DIR/bs/b.py
              - $SKILL_ASSETS_DIR/bs/c.py
              - $SKILL_ASSETS_DIR/bs/d.py
            </task_setup>
            """.ReplaceLineEndings("\n"),
            facts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithRunFacts_BlankAuthoredPrompt_SendsTheSectionAlone(string? authored)
    {
        AgentRunStartRequest request = AgentRunFacts.WithRunFacts(Request(
            skills: [new AgentRunSkillFile("work", "c", "Does work.")], systemPrompt: authored));

        Assert.Equal(
            "<task_setup>\nAdded automatically by the system.\n- Skills: skills:work\n</task_setup>",
            request.SystemPrompt);
    }

    [Fact]
    public void WithRunFacts_NothingToStateAndNoAuthoredPrompt_KeepsTheSystemPromptNull()
    {
        Assert.Null(AgentRunFacts.WithRunFacts(Request()).SystemPrompt);
    }

    [Fact]
    public void WithRunFacts_NothingToState_SendsTheInstructionsAlone()
    {
        Assert.Equal(
            "<instructions>\n" + AUTHORED_SYSTEM_PROMPT + "\n</instructions>",
            AgentRunFacts.WithRunFacts(Request(systemPrompt: AUTHORED_SYSTEM_PROMPT)).SystemPrompt);
    }

    [Fact]
    public void Preview_MatchesTheRunSectionForTheSameSkills()
    {
        List<AgentWorkerSkill> skills =
        [
            new AgentWorkerSkill
            {
                Name = "balance-sheet",
                Description = "Standardizes a balance sheet.",
                Content = "c",
                Assets = [new AgentWorkerSkillAsset { Name = "check.py", Content = "print(1)" }],
            },
        ];
        AgentRunStartRequest request = Request(
            [Document("a.pdf")],
            [new AgentRunSkillFile("balance-sheet", "c", "Standardizes a balance sheet.")
            {
                Assets = [new AgentRunSkillAsset("check.py", "print(1)")],
            }]);

        Assert.Equal(AgentRunFacts.ForRequest(request), AgentRunFacts.Preview(skills, hasOutputSchema: false));
    }

    [Fact]
    public void Preview_UnnamedSkill_IsLeftOut_AndTheSchemaLineShowsWhenSet()
    {
        string? preview = AgentRunFacts.Preview([new AgentWorkerSkill()], hasOutputSchema: true);

        Assert.Equal(
            "<task_setup>\nAdded automatically by the system.\n"
                + "- The final answer is checked against an enforced output schema.\n</task_setup>",
            preview);
    }
}
