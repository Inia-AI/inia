using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.WebApp.Components;
using IniaAi.WebApp.Components.WorkflowEditor.Pages;
using Microsoft.Extensions.DependencyInjection;
using EditorComponent = IniaAi.WebApp.Components.WorkflowEditor.Components.WorkflowEditor;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class DesignerPageOpeningTests : DesignerPageOpeningTest
{
    private const string WORKFLOW_TYPE_ID = "Corp";

    public DesignerPageOpeningTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public async Task TheGenericDesigner_OpensAWorkflowWhoseLookupsReturnBeforeTheFirstRender()
    {
        IRenderedComponent<WorkflowDesignerPage> page = await OpenAsync<WorkflowDesignerPage>(WORKFLOW_TYPE_ID, parameters => parameters
            .Add(p => p.WorkflowTypeId, WORKFLOW_TYPE_ID));

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(SAVE_BUTTON)));
    }

    [Fact]
    public async Task ANeverSavedWorkflow_ShowsUnsavedChanges_OnceTheAuthorEditsIt()
    {
        IRenderedComponent<WorkflowDesignerPage> page = await OpenAsync<WorkflowDesignerPage>(WORKFLOW_TYPE_ID, parameters => parameters
            .Add(p => p.WorkflowTypeId, WORKFLOW_TYPE_ID));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(SAVE_BUTTON)));
        IRenderedComponent<EditorComponent> editor = page.FindComponent<EditorComponent>();
        await editor.InvokeAsync(editor.Instance.RecheckUnsavedChanges);
        Assert.Empty(UnsavedChips(editor));

        page.FindAll("button").First(button => button.TextContent.Trim() == "Add Section").Click();
        await editor.InvokeAsync(editor.Instance.RecheckUnsavedChanges);

        editor.WaitForAssertion(() => Assert.Single(UnsavedChips(editor)));
    }

    [Fact]
    public async Task AWorkflowWhoseNewestVersionHoldsOtherContent_OpensWithoutUnsavedChanges_UntilTheAuthorEditsIt()
    {
        Workflow olderContent = new("Saved before the stored copy moved on") { WorkflowTypeId = WORKFLOW_TYPE_ID };
        _ = new Section("Removed since", olderContent);
        IRenderedComponent<WorkflowDesignerPage> page = await OpenAsync<WorkflowDesignerPage>(
            WORKFLOW_TYPE_ID,
            parameters => parameters.Add(p => p.WorkflowTypeId, WORKFLOW_TYPE_ID),
            () => SeedNewestVersionAsync(olderContent));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(SAVE_BUTTON)));
        IRenderedComponent<EditorComponent> editor = page.FindComponent<EditorComponent>();
        await editor.InvokeAsync(editor.Instance.RecheckUnsavedChanges);
        Assert.Empty(UnsavedChips(editor));

        page.FindAll("button").First(button => button.TextContent.Trim() == "Add Section").Click();
        await editor.InvokeAsync(editor.Instance.RecheckUnsavedChanges);

        editor.WaitForAssertion(() => Assert.Single(UnsavedChips(editor)));
    }

    [Fact]
    public async Task ClosingTheEditor_AsTheBrowserGoesAway_DoesNotFailTheCircuit()
    {
        BunitJSModuleInterop module = JSInterop.SetupModule("./js/edit-activity.js");
        BunitJSModuleInterop watch = module.SetupModule("watchEditActivity", _ => true);
        watch.SetupVoid("dispose").SetException(new TaskCanceledException());
        IRenderedComponent<WorkflowDesignerPage> page = await OpenAsync<WorkflowDesignerPage>(WORKFLOW_TYPE_ID, parameters => parameters
            .Add(p => p.WorkflowTypeId, WORKFLOW_TYPE_ID));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(SAVE_BUTTON)));

        page.WaitForAssertion(() => Assert.Single(module.Invocations["watchEditActivity"]));
        EditorComponent editor = page.FindComponent<EditorComponent>().Instance;

        Exception? thrown = await Record.ExceptionAsync(() => page.InvokeAsync(async () => await editor.DisposeAsync()));

        Assert.Null(thrown);
        Assert.Single(watch.Invocations["dispose"]);
    }

    [Fact]
    public async Task AWorkflowTypeThatDoesNotExist_SaysSoOnThePage_WithAWayBack_AndNothingToUploadTo()
    {
        const string missing = "NoSuchType-xx01";

        IRenderedComponent<WorkflowDesignerPage> page = await OpenAsync<WorkflowDesignerPage>(WORKFLOW_TYPE_ID, parameters => parameters
            .Add(p => p.WorkflowTypeId, missing));

        page.WaitForAssertion(() => Assert.Contains($"Workflow type '{missing}' not found", page.Find(".mud-alert").TextContent));
        Assert.Equal(AppRoutes.EDITORS, page.Find("a.editor-soft-button").GetAttribute("href"));
        Assert.Empty(page.FindAll(".mud-progress-circular"));
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Contains("Upload Files", StringComparison.Ordinal));
    }

    private async Task SeedNewestVersionAsync(Workflow content)
    {
        using IServiceScope scope = Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        WorkflowCanonicalizer canonicalizer = new();
        WorkflowVersion version = new()
        {
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            ContentHash = canonicalizer.ComputeContentHash(content),
            CreatedByUserId = "system",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            WorkflowJson = canonicalizer.ToCanonicalJson(content),
        };
        _ = core.Add(version);
        _ = await core.SaveChangesAsync();
    }

    private static List<AngleSharp.Dom.IElement> UnsavedChips(IRenderedComponent<EditorComponent> editor)
    {
        return [.. editor.FindAll(".mud-chip").Where(chip => chip.TextContent.Trim() == "Unsaved changes")];
    }
}
