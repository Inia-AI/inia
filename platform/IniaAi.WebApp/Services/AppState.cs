using ErrorOr;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.Tools;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Models.BackgroundJobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Services.Models.Jobs.Enums;
using IniaAi.WebApp.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using System.Security.Claims;
using System.Security.Principal;

namespace IniaAi.WebApp.Services;

public class AppState : IDisposable
{
    public const int WARNING_TOAST_MS = 8000;

    // Event triggered when organization changes
    public event EventHandler? OrganizationChanged;

    /// <summary>
    /// Raised when signature override values may have changed underneath any
    /// component that's holding a cache of them. Fired after Save-As-Core
    /// completes — by the time staff hits Save a client may have promoted a
    /// pending override or written one directly via the Vue Signatures page,
    /// and components like <c>SignatureFieldsEditor</c> need a kick to refetch
    /// rather than continue showing the value loaded when the editor mounted.
    /// </summary>
    public event EventHandler? SignatureOverridesInvalidated;

    public void NotifySignatureOverridesInvalidated()
    {
        SignatureOverridesInvalidated?.Invoke(this, EventArgs.Empty);
    }

    // titles for routes used in the app
    private static readonly Dictionary<string, string> _routeTitleInfo = new()
    {
        { AppRoutes.HOME, "Dashboard" },
        { AppRoutes.DASHBOARD, "Dashboard" },
        { AppRoutes.TOOLS_SETTINGS, "Tools Settings" },
        { AppRoutes.USER_WORKFLOWS, "Executed Workflows" },
        { AppRoutes.ADMINISTRATION, "Administration" },
        { AppRoutes.CREATE_USER, "Create New User" },
        { AppRoutes.EDITORS, "Workflow Editors" },
        { AppRoutes.LIBRARY, "Component Library" },
        { AppRoutes.WORKFLOW_TYPE_MANAGEMENT, "Workflow Type Management" },
    };

    public bool IsAuthenticated { get; private set; } = false;

    public bool IsSuperAdmin { get; private set; } = false;

    /// <summary>
    /// Whether the workflow editor trusts this user's uploads and runs. See <see cref="EditorTrust"/>.
    /// </summary>
    public bool IsTrustedEditor { get; private set; } = false;

    public User? User { get; private set; } = null;

    /// <summary>
    /// Gets the list of organizations the current user has access to.
    /// </summary>
    public List<Organization> UserOrganizations { get; private set; } = [];

    /// <summary>
    /// Whether the signed-in user may act in <paramref name="organizationId"/>: a super admin in any
    /// organization not closed to staff, anyone else in the organizations they belong to that are not
    /// closed to staff.
    /// </summary>
    public bool CanActIn(string organizationId)
    {
        return !_closedOrganizationIds.Contains(organizationId) && (IsSuperAdmin || UserOrganizations.Any(o => o.Id == organizationId));
    }

    /// <summary>
    /// The organizations closed to staff, read now, so a customer who signed up after this sign-in
    /// counts. A page that lists every organization leaves these out. The admin app's pages act across
    /// organizations, so a support grant, which lets one person read one account, opens none of them here.
    /// </summary>
    public async Task<IReadOnlySet<string>> ClosedOrganizationsAsync()
    {
        _closedOrganizationIds = await ReadClosedOrganizationsAsync();
        return _closedOrganizationIds;
    }

    /// <summary>
    /// Gets the current organization ID (selected tenant).
    /// </summary>
    public string? CurrentOrganizationId { get; private set; }

    private readonly AuthenticationStateProvider _authenticationStateProvider;

    private readonly IJobsService _jobsService;
    private readonly ISnackbar _snackbar;

    private readonly ILogger<AppState> _logger;

    private readonly NavigationManager _navigation;

    private readonly IDbContextFactory<UserDataDbContext> _userDataDbContextFactory;

    private readonly IServiceScopeFactory _scopes;

    private IReadOnlySet<string> _closedOrganizationIds = new HashSet<string>();

    /// <summary>
    /// Lazy cache for organization-scoped ConversionTypes. Shared across all components
    /// in the circuit so two Json2XlsxBlock instances rendered in the same pass don't
    /// race on a shared DbContext. Invalidated on organization change.
    /// </summary>
    private Lazy<Task<List<ConversionType>>>? _conversionTypesLazy;

    public string CurrentRoute => $"/{_navigation.ToBaseRelativePath(_navigation.Uri)}";

    public AppState(
        AuthenticationStateProvider authenticationStateProvider,
        IServiceProvider serviceProvider,
        IDbContextFactory<UserDataDbContext> userDataDbContextFactory,
        ILogger<AppState> logger)
    {
        _userDataDbContextFactory = userDataDbContextFactory;

        _authenticationStateProvider = authenticationStateProvider;
        _authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChangedAsync;

        _logger = logger;

        _scopes = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _jobsService = serviceProvider.GetRequiredService<IJobsService>();

        _jobsService.JobStateChanged += OnJobStateChanged;

        _navigation = serviceProvider.GetRequiredService<NavigationManager>();

        _snackbar = serviceProvider.GetRequiredService<ISnackbar>();

        Task.Run(UpdateAuthenticationStateAsync).Wait();

        _logger.LogInformation("Initialize AppState");
    }

