using Bunit;
using IniaAi.Domain.Business.Reporting;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApp.Components.Layout;
using IniaAi.WebApp.Components.Pages;
using IniaAi.WebApp.Components.UserWorkflows.Pages;
using IniaAi.WebApp.Components.WorkflowTypes.Components;
using Microsoft.AspNetCore.Components;

namespace IniaAi.WebApp.Tests.Components;

/// <summary>
/// A control inside a page is gated by the AuthorizeView around it, not by the page. Each test signs
/// in with one role at a time and renders the card: authoring controls show to editors, operations
/// controls to operators, and the "Remove All" controls to super admins only.
/// </summary>
public class ControlRoleGatesTests : SignedInRenderTest
{
    private const string EMAIL = "someone@example.test";
    private const string NAME = "Credit Review";

    private static readonly string[] _editors = [Roles.WORKFLOW_EDITOR, Roles.SUPER_ADMIN];
    private static readonly string[] _operators = [Roles.OPERATOR, Roles.SUPER_ADMIN];
    private static readonly string[] _superAdmins = [Roles.SUPER_ADMIN];

    public static TheoryData<string> EveryRole => new(TestServiceProviderBuilder.EveryDefinedRole);

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task TheWorkflowTypeCard_ShowsEachControlToItsRoles(string role)
    {
        _ = await SignInAsync(EMAIL, Roles.USER, role);

        IRenderedComponent<WorkflowTypeManagementCard> card = RenderInApp<WorkflowTypeManagementCard>(parameters => parameters
            .Add(p => p.WorkflowTypes, [new WorkflowType { Id = "credit-review", Name = NAME }]));

        AssertGates(card, role,
            ("Add Workflow Type", _editors),
            ($"Edit workflow type {NAME}", _editors),
            ($"Duplicate workflow type {NAME}", _editors),
            ($"Delete workflow type {NAME}", _editors),
            ($"Manage tenants of workflow type {NAME}", _operators),
            ("Remove All Workflow Types", _superAdmins));
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task TheWorkflowTypeCategoryCard_ShowsEachControlToItsRoles(string role)
    {
        _ = await SignInAsync(EMAIL, Roles.USER, role);

        IRenderedComponent<WorkflowTypeCategoryManagementCard> card = RenderInApp<WorkflowTypeCategoryManagementCard>(parameters => parameters
            .Add(p => p.Categories, [new WorkflowTypeCategory { Name = NAME }]));

        AssertGates(card, role,
            ("Add Category", _editors),
            ($"Edit category {NAME}", _editors),
            ($"Delete category {NAME}", _editors),
            ("Remove All Categories", _superAdmins));
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task TheDashboard_ShowsNoHedgeFundObservations(string role)
    {
        _ = await SignInAsync(EMAIL, Roles.USER, role);
        await SeedAsync(new HedgeFundsMonitoringObservation { OrganizationId = ORGANIZATION_ID, FundName = NAME });

        IRenderedComponent<Dashboard> dashboard = RenderInApp<Dashboard>();

        Assert.DoesNotContain("Hedge Funds Monitoring Observations", dashboard.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(NAME, dashboard.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task TheExecutedWorkflowsPage_ClearsWorkflowsForSuperAdminsOnly(string role)
    {
        _ = await SignInAsync(EMAIL, Roles.USER, role);

        IRenderedComponent<Workflows> page = RenderInApp<Workflows>();

        AssertGates(page, role, ("Clear Workflows", _superAdmins));
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task TheLayout_OffersTheLogToOperators(string role)
    {
        _ = await SignInAsync(EMAIL, Roles.USER, role);
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));

        IRenderedComponent<MainLayout> layout = Render<MainLayout>();

        AssertGates(layout, role, ("Log", _operators));
    }

    private static void AssertGates(IRenderedComponent<IComponent> rendered, string role, params (string Control, string[] Roles)[] gates)
    {
        Assert.All(gates, gate =>
        {
            bool admitted = gate.Roles.Contains(role);
            Assert.True(
                admitted == Shows(rendered, gate.Control),
                $"'{gate.Control}' should be {(admitted ? "shown" : "hidden")} for {role}.");
        });
    }
}
