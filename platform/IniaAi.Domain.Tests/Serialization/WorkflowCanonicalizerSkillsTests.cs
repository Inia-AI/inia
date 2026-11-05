using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;

namespace IniaAi.Domain.Tests.Serialization;

public class WorkflowCanonicalizerSkillsTests
{
    private readonly WorkflowCanonicalizer _canonicalizer = new();

    private static Workflow WorkflowWithSkills(params string[] namesInOrder)
    {
        var workflow = new Workflow("hashing") { Title = "hashing", WorkflowTypeId = "hashing" };
        var section = new Section("body", workflow);
        var block = new Block(BlockTypes.Agent, section, "agent_block");
        var props = (AgentProperties)block.Properties;
        foreach (string name in namesInOrder)
        {
            props.WorkerSkills.Add(new AgentWorkerSkill
            {
                Name = name,
                Description = $"Handles {name}.",
                Content = $"Body of {name}.",
            });
        }

        return workflow;
    }

    [Fact]
    public void SkillOrder_DoesNotChangeTheContentHash()
    {
        string forward = _canonicalizer.ComputeContentHash(WorkflowWithSkills("alpha", "beta"));
        string reversed = _canonicalizer.ComputeContentHash(WorkflowWithSkills("beta", "alpha"));

        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void DifferentSkillSets_StillHashDifferently()
    {
        string twoSkills = _canonicalizer.ComputeContentHash(WorkflowWithSkills("alpha", "beta"));
        string oneSkill = _canonicalizer.ComputeContentHash(WorkflowWithSkills("alpha"));

        Assert.NotEqual(twoSkills, oneSkill);
    }

    [Fact]
    public void EmptyAssets_DoNotChangeThePreAssetContentHash()
    {
        string withoutAssets = _canonicalizer.ComputeContentHash(WorkflowWithSkills("alpha"));

        Workflow workflow = WorkflowWithSkills("alpha");
        AgentProps(workflow).WorkerSkills[0].Assets = [];
        string withEmptyAssets = _canonicalizer.ComputeContentHash(workflow);

        Assert.Equal(withoutAssets, withEmptyAssets);
    }

    [Fact]
    public void AddingAnAsset_ChangesTheContentHash()
    {
        string withoutAssets = _canonicalizer.ComputeContentHash(WorkflowWithSkills("alpha"));

        Workflow workflow = WorkflowWithSkills("alpha");
        AgentProps(workflow).WorkerSkills[0].Assets =
            [new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)" }];
        string withAsset = _canonicalizer.ComputeContentHash(workflow);

        Assert.NotEqual(withoutAssets, withAsset);
    }

    [Fact]
    public void AnUnflaggedAsset_KeepsTheContentHashItHadBeforeTheFlagExisted()
    {
        Workflow legacy = WorkflowWithSkills("alpha");
        AgentProps(legacy).WorkerSkills[0].Assets =
            [new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)" }];
        string legacyHash = _canonicalizer.ComputeContentHash(legacy);

        Workflow explicitFalse = WorkflowWithSkills("alpha");
        AgentProps(explicitFalse).WorkerSkills[0].Assets =
            [new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)", FillTags = false }];

        Assert.Equal(legacyHash, _canonicalizer.ComputeContentHash(explicitFalse));
        Assert.DoesNotContain("FillTags", _canonicalizer.ToCanonicalJson(explicitFalse), StringComparison.Ordinal);
    }

    [Fact]
    public void FlaggingAnAsset_ChangesTheContentHash()
    {
        Workflow unflagged = WorkflowWithSkills("alpha");
        AgentProps(unflagged).WorkerSkills[0].Assets =
            [new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)" }];

        Workflow flagged = WorkflowWithSkills("alpha");
        AgentProps(flagged).WorkerSkills[0].Assets =
            [new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)", FillTags = true }];

        Assert.NotEqual(_canonicalizer.ComputeContentHash(unflagged), _canonicalizer.ComputeContentHash(flagged));
    }

    private static AgentProperties AgentProps(Workflow workflow)
    {
        return (AgentProperties)workflow.GetAllBlocks().Single().Properties;
    }
}
