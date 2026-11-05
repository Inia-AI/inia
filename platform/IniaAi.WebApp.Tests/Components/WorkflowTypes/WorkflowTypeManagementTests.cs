using System.Data.Common;
using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApp.Components.WorkflowTypes.Pages;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace IniaAi.WebApp.Tests.Components.WorkflowTypes;

/// <summary>
/// Workflow Type Management loads the categories and counterparty types that the Add Workflow Type
/// dialog offers. A dialog opened before they arrive offers neither.
/// </summary>
public class WorkflowTypeManagementTests : SignedInRenderTest
{
    private const string EMAIL = "editor@example.test";
    private const string ADD_WORKFLOW_TYPE = "Add Workflow Type";
    private const string CATEGORY = "Corporate";
    private const string COUNTERPARTY_TYPE = "Bank";

    private readonly HeldCounterpartyTypesQuery _counterpartyTypesQuery;

    public WorkflowTypeManagementTests()
        : this(new HeldCounterpartyTypesQuery())
    {
    }

    private WorkflowTypeManagementTests(HeldCounterpartyTypesQuery counterpartyTypesQuery)
        : base(counterpartyTypesQuery)
    {
        _counterpartyTypesQuery = counterpartyTypesQuery;
        _ = Services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();
    }

    [Fact]
    public async Task AddWorkflowType_WaitsForTheListsItsDialogOffers()
    {
        _ = await SignInAsync(EMAIL, Roles.USER, Roles.WORKFLOW_EDITOR);
        await SeedAsync();
        _counterpartyTypesQuery.Hold();

        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<WorkflowTypeManagement> page = RenderInApp<WorkflowTypeManagement>();

        page.WaitForAssertion(() => Assert.True(AddButton(page).HasAttribute("disabled")));

        _counterpartyTypesQuery.Release();
        page.WaitForAssertion(() => Assert.False(AddButton(page).HasAttribute("disabled")));

        await page.InvokeAsync(() => AddButton(page).Click());
        dialogs.WaitForAssertion(() => Assert.Equal(2, dialogs.FindAll(".mud-select .mud-input-control").Count));
        Assert.Contains(CATEGORY, await OptionsAsync(dialogs, 0));
        Assert.Contains(COUNTERPARTY_TYPE, await OptionsAsync(dialogs, 1));
    }

    private static IElement AddButton(IRenderedComponent<WorkflowTypeManagement> page)
    {
        return page.FindAll("button").Single(button => button.TextContent.Trim() == ADD_WORKFLOW_TYPE);
    }

    private async Task<IReadOnlyList<string>> OptionsAsync(IRenderedComponent<MudDialogProvider> dialogs, int select)
    {
        await dialogs.InvokeAsync(() => dialogs.FindAll(".mud-select .mud-input-control")[select].MouseDown());
        Popovers.WaitForAssertion(() => Assert.NotEmpty(Popovers.FindAll(".mud-popover-open .mud-list-item")));
        List<string> options = [.. Popovers.FindAll(".mud-popover-open .mud-list-item").Select(item => item.TextContent.Trim())];
        await dialogs.InvokeAsync(() => dialogs.FindAll(".mud-select .mud-input-control")[select].MouseDown());
        return options;
    }

    private async Task SeedAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _ = core.WorkflowTypeCategories.Add(new WorkflowTypeCategory { Id = "corporate", Name = CATEGORY });
        _ = await core.SaveChangesAsync();

        UserDataDbContext userData = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = userData.CounterpartyTypes.Add(new CounterpartyType { Name = COUNTERPARTY_TYPE });
        _ = await userData.SaveChangesAsync();
    }

    /// <summary>
    /// Holds the page's counterparty types query until the test releases it, as a slow database would.
    /// </summary>
    private sealed class HeldCounterpartyTypesQuery : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _holding;

        public void Hold()
        {
            _holding = true;
        }

        public void Release()
        {
            _ = _released.TrySetResult();
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_holding && command.CommandText.Contains("\"CounterpartyTypes\"", StringComparison.Ordinal))
            {
                await _released.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
