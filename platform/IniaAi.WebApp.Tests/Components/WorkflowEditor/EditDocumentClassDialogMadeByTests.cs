using Bunit;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.WebApp.Components.WorkflowEditor.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// The document class dialog offers the outputs of other workflows as an input's Made by, warns when the stored one is
/// gone without dropping it, and shows an output which inputs it makes.
/// </summary>
public class EditDocumentClassDialogMadeByTests : SignedInRenderTest
{
    private const string PART_1 = "CorpPart1TypeA";
    private const string PART_2 = "CorpPart2TypeA";
    private const string SPREAD_OUTPUT = "Financial Spread Output";

    [Fact]
    public async Task AnInputMadeByAnotherWorkflow_ShowsThatOutput_AndKeepsItOnSave()
    {
        SeedPart1();
        (Workflow part2, DocumentClass spreads) = Part2(madeByType: PART_1);
        (IRenderedComponent<MudDialogProvider> dialogs, IDialogReference dialog) = await OpenAsync(part2, spreads);

        Assert.Contains("INIA Corporate Analysis - Part 1 · Financial Spread", dialogs.Markup);
        Assert.DoesNotContain("which no workflow makes any more", dialogs.Markup);
        Save(dialogs);

        DialogResult? result = await dialog.Result;
        DocumentClass saved = Assert.IsType<DocumentClass>(result!.Data);
        Assert.Equal(PART_1, saved.MadeByWorkflowTypeId);
        Assert.Equal(SPREAD_OUTPUT, saved.MadeByDocumentClassKey);
    }

    [Fact]
    public async Task AnInputMadeByAWorkflowThatIsGone_Warns_AndKeepsTheLinkOnSave()
    {
        SeedPart1();
        (Workflow part2, DocumentClass spreads) = Part2(madeByType: "RemovedType");
        (IRenderedComponent<MudDialogProvider> dialogs, IDialogReference dialog) = await OpenAsync(part2, spreads);

        Assert.Contains($"This input is made by RemovedType · {SPREAD_OUTPUT}, which no workflow makes any more.", dialogs.Markup);
        Save(dialogs);

        DialogResult? result = await dialog.Result;
        Assert.Equal("RemovedType", Assert.IsType<DocumentClass>(result!.Data).MadeByWorkflowTypeId);
    }

    [Fact]
    public async Task ChoosingAnOutputThatExists_InPlaceOfOneThatIsGone_TakesTheWarningAway()
    {
        SeedPart1();
        (Workflow part2, DocumentClass spreads) = Part2(madeByType: "RemovedType");
        (IRenderedComponent<MudDialogProvider> dialogs, _) = await OpenAsync(part2, spreads);
        IRenderedComponent<MudAutocomplete<MadeByChoice>> madeBy = dialogs.FindComponent<MudAutocomplete<MadeByChoice>>();

        await dialogs.InvokeAsync(() => madeBy.Instance.ValueChanged.InvokeAsync(
            new MadeByChoice(PART_1, "INIA Corporate Analysis - Part 1", SPREAD_OUTPUT, "Financial Spread")));

        Assert.DoesNotContain("which no workflow makes any more", dialogs.Markup);
    }

    [Fact]
    public async Task TheMadeByList_FindsTheOutputsWhoseNameHoldsWhatIsTyped()
    {
        SeedPart1();
        var peer = new Workflow("Peer Review") { WorkflowTypeId = "PeerReviewType" };
        _ = new DocumentClass(peer, "Peer Report") { DisplayName = "Peer Report", IsOutputDocument = true };
        Seed("PeerReviewType", "Peer Review", peer);
        (Workflow part2, DocumentClass spreads) = Part2(madeByType: PART_1);
        (IRenderedComponent<MudDialogProvider> dialogs, _) = await OpenAsync(part2, spreads);
        IRenderedComponent<MudAutocomplete<MadeByChoice>> madeBy = dialogs.FindComponent<MudAutocomplete<MadeByChoice>>();

        Func<string?, CancellationToken, Task<IEnumerable<MadeByChoice>>> search = Assert.IsType<Func<string?, CancellationToken, Task<IEnumerable<MadeByChoice>>>>(madeBy.Instance.SearchFunc, exactMatch: false);

        IEnumerable<MadeByChoice> found = await search("part 1", Xunit.TestContext.Current.CancellationToken);
        IEnumerable<MadeByChoice> all = await search(string.Empty, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal([(PART_1, SPREAD_OUTPUT)], found.Select(choice => (choice.WorkflowTypeId, choice.DocumentClassKey)));
        Assert.Equal(2, all.Count());
    }

    [Fact]
    public async Task AnOutput_ShowsTheInputsItMakes()
    {
        Workflow part1 = SeedPart1();
        (Workflow part2, _) = Part2(madeByType: PART_1);
        Seed(PART_2, "INIA Corporate Analysis - Part 2", part2);
        DocumentClass output = part1.DocumentClasses.Single();

        (IRenderedComponent<MudDialogProvider> dialogs, _) = await OpenAsync(part1, output);

        Assert.Contains("Used by: INIA Corporate Analysis - Part 2 · Financial Spreads", dialogs.Markup);
    }

    private Workflow SeedPart1()
    {
        var part1 = new Workflow("Part 1") { WorkflowTypeId = PART_1 };
        _ = new DocumentClass(part1, SPREAD_OUTPUT) { DisplayName = "Financial Spread", IsOutputDocument = true };
        Seed(PART_1, "INIA Corporate Analysis - Part 1", part1);
        return part1;
    }

    private static (Workflow Workflow, DocumentClass Spreads) Part2(string madeByType)
    {
        var part2 = new Workflow("Part 2") { WorkflowTypeId = PART_2 };
        var spreads = new DocumentClass(part2, "CorporateFinancialModel")
        {
            DisplayName = "Financial Spreads",
            MadeByWorkflowTypeId = madeByType,
            MadeByDocumentClassKey = SPREAD_OUTPUT,
        };
        return (part2, spreads);
    }

    private void Seed(string workflowTypeId, string name, Workflow workflow)
    {
        using IServiceScope scope = Services.CreateScope();
        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _ = core.WorkflowTypes.Add(new WorkflowType { Id = workflowTypeId, Name = name });
        _ = core.Workflows.Add(workflow);
        _ = core.SaveChanges();
    }

    private async Task<(IRenderedComponent<MudDialogProvider>, IDialogReference)> OpenAsync(Workflow workflow, DocumentClass documentClass)
    {
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IDialogService service = Services.GetRequiredService<IDialogService>();
        IDialogReference dialog = await dialogs.InvokeAsync(() => service.ShowAsync<EditDocumentClassDialog>(
            "Edit document class",
            new DialogParameters<EditDocumentClassDialog> { { x => x.Workflow, workflow }, { x => x.DocumentClass, documentClass } }));
        dialogs.WaitForAssertion(() => Assert.Contains(EditDocumentClassDialog.IS_FOR_OUTPUT_DOCUMENTS, dialogs.Markup));
        return (dialogs, dialog);
    }

    private static void Save(IRenderedComponent<MudDialogProvider> dialogs)
    {
        dialogs.FindAll("button").First(button => button.TextContent.Trim() == "Save").Click();
    }
}
