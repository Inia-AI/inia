using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Components.WorkflowEditor.Agent;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Agent;

public sealed class EditorAgentEntryTests : EditorAgentRenderTest
{
    private const string GET_ITEM = "localStorage.getItem";
    private const string SET_ITEM = "localStorage.setItem";
    private const string LISTEN_FOR_SHORTCUT = "editorAgent.listenForShortcut";
    private const string GOT_IT = "Got it";

    public EditorAgentEntryTests()
        : base(enabled: true)
    {
    }

    [Fact]
    public async Task AnEditor_SeesAskTheAgent_WithTheShortcutInItsTooltip()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);

        IRenderedComponent<EditorAgentEntry> entry = RenderEntry(user, isOn: false, _ => { });

        Assert.True(Shows(entry, EditorAgentEntry.WORK_WITH_THE_AGENT));
        Assert.Contains("Ctrl+J", EditorAgentEntry.ASK_TOOLTIP);
        Assert.Contains("⌘J", EditorAgentEntry.ASK_TOOLTIP);
        Assert.Equal("Control+J Meta+J", entry.Find("button.editor-agent-entry").GetAttribute("aria-keyshortcuts"));
    }

    [Fact]
    public async Task AUserWithoutTheEditorRoles_DoesNotSeeIt()
    {
        User user = await SignInAsync("analyst@inia.ai", Roles.USER);

        IRenderedComponent<EditorAgentEntry> entry = RenderEntry(user, isOn: false, _ => { });

        Assert.Empty(entry.FindAll("button"));
        Assert.Empty(JSInterop.Invocations[LISTEN_FOR_SHORTCUT]);
    }

    [Fact]
    public async Task ClickingAskTheAgent_OpensAgentMode()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);
        bool? requested = null;

        IRenderedComponent<EditorAgentEntry> entry = RenderEntry(user, isOn: false, isOn => requested = isOn);
        await entry.Find("button.editor-agent-entry").ClickAsync(new());

        Assert.True(requested);
    }

    [Fact]
    public async Task InAgentMode_TheSamePlaceLeadsBackToTheEditor()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);
        bool? requested = null;

        IRenderedComponent<EditorAgentEntry> entry = RenderEntry(user, isOn: true, isOn => requested = isOn);
        Assert.False(Shows(entry, EditorAgentEntry.WORK_WITH_THE_AGENT));
        await entry.FindAll("button").Single(button => button.TextContent.Trim() == EditorAgentEntry.BACK_TO_EDITOR).ClickAsync(new());

        Assert.False(requested);
    }

    [Fact]
    public async Task TheFirstVisit_ShowsTheHint_AndGotItRemembersItForTheUser()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);

        IRenderedComponent<EditorAgentEntry> entry = RenderEntry(user, isOn: false, _ => { });
        entry.WaitForAssertion(() => Assert.Contains(EditorAgentEntry.HINT, entry.Find(".editor-agent-hint").TextContent));
        await entry.FindAll("button").Single(button => button.TextContent.Trim() == GOT_IT).ClickAsync(new());

        Assert.Empty(entry.FindAll(".editor-agent-hint"));
        Assert.Contains(JSInterop.Invocations[SET_ITEM], invocation =>
            Equals(invocation.Arguments[0], EditorAgentHintPreference.STORAGE_KEY_PREFIX + user.Id)
            && Equals(invocation.Arguments[1], EditorAgentHintPreference.SEEN));
    }

    [Fact]
    public async Task OnceSeen_TheHintIsNotShownAgain()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);
        _ = JSInterop.Setup<string?>(GET_ITEM, EditorAgentHintPreference.STORAGE_KEY_PREFIX + user.Id)
            .SetResult(EditorAgentHintPreference.SEEN);

        IRenderedComponent<EditorAgentEntry> entry = RenderEntry(user, isOn: false, _ => { });

        Assert.Single(JSInterop.Invocations[LISTEN_FOR_SHORTCUT]);
        Assert.Empty(entry.FindAll(".editor-agent-hint"));
    }

    [Fact]
    public async Task OpeningAgentMode_RetiresTheHint()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);

        IRenderedComponent<EditorAgentEntry> entry = RenderEntry(user, isOn: false, _ => { });
        entry.WaitForElement(".editor-agent-hint");
        await entry.Find("button.editor-agent-entry").ClickAsync(new());

        Assert.Empty(entry.FindAll(".editor-agent-hint"));
        Assert.Contains(JSInterop.Invocations[SET_ITEM], invocation =>
            Equals(invocation.Arguments[0], EditorAgentHintPreference.STORAGE_KEY_PREFIX + user.Id));
    }

    [Fact]
    public async Task TheShortcut_IsListenedFor_AndTogglesAgentModeBothWays()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);
        var requests = new List<bool>();

        IRenderedComponent<EditorAgentEntry> entry = RenderEntry(user, isOn: false, requests.Add);
        Assert.Single(JSInterop.Invocations[LISTEN_FOR_SHORTCUT]);
        await entry.InvokeAsync(entry.Instance.ToggleFromShortcutAsync);
        entry.Render(parameters => parameters.Add(component => component.IsOn, true));
        await entry.InvokeAsync(entry.Instance.ToggleFromShortcutAsync);

        Assert.Equal(new[] { true, false }, requests);
    }

    private IRenderedComponent<EditorAgentEntry> RenderEntry(User user, bool isOn, Action<bool> onChanged)
    {
        return RenderInApp<EditorAgentEntry>(parameters => parameters
            .Add(component => component.UserId, user.Id)
            .Add(component => component.IsOn, isOn)
            .Add(component => component.IsOnChanged, onChanged));
    }
}

public sealed class EditorAgentEntryDisabledTests : EditorAgentRenderTest
{
    public EditorAgentEntryDisabledTests()
        : base(enabled: false)
    {
    }

    [Fact]
    public async Task WhereTheAgentIsOff_TheEntryIsHiddenEvenFromAnEditor()
    {
        User user = await SignInAsync("engineer@inia.ai", Roles.WORKFLOW_EDITOR);

        IRenderedComponent<EditorAgentEntry> entry = Render<EditorAgentEntry>(parameters => parameters
            .Add(component => component.UserId, user.Id));

        Assert.Empty(entry.FindAll("button"));
    }
}
