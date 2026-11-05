using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using LinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;
using TransferMethod = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferCorrelationMethod;
using WireKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.UnitTests.Common.SharedComponents;

public class SharedComponentTransferCompareTests
{
    [Fact]
    public void Localize_SectionRefEntry_SubstitutesCorrelatedLocalId()
    {
        using var harness = new SharedComponentsTestHarness();
        TransferComponentDto component = BuildForeignSectionComponent(harness);
        var correlations = new Dictionary<string, SharedComponentCorrelator.Correlation>(StringComparer.Ordinal)
        {
            ["foreign-block"] = new("local-block", TransferMethod.HashTrail),
        };

        string localized = SharedComponentTransferCompare.LocalizeHeadDefinition(component, correlations);

        Section? parsed = Section.FromJson(localized);
        SectionEntry refEntry = Assert.Single(parsed!.Entries!, e => e.Kind == SectionEntryKind.ComponentRef);
        Assert.Equal("local-block", refEntry.SharedComponentId);
    }

    [Fact]
    public void Localize_KeepsADateInTheDefinitionAsWritten()
    {
        const string definition = """{"Entries":[{"SharedComponentId":"foreign-block"}],"Blocks":[{"Properties":{"StartPublishedDate":"2024-03-12T00:00:00.000Z"}}]}""";
        var component = new TransferComponentDto(
            "s", WireKind.Section, "S", null, null, "h", definition, ["h"]);
        var correlations = new Dictionary<string, SharedComponentCorrelator.Correlation>(StringComparer.Ordinal)
        {
            ["foreign-block"] = new("local-block", TransferMethod.HashTrail),
        };

        string localized = SharedComponentTransferCompare.LocalizeHeadDefinition(component, correlations);

        Assert.Contains("\"local-block\"", localized);
        Assert.Contains("\"2024-03-12T00:00:00.000Z\"", localized);
    }

    [Fact]
    public void Localize_BlockComponent_ReturnsUnchanged()
    {
        const string blockJson = """{"Title":"x"}""";
        var component = new TransferComponentDto(
            "b", WireKind.Block, "B", null, null, "h", blockJson, ["h"]);

        string result = SharedComponentTransferCompare.LocalizeHeadDefinition(
            component, new Dictionary<string, SharedComponentCorrelator.Correlation>(StringComparer.Ordinal));

        Assert.Equal(blockJson, result);
    }

    [Fact]
    public void Localize_UncorrelatedRefTarget_LeavesIdUnchanged()
    {
        using var harness = new SharedComponentsTestHarness();
        TransferComponentDto component = BuildForeignSectionComponent(harness);
        var correlations = new Dictionary<string, SharedComponentCorrelator.Correlation>(StringComparer.Ordinal)
        {
            ["foreign-block"] = new(null, TransferMethod.Create),
        };

        string localized = SharedComponentTransferCompare.LocalizeHeadDefinition(component, correlations);

        Section? parsed = Section.FromJson(localized);
        SectionEntry refEntry = Assert.Single(parsed!.Entries!, e => e.Kind == SectionEntryKind.ComponentRef);
        Assert.Equal("foreign-block", refEntry.SharedComponentId);
    }

    private static TransferComponentDto BuildForeignSectionComponent(SharedComponentsTestHarness harness)
    {
        var section = new Section("Market", new Workflow("S")) { Title = "Market" };
        var block = new Block(BlockTypes.AiQuery, section, "r")
        {
            Name = "A",
            Properties = new AiQueryProperties { UserPromptTemplate = "a" },
        };
        block.SharedComponentId = "foreign-block";
        block.SharedComponentLinkState = LinkState.Linked;
        block.SharedComponentVersionHash = new string('a', 64);
        block.SharedComponentBindings = new Dictionary<string, string>();
        (string defJson, _) = harness.Definition(section);

        return new TransferComponentDto(
            "foreign-sec", WireKind.Section, "Market", null, null, "h", defJson, ["h"]);
    }
}
