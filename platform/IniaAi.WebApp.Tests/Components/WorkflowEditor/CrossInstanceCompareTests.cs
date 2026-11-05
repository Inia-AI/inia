using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using IniaAi.WebApp.Components.WorkflowEditor.Pages;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using System.Text.Json;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// A file that is not a deployment report is named in a warning and left out, so the reports added
/// after it still compare on the same page.
/// </summary>
public class CrossInstanceCompareTests : SignedInRenderTest
{
    private const string WORKFLOW_FILE = "HedgeFundsMonitoring-workflow.workflow";
    private const string EMPTY_STATE = "No deployment reports loaded yet.";

    [Fact]
    public async Task AWorkflowFile_IsNamedAndLeftOut_AndTheReportsAfterItStillCompare()
    {
        _ = await SignInAsync("editor@example.test", Roles.USER, Roles.WORKFLOW_EDITOR);
        IRenderedComponent<CrossInstanceCompare> page = RenderInApp<CrossInstanceCompare>();
        string workflowFile = new Workflow("Hedge Funds Monitoring") { Title = "Hedge Funds Monitoring" }.ToExportJson();

        Upload(page, InputFileContent.CreateFromText(workflowFile, WORKFLOW_FILE));

        page.WaitForAssertion(() => Assert.Contains(Warnings(), message => message.Contains(WORKFLOW_FILE, StringComparison.Ordinal)));
        Assert.Contains(EMPTY_STATE, page.Markup);

        Upload(
            page,
            InputFileContent.CreateFromText(Report("this-deploy"), "this.json"),
            InputFileContent.CreateFromText(Report("other-deploy"), "other.json"));

        page.WaitForAssertion(() => Assert.Contains("Comparing 2 deployment(s) across 1 workflow(s)", page.Markup));
    }

    private static void Upload(IRenderedComponent<CrossInstanceCompare> page, params InputFileContent[] files)
    {
        page.FindComponent<InputFile>().UploadFiles(files);
    }

    private IEnumerable<string> Warnings()
    {
        return Services.GetRequiredService<ISnackbar>().ShownSnackbars
            .Where(snackbar => snackbar.Severity == Severity.Warning)
            .Select(snackbar => snackbar.Message ?? string.Empty);
    }

    private static string Report(string instanceName)
    {
        return JsonSerializer.Serialize(new CurrencyCardDto(
            instanceName,
            new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc),
            [new CurrencyCardEntryDto("HedgeFundsMonitoring", "v1", "hash-1", "abc123", new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc), "user-1", "Editor", ["hash-1"])]));
    }
}
