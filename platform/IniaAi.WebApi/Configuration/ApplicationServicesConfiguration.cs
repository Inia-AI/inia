using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Core.Workflows.Activity;
using IniaAi.Core.Workflows.Agents;
using IniaAi.AiConnection.Common;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Application.Queues;
using IniaAi.Application.Workflows.Interfaces;
using IniaAi.Application.Workflows.Services;
using IniaAi.Common.Interfaces;
using IniaAi.Configuration;
using IniaAi.Core.AugmentedSearch;
using IniaAi.Core.AugmentedSearch.Exa;
using IniaAi.Core.AugmentedSearch.Exa.Interfaces;
using IniaAi.Core.Rag;
using IniaAi.Core.Services;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Services.Models;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.BlockExtensions;
using IniaAi.Core.Workflows.Executions;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.FileFormats;
using IniaAi.FileFormats.Pdf;
using IniaAi.Infrastructure;
using IniaAi.Infrastructure.Persistence.Graph;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.StoredKeys;
using IniaAi.Services.Abstractions.Services;
using IniaAi.Services.CsvImport;
using IniaAi.Services.Documents;
using IniaAi.Services.Email;
using IniaAi.Services.Export;
using IniaAi.Services.Jobs;
using IniaAi.Services.Queues;
using IniaAi.Services.ContentRefinement;
using IniaAi.WebApi.Filters;
using IniaAi.WebApi.Middleware;
using IniaAi.WebApi.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Provides methods to register scoped, singleton, and transient application services.
/// </summary>
public static class ApplicationServicesConfiguration
{
    /// <summary>
    /// Adds transient services to the container.
    /// </summary>
    public static void AddTransientServices(this IServiceCollection services, PlatformSettings platformSettings)
    {
        _ = services.AddTransient<DatabaseMigrationHelper>()
            .AddTransient<PdfExtractor>()
            .AddOpenFileFormats()
            .AddTransient<DocumentProcessingService>()
            .AddTransient<IDocumentProcessingService>(sp => sp.GetRequiredService<DocumentProcessingService>())
            .AddTransient<DocumentAnalysisService>()
            .AddTransient<ExcelSheetAnalysisService>()
            .AddTransient<ReconciliationService>()
            .AddTransient<IContentRefinementService, ContentRefinementService>()
            .AddTransient<RagService>()
            .AddTransient<IJobExecutor, JobExecutor>()
            .AddTransient<EmailIntakeJobStage>()
            .AddTransient<IExaSearchService, ExaSearchService>()
            .AddTransient<ICsvImportService, CsvImportService>()
            .AddTransient<IWorkflowOutputBuilder, WorkflowOutputBuilder>()
            .AddTransient<IWorkflowOutputValidator, WorkflowOutputValidator>()
            .AddTransient<IWorkflowOutputService, WorkflowOutputService>()
            .AddTransient<StuckWorkflowExecutionJanitor>()
            .AddTransient<HostLossSweep>()
            .AddTransient<IEntityPlaceholderResolver, EntityPlaceholderResolver>()
            .AddKeyedTransient<IWorkflowHandler, WorkflowHandler>(WorkflowType.GENERIC)
            .AddTransient<OcrTranscriptionService>()
            .AddTransient<IDocumentClassValidationService, DocumentClassValidationService>()
            .AddTransient<IWorkflowExecutionOrchestrator, WorkflowExecutionOrchestrator>()
            .AddTransient<IExecutionFreezeSourceResolver, ExecutionFreezeSourceResolver>()
            .AddTransient<IEmailExpansionService, EmailExpansionService>()
            .AddTransient<BlockExecutionContext>()
            .AddWorkflowScheduling()
            .AddBlockExtensions(platformSettings.BlockExtensions)
            .AddTransient<IGraphBuilderService, GraphBuilderService>()
            .AddTransient<IGraphRepository, GraphRepository>()
            .AddTransient<IGraphQueryService, GraphQueryService>();
    }

    /// <summary>
    /// Adds scoped services to the container.
    /// </summary>
    public static void AddScopedServices(this IServiceCollection services)
    {
        _ = services.AddScoped<UserValidationFilter>()
                .AddReportExport()
                .AddScoped<IRequestContext, RequestContext>()
                .AddScoped<IMfaTokenService, MfaTokenService>()
                .AddScoped<SignInAttempts>()
                .AddScoped<ITokenGenerationService, TokenGenerationService>()
                .AddScoped<IExternalLoginUserResolver, ExternalLoginUserResolver>()
                .AddStoredKeys();
    }

    /// <summary>
    /// Adds singleton services and conditional DI for connectors based on platform settings.
    /// </summary>
    public static void AddSingletonServices(this IServiceCollection services, PlatformSettings platformSettings)
    {
        _ = services.AddSingleton<IJobsService, JobsService>()
          .AddSingleton<ICurrentUserService, CurrentUserService>()
          .AddSingleton<IActingUser, ApiActingUser>()
          .AddSingleton<IWorkflowVariableValidationService, WorkflowVariableValidationService>()
          .AddMemoryCache()
          .AddHttpClient<IExchangeRateService, ExchangeRateService>();

        services.TryAddSingleton<ITenantAccessPolicy, MembershipTenantAccessPolicy>();

        _ = services.AddAgentRuntimeClient(platformSettings);
        _ = services.AddSingleton<IAgentNarrationTidier, AgentNarrationTidier>();
        _ = services.AddSingleton<IWorkflowActivityScriptWriter, WorkflowActivityScriptWriter>();
        _ = services.AddSingleton<IWorkflowActivityScriptEnsurer, WorkflowActivityScriptEnsurer>();

        _ = services
            .AddWorkQueue<DocumentProcessingQueue, DocumentProcessingRequest, DocumentProcessingRunner, DocumentProcessingWorker>();

        _ = services.AddModelConnectors(platformSettings);

        _ = services.AddSingleton<TenantModelFamilyResolver>();

        _ = services.AddSingleton(sp =>
        {
            PlatformSettings config = sp.GetRequiredService<IOptions<PlatformSettings>>().Value;

            int tokenLimit = config.Anthropic.MaxInputTokensPerWindow;

            ILogger<TokenSlidingWindowRateLimiter> logger = sp.GetRequiredService<ILogger<TokenSlidingWindowRateLimiter>>();
            return new TokenSlidingWindowRateLimiter(tokenLimit, TimeSpan.FromMinutes(1), logger);
        });
    }
}