    public void Dispose()
    {
        _logger.LogInformation("Dispose AppState");

        _authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChangedAsync;
        _jobsService.JobStateChanged -= OnJobStateChanged;

        GC.SuppressFinalize(this);
    }

    public string? GetName(string route)
    {
        int queryIndex = route.IndexOf('?');
        if (queryIndex != -1)
        {
            route = route[..queryIndex];
        }

        return _routeTitleInfo.TryGetValue(route, out string? title)
            ? title
            : null;
    }

    public async Task ReloadUserAsync()
    {
        if (User is not null)
        {
            await using UserDataDbContext userDataUnitOfWork = await _userDataDbContextFactory.CreateDbContextAsync();

            User = await userDataUnitOfWork.Users.SingleOrDefaultAsync(x => x.Id == User.Id);
        }
    }

    /// <summary>
    /// Returns the full <see cref="ConversionType"/> list for the current circuit.
    /// Result is memoized for the circuit and invalidated when the organization changes,
    /// so concurrent callers (e.g. multiple Json2XlsxBlock components in the same render
    /// pass) share a single DB round-trip via a fresh DbContext — no races.
    /// </summary>
    public Task<List<ConversionType>> GetConversionTypesAsync()
    {
        _conversionTypesLazy ??= new Lazy<Task<List<ConversionType>>>(LoadConversionTypesAsync);
        return _conversionTypesLazy.Value;
    }

    private async Task<List<ConversionType>> LoadConversionTypesAsync()
    {
        await using UserDataDbContext context = await _userDataDbContextFactory.CreateDbContextAsync();
        return await context.Set<ConversionType>().AsNoTracking().ToListAsync();
    }

    #region Private

    private async void OnAuthenticationStateChangedAsync(Task<AuthenticationState> task)
    {
        try
        {
            await UpdateAuthenticationStateAsync();
        }
        catch (Exception ex)
        {
            // async void: an unhandled exception here would tear down the circuit
            _logger.LogError(ex, "Failed to update authentication state");
        }
    }

    private async Task<IReadOnlySet<string>> ReadClosedOrganizationsAsync()
    {
        // Its own scope: the circuit's shared context may be busy with a page's query.
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<IOrganizationsClosedToStaff>().ClosedToAllStaff() is { } closed
            ? (await closed.ToListAsync()).ToHashSet()
            : new HashSet<string>();
    }

    private async Task UpdateAuthenticationStateAsync()
    {
        AuthenticationState authState = await _authenticationStateProvider.GetAuthenticationStateAsync();

        // Store current selection before clearing
        string? previousOrganizationId = CurrentOrganizationId;

        User = null;
        IsAuthenticated = false;
        IsSuperAdmin = false;
        IsTrustedEditor = false;
        UserOrganizations.Clear();
        _closedOrganizationIds = new HashSet<string>();
        CurrentOrganizationId = null;
        _conversionTypesLazy = null;

        if (authState is not null && authState.User is ClaimsPrincipal user)
        {
            if (user.Identity is IIdentity identity && identity.IsAuthenticated)
            {
                IsAuthenticated = true;
                IsSuperAdmin = user.IsInRole(Roles.SUPER_ADMIN);
                IsTrustedEditor = EditorTrust.Extends(user);
                string? userId = user.Claims.FirstOrDefault(c => c.Type.EndsWith("nameidentifier"))?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    throw new NullReferenceException($"UserId is not defined for user: {user}");
                }

                await using UserDataDbContext userDataUnitOfWork = await _userDataDbContextFactory.CreateDbContextAsync();
                User = await userDataUnitOfWork.Users.SingleOrDefaultAsync(x => x.Id == userId)
                    ?? throw new ApplicationException($"User could not be found with id: {userId}");

                // Load user's organizations using EF Core directly
                List<UserOrganization> userOrganizations = await userDataUnitOfWork.UserOrganizations
                    .Where(uo => uo.UserId == userId && uo.IsActive && uo.Organization.IsActive)
                    .Include(uo => uo.Organization)
                    .OrderBy(uo => uo.Organization.Name)
                    .ToListAsync();

                _closedOrganizationIds = await ReadClosedOrganizationsAsync();
                _ = userOrganizations.RemoveAll(membership => _closedOrganizationIds.Contains(membership.OrganizationId));

                UserOrganizations = [.. userOrganizations.Select(uo => uo.Organization)];

                // Try to restore previous selection first
                if (!string.IsNullOrEmpty(previousOrganizationId) &&
                    UserOrganizations.Any(o => o.Id == previousOrganizationId))
                {
                    CurrentOrganizationId = previousOrganizationId;
                    _logger.LogDebug("Restored previous organization selection: {OrganizationId}", previousOrganizationId);
                }
                else
                {
                    // Fall back to default organization logic
                    UserOrganization? defaultUserOrg = userOrganizations.FirstOrDefault(uo => uo.IsDefault);
                    CurrentOrganizationId = defaultUserOrg?.OrganizationId ?? userOrganizations.FirstOrDefault()?.OrganizationId;
                }

                _logger.LogInformation("Organization context updated - Previous: {Previous}, Current: {Current}, Available: {Count}",
                    previousOrganizationId, CurrentOrganizationId, UserOrganizations.Count);
                _logger.LogInformation("Organizations: {Organizations}",
                    string.Join(", ", UserOrganizations.Select(o => $"{o.Name} ({o.Id})")));
            }
        }

