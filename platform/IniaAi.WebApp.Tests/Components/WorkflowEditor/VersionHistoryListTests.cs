using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using IniaAi.WebApp.Components.WorkflowEditor.Components;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// A version history is a list of versions with buttons beside each one, not a list a user picks
/// from, so a screen reader hears each version apart from its actions.
/// </summary>
public class VersionHistoryListTests : SignedInRenderTest
{
    private const string VERSION_HISTORY = "Version history";

    [Fact]
    public async Task TheComponentVersionHistory_IsANamedListOfItems_NotAListboxOfOptions()
    {
        _ = await SignInAsync("editor@inia.ai", Roles.USER, Roles.WORKFLOW_EDITOR);
        var component = new SharedComponentSummaryDto(
            "component-1", SharedComponentKind.Block, "QA block", null, null, null,
            SharedComponentState.Active, "version-2", "hash-2", DateTime.UtcNow, 0, []);
        List<SharedComponentVersionDto> versions =
        [
            new("version-2", "hash-2", "version-1", DateTime.UtcNow, "user-1", "QA Editor", false, "Second version"),
            new("version-1", "hash-1", null, DateTime.UtcNow.AddHours(-1), "user-1", "QA Editor", false, "First version"),
        ];

        IRenderedComponent<SharedComponentHistory> history = RenderInApp<SharedComponentHistory>(parameters => parameters
            .Add(p => p.OrganizationId, ORGANIZATION_ID)
            .Add(p => p.UserId, "user-1")
            .Add(p => p.Component, component)
            .Add(p => p.Versions, versions));

        Assert.Equal(VERSION_HISTORY, history.Find("[role=list]").GetAttribute("aria-label"));
        Assert.Empty(history.FindAll("[role=listbox], [role=option]"));
        Assert.Equal(2, history.FindAll("[role=listitem]").Count);
        Assert.Contains(history.FindAll("button"), button => button.TextContent.Trim() == "Show changes");
    }
}
