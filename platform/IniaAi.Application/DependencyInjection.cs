using FluentValidation;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Reports;
using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Common.RunLimits;
using IniaAi.Application.Common.DailyAllowances;
using IniaAi.Application.Common.DataManagement;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Common.UploadLimits;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.DocumentAnalysis;
using IniaAi.Application.Queries.Counterparties.ListPeerCandidates;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.Reports;
using IniaAi.Common.Interfaces.Services.Runs;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IniaAi.Application;

/// <summary>
/// Provides extension methods for registering application-level services.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds application services, including the request handlers, to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to which application services are added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        _ = services.AddRequestDispatching(typeof(DependencyInjection).Assembly);

        // Add FluentValidation validators
        _ = services.AddValidatorsFromAssemblyContaining<StartWorkflowExecutionCommandValidator>();

        _ = services.AddSingleton<IEditorAgentNotifications, EditorAgentNotifications>();
        _ = services.AddSingleton<IWorkflowEditorAgentService, WorkflowEditorAgentService>();
        _ = services.AddTransient<IAgentConversationConsumer, EditorAgentConversationConsumer>();
        services.TryAddScoped<IRequestModelUse, RequestModelUse>();
        _ = services.AddScoped<EditorAgentProposalGate>();
        _ = services.AddScoped<EditorAgentReports>();
        _ = services.AddScoped<EditorAgentReportDocuments>();
        _ = services.AddScoped<EditorAgentFeedback>();
        _ = services.AddScoped<WorkflowSavePipeline>();
        _ = services.AddSingleton<ICorePromptsSeed, DocumentAnalysisPromptsSeed>();
        _ = services.AddScoped<IStartupDataMigration, PageOverviewPromptsMigration>();
        _ = services.AddScoped<WorkflowTemplateSavePreflight>();
        _ = services.AddScoped<WorkflowSaveChecks>();
        _ = services.AddSingleton<IDocumentClassEditor, DocumentClassEditor>();
        _ = services.AddSingleton<ISharedComponentEditor, SharedComponentEditor>();
        _ = services.AddScoped<IUserDisplayNameResolver, UserDisplayNameResolver>();
        _ = services.AddSingleton<WorkflowCanonicalizer>();
        _ = services.AddSingleton<SharedComponentContentNormalizer>();
        _ = services.AddSingleton<SharedComponentReconciler>();
        _ = services.AddScoped<SharedComponentFanOutService>();
        _ = services.AddSingleton<WorkflowDiffService>();
        _ = services.AddSingleton<WorkflowNarrativeGenerator>();
        _ = services.AddSingleton<SharedComponentDefinitionDiffer>();
        _ = services.AddSingleton<IWorkflowTemplateStructuralValidator, WorkflowTemplateStructuralValidator>();
        _ = services.AddSeamDefaults();

        return services;
    }

    /// <summary>
    /// The platform's defaults for the seams a closed module fills. Each is added only when absent,
    /// so a module's registration wins whichever of the two runs first.
    /// </summary>
    public static IServiceCollection AddSeamDefaults(this IServiceCollection services)
    {
        services.TryAddTransient<ICaseFreezeSources, NoCaseFreezeSources>();
        services.TryAddTransient<IBaselineReader, NoBaselineReader>();
        services.TryAddTransient<IPeerCandidateJudge, UnjudgedPeerCandidates>();
        services.TryAddSingleton<RoleGrants>();
        services.TryAddSingleton<BuiltInCounterpartyTypes>();
        services.TryAddSingleton<ModuleWorkflowTypes>();
        services.TryAddSingleton<IOrganizationCapabilities, EveryCapability>();
        services.TryAddSingleton<IReportContentRule, EveryReportContent>();
        services.TryAddSingleton<IOrganizationsClosedToStaff, NoOrganizationsClosedToStaff>();
        services.TryAddSingleton<IDailyAllowance, NoDailyLimit>();
        services.TryAddSingleton<IUploadLimits, NoUploadLimits>();
        services.TryAddSingleton<IWorkflowCreditCost, NoCreditCost>();
        services.TryAddSingleton<IWorkflowPageLimit, NoPageLimit>();
        services.TryAddSingleton<IDeletedRecordRetention, DefaultDeletedRecordRetention>();
        services.TryAddSingleton<IReportNotice, NoReportNotice>();
        services.TryAddTransient<IRunAdmission, AdmitEveryRun>();
        services.TryAddSingleton<IWorkbookInputSheets, NoWorkbookInputSheets>();
        services.TryAddSingleton<IWorkbookOutputStamps, NoWorkbookOutputStamps>();
        services.TryAddSingleton<IWebCallAddresses, AnyPublicWebAddress>();
        services.TryAddSingleton<IWorkflowAvailability, EveryWorkflowAvailable>();
        services.TryAddSingleton<IWorkflowTypeReach, AccessRowsDecideReach>();
        services.TryAddScoped<WorkflowTypeAccess>();
        services.TryAddScoped<NextWorkflows>();
        services.TryAddSingleton<RunIntake>();
        services.TryAddTransient<IRunSettlement, NoRunSettlement>();
        services.TryAddTransient<IExternalSignUp, NoExternalSignUp>();
        services.TryAddSingleton<ISessionLifetimePolicy, NoSessionLimit>();
        services.TryAddSingleton<ISignInMethods, NoSignInMethods>();

        return services;
    }
}
