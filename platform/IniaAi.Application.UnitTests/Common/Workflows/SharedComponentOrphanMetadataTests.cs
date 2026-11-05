using ErrorOr;
using IniaAi.Application.Common.Workflows;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class SharedComponentOrphanMetadataTests
{
    private static readonly SharedComponentOverrideImpact _orphan = new(
        "org-client", "WT-A", "block-1", "source", "director_name", false,
        SharedComponentOverrideImpactKind.Deleted);

    [Fact]
    public void ReadOrphanedOverrides_RoundTripsWhatTheRefusalWrote()
    {
        Error refusal = Error.Conflict(
            SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT,
            "Applying this decision set would orphan 1 signature override(s).",
            new Dictionary<string, object>
            {
                [SharedComponentOrphanMetadata.ORPHANED_OVERRIDES_KEY] =
                    new List<SharedComponentOverrideImpact> { _orphan },
            });

        Assert.Equal(_orphan, Assert.Single(SharedComponentOrphanMetadata.ReadOrphanedOverrides(refusal)));
    }

    [Fact]
    public void ReadOrphanedOverrides_WithoutMetadata_ReturnsEmpty()
    {
        Assert.Empty(SharedComponentOrphanMetadata.ReadOrphanedOverrides(
            SharedComponentErrorCodes.UnknownComponent("component-1")));
    }

    [Fact]
    public void ReadOrphanedOverrides_WhenTheEntryIsMissingOrNotAnImpactList_ReturnsEmpty()
    {
        Error unrelatedEntry = Error.Conflict(
            SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT,
            "orphans",
            new Dictionary<string, object> { ["sharedComponentId"] = "component-1" });
        Error wrongType = Error.Conflict(
            SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT,
            "orphans",
            new Dictionary<string, object>
            {
                [SharedComponentOrphanMetadata.ORPHANED_OVERRIDES_KEY] = "director_name",
            });

        Assert.Empty(SharedComponentOrphanMetadata.ReadOrphanedOverrides(unrelatedEntry));
        Assert.Empty(SharedComponentOrphanMetadata.ReadOrphanedOverrides(wrongType));
    }
}
