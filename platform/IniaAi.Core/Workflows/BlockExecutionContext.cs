using System.Collections.Concurrent;
using System.Net;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration;
using IniaAi.Core.AugmentedSearch.Exa.Interfaces;
using IniaAi.Core.Rag;
using IniaAi.Core.Services;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Core.Workflows.StoredKeys;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.FileFormats.Pdf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Core.Workflows;

public class BlockExecutionContext
{
    public ILogger Logger { get; }
    public PlatformSettings PlatformSettings { get; }
    public IChatConnector ChatConnector { get; }
    public RagService RagService { get; }
    public IExchangeRateService ExchangeRateService { get; }
    public DocumentAnalysisService DocumentAnalysisService { get; }
    public IExaSearchService ExaSearchService { get; }
    public TenantModelFamilyResolver ModelFamilyResolver { get; }
    public IFileStorageManager FileStorageManager { get; }
    public IPdfFormat PdfFormat { get; }
    public ITokenUsageService TokenUsageService { get; }

    /// <summary>
    /// The block's own service scope. A strategy that a closed module or an extension registers
    /// resolves its own services here; the open strategies take what they need from the members above.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Null on hosts running with AgentRuntimeSettings.Enabled=false — the shipped default. A
    /// strategy needing it must fail its own block with a descriptive error, never assume it.
    /// The runtime client and trace collector are internals of this seam: a strategy runs
    /// agents through the executor's collect-then-acknowledge lifecycle, never against the raw
    /// client.
    /// </summary>
    public IAgentRunExecutor? AgentRunExecutor { get; }

    /// <summary>
    /// Null when the host registers no stored keys, as in most tests. A block that names a
    /// stored key fails with a descriptive error then.
    /// </summary>
    public IStoredKeyResolver? StoredKeys { get; }

    /// <summary>
    /// Persists the executing block's row mid-run as a checkpoint through the handler's
    /// breaker-guarded writer — never the raw writer, and never the lost-results taint queue,
    /// because a checkpoint carries no result. Bound to this context's own block at wiring
    /// time. Throws when the write did not land, so a strategy that needs the record durable
    /// before spending can stop instead of proceeding unrecorded. Null when the run has no
    /// persisted execution to flush into (editor block/section tests): strategies skip the
    /// flush and the block's state rides the usual post-block persist instead.
    /// </summary>
    public Func<CancellationToken, Task>? FlushBlockStateAsync { get; set; }

    public string WorkflowTypeId { get; set; } = WorkflowType.GENERIC;
    public string? UserId { get; set; }
    public ConcurrentDictionary<string, string> AnonymisationMap { get; set; } = [];
    public Dictionary<string, string?> TextInputs { get; set; } = [];
    public Dictionary<string, Block> ReferencedBlocks { get; set; } = [];
    public List<Page> AllPages { get; set; } = [];
    public BlockAnnotations BlockAnnotations { get; set; } = new();
    public bool IsCitationSource { get; set; } = true;
    public Counterparty? Counterparty { get; set; }
    public List<Counterparty> PeerCompanies { get; set; } = [];
    public string? WorkflowExecutionId { get; set; }

    /// <summary>
    /// The cookies the WebApiCall blocks of one run send and keep. The workflow handler gives
    /// every block of a run the same jar; another run never sees it.
    /// </summary>
    public CookieContainer WebApiCookies { get; set; } = new();

    /// <summary>
    /// The verification leg this invocation runs under — the round axis
    /// <see cref="AgentRunIds.For"/> keys run identity by; a verification loop raises it when
    /// dispatching a redo leg.
    /// </summary>
    public int VerificationRound { get; set; } = AgentRunIds.FIRST_ROUND;

    /// <summary>
    /// Set by the verification loop on the context of a member's redo leg, and only there: the
    /// rendered directive the member receives ahead of its own prompt, plus the mode stamps the
    /// run bookkeeping records. Null on every other invocation, which leaves the composed
    /// prompt byte-for-byte what it is today. This context seam is the dispatch seam D37a's
    /// renderer feeds — the alternative, a caller-composed start request, would lift document
    /// staging and skill rendering out of the strategy.
    /// </summary>
    public VerificationRedoLeg? VerificationRedoLeg { get; set; }

