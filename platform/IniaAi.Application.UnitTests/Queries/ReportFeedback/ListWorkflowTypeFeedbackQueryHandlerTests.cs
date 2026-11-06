using IniaAi.Application.Common.Capabilities;
using ErrorOr;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Queries.ReportFeedback;
using IniaAi.Application.Queries.ReportFeedback.ListWorkflowTypeFeedback;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.ReportFeedback;

public class ListWorkflowTypeFeedbackQueryHandlerTests : IDisposable
{
    private const string ORG_A = "org-a";
    private const string ORG_B = "org-b";
    private const string SUPER_ADMIN_ORG = "org-super-admin";
    private const string WORKFLOW_TYPE_ID = "WT";
    private const string OTHER_TYPE_ID = "OTHER";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly ListWorkflowTypeFeedbackQueryHandler _handler;

    public ListWorkflowTypeFeedbackQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new ListWorkflowTypeFeedbackQueryHandler(unitOfWork, new UserDisplayNameResolver(unitOfWork), new NoOrganizationsClosedToStaff());
    }

    [Fact]
    public async Task Handle_ReturnsEveryOrganizationsItems_NewestFirst_WithOrganizationId()
    {
        await SeedItemAsync(ORG_A, WORKFLOW_TYPE_ID, "a-old", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        await SeedItemAsync(ORG_B, WORKFLOW_TYPE_ID, "b-new", new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));
        await SeedItemAsync(ORG_A, OTHER_TYPE_ID, "other type", new DateTime(2026, 9, 1, 11, 0, 0, DateTimeKind.Utc));

        ErrorOr<ReportFeedbackPageDto> result = await _handler.Handle(
            new ListWorkflowTypeFeedbackQuery(SUPER_ADMIN_ORG, "super-admin", WORKFLOW_TYPE_ID, CallerIsSuperAdmin: true),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal("b-new", result.Value.Items[0].Comment);
        Assert.Equal(ORG_B, result.Value.Items[0].OrganizationId);
        Assert.Equal("a-old", result.Value.Items[1].Comment);
        Assert.Equal(ORG_A, result.Value.Items[1].OrganizationId);
        Assert.False(result.Value.HasMore);
        Assert.Null(result.Value.NextCursor);
    }

    [Fact]
    public async Task Handle_StatusFilter_NarrowsToThatStatus()
    {
        await SeedItemAsync(ORG_A, WORKFLOW_TYPE_ID, "open", DateTime.UtcNow);
        await SeedItemAsync(ORG_B, WORKFLOW_TYPE_ID, "addressed", DateTime.UtcNow, ReportFeedbackStatus.Addressed);

        ErrorOr<ReportFeedbackPageDto> result = await _handler.Handle(
            new ListWorkflowTypeFeedbackQuery(SUPER_ADMIN_ORG, "super-admin", WORKFLOW_TYPE_ID, CallerIsSuperAdmin: true, ReportFeedbackStatusDto.Addressed),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        ReportFeedbackDto dto = Assert.Single(result.Value.Items);
        Assert.Equal("addressed", dto.Comment);
        Assert.Equal(ReportFeedbackStatusDto.Addressed, dto.Status);
    }

    [Fact]
    public async Task Handle_Cursor_ContinuesWhereThePreviousPageStopped()
    {
        await SeedItemAsync(ORG_A, WORKFLOW_TYPE_ID, "third", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        await SeedItemAsync(ORG_B, WORKFLOW_TYPE_ID, "second", new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc));
        await SeedItemAsync(ORG_A, WORKFLOW_TYPE_ID, "first", new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));

        ErrorOr<ReportFeedbackPageDto> pageOne = await _handler.Handle(
            new ListWorkflowTypeFeedbackQuery(SUPER_ADMIN_ORG, "super-admin", WORKFLOW_TYPE_ID, CallerIsSuperAdmin: true, Take: 2),
            TestContext.Current.CancellationToken);

        Assert.False(pageOne.IsError);
        Assert.Equal(["first", "second"], pageOne.Value.Items.Select(item => item.Comment).ToArray());
        Assert.True(pageOne.Value.HasMore);
        Assert.NotNull(pageOne.Value.NextCursor);
        Assert.True(ReportFeedbackCursor.TryDecode(pageOne.Value.NextCursor, out _, out string cursorId));
        Assert.Equal(pageOne.Value.Items[1].Id, cursorId);

        ErrorOr<ReportFeedbackPageDto> pageTwo = await _handler.Handle(
            new ListWorkflowTypeFeedbackQuery(SUPER_ADMIN_ORG, "super-admin", WORKFLOW_TYPE_ID, CallerIsSuperAdmin: true, Take: 2, Cursor: pageOne.Value.NextCursor),
            TestContext.Current.CancellationToken);

        Assert.False(pageTwo.IsError);
        ReportFeedbackDto last = Assert.Single(pageTwo.Value.Items);
        Assert.Equal("third", last.Comment);
        Assert.False(pageTwo.Value.HasMore);
        Assert.Null(pageTwo.Value.NextCursor);
    }

    [Fact]
    public void Cursor_WithTicksBeyondDateTimeRange_IsRefusedNotThrown()
    {
        string cursor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{long.MaxValue}|x"));

        Assert.False(ReportFeedbackCursor.TryDecode(cursor, out _, out _));
        Assert.False(ReportFeedbackCursor.TryDecode("not base64", out _, out _));
    }

    [Fact]
    public async Task Handle_CallerNotSuperAdmin_IsForbidden()
    {
        await SeedItemAsync(ORG_A, WORKFLOW_TYPE_ID, "org a", DateTime.UtcNow);

        ErrorOr<ReportFeedbackPageDto> result = await _handler.Handle(
            new ListWorkflowTypeFeedbackQuery(ORG_B, "workflow-editor", WORKFLOW_TYPE_ID, CallerIsSuperAdmin: false),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
        Assert.Equal(ListWorkflowTypeFeedbackQueryHandler.SUPER_ADMIN_ONLY, result.FirstError.Description);
    }

    [Fact]
    public async Task Handle_UnknownType_ReturnsEmptyPage()
    {
        await SeedItemAsync(ORG_A, WORKFLOW_TYPE_ID, "x", DateTime.UtcNow);

        ErrorOr<ReportFeedbackPageDto> result = await _handler.Handle(
            new ListWorkflowTypeFeedbackQuery(SUPER_ADMIN_ORG, "super-admin", "nope", CallerIsSuperAdmin: true),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Items);
        Assert.False(result.Value.HasMore);
    }

    [Fact]
    public async Task Handle_LeavesOutTheOrganizationsClosedToTheCaller()
    {
        await SeedItemAsync(ORG_A, WORKFLOW_TYPE_ID, "open org", DateTime.UtcNow);
        await SeedItemAsync(ORG_B, WORKFLOW_TYPE_ID, "closed org", DateTime.UtcNow);
        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        var handler = new ListWorkflowTypeFeedbackQueryHandler(unitOfWork, new UserDisplayNameResolver(unitOfWork), new ClosedOrganization(_context, ORG_B));

        ErrorOr<ReportFeedbackPageDto> result = await handler.Handle(
            new ListWorkflowTypeFeedbackQuery(SUPER_ADMIN_ORG, "super-admin", WORKFLOW_TYPE_ID, CallerIsSuperAdmin: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(ORG_A, Assert.Single(result.Value.Items).OrganizationId);
    }

    /// <summary>Closes one organization, as a query on the same context, the way a module's rule composes into the read.</summary>
    private sealed class ClosedOrganization(UserDataDbContext context, string organizationId) : IOrganizationsClosedToStaff
    {
        public IQueryable<string>? ClosedTo(string? userId)
        {
            return context.Set<ReportFeedbackItem>().Where(item => item.OrganizationId == organizationId).Select(item => item.OrganizationId);
        }
    }

    private async Task SeedItemAsync(
        string organizationId,
        string workflowTypeId,
        string comment,
        DateTime createdAt,
        ReportFeedbackStatus status = ReportFeedbackStatus.Open)
    {
        ReportFeedbackItem item = ReportFeedbackItem.Create(
            organizationId,
            $"exec-{organizationId}",
            workflowTypeId,
            null,
            ReportFeedbackCategory.WrongClassification,
            comment,
            $"author-{organizationId}");
        item.CreatedAt = createdAt;
        item.Status = status;

        _ = _context.ReportFeedbackItems.Add(item);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
