using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Extensions;
using IniaAi.Application;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Services.Abstractions.Services;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.MultiTenant;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Controllers.v1;
using IniaAi.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace IniaAi.WebApi.UnitTests.Controllers;

/// <summary>
/// The run-evidence endpoints decide, from the caller's roles, whether the handler may serve any
/// run in the organization. These tests drive each endpoint with a real principal against a run
/// someone else started, so a role mapped to the wrong flag shows up as the wrong status code.
/// </summary>
public sealed class RunEvidenceControllerTests : IDisposable
{
    private const string ORG_ID = "org-evidence";
    private const string CALLER_ID = "caller";
    private const string STARTER_ID = "analyst";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly WorkflowExecution _execution;
    private readonly Block _agent;

    public RunEvidenceControllerTests()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddMvcCore();
        _ = services.AddTestDatabaseServices();
        _ = services.AddApplication();
        _ = services.AddHttpContextAccessor();
        _ = services.AddMultiTenant<OrganizationTenantInfo>();
        _ = services.AddScoped<ICurrentUserService, CurrentUserService>();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        UserDataDbContext context = _scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        var workflow = new Workflow("Evidence run") { OrganizationId = ORG_ID };
        var section = new Section("Section", workflow);
        _agent = new Block(BlockTypes.Agent, section, "agent_tag");
        _execution = new WorkflowExecution { OrganizationId = ORG_ID, WorkflowId = workflow.Id, UserId = STARTER_ID, Title = "Run" };
        context.Users.Add(new User { Id = STARTER_ID, UserName = STARTER_ID, OrganizationId = ORG_ID });
        context.Workflows.Add(workflow);
        context.WorkflowExecutions.Add(_execution);
        context.AgentRunTraceFiles.Add(new AgentRunTraceFile
        {
            OrganizationId = ORG_ID,
            BlockId = _agent.Id,
            RunExecutionId = "run-1",
            Attempt = 1,
            Path = "trace/trace.ndjson",
            Sha256 = new string('a', 64),
            SizeBytes = 2,
            Content = "{}"u8.ToArray(),
        });
        _ = context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    private T WithCaller<T>(T controller, params string[] roles)
        where T : ControllerBase
    {
        var identity = new ClaimsIdentity(roles.Select(role => new Claim(ClaimTypes.Role, role)), authenticationType: "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = _scope.ServiceProvider, User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    private RequestContext Context()
    {
        return new RequestContext { OrganizationId = ORG_ID, UserId = CALLER_ID };
    }

    private ISender Sender => _scope.ServiceProvider.GetRequiredService<ISender>();

    private WorkflowExecutionsController Executions(params string[] roles)
    {
        ICurrentUserService currentUser = _scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        return WithCaller(
            new WorkflowExecutionsController(Context(), NullLogger<WorkflowExecutionsController>.Instance, currentUser, Sender),
            roles);
    }

    private static int? StatusOf(IActionResult result)
    {
        return result switch
        {
            FileContentResult => StatusCodes.Status200OK,
            ObjectResult objectResult => objectResult.StatusCode ?? StatusCodes.Status200OK,
            _ => null,
        };
    }

    [Theory]
    [InlineData(Roles.OPERATOR, StatusCodes.Status200OK)]
    [InlineData(Roles.SUPER_ADMIN, StatusCodes.Status200OK)]
    [InlineData(Roles.ADMIN, StatusCodes.Status403Forbidden)]
    [InlineData(Roles.WORKFLOW_EDITOR, StatusCodes.Status403Forbidden)]
    public async Task AgentRunEndpoints_AdmitOnlyOperationsRolesForSomeoneElsesRun(string role, int expected)
    {
        Assert.Equal(expected, StatusOf(await Executions(role).ListAgentRunFiles(_execution.Id)));
        Assert.Equal(expected, StatusOf(await Executions(role).DownloadAgentRunTrace(_execution.Id, _agent.Id)));
        Assert.Equal(expected, StatusOf(await Executions(role).DownloadAgentRunFile(_execution.Id, _agent.Id, "trace/trace.ndjson")));
    }

    [Theory]
    [InlineData(Roles.OPERATOR, StatusCodes.Status200OK)]
    [InlineData(Roles.SUPER_ADMIN, StatusCodes.Status200OK)]
    [InlineData(Roles.ADMIN, StatusCodes.Status403Forbidden)]
    public async Task ExecutedWorkflowDownload_AdmitsOnlyOperationsRolesForSomeoneElsesRun(string role, int expected)
    {
        ExecutedWorkflowsController controller = WithCaller(
            new ExecutedWorkflowsController(Context(), NullLogger<ExecutedWorkflowsController>.Instance, Sender),
            role);

        Assert.Equal(expected, StatusOf(await controller.Download(_execution.Id, workflowId: null, jobId: null)));
    }

    [Theory]
    [InlineData(Roles.SUPER_ADMIN, StatusCodes.Status200OK)]
    [InlineData(Roles.OPERATOR, StatusCodes.Status403Forbidden)]
    [InlineData(Roles.ADMIN, StatusCodes.Status403Forbidden)]
    public async Task ReportCost_AdmitsOnlyASuperAdminForSomeoneElsesRun(string role, int expected)
    {
        WorkflowReportsController controller = WithCaller(
            new WorkflowReportsController(Context(), NullLogger<WorkflowReportsController>.Instance, Sender),
            role);

        Assert.Equal(expected, StatusOf(await controller.GetCost(_execution.Id)));
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
