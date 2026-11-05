using ErrorOr;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Queries.ReportFeedback.ListReportFeedback;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.ReportFeedback;

public class ListReportFeedbackQueryHandlerTests : IDisposable
{
    private const string ORG_A = "org-a";
    private const string ORG_B = "org-b";
    private const string EXECUTION_ID = "exec-shared-id";
    private const string WORKFLOW_TYPE_ID = "WT";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly ListReportFeedbackQueryHandler _handler;

    public ListReportFeedbackQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new ListReportFeedbackQueryHandler(unitOfWork, new UserDisplayNameResolver(unitOfWork));
    }

    [Fact]
    public async Task Handle_ReturnsOnlyCallerOrganizationItems_OldestFirst()
    {
        _ = _context.Users.Add(new User(ORG_A) { Id = "author-a", FirstName = "Ada", LastName = "Lovelace" });
        await SeedItemAsync(ORG_A, "author-a", "first", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        await SeedItemAsync(ORG_A, "author-a", "second", new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc));
        await SeedItemAsync(ORG_B, "author-b", "other org", new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc));

        ErrorOr<List<ReportFeedbackDto>> result = await _handler.Handle(
            new ListReportFeedbackQuery(ORG_A, "reader", EXECUTION_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("first", result.Value[0].Comment);
        Assert.Equal("second", result.Value[1].Comment);
        Assert.All(result.Value, dto => Assert.Null(dto.OrganizationId));
        Assert.All(result.Value, dto => Assert.Equal("Ada Lovelace", dto.AuthorDisplayName));
    }

    [Fact]
    public async Task Handle_OrganizationWithoutItems_ReturnsEmpty()
    {
        await SeedItemAsync(ORG_A, "author-a", "only org a", DateTime.UtcNow);

        ErrorOr<List<ReportFeedbackDto>> result = await _handler.Handle(
            new ListReportFeedbackQuery(ORG_B, "reader", EXECUTION_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_OtherReportOfSameOrganization_IsNotListed()
    {
        await SeedItemAsync(ORG_A, "author-a", "on this report", DateTime.UtcNow);

        ErrorOr<List<ReportFeedbackDto>> result = await _handler.Handle(
            new ListReportFeedbackQuery(ORG_A, "reader", "exec-other"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_UnknownAuthor_FallsBackToUserId()
    {
        await SeedItemAsync(ORG_A, "ghost", "no user row", DateTime.UtcNow);

        ErrorOr<List<ReportFeedbackDto>> result = await _handler.Handle(
            new ListReportFeedbackQuery(ORG_A, "reader", EXECUTION_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        ReportFeedbackDto dto = Assert.Single(result.Value);
        Assert.Equal("ghost", dto.AuthorDisplayName);
    }

    private async Task SeedItemAsync(string organizationId, string authorId, string comment, DateTime createdAt)
    {
        ReportFeedbackItem item = ReportFeedbackItem.Create(
            organizationId,
            EXECUTION_ID,
            WORKFLOW_TYPE_ID,
            null,
            ReportFeedbackCategory.MissingInformation,
            comment,
            authorId);
        item.CreatedAt = createdAt;

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
