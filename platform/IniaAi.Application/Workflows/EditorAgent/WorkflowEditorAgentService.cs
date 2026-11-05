using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Text;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Proposals;
using IniaAi.Domain.Serialization;
using IniaAi.Core.Workflows.Dependencies;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// The editor agent's conversations and drafts. The turns themselves, with their caps, order,
/// start, cancel and retention, belong to the shared conversation layer; this service checks the
/// caller and the workflow, stores the draft and hands the message over. A singleton that opens a
/// scope per call for the database.
/// </summary>
public sealed class WorkflowEditorAgentService : IWorkflowEditorAgentService, IDisposable
{
    /// <summary>
    /// The conversation purpose, and the prefix of every run id an editor conversation buys.
    /// </summary>
    public const string PURPOSE = "edchat";

    public const string SAVE_NOTE_ONE = "1 change proposed by the editor agent";
    public const string SAVE_NOTE_MANY = "{0} changes proposed by the editor agent";
    public const string UNKNOWN_CHANGE_REASON = "The proposal has no such change.";
    public const string ALREADY_ACCEPTED_REASON = "The change is already accepted.";
    public const string OUTBOUND_ALONE_REASON =
        "This change sends data to an outside address. Accept it on its own.";
    public const string UNRESOLVED_TAG_WARNING =
        "{{{0}}} is now read but nothing supplies it. Accept the change that supplies it, or undo.";

    private const string ORDER_FIELD = "Order";

    private const int BYTES_PER_MEGABYTE = 1024 * 1024;
    private const char NODE_SEPARATOR = ':';
    private const int VIEW_CACHE_ENTRIES = 2000;
    private const string ACTIVITY_CACHE_PREFIX = "activity:";
    private const string PROPOSAL_CACHE_PREFIX = "proposal:";

    private static readonly TimeSpan VIEW_CACHE_IDLE = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions _activityJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentConversationTurns _turns;
    private readonly AgentRuntimeSettings _runtime;
    private readonly EditorAgentSettings _settings;
    private readonly IAgentRunActivityFeed? _activityFeed;
    private readonly IChatConnector? _chatConnector;
    private readonly TenantModelFamilyResolver? _tenantModels;
    private readonly ILogger<WorkflowEditorAgentService> _logger;
    private readonly ConcurrentDictionary<string, byte> _warnedTiers = new(StringComparer.Ordinal);
    private readonly MemoryCache _viewCache = new(new MemoryCacheOptions { SizeLimit = VIEW_CACHE_ENTRIES });

    public WorkflowEditorAgentService(
        IServiceScopeFactory scopeFactory,
        IAgentConversationTurns turns,
        IOptions<AgentRuntimeSettings> runtime,
        IOptions<EditorAgentSettings> settings,
        IAgentRunActivityFeed? activityFeed = null,
        IChatConnector? chatConnector = null,
        TenantModelFamilyResolver? tenantModels = null,
        ILogger<WorkflowEditorAgentService>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _turns = turns;
        _runtime = runtime.Value;
        _settings = settings.Value;
        _activityFeed = activityFeed;
        _chatConnector = chatConnector;
        _tenantModels = tenantModels;
        _logger = logger ?? NullLogger<WorkflowEditorAgentService>.Instance;
    }

    public bool IsEnabled => _settings.Enabled && _runtime.Enabled;

    public void Dispose()
    {
        _viewCache.Dispose();
    }

    public async Task<IReadOnlyList<EditorAgentModelOption>> ListModelsAsync(
        string organizationId,
        string? currentModelId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return [];
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        ICoreUnitOfWork core = scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();

        return await EditorAgentModelCatalog.ListAsync(
            core, _runtime, _settings, ResolveTiers(organizationId), currentModelId, cancellationToken);
    }

