using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace IniaAi.Domain.Serialization;

/// <summary>
/// Suppresses members that make a workflow's JSON unstable across saves and across
/// deployments — every entity <c>Id</c>, all audit timestamps, any execution-side state,
/// the deployment-assigned <c>WorkflowTypeId</c>, and transport metadata such as the
/// <c>.workflow</c> file's <c>FormatVersion</c> stamp — so two semantically identical
/// templates serialize to the same bytes. Two profiles exist: the HASH profile additionally
/// suppresses the instance-identity members on Block and Section — the shared-component link
/// fields and the stable ids — because neither is semantic content: linking must not restamp a
/// template's ContentHash, and backfilling a stable id onto an existing template must not mark
/// every regression baseline stale. The PAYLOAD profile keeps both, because canonical JSON is
/// also the stored version payload and the served template representation — dropping them there
/// would strip every link and re-mint every stable id on version-restore, conflict-recovery,
/// and API GET→PUT round-trips.
/// </summary>
internal sealed class CanonicalContractResolver : DefaultContractResolver
{
    private const string ID = "Id";
    private const string EXECUTION_STATE = "ExecutionState";
    private const string BLOCK_RESPONSE = "BlockResponse";
    private const string WORKFLOW_TYPE_ID = nameof(Workflow.WorkflowTypeId);
    private const string STABLE_BLOCK_ID = nameof(Block.StableBlockId);
    private const string STABLE_SECTION_ID = nameof(Section.StableSectionId);
    private const string SHARED_COMPONENT_SLOT_STATES = nameof(Section.SharedComponentSlotStates);
    private const string FORMAT_VERSION = nameof(Workflow.FormatVersion);

    private static readonly HashSet<string> _sharedComponentLinkMembers =
    [
        nameof(Block.SharedComponentId),
        nameof(Block.SharedComponentLinkState),
        nameof(Block.SharedComponentVersionHash),
        nameof(Block.SharedComponentBindings),
    ];

    private readonly bool _payloadProfile;

    public CanonicalContractResolver(bool payloadProfile)
    {
        _payloadProfile = payloadProfile;
    }

    private static readonly HashSet<string> _auditMembers =
    [
        ID,
        nameof(IAuditableEntity.CreatedAt),
        nameof(IAuditableEntity.UpdatedAt),
        nameof(IAuditableEntity.UpdatedByUserId),
        nameof(IAuditableEntity.IsDeleted),
        nameof(IAuditableEntity.DeletedAt),
        nameof(IAuditableEntity.DeletedByUserId),
    ];

    private static readonly HashSet<string> _executionMembers =
    [
        EXECUTION_STATE,
        BLOCK_RESPONSE,
    ];

    private static readonly HashSet<string> _transportMembers =
    [
        FORMAT_VERSION,
    ];

    protected override JsonProperty CreateProperty(
        MemberInfo member,
        MemberSerialization memberSerialization)
    {
        JsonProperty property = base.CreateProperty(member, memberSerialization);

        if (ShouldSuppress(member))
        {
            property.Ignored = true;
        }

        return property;
    }

    private bool ShouldSuppress(MemberInfo member)
    {
        if (_executionMembers.Contains(member.Name) || _transportMembers.Contains(member.Name))
        {
            return true;
        }

        if (!_payloadProfile && IsInstanceIdentityMember(member))
        {
            return true;
        }

        if (member.Name == WORKFLOW_TYPE_ID && member.DeclaringType == typeof(Workflow))
        {
            return true;
        }

        return _auditMembers.Contains(member.Name)
            && member.DeclaringType is not null
            && typeof(IAuditableEntity).IsAssignableFrom(member.DeclaringType);
    }

    private static bool IsInstanceIdentityMember(MemberInfo member)
    {
        if (member.DeclaringType == typeof(Block))
        {
            return member.Name == STABLE_BLOCK_ID || _sharedComponentLinkMembers.Contains(member.Name);
        }

        if (member.DeclaringType == typeof(Section))
        {
            return member.Name == STABLE_SECTION_ID
                || member.Name == SHARED_COMPONENT_SLOT_STATES
                || _sharedComponentLinkMembers.Contains(member.Name);
        }

        return false;
    }
}
