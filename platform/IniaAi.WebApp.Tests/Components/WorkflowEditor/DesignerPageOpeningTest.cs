using Bunit;
using IniaAi.AiConnection.Abstractions;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Common.Interfaces.GitHub;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.GitHub;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Services.Documents;
using IniaAi.Services.Email;
using IniaAi.Services.Jobs;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApp.Services;
using IniaAi.WebApp.Tests.Components.WorkflowEditor.Agent;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// A designer page wires the upload card into the workflow it opens. The card exists only after the
/// page has rendered, and Blazor renders a page before its first lookup returns only when that
/// lookup waits: here the lookups return at once, and a page the layout discards on a tenant switch
/// never renders at all. The page must open either way.
/// </summary>
public abstract class DesignerPageOpeningTest : EditorAgentRenderTest
{
    protected const string SAVE_BUTTON = ".floating-button-db-save";

    private const string EDITOR = "editor@example.test";

    protected DesignerPageOpeningTest(ITestOutputHelper output)
        : base(enabled: false)
    {
        Platform = new TestServiceProviderBuilder(new PlatformSettings(), output).Build(services =>
        {
            // Opening a page calls no model. Without these, the nightly run, which allows paid calls,
            // builds real connectors from settings that hold no keys.
            _ = services.AddSingleton<IChatConnector>(new RefusingChatConnector());
            _ = services.AddSingleton<IEmbeddingsConnector>(new RefusingEmbeddingsConnector());
            _ = services.AddTransient<DocumentProcessingService>();
            _ = services.AddTransient<IEmailExpansionService, EmailExpansionService>();
            _ = services.AddTransient<IIntakeRefusalRecorder, LoggingIntakeRefusalRecorder>();
            _ = services.AddTransient<DocumentIntakeGate>();
            _ = services.AddTransient<EmailIntakeJobStage>();
        });
        Services.AddFallbackServiceProvider(Platform);
        _ = Services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();
        _ = Services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
        _ = Services.AddScoped<WorkflowTextExportService>();
        // A constructor in bUnit's own container does not reach the fallback, so the gate comes across by hand.
        _ = Services.AddTransient(_ => Platform.GetRequiredService<DocumentIntakeGate>());
        _ = Services.AddScoped<IGitHubWorkflowVersioningService, GitHubWorkflowVersioningService>();
        _ = Services.AddHttpClient();
        _ = Services.AddOptions<GitHubSettings>();
        AddHandler(WorkflowType.GENERIC);
    }

    private IServiceProvider Platform { get; }

    /// <summary>bUnit's fallback answers plain services only, so a keyed handler comes across by hand.</summary>
    protected void AddHandler(string workflowTypeId)
    {
        _ = Services.AddKeyedTransient(workflowTypeId, (_, key) => Platform.GetRequiredKeyedService<IWorkflowHandler>(key));
    }

    protected async Task<IRenderedComponent<TPage>> OpenAsync<TPage>(
        string workflowTypeId,
        Action<ComponentParameterCollectionBuilder<TPage>>? parameters = null,
        Func<Task>? seedBeforeOpening = null,
        string role = Roles.WORKFLOW_EDITOR)
        where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        await SignInAsync(EDITOR, Roles.USER, role);
        await SeedWorkflowTypeAsync(workflowTypeId);
        if (seedBeforeOpening is not null)
        {
            await seedBeforeOpening();
        }

        return RenderInApp(parameters);
    }
}
