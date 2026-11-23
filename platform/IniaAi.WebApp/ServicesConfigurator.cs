using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Core.Workflows.Activity;
using IniaAi.Core.Workflows.Agents;
using IniaAi.AiConnection.Common;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Application.Workflows.Interfaces;
using IniaAi.Application.Workflows.Services;
using IniaAi.Common.Interfaces;
using IniaAi.Configuration;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Configuration.Messaging;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Security;
using IniaAi.Core.AugmentedSearch;
using IniaAi.Core.AugmentedSearch.Exa;
using IniaAi.Core.AugmentedSearch.Exa.Interfaces;
using IniaAi.Core.Rag;
using IniaAi.Core.Services;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Services.Models;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.BlockExtensions;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.FileFormats;
using IniaAi.FileFormats.Pdf;
using IniaAi.Infrastructure;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.StoredKeys;
using IniaAi.Services.ContentRefinement;
using IniaAi.Services.CsvImport;
using IniaAi.Services.Documents;
using IniaAi.Services.Email;
using IniaAi.Services.Export;
using IniaAi.Services.Jobs;
using IniaAi.Services.Queues;
using IniaAi.Services.Usage;
using IniaAi.WebApp.Components.Account;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using IniaAi.WebApp.Configuration;
using IniaAi.WebApp.Services;
using IniaAi.WebApp.Services.Signatures;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using IniaAi.Services.FileDownload;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IniaAi.WebApp;

public class ServicesConfigurator
{
    private readonly IServiceCollection _services;
    private readonly ConfigurationManager _configuration;
    private readonly ILogger _logger;

