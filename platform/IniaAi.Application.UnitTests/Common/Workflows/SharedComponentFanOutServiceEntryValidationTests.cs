using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class SharedComponentFanOutServiceEntryValidationTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-apply";
    private const string SECTION_COMPONENT_ID = "component-sec";
    private const string TARGET_ID = "component-kyc";

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly SharedComponentFanOutService _service;

    public SharedComponentFanOutServiceEntryValidationTests()
    {
        _service = _harness.CreateFanOutService(_harness.CreatePipeline());
    }

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Append_EntryTagDuplicatedAcrossEntries_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        await SeedBlockTargetAsync();
        Section submitted = BuildSection(("intro", "intro prompt"));
        submitted.Entries = [Inline("intro"), Ref("intro", TARGET_ID)];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.INVALID_LINK_SHAPE);
    }

    [Fact]
    public async Task Append_InlineEntryWithoutBlock_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        Section submitted = BuildSection(("intro", "intro prompt"));
        submitted.Entries = [Inline("intro"), Inline("ghost")];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.INVALID_LINK_SHAPE);
    }

    [Fact]
    public async Task Append_BlockWithoutInlineEntry_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        Section submitted = BuildSection(("intro", "intro prompt"), ("extra", "extra prompt"));
        submitted.Entries = [Inline("intro")];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.INVALID_LINK_SHAPE);
    }

    [Fact]
    public async Task Append_RefEntryMissingComponentId_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        Section submitted = BuildSection(("intro", "intro prompt"));
        submitted.Entries =
        [
            Inline("intro"),
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef },
        ];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.INVALID_LINK_SHAPE);
    }

    [Fact]
    public async Task Append_InlineEntryCarryingComponentId_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        await SeedBlockTargetAsync();
        Section submitted = BuildSection(("intro", "intro prompt"));
        submitted.Entries =
        [
            new SectionEntry
            {
                SlotTag = "intro",
                Kind = SectionEntryKind.Inline,
                SharedComponentId = TARGET_ID,
            },
        ];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.INVALID_LINK_SHAPE);
    }

    [Fact]
    public async Task Append_InlineEntryCarryingBindings_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        Section submitted = BuildSection(("intro", "intro prompt"));
        submitted.Entries =
        [
            new SectionEntry
            {
                SlotTag = "intro",
                Kind = SectionEntryKind.Inline,
                Bindings = new Dictionary<string, string> { ["a"] = "b" },
            },
        ];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.INVALID_LINK_SHAPE);
    }

    [Fact]
    public async Task Append_EntriesWithLinkedInnerBlock_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        await SeedBlockTargetAsync();
        Section submitted = BuildSection(("intro", "intro prompt"));
        Block inner = submitted.Blocks.Single();
        inner.SharedComponentId = TARGET_ID;
        inner.SharedComponentLinkState = SharedComponentLinkState.Linked;
        submitted.Entries = [Inline("intro")];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.INVALID_LINK_SHAPE);
    }

    [Fact]
    public async Task Append_AllRefDefinition_ZeroBlocks_Appends()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        await SeedBlockTargetAsync();
        Section submitted = BuildSection();
        submitted.Entries = [Ref("kyc", TARGET_ID)];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        Assert.False(result.IsError);
        Assert.False(result.Value.WasDeduped);
        SharedComponentVersion v2 = await _harness.Context.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(
                v => v.SharedComponentId == SECTION_COMPONENT_ID && v.SequenceNumber == 2,
                TestContext.Current.CancellationToken);
        Assert.Contains($"\"{nameof(SectionEntry.SharedComponentId)}\":\"{TARGET_ID}\"", v2.ContentJson);
        Assert.DoesNotContain($"\"{nameof(Section.Blocks)}\"", v2.ContentJson);
    }

    [Fact]
    public async Task Append_MixedInlineAndRef_AppendsWithEntriesPreserved()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        await SeedBlockTargetAsync();
        Section submitted = BuildSection(("intro", "intro prompt"));
        submitted.Entries = [Inline("intro"), Ref("kyc", TARGET_ID)];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        Assert.False(result.IsError);
        SharedComponentVersion v2 = await _harness.Context.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(
                v => v.SharedComponentId == SECTION_COMPONENT_ID && v.SequenceNumber == 2,
                TestContext.Current.CancellationToken);
        Assert.Contains($"\"{nameof(Section.Entries)}\"", v2.ContentJson);
        Assert.Contains($"\"{nameof(SectionEntry.SharedComponentId)}\":\"{TARGET_ID}\"", v2.ContentJson);
        Assert.Contains("intro prompt", v2.ContentJson);
    }

    [Fact]
    public async Task Append_RefTargetUnknown_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        Section submitted = BuildSection(("intro", "intro prompt"));
        submitted.Entries = [Inline("intro"), Ref("kyc", "component-missing")];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.UNKNOWN_COMPONENT);
    }

    [Fact]
    public async Task Append_RefTargetSectionKind_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Other Section", id: TARGET_ID);
        (string json, string hash) = _harness.Definition(BuildSection(("t", "p")));
        _ = await _harness.AddVersionAsync(TARGET_ID, 1, hash, json);
        Section submitted = BuildSection(("intro", "intro prompt"));
        submitted.Entries = [Inline("intro"), Ref("kyc", TARGET_ID)];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.KIND_MISMATCH);
    }

    [Fact]
    public async Task Append_RefTargetRetired_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        await SeedBlockTargetAsync(state: SharedComponentState.Retired);
        Section submitted = BuildSection(("intro", "intro prompt"));
        submitted.Entries = [Inline("intro"), Ref("kyc", TARGET_ID)];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.RETIRED);
        Assert.Contains("unretire", result.FirstError.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Append_RefTargetPayloadAbsentHead_Rejected()
    {
        SharedComponentVersion head = await SeedSectionComponentAsync();
        await SeedBlockTargetAsync(payloadAbsent: true);
        Section submitted = BuildSection(("intro", "intro prompt"));
        submitted.Entries = [Inline("intro"), Ref("kyc", TARGET_ID)];

        ErrorOr<SharedComponentAppendResult> result = await AppendAsync(submitted, head);

        AssertValidationWithCode(result, SharedComponentErrorCodes.PAYLOAD_ABSENT);
    }

    private Task<ErrorOr<SharedComponentAppendResult>> AppendAsync(
        Section submitted,
        SharedComponentVersion head)
    {
        return _service.AppendAndFanOutAsync(
            ORG,
            USER,
            SECTION_COMPONENT_ID,
            submitted.ToJson(pretty: false),
            head.Id,
            TestContext.Current.CancellationToken);
    }

    private async Task<SharedComponentVersion> SeedSectionComponentAsync()
    {
        (string json, string hash) = _harness.Definition(BuildSection(("intro", "intro prompt")));
        _ = await _harness.AddComponentAsync(
            SharedComponentKind.Section, "Client Onboarding", id: SECTION_COMPONENT_ID);

        return await _harness.AddVersionAsync(SECTION_COMPONENT_ID, 1, hash, json);
    }

    private async Task SeedBlockTargetAsync(
        SharedComponentState state = SharedComponentState.Active,
        bool payloadAbsent = false)
    {
        _ = await _harness.AddComponentAsync(
            SharedComponentKind.Block, "KYC Check", state: state, id: TARGET_ID);
        (string json, string hash) = _harness.Definition(BuildTargetBlock("target prompt"));
        _ = await _harness.AddVersionAsync(
            TARGET_ID,
            1,
            hash,
            payloadAbsent ? null : json,
            payloadAbsentAt: payloadAbsent ? DateTime.UtcNow : null);
    }

    private static void AssertValidationWithCode(
        ErrorOr<SharedComponentAppendResult> result,
        string code)
    {
        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(code, result.FirstError.Description);
    }

    private static SectionEntry Inline(string tag)
    {
        return new SectionEntry { SlotTag = tag, Kind = SectionEntryKind.Inline };
    }

    private static SectionEntry Ref(string tag, string componentId)
    {
        return new SectionEntry
        {
            SlotTag = tag,
            Kind = SectionEntryKind.ComponentRef,
            SharedComponentId = componentId,
        };
    }

    private static Section BuildSection(params (string Tag, string Prompt)[] blocks)
    {
        var workflow = new Workflow("scratch");
        var section = new Section("shared section", workflow) { Title = "Client Onboarding" };
        foreach ((string tag, string prompt) in blocks)
        {
            _ = new Block(BlockTypes.AiQuery, section, tag)
            {
                Name = $"{tag} name",
                Properties = new AiQueryProperties { UserPromptTemplate = prompt },
            };
        }

        return section;
    }

    private static Block BuildTargetBlock(string prompt)
    {
        var workflow = new Workflow("scratch");
        var section = new Section("scratch", workflow);

        return new Block(BlockTypes.AiQuery, section, "slot")
        {
            Name = "KYC Check",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }
}
