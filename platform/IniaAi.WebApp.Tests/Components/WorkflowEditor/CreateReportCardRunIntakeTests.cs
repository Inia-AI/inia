using Bunit;
using IniaAi.AiConnection.Common;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Services.Models.Jobs;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using IniaAi.WebApp.Models;
using IniaAi.WebApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// The editor's Generate Report button starts a report without the shared orchestrator, so it
/// checks run intake itself: while a deploy pauses runs, it shows the update message and starts
/// nothing, also when the pause comes while the missing-previews dialog is open.
/// </summary>
public sealed class CreateReportCardRunIntakeTests : SignedInRenderTest
{
    private readonly RecordingJobsService _jobs = new();

    public CreateReportCardRunIntakeTests()
    {
        _ = Services.AddSingleton<IJobsService>(_jobs);
        _ = Services.AddSingleton(Options.Create(new AgentRuntimeSettings()));
        _ = Services.AddSingleton<TenantModelFamilyResolver>();
    }

    [Fact]
    public async Task WhileADeployPausesRuns_GenerateReport_ShowsTheUpdateMessage_AndStartsNoReport()
    {
        _ = await SignInAsync("analyst@inia.example", Roles.USER);
        await Services.GetRequiredService<AppState>().ReloadUserAsync();
        Services.GetRequiredService<RunIntake>().Pause(TimeSpan.FromMinutes(30));

        IRenderedComponent<CreateReportCard> card = RenderInApp<CreateReportCard>(parameters => parameters
            .Add(component => component.Job, new Job(ORGANIZATION_ID, "Corp"))
            .AddCascadingValue("EditorContext", new WorkflowEditorContext("Corp", new Workflow("root"), null!)));
        Button(card, "Generate Report").Click();

        card.WaitForAssertion(() => Assert.Contains(RunIntake.PAUSED_MESSAGE, Warnings()));
        Assert.Empty(_jobs.ExecutedJobs);
    }

    [Fact]
    public async Task APauseThatComesWhileThePreviewsDialogIsOpen_StillStartsNoReport()
    {
        _ = await SignInAsync("analyst@inia.example", Roles.USER);
        await Services.GetRequiredService<AppState>().ReloadUserAsync();
        var workflow = new Workflow("root");
        _ = new Block(BlockTypes.AiQuery, new Section("Summary", workflow))
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarise the figures." },
        };
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<CreateReportCard> card = RenderInApp<CreateReportCard>(parameters => parameters
            .Add(component => component.Job, new Job(ORGANIZATION_ID, "Corp"))
            .AddCascadingValue("EditorContext", new WorkflowEditorContext("Corp", workflow, null!)));

        Button(card, "Generate Report").Click();
        dialogs.WaitForAssertion(() => Assert.Contains(dialogs.FindAll("button"), button => button.TextContent.Trim() == "Proceed Anyway"));
        Services.GetRequiredService<RunIntake>().Pause(TimeSpan.FromMinutes(30));
        dialogs.FindAll("button").Single(button => button.TextContent.Trim() == "Proceed Anyway").Click();

        card.WaitForAssertion(() => Assert.Contains(RunIntake.PAUSED_MESSAGE, Warnings()));
        Assert.Empty(_jobs.ExecutedJobs);
    }

    private IEnumerable<string> Warnings()
    {
        return Services.GetRequiredService<ISnackbar>().ShownSnackbars
            .Where(snackbar => snackbar.Severity == Severity.Warning)
            .Select(snackbar => snackbar.Message ?? string.Empty);
    }
}
