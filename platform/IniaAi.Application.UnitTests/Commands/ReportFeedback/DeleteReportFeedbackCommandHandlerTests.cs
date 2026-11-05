using ErrorOr;
using IniaAi.Application.Commands.ReportFeedback.DeleteReportFeedback;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.ReportFeedback;

public class DeleteReportFeedbackCommandHandlerTests : IDisposable
{
    private const string ORG_A = "org-a";
    private const string ORG_B = "org-b";
    private const string EXECUTION_ID = "exec-1";
    private const string READER_ID = "reader-1";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly DeleteReportFeedbackCommandHandler _handler;

    public DeleteReportFeedbackCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _handler = new DeleteReportFeedbackCommandHandler(
            NullLogger<DeleteReportFeedbackCommandHandler>.Instance,
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>());
    }

    [Theory]
    [InlineData(ReportFeedbackStatus.Open)]
    [InlineData(ReportFeedbackStatus.Addressed)]
    [InlineData(ReportFeedbackStatus.Dismissed)]
    public async Task Handle_OwnOrganization_DeletesTheRowWhateverItsStatus(ReportFeedbackStatus status)
    {
        ReportFeedbackItem item = await SeedItemAsync(ORG_A, status);
        ReportFeedbackItem other = await SeedItemAsync(ORG_A);

        ErrorOr<Deleted> result = await _handler.Handle(
            new DeleteReportFeedbackCommand(ORG_A, READER_ID, EXECUTION_ID, item.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        ReportFeedbackItem remaining = Assert.Single(await _context.ReportFeedbackItems.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(other.Id, remaining.Id);
    }

    [Fact]
    public async Task Handle_OtherOrganization_ReturnsNotFoundAndKeepsTheRow()
    {
        ReportFeedbackItem item = await SeedItemAsync(ORG_A);

        ErrorOr<Deleted> result = await _handler.Handle(
            new DeleteReportFeedbackCommand(ORG_B, READER_ID, EXECUTION_ID, item.Id),
            TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Single(_context.ReportFeedbackItems);
    }

    [Fact]
    public async Task Handle_SameOrganizationButOtherReport_ReturnsNotFoundAndKeepsTheRow()
    {
        ReportFeedbackItem item = await SeedItemAsync(ORG_A);

        ErrorOr<Deleted> result = await _handler.Handle(
            new DeleteReportFeedbackCommand(ORG_A, READER_ID, "exec-other", item.Id),
            TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Single(_context.ReportFeedbackItems);
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        _ = await SeedItemAsync(ORG_A);

        ErrorOr<Deleted> result = await _handler.Handle(
            new DeleteReportFeedbackCommand(ORG_A, READER_ID, EXECUTION_ID, "no-such-item"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Single(_context.ReportFeedbackItems);
    }

    private async Task<ReportFeedbackItem> SeedItemAsync(string organizationId, ReportFeedbackStatus status = ReportFeedbackStatus.Open)
    {
        ReportFeedbackItem item = ReportFeedbackItem.Create(
            organizationId,
            EXECUTION_ID,
            "WT",
            null,
            ReportFeedbackCategory.WrongFigure,
            "Wrong number.",
            "author-1");
        if (status != ReportFeedbackStatus.Open)
        {
            item.SetStatus(status, "Seeded.", "editor-1");
        }

        _ = _context.ReportFeedbackItems.Add(item);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        return item;
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
