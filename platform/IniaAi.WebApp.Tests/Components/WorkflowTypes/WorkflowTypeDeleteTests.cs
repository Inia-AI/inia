using Bunit;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApp.Components.WorkflowTypes.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;

namespace IniaAi.WebApp.Tests.Components.WorkflowTypes;

/// <summary>
/// The Delete button on Workflow Type Management retires a type that one organization uses, the
/// way the template API does, so the type keeps its retention window instead of vanishing at once.
/// </summary>
public sealed class WorkflowTypeDeleteTests : SignedInRenderTest
{
    private const string EMAIL = "editor@example.test";
    private const string TYPE = "tmw_test_detect";
    private const string TYPE_NAME = "TMW test detect";
    private const string REMOVE = "Remove";
    private const string OTHER_ORGANIZATION_ID = "org-customer";

    private readonly SqliteCoreDatabase _core = new();

    public WorkflowTypeDeleteTests()
    {
        _ = Services.AddScoped<IWorkflowTypeRetirement>(provider => new WorkflowTypeRetirement(
            _core.Context,
            provider.GetRequiredService<UserDataDbContext>(),
            new ModuleWorkflowTypes([]),
            NullLogger<WorkflowTypeRetirement>.Instance));
    }

    [Fact]
    public async Task Delete_RetiresATypeOneOrganizationUses()
    {
        _ = await SignInAsync(EMAIL, Roles.USER, Roles.WORKFLOW_EDITOR);
        CancellationToken ct = Xunit.TestContext.Current.CancellationToken;
        SqliteCoreDbContext core = _core.Context;
        _ = core.WorkflowTypes.Add(new WorkflowType(TYPE, TYPE_NAME));
        core.GrantWorkflowTypeAccess(TYPE, ORGANIZATION_ID);
        _ = core.Workflows.Add(new Workflow(TYPE) { WorkflowTypeId = TYPE });
        _ = await core.SaveChangesAsync(ct);
        core.ChangeTracker.Clear();

        IEnumerable<WorkflowType> types = await _core.UnitOfWork.WorkflowTypes.GetAllAsync();
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<WorkflowTypeManagementCard> card = RenderInApp<WorkflowTypeManagementCard>(parameters => parameters
            .Add(component => component.CoreUnitOfWork, _core.UnitOfWork)
            .Add(component => component.WorkflowTypes, types));

        await card.InvokeAsync(() => card.Find($"[aria-label='Delete workflow type {TYPE_NAME}']").Click());
        dialogs.WaitForAssertion(() => Assert.NotNull(Button(dialogs, REMOVE)));
        await dialogs.InvokeAsync(() => Button(dialogs, REMOVE).Click());

        card.WaitForAssertion(() =>
        {
            core.ChangeTracker.Clear();
            WorkflowType retired = core.WorkflowTypes.Single(type => type.Id == TYPE);
            Assert.NotNull(retired.RetiredAt);
        });
        Assert.False(await core.WorkflowTypeOrganizationAccess.AnyAsync(access => access.WorkflowTypeId == TYPE, ct));
        Assert.True(await core.Workflows.IgnoreQueryFilters().AnyAsync(workflow => workflow.WorkflowTypeId == TYPE && workflow.IsDeleted, ct));
    }

    [Fact]
    public async Task Delete_OfATypeAnotherOrganizationUses_AsksToSwitchToIt_AndRetiresNothing()
    {
        _ = await SignInAsync(EMAIL, Roles.USER, Roles.WORKFLOW_EDITOR);
        CancellationToken ct = Xunit.TestContext.Current.CancellationToken;
        SqliteCoreDbContext core = _core.Context;
        _ = core.WorkflowTypes.Add(new WorkflowType(TYPE, TYPE_NAME));
        core.GrantWorkflowTypeAccess(TYPE, OTHER_ORGANIZATION_ID);
        _ = await core.SaveChangesAsync(ct);
        core.ChangeTracker.Clear();

        IEnumerable<WorkflowType> types = await _core.UnitOfWork.WorkflowTypes.GetAllAsync();
        _ = Render<MudDialogProvider>();
        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<WorkflowTypeManagementCard> card = RenderInApp<WorkflowTypeManagementCard>(parameters => parameters
            .Add(component => component.CoreUnitOfWork, _core.UnitOfWork)
            .Add(component => component.WorkflowTypes, types));

        await card.InvokeAsync(() => card.Find($"[aria-label='Delete workflow type {TYPE_NAME}']").Click());

        snackbars.WaitForAssertion(() => Assert.Contains(OTHER_ORGANIZATION_ID, snackbars.Markup));
        core.ChangeTracker.Clear();
        Assert.Null((await core.WorkflowTypes.SingleAsync(type => type.Id == TYPE, ct)).RetiredAt);
        Assert.True(await core.WorkflowTypeOrganizationAccess.AnyAsync(access => access.WorkflowTypeId == TYPE, ct));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _core.Dispose();
        }

        base.Dispose(disposing);
    }
}
