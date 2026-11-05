using IniaAi.Domain.Entities.Workflows.Blocks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// Serializes the run evidence the persistence contract hides. <see cref="Block.AgentRunTraceFiles"/>
/// and <see cref="Block.CitationTrace"/> are <c>[JsonIgnore]</c> so persisted workflow JSON never
/// embeds them — they live as UserData rows — and so is <see cref="Workflow.CitationTracePages"/>,
/// but a downloaded snapshot is the run's evidence record and carries them. A workflow without
/// evidence stays byte-identical to the default export: each property serializes only when it
/// holds something.
/// </summary>
internal sealed class RunTraceExportContractResolver : DefaultContractResolver
{
    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        JsonProperty property = base.CreateProperty(member, memberSerialization);
        if (member.DeclaringType == typeof(Block) && member.Name == nameof(Block.AgentRunTraceFiles))
        {
            property.Ignored = false;
            property.ShouldSerialize = static instance => ((Block)instance).AgentRunTraceFiles.Count > 0;
        }
        else if (member.DeclaringType == typeof(Block) && member.Name == nameof(Block.CitationTrace))
        {
            property.Ignored = false;
            property.ShouldSerialize = static instance => ((Block)instance).CitationTrace is not null;
        }
        else if (member.DeclaringType == typeof(Workflow) && member.Name == nameof(Workflow.CitationTracePages))
        {
            property.Ignored = false;
            property.ShouldSerialize = static instance => ((Workflow)instance).CitationTracePages.Count > 0;
        }

        return property;
    }
}
