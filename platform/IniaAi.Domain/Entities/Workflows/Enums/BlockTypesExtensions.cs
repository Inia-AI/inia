namespace IniaAi.Domain.Entities.Workflows.Enums;

public static class BlockTypesExtensions
{
    /// <summary>
    /// The agent-family block types: the ones whose blocks declare agent-runtime runs and are
    /// gated on the runtime being enabled. Exposed as a set so EF queries can translate a
    /// family filter (<c>AgentFamily.Contains(block.Type)</c>) that an extension-method call
    /// cannot express.
    /// </summary>
    public static readonly BlockTypes[] AgentFamily = [BlockTypes.Agent, BlockTypes.Verification];

    public static bool IsAgentFamily(this BlockTypes type)
    {
        return AgentFamily.Contains(type);
    }
}