    /// <summary>
    /// True when this invocation belongs to a verification group — a member's leg or the
    /// verifier's own — so the run's session bundle is worth taking into platform custody. Only
    /// a group leg can ever be sent back to redo its work, so every other block leaves the
    /// bundle where it is and costs no storage. Set by the engine at dispatch, which derives
    /// membership from the verifier's declared member list.
    /// </summary>
    public bool BelongsToVerificationGroup { get; set; }

    public string? SectionId { get; set; }
    public string? WorkflowName { get; set; }
    public string? WorkflowVersion { get; set; }
    public string? OrganizationId { get; set; }

    /// <summary>
    /// The template version the execution was stamped with at submit, copied onto
    /// failure-record rows so the longitudinal record reads without a join.
    /// </summary>
    public string? TemplateVersionId { get; set; }
    public string? TemplateContentHash { get; set; }
    public ConcurrentDictionary<string, Dictionary<string, Dictionary<int, Page>>> RagResultsCache { get; set; } = [];

    /// <summary>
    /// Gets or sets the execution context type (WorkflowExecution, BlockTest, SectionTest, etc.).
    /// Used for token usage tracking to distinguish between production runs and test executions.
    /// </summary>
    public TokenExecutionContext ExecutionContext { get; set; } = TokenExecutionContext.WorkflowExecution;

    /// <summary>
    /// Takes a permit from the workflow execution's block-concurrency cap around one billable unit
    /// this block dispatches for itself, such as a verification redo leg. Every context the engine
    /// builds carries the run's real permit, including the editor's single-block run, which takes
    /// no permit for the block itself but would cap any unit the block dispatched. A block already
    /// running inside a unit - which is every block the scheduler dispatched that did not answer
    /// <c>IBlockStrategy.DispatchesNestedUnits</c> - throws here rather than self-waiting.
    /// <see cref="BlockUnitPermit.Unbounded"/> is the default only for a context built by hand,
    /// which today means a strategy unit test; it caps nothing and still refuses a nested unit.
    /// </summary>
    public IBlockUnitPermit UnitPermits { get; set; } = BlockUnitPermit.Unbounded;

    public BlockExecutionContext(
        ILogger<BlockExecutionContext> logger,
        IChatConnector chatConnector,
        RagService ragService,
        IExchangeRateService exchangeRateService,
        DocumentAnalysisService documentAnalysisService,
        IExaSearchService exaSearchService,
        TenantModelFamilyResolver modelFamilyResolver,
        IFileStorageManager fileStorageManager,
        IPdfFormat pdfFormat,
        ITokenUsageService tokenUsageService,
        IOptions<PlatformSettings> platformSettings,
        IServiceProvider services,
        IAgentRunExecutor? agentRunExecutor = null,
        IStoredKeyResolver? storedKeys = null) : this(
            logger,
            chatConnector,
            ragService,
            exchangeRateService,
            documentAnalysisService,
            exaSearchService,
            modelFamilyResolver,
            fileStorageManager,
            pdfFormat,
            tokenUsageService,
            platformSettings.Value,
            services,
            agentRunExecutor,
            storedKeys)
    {
    }

    public BlockExecutionContext(
        ILogger<BlockExecutionContext> logger,
        IChatConnector chatConnector,
        RagService ragService,
        IExchangeRateService exchangeRateService,
        DocumentAnalysisService documentAnalysisService,
        IExaSearchService exaSearchService,
        TenantModelFamilyResolver modelFamilyResolver,
        IFileStorageManager fileStorageManager,
        IPdfFormat pdfFormat,
        ITokenUsageService tokenUsageService,
        PlatformSettings platformSettings,
        IServiceProvider services,
        IAgentRunExecutor? agentRunExecutor = null,
        IStoredKeyResolver? storedKeys = null)
    {
        Logger = logger;
        AgentRunExecutor = agentRunExecutor;
        StoredKeys = storedKeys;
        ChatConnector = chatConnector;
        RagService = ragService;
        ExchangeRateService = exchangeRateService;
        DocumentAnalysisService = documentAnalysisService;
        ExaSearchService = exaSearchService;
        ModelFamilyResolver = modelFamilyResolver;
        FileStorageManager = fileStorageManager;
        PdfFormat = pdfFormat;
        TokenUsageService = tokenUsageService;
        PlatformSettings = platformSettings;
        Services = services;
    }
}