    public async Task<ErrorOr<EditorAgentConversationView>> GetOrStartConversationAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        if (await CheckCallerAsync(scope.ServiceProvider, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        IUserDataUnitOfWork userData = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        AgentConversation? current = await LiveConversationAsync(userData, organizationId, userId, workflowTypeId, cancellationToken);
        current ??= await CreateConversationAsync(
            userData, organizationId, userId, workflowTypeId, modelId: null, cancellationToken);

        return await ViewAsync(scope.ServiceProvider, current, cancellationToken);
    }

    public async Task<ErrorOr<EditorAgentConversationView>> StartNewConversationAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        string? modelId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        if (modelId is not null
            && !EditorAgentModelCatalog.IsOffered(_runtime, _settings, ResolveTiers(organizationId), currentModelId: null, modelId))
        {
            return EditorAgentErrors.ModelNotAllowed(modelId);
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        if (await CheckCallerAsync(scope.ServiceProvider, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        IUserDataUnitOfWork userData = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        List<string> previous = await userData.Repository<AgentConversation>()
            .GetAll()
            .AsNoTracking()
            .Where(conversation => conversation.OrganizationId == organizationId
                && conversation.UserId == userId
                && conversation.Purpose == PURPOSE
                && conversation.SubjectId == workflowTypeId
                && conversation.EndedAt == null)
            .Select(static conversation => conversation.Id)
            .ToListAsync(cancellationToken);

        foreach (string conversationId in previous)
        {
            await _turns.EndAsync(organizationId, userId, PURPOSE, conversationId, cancellationToken);
        }

        AgentConversation created = await CreateConversationAsync(
            userData, organizationId, userId, workflowTypeId, modelId, cancellationToken);

        return await ViewAsync(scope.ServiceProvider, created, cancellationToken);
    }

    public async Task<ErrorOr<EditorAgentConversationView>> GetConversationAsync(
        string organizationId,
        string userId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        IUserDataUnitOfWork userData = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        AgentConversation? conversation = await OwnedConversationAsync(
            userData, organizationId, userId, conversationId, cancellationToken);

        return conversation is null
            ? AgentConversationErrors.NotFound
            : await ViewAsync(scope.ServiceProvider, conversation, cancellationToken);
    }

    public async Task<ErrorOr<EditorAgentReportPage>> ListReportsAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        EditorAgentReportQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        if (await CheckCallerAsync(scope.ServiceProvider, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        query ??= new EditorAgentReportQuery();
        EditorAgentReports reports = scope.ServiceProvider.GetRequiredService<EditorAgentReports>();
        string listedTypeId = string.IsNullOrWhiteSpace(query.WorkflowTypeId) ? workflowTypeId : query.WorkflowTypeId;
        if (listedTypeId != workflowTypeId)
        {
            IReadOnlyList<EditorAgentWorkflowTypeOption> types = await reports.AccessibleTypesAsync(organizationId, workflowTypeId, cancellationToken);
            if (types.All(type => type.Id != listedTypeId))
            {
                return EditorAgentErrors.Forbidden;
            }
        }

        return await reports.PageAsync(organizationId, listedTypeId, query, cancellationToken);
    }

    public async Task<ErrorOr<IReadOnlyList<EditorAgentWorkflowTypeOption>>> ListReportWorkflowTypesAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        if (await CheckCallerAsync(scope.ServiceProvider, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        EditorAgentReports reports = scope.ServiceProvider.GetRequiredService<EditorAgentReports>();
        return ErrorOrFactory.From(await reports.AccessibleTypesAsync(organizationId, workflowTypeId, cancellationToken));
    }

    public async Task<ErrorOr<EditorAgentTurnView>> SendMessageAsync(
        EditorAgentMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return EditorAgentErrors.EmptyMessage;
        }

        if (message.Text.Length > EditorAgentErrors.MAX_MESSAGE_LENGTH)
        {
            return EditorAgentErrors.MessageTooLong;
        }

        if (!EditorAgentModelCatalog.IsAllowed(_runtime, message.ModelId))
        {
            return EditorAgentErrors.ModelNotAllowed(message.ModelId);
        }

        List<string> reportIds = [.. message.ReportIds.Distinct(StringComparer.Ordinal)];
        if (reportIds.Count > EditorAgentReports.MAX_PICKED)
        {
            return EditorAgentErrors.TooManyReports;
        }

        // Serialized on the caller's thread: the workflow is the editor's live object, and nothing
        // else may touch it once the caller has moved on.
        string workflowJson = message.Workflow.ToExportJson(includeRunTraces: true);
        byte[] workflowBytes = Encoding.UTF8.GetBytes(workflowJson);
        if (workflowBytes.LongLength > (long)_settings.MaxDraftMegabytes * BYTES_PER_MEGABYTE)
        {
            return EditorAgentErrors.DraftTooLarge(_settings.MaxDraftMegabytes);
        }

        string contentSha256 = Convert.ToHexStringLower(SHA256.HashData(workflowBytes));

        using IServiceScope scope = _scopeFactory.CreateScope();
        IUserDataUnitOfWork userData = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        AgentConversation? conversation = await OwnedConversationAsync(
            userData, message.OrganizationId, message.UserId, message.ConversationId, cancellationToken);
        if (conversation is null || conversation.EndedAt is not null)
        {
            return AgentConversationErrors.NotFound;
        }

        if (!string.Equals(message.Workflow.WorkflowTypeId, conversation.SubjectId, StringComparison.Ordinal))
        {
            return EditorAgentErrors.WorkflowMismatch;
        }

        if (await CheckCallerAsync(
            scope.ServiceProvider, message.OrganizationId, message.UserId, conversation.SubjectId!, cancellationToken) is Error refused)
        {
            return refused;
        }

        if (!EditorAgentModelCatalog.IsOffered(
            _runtime, _settings, ResolveTiers(message.OrganizationId), conversation.ModelId, message.ModelId))
        {
            return EditorAgentErrors.ModelNotAllowed(message.ModelId);
        }

        EditorAgentDailySpend spend = await ReadDailySpendAsync(userData, message.OrganizationId, DateTime.UtcNow, cancellationToken);
        if (spend.IsUsedUp)
        {
            return EditorAgentErrors.DailySpendUsed(spend.UsedUsd, spend.BatchUsd);
        }

        string turnId = Guid.NewGuid().ToString();
        IReadOnlyList<EditorAgentReportSummary> attached = [];
        if (reportIds.Count > 0)
        {
            EditorAgentReports reports = scope.ServiceProvider.GetRequiredService<EditorAgentReports>();
            IReadOnlyList<EditorAgentWorkflowTypeOption> types = await reports.AccessibleTypesAsync(
                message.OrganizationId, conversation.SubjectId!, cancellationToken);
            IReadOnlyList<string> owned = await reports.OwnedIdsAsync(
                message.OrganizationId, [.. types.Select(static type => type.Id)], reportIds, cancellationToken);
            if (owned.Count != reportIds.Count)
            {
                return EditorAgentErrors.ReportNotFound;
            }

            IReadOnlyDictionary<string, EditorAgentReportSummary> summaries = await reports.SummariesByIdAsync(
                message.OrganizationId, reportIds, cancellationToken);
            attached = [.. reportIds.Select(id => summaries.GetValueOrDefault(id)).OfType<EditorAgentReportSummary>()];
        }

        EditorAgentTurnFeedback? feedback = null;
        if (message.FeedbackItemIds.Count > 0)
        {
            ErrorOr<EditorAgentTurnFeedback> sentFeedback = await FeedbackToSendAsync(
                scope.ServiceProvider, message, conversation, turnId, cancellationToken);
            if (sentFeedback.IsError)
            {
                return sentFeedback.Errors;
            }

            feedback = sentFeedback.Value;
        }

        ErrorOr<EditorAgentDraft> draft = await DraftAsync(userData, conversation, workflowJson, contentSha256, cancellationToken);
        if (draft.IsError)
        {
            return draft.Errors;
        }

        // Recorded before the turn, which may start at once and stages the picks first thing.
        if (feedback is not null)
        {
            userData.Repository<EditorAgentTurnFeedback>().Add(feedback);
        }

        EditorAgentTurnReports? picks = null;
        if (reportIds.Count > 0)
        {
            picks = new EditorAgentTurnReports
            {
                TurnId = turnId,
                ConversationId = conversation.Id,
                PickedExecutionIds = string.Join(',', reportIds),
            };
            userData.Repository<EditorAgentTurnReports>().Add(picks);
        }

        if (feedback is not null || picks is not null)
        {
            _ = await userData.CompleteAsync(cancellationToken);
        }

        ErrorOr<AgentConversationTurn> submitted = await _turns.SubmitAsync(
            new AgentConversationSubmission(
                message.OrganizationId,
                message.UserId,
                PURPOSE,
                conversation.Id,
                message.Text.Trim(),
                message.ModelId,
                draft.Value.Id)
            {
                TurnId = turnId,
            },
            cancellationToken);

        if (submitted.IsError && (picks is not null || feedback is not null))
        {
            if (picks is not null)
            {
                userData.Repository<EditorAgentTurnReports>().HardRemove(picks);
            }

            if (feedback is not null)
            {
                userData.Repository<EditorAgentTurnFeedback>().HardRemove(feedback);
            }

            _ = await userData.CompleteAsync(CancellationToken.None);
        }

        return submitted.IsError
            ? submitted.Errors
            : TurnView(submitted.Value, draft.Value.Revision) with
            {
                AttachedReports = attached,
                FeedbackSent = feedback?.Sent.Count ?? 0,
            };
    }

    /// <summary>
    /// The chosen feedback items still open, as the row the turn stages them from: of the
    /// conversation's type, and of the caller's organization unless the caller is a super admin.
    /// </summary>
    private static async Task<ErrorOr<EditorAgentTurnFeedback>> FeedbackToSendAsync(
        IServiceProvider services,
        EditorAgentMessage message,
        AgentConversation conversation,
        string turnId,
        CancellationToken cancellationToken)
    {
        string[] ids = [.. message.FeedbackItemIds.Distinct(StringComparer.Ordinal)];
        if (ids.Length > EditorAgentFeedback.MAX_SENT)
        {
            return EditorAgentErrors.TooManyFeedback;
        }

        bool acrossOrganizations = await IsSuperAdminAsync(services, message.UserId);
        EditorAgentFeedback feedback = services.GetRequiredService<EditorAgentFeedback>();
        List<ReportFeedbackItem> chosen = await feedback.ChosenOpenAsync(
            message.OrganizationId, acrossOrganizations, conversation.SubjectId!, ids, cancellationToken);
        if (chosen.Count == 0)
        {
            return EditorAgentErrors.NoOpenFeedback;
        }

        return new EditorAgentTurnFeedback
        {
            TurnId = turnId,
            ConversationId = conversation.Id,
            FeedbackItemIds = string.Join(',', chosen.Select(static item => item.Id)),
            OpenCount = await feedback.CountOpenAsync(
                message.OrganizationId, acrossOrganizations, conversation.SubjectId!, cancellationToken),
        };
    }

    public async Task<ErrorOr<int>> CountOpenFeedbackAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        if (await CheckCallerAsync(scope.ServiceProvider, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        bool acrossOrganizations = await IsSuperAdminAsync(scope.ServiceProvider, userId);
        return await scope.ServiceProvider.GetRequiredService<EditorAgentFeedback>()
            .CountOpenAsync(organizationId, acrossOrganizations, workflowTypeId, cancellationToken);
    }

    public async Task<ErrorOr<IReadOnlyList<EditorAgentFeedbackOption>>> ListOpenFeedbackAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        if (await CheckCallerAsync(scope.ServiceProvider, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        bool acrossOrganizations = await IsSuperAdminAsync(scope.ServiceProvider, userId);
        List<ReportFeedbackItem> open = await scope.ServiceProvider.GetRequiredService<EditorAgentFeedback>()
            .NewestOpenAsync(organizationId, acrossOrganizations, workflowTypeId, cancellationToken);
        return open
            .Select(static item => new EditorAgentFeedbackOption(item.Id, item.Category, item.Comment, item.QuotedText, item.CreatedAt))
            .ToList();
    }

    public async Task<ErrorOr<Deleted>> DeleteFeedbackAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        string feedbackItemId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        if (await CheckCallerAsync(scope.ServiceProvider, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        bool acrossOrganizations = await IsSuperAdminAsync(scope.ServiceProvider, userId);
        if (!await scope.ServiceProvider.GetRequiredService<EditorAgentFeedback>()
            .DeleteAsync(organizationId, acrossOrganizations, workflowTypeId, feedbackItemId, cancellationToken))
        {
            return EditorAgentErrors.FeedbackNotFound;
        }

        _logger.LogInformation(
            "Feedback {FeedbackId} on workflow type {WorkflowTypeId} deleted by user {UserId} in the editor agent",
            LogSanitizer.ForLog(feedbackItemId), workflowTypeId, userId);
        return Result.Deleted;
    }

    public async Task<ErrorOr<Success>> DismissFeedbackAsync(
        string organizationId,
        string userId,
        string turnId,
        string feedbackItemId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        IUserDataUnitOfWork userData = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        string? workflowTypeId = await userData.Repository<AgentConversationTurn>()
            .GetAll()
            .AsNoTracking()
            .Where(turn => turn.Id == turnId
                && turn.Conversation!.OrganizationId == organizationId
                && turn.Conversation.UserId == userId
                && turn.Conversation.Purpose == PURPOSE)
            .Select(static turn => turn.Conversation!.SubjectId)
            .SingleOrDefaultAsync(cancellationToken);
        if (workflowTypeId is null)
        {
            return EditorAgentErrors.FeedbackNotSuggested;
        }

        if (await CheckCallerAsync(scope.ServiceProvider, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        string? suggestionsJson = await userData.Repository<EditorAgentTurnFeedback>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.TurnId == turnId)
            .Select(static row => row.DismissSuggestionsJson)
            .SingleOrDefaultAsync(cancellationToken);
        EditorAgentDismissSuggestion? suggestion = EditorAgentFeedback.Dismissals(suggestionsJson)
            .FirstOrDefault(candidate => candidate.FeedbackItemId == feedbackItemId);
        ReportFeedbackItem? item = suggestion is null
            ? null
            : await userData.Repository<ReportFeedbackItem>()
                .GetAll()
                .SingleOrDefaultAsync(row => row.Id == feedbackItemId, cancellationToken);
        if (suggestion is null || item is null)
        {
            return EditorAgentErrors.FeedbackNotSuggested;
        }

        if (item.OrganizationId != organizationId && !await IsSuperAdminAsync(scope.ServiceProvider, userId))
        {
            return EditorAgentErrors.Forbidden;
        }

        if (item.Status != ReportFeedbackStatus.Open)
        {
            return Result.Success;
        }

        item.SetStatus(ReportFeedbackStatus.Dismissed, suggestion.Reason, userId);
        _ = await userData.CompleteAsync(cancellationToken);
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> UnlockDailySpendAsync(
        string organizationId,
        string userId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        IUserDataUnitOfWork userData = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        AgentConversation? conversation = await OwnedConversationAsync(
            userData, organizationId, userId, conversationId, cancellationToken);
        if (conversation is null || conversation.EndedAt is not null)
        {
            return AgentConversationErrors.NotFound;
        }

        if (await CheckCallerAsync(
            scope.ServiceProvider, organizationId, userId, conversation.SubjectId!, cancellationToken) is Error refused)
        {
            return refused;
        }

        DateTime now = DateTime.UtcNow;
        EditorAgentDailySpend spend = await ReadDailySpendAsync(userData, organizationId, now, cancellationToken);

        // Another user may have unlocked a batch since this user saw the warning.
        if (!spend.IsUsedUp)
        {
            return Result.Success;
        }

        userData.Repository<EditorAgentSpendUnlock>().Add(new EditorAgentSpendUnlock
        {
            OrganizationId = organizationId,
            UserId = userId,
            Day = DateOnly.FromDateTime(now),
            FromBatches = spend.Batches,
            Batches = spend.BatchesToUnlock,
            UnlockedAt = now,
        });

        try
        {
            _ = await userData.CompleteAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (scope.ServiceProvider.GetRequiredService<IDbExceptionAnalyzer>().IsUniqueConstraintViolation(exception))
        {
            // Another user unlocked from the same point at the same moment; theirs stands.
            return Result.Success;
        }

        _logger.LogInformation(
            "User {UserId} unlocked {Batches} more batches of {BatchUsd} USD of workflow agent spend for organization {OrganizationId}, with {UsedUsd} USD used today",
            userId,
            spend.BatchesToUnlock,
            spend.BatchUsd,
            organizationId,
            spend.UsedUsd);

        return Result.Success;
    }

    private Task<EditorAgentDailySpend> ReadDailySpendAsync(
        IUserDataUnitOfWork userData,
        string organizationId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        return EditorAgentDailySpend.ReadAsync(
            userData, organizationId, _settings.DailySpendBatchUsd, _settings.MaxBudgetUsdPerMessage, nowUtc, cancellationToken);
    }

    public async Task<ErrorOr<Success>> CancelTurnAsync(
        string organizationId,
        string userId,
        string turnId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        return await _turns.CancelAsync(new AgentConversationTurnKey(organizationId, userId, PURPOSE, turnId), cancellationToken);
    }

    public async Task<ErrorOr<EditorAgentInFlightTurn?>> GetInFlightTurnAsync(
        string organizationId,
        string userId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        IUserDataUnitOfWork userData = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        var owned = await userData.Repository<AgentConversation>()
            .GetAll()
            .AsNoTracking()
            .Where(conversation => conversation.Id == conversationId
                && conversation.OrganizationId == organizationId
                && conversation.UserId == userId
                && conversation.Purpose == PURPOSE)
            .Select(static conversation => new
            {
                InFlight = conversation.Turns
                    .Where(static turn => turn.Status == AgentConversationTurnStatus.Queued
                        || turn.Status == AgentConversationTurnStatus.Running)
                    .OrderBy(static turn => turn.Number)
                    .Select(static turn => new { turn.Number, turn.Status, turn.DispatchedAt })
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (owned is null)
        {
            return AgentConversationErrors.NotFound;
        }

        if (owned.InFlight is null)
        {
            return (EditorAgentInFlightTurn?)null;
        }

        AgentRunActivityEntry? latest = _activityFeed?
            .SnapshotForConversation(organizationId, conversationId)
            .LastOrDefault(static entry => !entry.Settled);

        return new EditorAgentInFlightTurn(
            owned.InFlight.Number,
            owned.InFlight.Status,
            owned.InFlight.Status == AgentConversationTurnStatus.Queued && owned.InFlight.DispatchedAt is null,
            latest?.Digest);
    }

    public async Task<ErrorOr<EditorAgentApplyResult>> AcceptChangesAsync(
        EditorAgentAcceptRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        ErrorOr<OwnedProposal> owned = await OwnedProposalAsync(
            scope.ServiceProvider, request.OrganizationId, request.UserId, request.TurnId, cancellationToken);
        if (owned.IsError)
        {
            return owned.Errors;
        }

        if (!string.Equals(request.Workflow.WorkflowTypeId, owned.Value.WorkflowTypeId, StringComparison.Ordinal))
        {
            return EditorAgentErrors.WorkflowMismatch;
        }

        EditorAgentProposal row = owned.Value.Row;
        WorkflowProposal proposal = row.Proposal;
        Dictionary<string, EditorAgentChangeDecision> decisions = row.Decisions
            .ToDictionary(static decision => decision.ChangeId, StringComparer.Ordinal);
        var notApplied = new List<ProposalNotApplied>();
        List<ProposedChange> chosen = request.ChangeIds is null
            ? [.. proposal.Changes.Where(change => AcceptsInBulk(change) && Undecided(decisions, change))]
            : Named(proposal, decisions, request, notApplied);
        chosen.AddRange(proposal.RenumberingFor(chosen).Where(change => Undecided(decisions, change)));

        HashSet<DependencyIssueKey> issuesBefore = chosen.Count == 0 ? [] : UnresolvedTags(request.Workflow);
        ProposalApplyResult result = WorkflowProposalApplier.Apply(
            request.Workflow,
            chosen,
            new HashSet<string>(request.TakeProposed, StringComparer.Ordinal),
            MintedKeys(proposal, row.Decisions));

        if (result.Applied.Count > 0)
        {
            ProposalUndo undo = row.Undo ?? new ProposalUndo();
            undo.Merge(result.Undo);
            row.Undo = undo;
            row.UpdatedAt = DateTime.UtcNow;
            row.Version = EditorAgentProposal.NewVersion();
            foreach (ProposalAppliedChange applied in result.Applied)
            {
                Decide(row, decisions, applied.ChangeId, EditorAgentChangeDecisionStatus.Accepted, applied.MintedStableId);
            }

            if (await SaveDecisionsAsync(scope.ServiceProvider, cancellationToken) is Error failed)
            {
                _ = WorkflowProposalApplier.Undo(request.Workflow, result.Undo);
                return failed;
            }
        }

        List<string> warnings = result.Applied.Count == 0
            ? []
            : [.. UnresolvedTags(request.Workflow)
                .Where(issue => !issuesBefore.Contains(issue))
                .Select(static issue => string.Format(CultureInfo.InvariantCulture, UNRESOLVED_TAG_WARNING, issue.Tag))
                .Distinct(StringComparer.Ordinal)];

        return new EditorAgentApplyResult(row.Id, result.Applied, result.Conflicts, [.. notApplied, .. result.NotApplied])
        {
            Warnings = warnings,
        };
    }

    public async Task<ErrorOr<EditorAgentUndoResult>> UndoChangesAsync(
        string organizationId,
        string userId,
        string turnId,
        Workflow workflow,
        IReadOnlyCollection<string>? changeIds = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        ErrorOr<OwnedProposal> owned = await OwnedProposalAsync(scope.ServiceProvider, organizationId, userId, turnId, cancellationToken);
        if (owned.IsError)
        {
            return owned.Errors;
        }

        if (!string.Equals(workflow.WorkflowTypeId, owned.Value.WorkflowTypeId, StringComparison.Ordinal))
        {
            return EditorAgentErrors.WorkflowMismatch;
        }

        EditorAgentProposal row = owned.Value.Row;
        if (row.Undo is not ProposalUndo undo)
        {
            return EditorAgentErrors.NothingToUndo;
        }

        HashSet<string>? only = changeIds is null ? null : UndoScope(row, changeIds);
        if (only is { Count: 0 })
        {
            return EditorAgentErrors.NothingToUndo;
        }

        ProposalUndoOutcome outcome = WorkflowProposalApplier.Undo(workflow, undo, only);
        HashSet<string> kept = [.. outcome.Conflicts.Select(static conflict => conflict.ChangeId)];

        List<string> undone = [];
        foreach (EditorAgentChangeDecision decision in row.Decisions.Where(d => d.Status == EditorAgentChangeDecisionStatus.Accepted
            && !kept.Contains(d.ChangeId)
            && (only is null || only.Contains(d.ChangeId))))
        {
            decision.Status = EditorAgentChangeDecisionStatus.Undone;
            decision.DecidedAt = DateTime.UtcNow;
            decision.MintedStableId = null;
            undone.Add(decision.ChangeId);
        }

        row.Undo = only is null || undo.IsEmpty ? null : undo;
        row.UpdatedAt = DateTime.UtcNow;
        row.Version = EditorAgentProposal.NewVersion();
        if (await SaveDecisionsAsync(scope.ServiceProvider, cancellationToken) is Error failed)
        {
            return failed;
        }

        return new EditorAgentUndoResult(undone) { Conflicts = outcome.Conflicts };
    }

    public async Task<ErrorOr<Success>> RejectChangesAsync(
        string organizationId,
        string userId,
        string turnId,
        IReadOnlyList<string>? changeIds,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return EditorAgentErrors.Disabled;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        ErrorOr<OwnedProposal> owned = await OwnedProposalAsync(scope.ServiceProvider, organizationId, userId, turnId, cancellationToken);
        if (owned.IsError)
        {
            return owned.Errors;
        }

        EditorAgentProposal row = owned.Value.Row;
        Dictionary<string, EditorAgentChangeDecision> decisions = row.Decisions
            .ToDictionary(static decision => decision.ChangeId, StringComparer.Ordinal);
        HashSet<string> known = [.. row.Proposal.Changes.Select(static change => change.Id)];
        IEnumerable<string> rejected = (changeIds ?? [.. known]).Where(known.Contains);

        foreach (string changeId in rejected)
        {
            if (decisions.TryGetValue(changeId, out EditorAgentChangeDecision? decided)
                && decided.Status != EditorAgentChangeDecisionStatus.Undone)
            {
                continue;
            }

            Decide(row, decisions, changeId, EditorAgentChangeDecisionStatus.Rejected, mintedStableId: null);
        }

        row.Version = EditorAgentProposal.NewVersion();
        return await SaveDecisionsAsync(scope.ServiceProvider, cancellationToken) is Error failed ? failed : Result.Success;
    }

    public async Task<ErrorOr<EditorAgentSaveNote?>> PeekSaveNoteAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return (EditorAgentSaveNote?)null;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        IUserDataUnitOfWork userData = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        List<EditorAgentChangeDecision> unsaved = await UnsavedAsync(
            userData, organizationId, userId, workflowTypeId, decisionIds: null, cancellationToken);

        int changes = unsaved
            .GroupBy(static decision => decision.ProposalId + NODE_SEPARATOR + NodeOf(decision.ChangeId), StringComparer.Ordinal)
            .Count(static node => node.Any(static decision => FieldOf(decision.ChangeId) != ORDER_FIELD));
        if (changes == 0)
        {
            return (EditorAgentSaveNote?)null;
        }

        if (await CheckCallerAsync(scope.ServiceProvider, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        string count = changes == 1 ? SAVE_NOTE_ONE : string.Format(CultureInfo.InvariantCulture, SAVE_NOTE_MANY, changes);
        List<string> overviews = await OverviewsAsync(userData, unsaved, cancellationToken);
        string text = overviews.Count == 0 ? count : $"{count}. {string.Join(' ', overviews)}";
        return new EditorAgentSaveNote(text, [.. unsaved.Select(static decision => decision.Id)]);
    }

    public async Task<ErrorOr<Success>> CommitSaveNoteAsync(
        string organizationId,
        string userId,
        string workflowTypeId,
        EditorAgentSaveNote note,
        string versionId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Result.Success;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        if (await CheckCallerAsync(scope.ServiceProvider, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        IUserDataUnitOfWork userData = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        List<EditorAgentChangeDecision> unsaved = await UnsavedAsync(
            userData, organizationId, userId, workflowTypeId, note.DecisionIds, cancellationToken);
        DateTime savedAt = DateTime.UtcNow;
        foreach (EditorAgentChangeDecision decision in unsaved)
        {
            decision.SavedAt = savedAt;
        }

        List<string> proposalIds = [.. unsaved.Select(static decision => decision.ProposalId).Distinct(StringComparer.Ordinal)];
        List<EditorAgentProposal> proposals = await userData.Repository<EditorAgentProposal>()
            .GetAll()
            .Where(proposal => proposalIds.Contains(proposal.Id))
            .ToListAsync(cancellationToken);
        foreach (EditorAgentProposal proposal in proposals)
        {
            proposal.Version = EditorAgentProposal.NewVersion();
        }

        int addressed = await MarkFeedbackAddressedAsync(
            scope.ServiceProvider, organizationId, userId, workflowTypeId, unsaved, proposals, versionId, cancellationToken);

        _ = await userData.CompleteAsync(cancellationToken);
        if (addressed > 0)
        {
            _logger.LogInformation(
                "A save of workflow type {WorkflowTypeId} as version {VersionId} addressed {Count} report feedback item(s)",
                workflowTypeId, versionId, addressed);
        }

        return Result.Success;
    }

    /// <summary>
    /// Marks addressed, unsaved, the open items the agent named for the saved changes, of those
    /// their conversations were sent and the caller may act on. Returns how many it marked.
    /// </summary>
    private static async Task<int> MarkFeedbackAddressedAsync(
        IServiceProvider services,
        string organizationId,
        string userId,
        string workflowTypeId,
        List<EditorAgentChangeDecision> saved,
        List<EditorAgentProposal> proposals,
        string versionId,
        CancellationToken cancellationToken)
    {
        var cited = new HashSet<string>(StringComparer.Ordinal);
        foreach (EditorAgentProposal proposal in proposals)
        {
            HashSet<string> changeIds = [.. saved
                .Where(decision => decision.ProposalId == proposal.Id)
                .Select(static decision => decision.ChangeId)];
            cited.UnionWith(EditorAgentFeedback.Cited(proposal.Proposal, changeIds));
        }

        if (cited.Count == 0)
        {
            return 0;
        }

        IUserDataUnitOfWork userData = services.GetRequiredService<IUserDataUnitOfWork>();
        List<string> conversationIds = [.. proposals.Select(static proposal => proposal.ConversationId).Distinct(StringComparer.Ordinal)];
        List<string> sentLists = await userData.Repository<EditorAgentTurnFeedback>()
            .GetAll()
            .AsNoTracking()
            .Where(row => conversationIds.Contains(row.ConversationId))
            .Select(static row => row.FeedbackItemIds)
            .ToListAsync(cancellationToken);
        cited.IntersectWith(sentLists.SelectMany(static ids => ids.Split(',', StringSplitOptions.RemoveEmptyEntries)));
        if (cited.Count == 0)
        {
            return 0;
        }

        bool acrossOrganizations = await IsSuperAdminAsync(services, userId);
        List<string> ids = [.. cited];
        List<ReportFeedbackItem> items = await userData.Repository<ReportFeedbackItem>()
            .GetAll()
            .Where(item => ids.Contains(item.Id)
                && item.Status == ReportFeedbackStatus.Open
                && item.WorkflowTypeId == workflowTypeId
                && (acrossOrganizations || item.OrganizationId == organizationId))
            .ToListAsync(cancellationToken);
        foreach (ReportFeedbackItem item in items)
        {
            item.MarkAddressed(versionId, userId);
        }

        return items.Count;
    }

    /// <summary>The named changes that are accepted and unsaved, with the accepted renumbering that came with them.</summary>
    private static HashSet<string> UndoScope(EditorAgentProposal row, IReadOnlyCollection<string> changeIds)
    {
        HashSet<string> accepted = [.. row.Decisions
            .Where(static decision => decision.Status == EditorAgentChangeDecisionStatus.Accepted && decision.SavedAt == null)
            .Select(static decision => decision.ChangeId)];
        WorkflowProposal proposal = row.Proposal;
        List<ProposedChange> named = [.. proposal.Changes.Where(change => changeIds.Contains(change.Id) && accepted.Contains(change.Id))];

        return [.. named.Concat(proposal.RenumberingFor(named)).Select(static change => change.Id).Where(accepted.Contains)];
    }

    /// <summary>The agent's overview of each proposal the decisions belong to, oldest proposal first.</summary>
    private static async Task<List<string>> OverviewsAsync(
        IUserDataUnitOfWork userData,
        List<EditorAgentChangeDecision> decisions,
        CancellationToken cancellationToken)
    {
        List<string> proposalIds = [.. decisions.Select(static decision => decision.ProposalId).Distinct(StringComparer.Ordinal)];
        List<EditorAgentProposal> proposals = await userData.Repository<EditorAgentProposal>()
            .GetAll()
            .AsNoTracking()
            .Where(proposal => proposalIds.Contains(proposal.Id))
            .OrderBy(static proposal => proposal.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. proposals.Select(static proposal => proposal.Proposal.Overview).OfType<string>()];
    }

    private static async Task<List<EditorAgentChangeDecision>> UnsavedAsync(
        IUserDataUnitOfWork userData,
        string organizationId,
        string userId,
        string workflowTypeId,
        IReadOnlyList<string>? decisionIds,
        CancellationToken cancellationToken)
    {
        IQueryable<EditorAgentChangeDecision> query = userData.Repository<EditorAgentChangeDecision>()
            .GetAll()
            .Where(decision => decision.Status == EditorAgentChangeDecisionStatus.Accepted
                && decision.SavedAt == null
                && decision.Proposal!.Turn!.Conversation!.OrganizationId == organizationId
                && decision.Proposal.Turn.Conversation.UserId == userId
                && decision.Proposal.Turn.Conversation.Purpose == PURPOSE
                && decision.Proposal.Turn.Conversation.SubjectId == workflowTypeId);
        if (decisionIds is not null)
        {
            query = query.Where(decision => decisionIds.Contains(decision.Id));
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// An accept-all takes only changes a person need not look at on their own: none that sends
    /// data outside, none the agent did not list, and none that makes a Library copy differ.
    /// </summary>
    private static bool AcceptsInBulk(ProposedChange change)
    {
        return change.CanAccept && !change.SendsDataOutside && !change.MissingFromSummary && !change.LibraryLinked;
    }

    private static bool Undecided(Dictionary<string, EditorAgentChangeDecision> decisions, ProposedChange change)
    {
        return !decisions.TryGetValue(change.Id, out EditorAgentChangeDecision? decided)
            || decided.Status == EditorAgentChangeDecisionStatus.Undone;
    }

    /// <summary>
    /// The named changes. One that sends data outside applies only when it is the call's only
    /// change or the caller confirmed it by id, so it is always accepted on its own.
    /// </summary>
    private static List<ProposedChange> Named(
        WorkflowProposal proposal,
        Dictionary<string, EditorAgentChangeDecision> decisions,
        EditorAgentAcceptRequest request,
        List<ProposalNotApplied> notApplied)
    {
        Dictionary<string, ProposedChange> byId = proposal.Changes.ToDictionary(static change => change.Id, StringComparer.Ordinal);
        List<string> named = [.. request.ChangeIds!.Distinct(StringComparer.Ordinal)];
        var confirmed = new HashSet<string>(request.ConfirmOutbound, StringComparer.Ordinal);
        var chosen = new List<ProposedChange>();

        foreach (string changeId in named)
        {
            if (!byId.TryGetValue(changeId, out ProposedChange? change))
            {
                notApplied.Add(new ProposalNotApplied(changeId, UNKNOWN_CHANGE_REASON));
            }
            else if (!change.CanAccept)
            {
                notApplied.Add(new ProposalNotApplied(changeId, change.RefusedReason!));
            }
            else if (decisions.TryGetValue(changeId, out EditorAgentChangeDecision? decided)
                && decided.Status == EditorAgentChangeDecisionStatus.Accepted)
            {
                notApplied.Add(new ProposalNotApplied(changeId, ALREADY_ACCEPTED_REASON));
            }
            else if (change.SendsDataOutside && named.Count != 1 && !confirmed.Contains(changeId))
            {
                notApplied.Add(new ProposalNotApplied(changeId, OUTBOUND_ALONE_REASON));
            }
            else
            {
                chosen.Add(change);
            }
        }

        return chosen;
    }

    /// <summary>The stable ids earlier accepts minted, by the key the proposal gives each added node.</summary>
    private static Dictionary<string, string> MintedKeys(WorkflowProposal proposal, IEnumerable<EditorAgentChangeDecision> decisions)
    {
        Dictionary<string, string?> keys = proposal.Changes
            .Where(static change => change.Kind == ProposedChangeKind.Added)
            .ToDictionary(static change => change.Id, static change => change.TargetKey, StringComparer.Ordinal);

        return decisions
            .Where(decision => decision.Status == EditorAgentChangeDecisionStatus.Accepted
                && decision.MintedStableId is not null
                && keys.GetValueOrDefault(decision.ChangeId) is not null)
            .ToDictionary(decision => keys[decision.ChangeId]!, static decision => decision.MintedStableId!, StringComparer.Ordinal);
    }

    private static HashSet<DependencyIssueKey> UnresolvedTags(Workflow workflow)
    {
        return [.. WorkflowDependencyMap.ForTemplate(workflow).Issues
            .Where(static issue => issue.Kind == DependencyIssueKind.UnresolvedTag && issue.Tag is not null)
            .SelectMany(static issue => issue.BlockIds.Select(block => new DependencyIssueKey(block, issue.Tag!)))];
    }

    /// <summary>
    /// Writes the decisions. A lost race with another tab or a database fault is reported, and
    /// the caller takes back what it applied, so the editor never holds changes nothing records.
    /// </summary>
    private static async Task<Error?> SaveDecisionsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            _ = await services.GetRequiredService<IUserDataUnitOfWork>().CompleteAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException)
        {
            return EditorAgentErrors.ProposalChanged;
        }
    }

    private static void Decide(
        EditorAgentProposal row,
        Dictionary<string, EditorAgentChangeDecision> decisions,
        string changeId,
        EditorAgentChangeDecisionStatus status,
        string? mintedStableId)
    {
        if (!decisions.TryGetValue(changeId, out EditorAgentChangeDecision? decision))
        {
            decision = new EditorAgentChangeDecision { ProposalId = row.Id, ChangeId = changeId };
            row.Decisions.Add(decision);
            decisions[changeId] = decision;
        }

        decision.Status = status;
        decision.MintedStableId = mintedStableId;
        decision.DecidedAt = DateTime.UtcNow;
        decision.SavedAt = null;
    }

    /// <summary>The node a change id names: its scope and key, without the field.</summary>
    private static string NodeOf(string changeId)
    {
        int lastSeparator = changeId.LastIndexOf(NODE_SEPARATOR);
        return lastSeparator < 0 ? changeId : changeId[..lastSeparator];
    }

    private static string FieldOf(string changeId)
    {
        int lastSeparator = changeId.LastIndexOf(NODE_SEPARATOR);
        return lastSeparator < 0 ? string.Empty : changeId[(lastSeparator + 1)..];
    }

    /// <summary>
    /// The proposal of a completed turn in one of the caller's own editor conversations, tracked
    /// with its decisions, after the same checks every editor agent call makes: the caller still
    /// holds an editor role and access to the workflow type.
    /// </summary>
    private static async Task<ErrorOr<OwnedProposal>> OwnedProposalAsync(
        IServiceProvider services,
        string organizationId,
        string userId,
        string turnId,
        CancellationToken cancellationToken)
    {
        IUserDataUnitOfWork userData = services.GetRequiredService<IUserDataUnitOfWork>();
        string? workflowTypeId = await userData.Repository<AgentConversationTurn>()
            .GetAll()
            .AsNoTracking()
            .Where(turn => turn.Id == turnId
                && turn.Status == AgentConversationTurnStatus.Completed
                && turn.Conversation!.OrganizationId == organizationId
                && turn.Conversation.UserId == userId
                && turn.Conversation.Purpose == PURPOSE)
            .Select(static turn => turn.Conversation!.SubjectId)
            .SingleOrDefaultAsync(cancellationToken);
        if (workflowTypeId is null)
        {
            return EditorAgentErrors.NoProposal;
        }

        if (await CheckCallerAsync(services, organizationId, userId, workflowTypeId, cancellationToken) is Error refused)
        {
            return refused;
        }

        EditorAgentProposal? row = await userData.Repository<EditorAgentProposal>()
            .GetAll()
            .Include(static proposal => proposal.Decisions)
            .SingleOrDefaultAsync(proposal => proposal.TurnId == turnId, cancellationToken);

        return row is null ? EditorAgentErrors.NoProposal : new OwnedProposal(row, workflowTypeId);
    }

    private sealed record OwnedProposal(EditorAgentProposal Row, string WorkflowTypeId);

    private sealed record DependencyIssueKey(string BlockId, string Tag);

    /// <summary>
    /// The type must exist; the caller must hold an editor role; and their organization must have
    /// access to the type, which a super admin does not need, because staff edit every template.
    /// </summary>
    private static async Task<Error?> CheckCallerAsync(
        IServiceProvider services,
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        ICoreUnitOfWork core = services.GetRequiredService<ICoreUnitOfWork>();
        bool typeExists = await core.Repository<WorkflowType>()
            .GetAll()
            .AsNoTracking()
            .Where(WorkflowType.NotRetired)
            .AnyAsync(type => type.Id == workflowTypeId, cancellationToken);
        if (!typeExists)
        {
            return EditorAgentErrors.WorkflowTypeNotFound;
        }

        UserManager<User> users = services.GetRequiredService<UserManager<User>>();
        User? user = await users.FindByIdAsync(userId);
        IList<string> roles = user is null ? [] : await users.GetRolesAsync(user);
        bool superAdmin = roles.Contains(Roles.SUPER_ADMIN, StringComparer.Ordinal);
        if (!superAdmin && !roles.Contains(Roles.WORKFLOW_EDITOR, StringComparer.Ordinal))
        {
            return EditorAgentErrors.Forbidden;
        }

        if (!superAdmin && !await WorkflowTypeAccessGuard.HasAccessAsync(core, organizationId, workflowTypeId, cancellationToken))
        {
            return EditorAgentErrors.Forbidden;
        }

        return null;
    }

    private static async Task<bool> IsSuperAdminAsync(IServiceProvider services, string userId)
    {
        UserManager<User> users = services.GetRequiredService<UserManager<User>>();
        User? user = await users.FindByIdAsync(userId);
        return user is not null && await users.IsInRoleAsync(user, Roles.SUPER_ADMIN);
    }

    /// <summary>
    /// A message over the workflow the previous message saw reuses its draft row; a changed
    /// workflow is a new revision.
    /// </summary>
    private static async Task<ErrorOr<EditorAgentDraft>> DraftAsync(
        IUserDataUnitOfWork userData,
        AgentConversation conversation,
        string workflowJson,
        string contentSha256,
        CancellationToken cancellationToken)
    {
        EditorAgentDraft? latest = await userData.Repository<EditorAgentDraft>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.ConversationId == conversation.Id)
            .OrderByDescending(static row => row.Revision)
            .Select(static row => new EditorAgentDraft
            {
                Id = row.Id,
                ConversationId = row.ConversationId,
                WorkflowTypeId = row.WorkflowTypeId,
                Revision = row.Revision,
                ContentSha256 = row.ContentSha256,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null && latest.ContentSha256 == contentSha256)
        {
            return latest;
        }

        var draft = new EditorAgentDraft
        {
            ConversationId = conversation.Id,
            WorkflowTypeId = conversation.SubjectId ?? string.Empty,
            Revision = (latest?.Revision ?? 0) + 1,
            ContentSha256 = contentSha256,
            WorkflowJson = workflowJson,
            CreatedAt = DateTime.UtcNow,
        };
        userData.Repository<EditorAgentDraft>().Add(draft);

        try
        {
            _ = await userData.CompleteAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return AgentConversationErrors.Busy;
        }

        return draft;
    }

    private async Task<AgentConversation> CreateConversationAsync(
        IUserDataUnitOfWork userData,
        string organizationId,
        string userId,
        string workflowTypeId,
        string? modelId,
        CancellationToken cancellationToken)
    {
        var conversation = new AgentConversation
        {
            OrganizationId = organizationId,
            UserId = userId,
            Purpose = PURPOSE,
            SubjectId = workflowTypeId,
            ModelId = modelId ?? EditorAgentModelCatalog.DefaultModel(_runtime, _settings, ResolveTiers(organizationId)),
            Effort = _settings.Effort,
        };

        userData.Repository<AgentConversation>().Add(conversation);
        _ = await userData.CompleteAsync(cancellationToken);

        return conversation;
    }

    /// <summary>
    /// The platform's advanced and basic models for the organization, resolved as every model call
    /// resolves them: the tenant's override through <see cref="TenantModelFamilyResolver"/>, then
    /// the deployment's settings through the chat connector. A tier the runtime allowlist lacks is
    /// logged, since the picker then leaves it out.
    /// </summary>
    private EditorAgentTierModels ResolveTiers(string organizationId)
    {
        if (_chatConnector is not { } connector || _tenantModels is not { } tenants)
        {
            return EditorAgentTierModels.None;
        }

        return new EditorAgentTierModels(
            ResolveTier(connector, tenants, organizationId, shouldUseAdvancedModel: true),
            ResolveTier(connector, tenants, organizationId, shouldUseAdvancedModel: false));
    }

    private string? ResolveTier(
        IChatConnector connector,
        TenantModelFamilyResolver tenants,
        string organizationId,
        bool shouldUseAdvancedModel)
    {
        string tier = shouldUseAdvancedModel ? EditorAgentModelCatalog.ADVANCED_TIER : EditorAgentModelCatalog.STANDARD_TIER;
        string? requested = tenants.GetModelOverride(organizationId, shouldUseAdvancedModel);
        string modelId;
        try
        {
            modelId = connector.GetModelId(shouldUseAdvancedModel, requested);
        }
        catch (InvalidOperationException exception)
        {
            if (_warnedTiers.TryAdd($"{organizationId}|{tier}|{requested}", 0))
            {
                _logger.LogWarning(
                    exception,
                    "The editor agent offers no {Tier} model for {OrganizationId}: {ModelId} does not resolve",
                    tier, organizationId, requested);
            }

            return null;
        }

        if (EditorAgentModelCatalog.AllowedForm(_runtime, modelId) is null
            && _warnedTiers.TryAdd($"{organizationId}|{tier}|{modelId}", 0))
        {
            _logger.LogWarning(
                "The editor agent offers no {Tier} model for {OrganizationId}: the agent runtime allowlist has no form of {ModelId}",
                tier, organizationId, modelId);
        }

        return modelId;
    }

    private Task<AgentConversation?> LiveConversationAsync(
        IUserDataUnitOfWork userData,
        string organizationId,
        string userId,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        DateTime liveSince = DateTime.UtcNow.AddDays(-_settings.RetentionDays);

        return userData.Repository<AgentConversation>()
            .GetAll()
            .AsNoTracking()
            .Where(conversation => conversation.OrganizationId == organizationId
                && conversation.UserId == userId
                && conversation.Purpose == PURPOSE
                && conversation.SubjectId == workflowTypeId
                && conversation.EndedAt == null
                && conversation.LastActivityAt >= liveSince)
            .OrderByDescending(static conversation => conversation.LastActivityAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static Task<AgentConversation?> OwnedConversationAsync(
        IUserDataUnitOfWork userData,
        string organizationId,
        string userId,
        string conversationId,
        CancellationToken cancellationToken)
    {
        return userData.Repository<AgentConversation>()
            .GetAll()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                conversation => conversation.Id == conversationId
                    && conversation.OrganizationId == organizationId
                    && conversation.UserId == userId
                    && conversation.Purpose == PURPOSE,
                cancellationToken);
    }

    /// <summary>
    /// The conversation as the panel shows it. A settled turn's steps and a proposal's view do not
    /// change until a decision renews the proposal's version, so both are parsed once and kept.
    /// </summary>
    private async Task<EditorAgentConversationView> ViewAsync(
        IServiceProvider services,
        AgentConversation conversation,
        CancellationToken cancellationToken)
    {
        IUserDataUnitOfWork userData = services.GetRequiredService<IUserDataUnitOfWork>();
        List<AgentConversationTurn> turns = await userData.Repository<AgentConversationTurn>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.ConversationId == conversation.Id)
            .OrderBy(static row => row.Number)
            .Select(static row => new AgentConversationTurn
            {
                Id = row.Id,
                ConversationId = row.ConversationId,
                Number = row.Number,
                UserMessage = row.UserMessage,
                AnswerMarkdown = row.AnswerMarkdown,
                Status = row.Status,
                ModelId = row.ModelId,
                CostUsd = row.CostUsd,
                Error = row.Error,
                ActivityJson = row.ActivityJson,
                ContextRef = row.ContextRef,
                CreatedAt = row.CreatedAt,
                DispatchedAt = row.DispatchedAt,
                StartedAt = row.StartedAt,
                CompletedAt = row.CompletedAt,
            })
            .ToListAsync(cancellationToken);

        Dictionary<string, int> revisions = await userData.Repository<EditorAgentDraft>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.ConversationId == conversation.Id)
            .Select(static row => new { row.Id, row.Revision })
            .ToDictionaryAsync(static row => row.Id, static row => row.Revision, cancellationToken);

        Dictionary<string, EditorAgentProposalView> proposals = await ProposalViewsAsync(userData, conversation.Id, cancellationToken);
        Dictionary<string, IReadOnlyList<EditorAgentReportSummary>> attached = await AttachedReportsAsync(
            services, conversation, cancellationToken);
        EditorAgentFeedback feedback = services.GetRequiredService<EditorAgentFeedback>();
        Dictionary<string, EditorAgentTurnFeedback> feedbackRows = await feedback.OfConversationAsync(conversation.Id, cancellationToken);
        Dictionary<string, IReadOnlyList<EditorAgentDismissSuggestionView>> suggestions = await DismissSuggestionsAsync(
            feedback, feedbackRows, cancellationToken);

        return new EditorAgentConversationView(
            conversation.Id,
            conversation.SubjectId ?? string.Empty,
            conversation.ModelId,
            conversation.Effort ?? _settings.Effort,
            conversation.CreatedAt,
            conversation.LastActivityAt,
            conversation.LastActivityAt.AddDays(_settings.RetentionDays),
            turns.Sum(static turn => turn.CostUsd),
            [.. turns.Select(turn => TurnView(
                turn, turn.ContextRef is string draftId ? revisions.GetValueOrDefault(draftId) : 0) with
            {
                Proposal = turn.Status == AgentConversationTurnStatus.Completed ? proposals.GetValueOrDefault(turn.Id) : null,
                AttachedReports = attached.GetValueOrDefault(turn.Id) ?? [],
                FeedbackSent = feedbackRows.GetValueOrDefault(turn.Id)?.Sent.Count ?? 0,
                DismissSuggestions = suggestions.GetValueOrDefault(turn.Id) ?? [],
            })])
        {
            OrganizationToday = await ReadDailySpendAsync(
                services.GetRequiredService<IUserDataUnitOfWork>(), conversation.OrganizationId, DateTime.UtcNow, cancellationToken),
        };
    }

    /// <summary>The items the agent suggested dismissing, by turn id, with their text and status now.</summary>
    private static async Task<Dictionary<string, IReadOnlyList<EditorAgentDismissSuggestionView>>> DismissSuggestionsAsync(
        EditorAgentFeedback feedback,
        Dictionary<string, EditorAgentTurnFeedback> rows,
        CancellationToken cancellationToken)
    {
        Dictionary<string, IReadOnlyList<EditorAgentDismissSuggestion>> byTurn = rows.Values
            .Where(static row => row.DismissSuggestionsJson is not null)
            .ToDictionary(static row => row.TurnId, static row => EditorAgentFeedback.Dismissals(row.DismissSuggestionsJson), StringComparer.Ordinal);
        if (byTurn.Count == 0)
        {
            return [];
        }

        Dictionary<string, ReportFeedbackItem> items = (await feedback.ByIdsAsync(
                [.. byTurn.Values.SelectMany(static list => list).Select(static suggestion => suggestion.FeedbackItemId).Distinct(StringComparer.Ordinal)],
                cancellationToken))
            .ToDictionary(static item => item.Id, StringComparer.Ordinal);

        return byTurn.ToDictionary(
            static pair => pair.Key,
            pair => (IReadOnlyList<EditorAgentDismissSuggestionView>)[.. pair.Value
                .Select(suggestion => items.GetValueOrDefault(suggestion.FeedbackItemId) is ReportFeedbackItem item
                    ? new EditorAgentDismissSuggestionView(suggestion.FeedbackItemId, suggestion.Reason, item.Category, item.Comment, item.QuotedText, item.Status)
                    : null)
                .OfType<EditorAgentDismissSuggestionView>()],
            StringComparer.Ordinal);
    }

    /// <summary>The reports each message was sent with, by turn id.</summary>
    private static async Task<Dictionary<string, IReadOnlyList<EditorAgentReportSummary>>> AttachedReportsAsync(
        IServiceProvider services,
        AgentConversation conversation,
        CancellationToken cancellationToken)
    {
        List<EditorAgentTurnReports> picks = await services.GetRequiredService<IUserDataUnitOfWork>()
            .Repository<EditorAgentTurnReports>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.ConversationId == conversation.Id && row.PickedExecutionIds != string.Empty)
            .ToListAsync(cancellationToken);
        if (picks.Count == 0)
        {
            return [];
        }

        IReadOnlyDictionary<string, EditorAgentReportSummary> summaries = await services
            .GetRequiredService<EditorAgentReports>()
            .SummariesByIdAsync(conversation.OrganizationId, [.. picks.SelectMany(static pick => pick.Picked)], cancellationToken);

        return picks.ToDictionary(
            static pick => pick.TurnId,
            pick => (IReadOnlyList<EditorAgentReportSummary>)[.. pick.Picked
                .Select(id => summaries.GetValueOrDefault(id))
                .OfType<EditorAgentReportSummary>()],
            StringComparer.Ordinal);
    }

    /// <summary>The conversation's proposals by turn id, reading in full only those not already kept at their version.</summary>
    private async Task<Dictionary<string, EditorAgentProposalView>> ProposalViewsAsync(
        IUserDataUnitOfWork userData,
        string conversationId,
        CancellationToken cancellationToken)
    {
        IQueryable<EditorAgentProposal> rows = userData.Repository<EditorAgentProposal>()
            .GetAll()
            .AsNoTracking()
            .Where(row => row.ConversationId == conversationId);

        var stamps = await rows
            .Select(static row => new { row.Id, row.TurnId, row.Version })
            .ToListAsync(cancellationToken);

        var views = new Dictionary<string, EditorAgentProposalView>(StringComparer.Ordinal);
        List<string> unread = [];
        foreach (var stamp in stamps)
        {
            if (_viewCache.TryGetValue(ProposalCacheKey(stamp.Id, stamp.Version), out EditorAgentProposalView? kept) && kept is not null)
            {
                views[stamp.TurnId] = kept;
            }
            else
            {
                unread.Add(stamp.Id);
            }
        }

        if (unread.Count == 0)
        {
            return views;
        }

        var read = await rows
            .Where(row => unread.Contains(row.Id))
            .Select(static row => new
            {
                row.Id,
                row.TurnId,
                row.Version,
                row.ProposalJsonCompressed,
                CanUndo = row.UndoJsonCompressed != null,
                Decisions = row.Decisions
                    .Select(static decision => new DecisionRow(decision.ChangeId, decision.Status, decision.MintedStableId, decision.SavedAt != null))
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        foreach (var row in read)
        {
            EditorAgentProposalView view = ProposalView(
                row.Id,
                WorkflowProposal.FromJson(WorkflowJsonCompression.Decompress(row.ProposalJsonCompressed)),
                row.Decisions,
                row.CanUndo);
            _ = _viewCache.Set(ProposalCacheKey(row.Id, row.Version), view, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = VIEW_CACHE_IDLE,
            });
            views[row.TurnId] = view;
        }

        return views;
    }

    private EditorAgentTurnView TurnView(AgentConversationTurn turn, int draftRevision)
    {
        return new EditorAgentTurnView(
            turn.Id,
            turn.Number,
            turn.UserMessage,
            turn.AnswerMarkdown,
            turn.Status,
            turn.ModelId,
            turn.CostUsd,
            turn.Error,
            ActivityOf(turn),
            draftRevision,
            turn.CreatedAt,
            turn.StartedAt,
            turn.CompletedAt,
            turn.IsWaiting());
    }

    private static EditorAgentProposalView ProposalView(
        string proposalId,
        WorkflowProposal proposal,
        IEnumerable<DecisionRow> decisionRows,
        bool canUndo)
    {
        Dictionary<string, DecisionRow> decisions = decisionRows.ToDictionary(static decision => decision.ChangeId, StringComparer.Ordinal);

        return new EditorAgentProposalView(
            proposalId,
            proposal.Rounds,
            [.. proposal.Changes.Select(change => decisions.TryGetValue(change.Id, out DecisionRow? decision)
                ? new EditorAgentChangeView(change, decision.Status, decision.MintedStableId) { Saved = decision.Saved }
                : new EditorAgentChangeView(change, null, null))],
            proposal.Summary,
            proposal.CheckErrors,
            canUndo)
        {
            Warnings = proposal.Warnings,
            Overview = proposal.Overview,
        };
    }

    /// <summary>A settled turn's steps never change, so they are parsed once.</summary>
    private AgentActivityDigest? ActivityOf(AgentConversationTurn turn)
    {
        if (string.IsNullOrWhiteSpace(turn.ActivityJson))
        {
            return null;
        }

        if (turn.Status.IsInFlight())
        {
            return ParseActivity(turn.ActivityJson);
        }

        return _viewCache.GetOrCreate(ACTIVITY_CACHE_PREFIX + turn.Id, entry =>
        {
            entry.Size = 1;
            entry.SlidingExpiration = VIEW_CACHE_IDLE;
            return ParseActivity(turn.ActivityJson);
        });
    }

    private static AgentActivityDigest? ParseActivity(string activityJson)
    {
        try
        {
            return JsonSerializer.Deserialize<AgentActivityDigest>(activityJson, _activityJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ProposalCacheKey(string proposalId, string version)
    {
        return PROPOSAL_CACHE_PREFIX + proposalId + NODE_SEPARATOR + version;
    }

    private sealed record DecisionRow(string ChangeId, EditorAgentChangeDecisionStatus Status, string? MintedStableId, bool Saved);
}
