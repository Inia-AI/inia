using IniaAi.Application.Common.Workflows;
using System.Reflection;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class SharedComponentErrorCodesTests
{
    [Fact]
    public void Registry_ContainsExactlyTheContractedCodes()
    {
        string[] expected =
        [
            "SharedComponent.InvalidLinkShape",
            "SharedComponent.UnknownComponent",
            "SharedComponent.KindMismatch",
            "SharedComponent.AlreadyShared",
            "SharedComponent.Retired",
            "SharedComponent.ContentMismatch",
            "SharedComponent.PayloadAbsent",
            "SharedComponent.TargetNotFound",
            "SharedComponent.InvalidTargetLocator",
            "SharedComponent.CorruptDefinition",
            "SharedComponent.NoVersions",
            "SharedComponent.UnknownVersion",
            "SharedComponent.HeadMoved",
            "SharedComponent.ConcurrentAppend",
            "SharedComponent.ConflictingDecisions",
            "SharedComponent.OrphansNeedAcknowledgement",
            "SharedComponent.OverwritesLocalEntries",
            "SharedComponent.UnknownCategory",
            "SharedComponent.CategoryInUse",
            "SharedComponent.DivergedLineage",
            "SharedComponent.BundleFormatUnsupported",
            "SharedComponent.BundleTypeNameMissing",
            "SharedComponent.AgentRuntimeDisabled",
            "SharedComponent.AgentExpansionScope",
            "SharedComponent.AgentBlockInvalid",
        ];

        List<string> actual = [.. typeof(SharedComponentErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)];

        Assert.Equal([.. expected.Order()], [.. actual.Order()]);
    }
}