    public ServicesConfigurator(WebApplicationBuilder builder)
    {
        _services = builder.Services;
        _configuration = builder.Configuration;

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            _ = builder.AddSimpleConsole();
        });

        _logger = loggerFactory.CreateLogger<ServicesConfigurator>();
    }

    public void SetupServices()
    {
        _ = _services.AddRazorComponents()
            .AddInteractiveServerComponents();
        _ = _services.AddHttpContextAccessor();

        _ = _services.AddHealthChecks()
            .AddCheck<BrowserSandboxHealthCheck>("Browser sandbox", HealthStatus.Degraded, tags: ["Browser"])
            .AddCheck<TokenUsageRecordingHealthCheck>(TokenUsageRecordingHealthCheck.NAME, HealthStatus.Degraded, tags: ["Usage"]);

        SetupAuth();
        SetupIniaServices();

        _logger.LogInformation("Services setup complete.");
    }

    private void SetupAuth()
    {
        _ = _services.AddCascadingAuthenticationState();
        _ = _services.AddScoped<IdentityUserAccessor>();
        _ = _services.AddScoped<IdentityRedirectManager>();
        _ = _services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        _ = _services.AddScoped<UserAccountAdminService>();
        _ = _services.AddScoped<CallerOrganizations>();

        _ = _services.AddWebAppAuthentication(_configuration);
        _ = _services.AddAdminCookieProtection(_configuration);
        _ = _services.AddForwardedHosts(_configuration);
        _ = _services.AddTrustedProxies(_configuration);
        if (ForwardedHostConfiguration.ForwardedHeadersEnabled(_configuration) && TrustedProxies.From(_configuration).IsEmpty)
        {
            _logger.LogWarning(
                "Forwarded headers are on but {KnownNetworksKey} lists no proxy, so any sender may set the client address. List the load balancer's network.",
                TrustedProxies.KNOWN_NETWORKS_KEY);
        }

        _ = _services.AddSingleton<IEmailSender<User>, IdentityMailSender>();
        _ = _services.AddSingleton<AccountLinks>();
        _ = _services.AddSignInRateLimiting(_configuration);

        _logger.LogInformation("Auth setup complete.");
    }

    private void SetupIniaServices()
    {
        _ = _services.Configure<PlatformSettings>(_configuration.GetSection("PlatformSettings"))
            .Configure<WorkflowExecutionJanitorSettings>(
                _configuration.GetSection(WorkflowExecutionJanitorSettings.SECTION_NAME))
            .Configure<WebApiCallSettings>(_configuration.GetSection(WebApiCallSettings.SECTION_NAME))
            .Configure<IniaAiSettings>(_configuration.GetSection("PlatformSettings:IniaAi"))
            .Configure<AnthropicSettings>(_configuration.GetSection("PlatformSettings:Anthropic"))
            .Configure<CohereSettings>(_configuration.GetSection("PlatformSettings:Cohere"))
            .Configure<MessageProcessorSettings>(_configuration.GetSection("PlatformSettings:MessageProcessor"));

        PlatformSettings platformSettings = _configuration.GetSection("PlatformSettings").Get<PlatformSettings>()
            ?? throw new ArgumentException("PlatformSettings not found in configuration.");

        _ = _services.AddAgentRuntimeClient(platformSettings);
        _ = _services.AddSingleton<IAgentNarrationTidier, AgentNarrationTidier>();
        _ = _services.AddSingleton<IWorkflowActivityScriptWriter, WorkflowActivityScriptWriter>();
        _ = _services.AddSingleton<IWorkflowActivityScriptEnsurer, WorkflowActivityScriptEnsurer>();

        _ = _services.AddLocalization();
        _ = _services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopCenter;
            config.SnackbarConfiguration.PreventDuplicates = false;
            config.SnackbarConfiguration.NewestOnTop = false;
            config.SnackbarConfiguration.ShowCloseIcon = true;
            config.SnackbarConfiguration.VisibleStateDuration = 2000;
            config.SnackbarConfiguration.HideTransitionDuration = 250;
            config.SnackbarConfiguration.ShowTransitionDuration = 250;
            config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
        });
        _ = _services.AddMudMarkdownServices();

        _ = _services.AddTransient<DatabaseMigrationHelper>();
        _ = _services.AddTransient<PdfExtractor>();
        _ = _services.AddOpenFileFormats();
        _ = _services.AddTransient<DocumentProcessingService>();
        _ = _services.AddTransient<IDocumentProcessingService>(sp => sp.GetRequiredService<DocumentProcessingService>());
        _ = _services.AddTransient<DocumentAnalysisService>();
        _ = _services.AddTransient<ExcelSheetAnalysisService>();
        _ = _services.AddTransient<ReconciliationService>();
        _ = _services.AddTransient<RagService>();
        _ = _services.AddTransient<IJobExecutor, JobExecutor>();
        _ = _services.AddTransient<EmailIntakeJobStage>();
        _ = _services.AddTransient<IEmailExpansionService, EmailExpansionService>();
        _ = _services.AddTransient<IExaSearchService, ExaSearchService>();
        _ = _services.AddTransient<IWorkflowOutputBuilder, WorkflowOutputBuilder>();
        _ = _services.AddTransient<IWorkflowOutputValidator, WorkflowOutputValidator>();
        _ = _services.AddTransient<IWorkflowOutputService, WorkflowOutputService>();
        _ = _services.AddTransient<ICsvImportService, CsvImportService>();
        _ = _services.AddTransient<IEntityPlaceholderResolver, EntityPlaceholderResolver>();
        _ = _services.AddWorkflowScheduling();
        _ = _services.AddBlockExtensions(platformSettings.BlockExtensions);
        _ = _services.AddKeyedTransient<IWorkflowHandler, WorkflowHandler>(WorkflowType.GENERIC);
        _ = _services.AddTransient<OcrTranscriptionService>();
        _ = _services.AddWorkQueue<DocumentProcessingQueue, DocumentProcessingRequest, DocumentProcessingRunner, DocumentProcessingWorker>();
        _ = _services.AddTransient<BlockExecutionContext>();
        _ = _services.AddTransient<IDocumentClassValidationService, DocumentClassValidationService>();
        _ = _services.AddSingleton<IWorkflowVariableValidationService, WorkflowVariableValidationService>();
        _ = _services.AddTransient<IWorkflowExecutionOrchestrator, WorkflowExecutionOrchestrator>();
        _ = _services.AddTransient<IExecutionFreezeSourceResolver, ExecutionFreezeSourceResolver>();
        _ = _services.AddTransient<IContentRefinementService, ContentRefinementService>();

        _ = _services.AddScoped<AppState>();
        _ = _services.AddSingleton<CircuitServicesAccessor>();
        _ = _services.AddScoped<CircuitHandler, CircuitServicesAccessorHandler>();
        _ = _services.AddSingleton<IActingUser, AdminActingUser>();
        _ = _services.AddSingleton<DesignerPages>();
        _ = _services.AddScoped<WorkflowTextExportService>();
        _ = _services.AddScoped<ISignatureOverrideReader, SignatureOverrideReader>();
        _ = _services.AddReportExport();
        _ = _services.AddScoped<IMfaTokenService, MfaTokenService>();
        _ = _services.AddScoped<IExternalLoginUserResolver, ExternalLoginUserResolver>();
        _ = _services.AddStoredKeys();

        _ = _services.AddSingleton<IJobsService, JobsService>();

        _ = _services.AddSingleton<IExchangeRateService, ExchangeRateService>();

        _ = _services.AddModelConnectors(platformSettings);

        _ = _services.AddSingleton(sp =>
        {
            PlatformSettings config = sp.GetRequiredService<IOptions<PlatformSettings>>().Value;

            int tokenLimit = config.Anthropic.MaxInputTokensPerWindow;

            ILogger<TokenSlidingWindowRateLimiter> logger = sp.GetRequiredService<ILogger<TokenSlidingWindowRateLimiter>>();
            return new TokenSlidingWindowRateLimiter(tokenLimit, TimeSpan.FromMinutes(1), logger);
        });

        _ = _services.AddSingleton<TenantModelFamilyResolver>();

        _ = _services.AddMemoryCache();

        _ = _services.AddHttpClient<IExchangeRateService, ExchangeRateService>();

        _ = _services.AddHostedService<JobQueueShutdownService>();
        _ = _services.AddHostBootHeartbeat();
        _ = _services.AddHostedService<AgentConversationHostService>();

        _logger.LogInformation("Inia services setup complete.");
    }
}
