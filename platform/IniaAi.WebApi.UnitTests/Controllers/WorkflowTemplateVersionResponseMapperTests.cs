using IniaAi.Application.Queries.Workflows.Versions;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Serialization;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using IniaAi.WebApi.Controllers.v1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BlockTypes = IniaAi.Domain.Entities.Workflows.Enums.BlockTypes;

namespace IniaAi.WebApi.UnitTests.Controllers;

public class WorkflowTemplateVersionResponseMapperTests
{
    private const string TYPE = "WT-Report";
    private const string USER = "user-1";
    private const string OLD_PROMPT = "Summarise the filing.";
    private const string NEW_PROMPT = "Summarise the filing in three bullet points.";

    private readonly WorkflowCanonicalizer _canonicalizer = new();
    private readonly WorkflowDiffService _diffService = new();
    private readonly WorkflowNarrativeGenerator _narrativeGenerator = new();

    [Fact]
    public void ToResponse_MapsHistoryNewestFirst_WithStoredFlagAndCursorOnlyWhenMore()
    {
        DateTime newerAt = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        DateTime olderAt = newerAt.AddMinutes(-7).AddTicks(1234567);
        var page = new WorkflowHistoryPageDto(
            [
                new WorkflowVersionSummaryDto("v2", newerAt, USER, "Ada", "bbbbbbb", new string('b', 64), "Edited prompt", null)
                {
                    ParentVersionId = "v1",
                },
                new WorkflowVersionSummaryDto("v1", olderAt, USER, "Ada", "aaaaaaa", new string('a', 64), "First saved version", olderAt.AddDays(90)),
            ],
            HasMore: true,
            TotalCount: 5);

        WorkflowTemplateVersionsResponse response = WorkflowTemplateVersionResponseMapper.ToResponse(TYPE, page);

        Assert.Equal(TYPE, response.WorkflowTypeId);
        Assert.Equal(5, response.TotalCount);
        Assert.True(response.HasMore);
        Assert.Equal(["v2", "v1"], response.Versions.Select(version => version.VersionId));

        WorkflowTemplateVersionDto head = response.Versions[0];
        Assert.Equal("v1", head.ParentVersionId);
        Assert.Equal("Ada", head.CreatedByName);
        Assert.Equal("Edited prompt", head.Narrative);
        Assert.True(head.PayloadStored);
        Assert.Null(head.PayloadTrimmedAtUtc);

        WorkflowTemplateVersionDto trimmed = response.Versions[1];
        Assert.False(trimmed.PayloadStored);
        Assert.Equal(olderAt.AddDays(90), trimmed.PayloadTrimmedAtUtc);

        Assert.True(WorkflowVersionCursor.TryParse(response.NextCursor, out WorkflowVersionCursor cursor));
        Assert.Equal(olderAt, cursor.CreatedAtUtc);
        Assert.Equal("v1", cursor.VersionId);

        WorkflowTemplateVersionsResponse lastPage = WorkflowTemplateVersionResponseMapper.ToResponse(
            TYPE, page with { HasMore = false });
        Assert.Null(lastPage.NextCursor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nounderscore")]
    [InlineData("_v1")]
    [InlineData("123_")]
    [InlineData("-5_v1")]
    [InlineData("12x_v1")]
    [InlineData("99999999999999999999999_v1")]
    public void Cursor_TryParse_RejectsMalformedTokens(string? token)
    {
        Assert.False(WorkflowVersionCursor.TryParse(token, out _));
    }

    [Fact]
    public void ToChangesResponse_CarriesBlockIdentityAndFullBeforeAfter()
    {
        Workflow before = Report(OLD_PROMPT, out string stableBlockId, stableBlockId: null);
        Workflow after = Report(NEW_PROMPT, out _, stableBlockId);
        WorkflowDiff diff = _diffService.Compare(_canonicalizer.ToCanonicalJson(before), _canonicalizer.ToCanonicalJson(after));
        var changes = new WorkflowVersionChangesResult(WorkflowVersionChangeStatus.Diffed, diff, "Edited the prompt of Summary")
        {
            VersionId = "v2",
            ParentVersionId = "v1",
        };

        WorkflowTemplateVersionDiffResponse response =
            WorkflowTemplateVersionResponseMapper.ToChangesResponse(TYPE, changes, _narrativeGenerator);

        Assert.Equal("v1", response.FromVersionId);
        Assert.Equal("v2", response.ToVersionId);
        Assert.False(response.IsFirstVersion);
        Assert.Equal("Edited the prompt of Summary", response.StoredNarrative);
        Assert.NotEmpty(response.NarrativeParts);

        Assert.NotNull(response.Diff);
        Assert.False(response.Diff.IsEmpty);
        WorkflowTemplateBlockDiffDto block = Assert.Single(response.Diff.Blocks);
        Assert.Equal("summary", block.ReplacementTag);
        Assert.Equal("Summary", block.Name);
        Assert.Equal("Analysis", block.SectionTitle);
        Assert.Equal(stableBlockId, block.StableBlockId);
        Assert.Equal(nameof(BlockTypes.AiQuery), block.BlockType);
        Assert.Equal(WorkflowTemplateChangeKind.Modified, block.ChangeKind);

        WorkflowTemplateFieldDiffDto field = Assert.Single(block.FieldDiffs);
        Assert.Equal("Properties.UserPromptTemplate", field.FieldName);
        Assert.Equal(OLD_PROMPT, field.Before);
        Assert.Equal(NEW_PROMPT, field.After);
    }

    [Fact]
    public void ToChangesResponse_FirstVersion_HasNoDiffAndNoFromVersion()
    {
        var changes = new WorkflowVersionChangesResult(
            WorkflowVersionChangeStatus.FirstVersion, Diff: null, WorkflowNarrativeGenerator.FirstVersionSummary)
        {
            VersionId = "v1",
        };

        WorkflowTemplateVersionDiffResponse response =
            WorkflowTemplateVersionResponseMapper.ToChangesResponse(TYPE, changes, _narrativeGenerator);

        Assert.True(response.IsFirstVersion);
        Assert.Null(response.FromVersionId);
        Assert.Equal("v1", response.ToVersionId);
        Assert.Null(response.Diff);
        Assert.Empty(response.NarrativeParts);
        Assert.Equal(WorkflowNarrativeGenerator.FirstVersionSummary, response.StoredNarrative);
    }

    [Fact]
    public void ToCompareResponse_MapsAddedRemovedAndVariableFieldDiffs()
    {
        Workflow before = Report(OLD_PROMPT, out string stableBlockId, stableBlockId: null);
        _ = new WorkflowVariable(before, "currency") { DisplayName = "Reporting currency", DefaultValue = "USD" };

        Workflow after = Report(OLD_PROMPT, out _, stableBlockId);
        _ = new WorkflowVariable(after, "currency") { DisplayName = "Reporting currency", DefaultValue = "EUR" };
        var appendix = new Section("Appendix", after) { Title = "Appendix" };
        _ = new Block(BlockTypes.AiQuery, appendix, "glossary")
        {
            Name = "Glossary",
            Properties = new AiQueryProperties { UserPromptTemplate = "List the defined terms." },
        };

        WorkflowDiff diff = _diffService.Compare(_canonicalizer.ToCanonicalJson(before), _canonicalizer.ToCanonicalJson(after));

        WorkflowTemplateVersionDiffResponse response =
            WorkflowTemplateVersionResponseMapper.ToCompareResponse(TYPE, "v1", "v3", diff, _narrativeGenerator);

        Assert.Equal("v1", response.FromVersionId);
        Assert.Equal("v3", response.ToVersionId);
        Assert.Null(response.StoredNarrative);
        Assert.NotNull(response.Diff);

        WorkflowTemplateSectionDiffDto section = Assert.Single(response.Diff.Sections);
        Assert.Equal("Appendix", section.Title);
        Assert.Equal(WorkflowTemplateChangeKind.Added, section.ChangeKind);

        WorkflowTemplateBlockDiffDto block = Assert.Single(response.Diff.Blocks);
        Assert.Equal("glossary", block.ReplacementTag);
        Assert.Equal("Appendix", block.SectionTitle);
        Assert.Equal(WorkflowTemplateChangeKind.Added, block.ChangeKind);

        WorkflowTemplateVariableDiffDto variable = Assert.Single(response.Diff.Variables);
        Assert.Equal("currency", variable.ReplacementTag);
        Assert.Equal(WorkflowTemplateChangeKind.Modified, variable.ChangeKind);
        Assert.Contains(variable.FieldDiffs, field => field.Before == "USD" && field.After == "EUR");
    }

    [Fact]
    public void ToChangesResponse_UnavailableChanges_AreRefused_NeverMappedToAnEmptyDiff()
    {
        var changes = new WorkflowVersionChangesResult(WorkflowVersionChangeStatus.Unavailable, Diff: null, "change")
        {
            VersionId = "v2",
            ParentVersionId = "v1",
        };

        _ = Assert.Throws<InvalidOperationException>(() =>
            WorkflowTemplateVersionResponseMapper.ToChangesResponse(TYPE, changes, _narrativeGenerator));
    }

    [Theory]
    [InlineData(nameof(WorkflowTemplatesController.GetVersions))]
    [InlineData(nameof(WorkflowTemplatesController.GetVersionContent))]
    [InlineData(nameof(WorkflowTemplatesController.GetVersionChanges))]
    [InlineData(nameof(WorkflowTemplatesController.CompareVersions))]
    public void VersionRoutes_AreGetOnly_AndGatedToTheEditorRoles(string actionName)
    {
        System.Reflection.MethodInfo action = typeof(WorkflowTemplatesController).GetMethod(actionName)!;

        AuthorizeAttribute authorize = Assert.Single(
            action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>());
        Assert.Equal(Roles.EDITOR_ROLES, authorize.Roles);
        _ = Assert.Single(action.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false));
        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false));
    }

    [Fact]
    public void Cursor_RoundTrips_WhenTheVersionIdContainsTheSeparator()
    {
        var cursor = new WorkflowVersionCursor(new DateTime(639255315677693570, DateTimeKind.Utc), "id_with_underscores");

        Assert.True(WorkflowVersionCursor.TryParse(cursor.Encode(), out WorkflowVersionCursor parsed));
        Assert.Equal(cursor, parsed);
        Assert.Equal(DateTimeKind.Utc, parsed.CreatedAtUtc.Kind);
    }

    private static Workflow Report(string prompt, out string mintedStableBlockId, string? stableBlockId)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = TYPE };
        var section = new Section("Analysis", workflow) { Title = "Analysis" };
        var block = new Block(BlockTypes.AiQuery, section, "summary")
        {
            Name = "Summary",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
        block.StableBlockId = stableBlockId ?? Guid.NewGuid().ToString("N");
        mintedStableBlockId = block.StableBlockId;

        return workflow;
    }
}