        if (User is null)
        {
            _logger.LogInformation("No user authenticated");
        }
        else
        {
            if (IsAuthenticated)
            {
                _logger.LogInformation("{UserId} authenticated", User.Id);
            }
            else
            {
                _logger.LogInformation("{UserId} not authenticated", User.Id);
            }
        }
    }

    /// <summary>
    /// Applies a persisted organization selection (e.g. from browser storage on
    /// circuit start) without throwing on a stale or no-longer-accessible id —
    /// unlike <see cref="SwitchOrganization"/>, a saved selection that the user
    /// can no longer reach is silently ignored, leaving the default in place.
    /// </summary>
    public void TryRestoreOrganization(string? organizationId)
    {
        if (!TenantPreference.ShouldRestore(organizationId, CurrentOrganizationId, UserOrganizations.Select(o => o.Id)))
        {
            return;
        }

        CurrentOrganizationId = organizationId;
        _conversionTypesLazy = null;

        _logger.LogInformation("Restored organization selection: {OrganizationId}", organizationId);

        OrganizationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Switches the current organization context.
    /// </summary>
    /// <param name="organizationId">The organization ID to switch to.</param>
    public async Task SwitchOrganization(string organizationId)
    {
        if (string.IsNullOrEmpty(organizationId) || organizationId == CurrentOrganizationId)
        {
            return;
        }

        // Validate user has access to this organization
        if (!UserOrganizations.Any(o => o.Id == organizationId))
        {
            throw new UnauthorizedAccessException($"User does not have access to organization: {organizationId}");
        }

        CurrentOrganizationId = organizationId;
        _conversionTypesLazy = null;

        _logger.LogInformation("Switched to organization: {OrganizationId}", organizationId);

        // Notify listeners that organization has changed
        OrganizationChanged?.Invoke(this, EventArgs.Empty);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Shows a failed result's errors as one error toast. Returns whether the result had failed,
    /// so a caller reads <c>if (NotifyErrors(result)) { return; }</c>.
    /// </summary>
    public bool NotifyErrors<T>(ErrorOr<T> result)
    {
        if (!result.IsError)
        {
            return false;
        }

        Notify(string.Join(" ", result.Errors.Select(e => e.Description)), Severity.Error);

        return true;
    }

    public void Notify(string message, Severity severity = Severity.Info)
    {
        // A failed action often leaves the page unchanged, so the toast is the only evidence it
        // happened at all: at the global 2s duration testers repeatedly concluded the control was
        // dead. Errors wait for a dismiss; warnings get long enough to read.
        if (severity == Severity.Error)
        {
            _ = _snackbar.Add(message, severity, options => options.RequireInteraction = true);

            return;
        }

        if (severity == Severity.Warning)
        {
            _ = _snackbar.Add(message, severity, options => options.VisibleStateDuration = WARNING_TOAST_MS);

            return;
        }

        _ = _snackbar.Add(message, severity);
    }

    /// <summary>
    /// A job's report toasts go only to the user who started it, in the organization it runs in.
    /// </summary>
    public static bool AnnouncesJobTo(BackgroundJob job, string? organizationId, string? userId)
    {
        return job.ListedAsReport
            && userId is not null
            && job.UserId == userId
            && job.OrganizationId == organizationId;
    }

    private void OnJobStateChanged(object? sender, JobStateChangeEventArgs args)
    {
        if (!IsAuthenticated || !AnnouncesJobTo(args.Job, CurrentOrganizationId, User?.Id))
        {
            return;
        }

        switch (args.Job.State)
        {
            case JobState.Preparing:
                _ = _snackbar.Add("Preparing documents for processing.", Severity.Info);
                break;
            case JobState.Processing:
                _ = _snackbar.Add("Processing documents, this may take a while.", Severity.Info);
                break;
            case JobState.Anonymizing:
                _ = _snackbar.Add("Anonymizing documents.", Severity.Info);
                break;
            case JobState.Done:
                _ = _snackbar.Add("Report generated successfully.", Severity.Success);
                break;
            case JobState.Error:
                _ = _snackbar.Add($"Failed to generate report: {args.Job.Error}.", Severity.Error);
                break;
            default:
                break;
        }
    }

    #endregion
}
