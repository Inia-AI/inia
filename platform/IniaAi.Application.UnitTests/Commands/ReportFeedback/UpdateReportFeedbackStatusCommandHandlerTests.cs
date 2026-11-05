using ErrorOr;
using IniaAi.Application.Commands.ReportFeedback.UpdateReportFeedbackStatus;
using IniaAi.Application.Common.Services;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Requests.ReportFeedback;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.ReportFeedback;

public class UpdateReportFeedbackStatusCommandHandlerTests : IDisposable
{
    private const string ORG_A = "org-a";
    private const string ORG_B = "org-b";
    private const string EXECUTION_ID = "exec-1";
    private const string EDITOR_ID = "editor-1";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly UpdateReportFeedbackStatusCommandHandler _handler;

    public UpdateReportFeedbackStatusCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new UpdateReportFeedbackStatusCommandHandler(
            NullLogger<UpdateReportFeedbackStatusCommandHandler>.Instance,
            unitOfWork,
            new UserDisplayNameResolver(unitOfWork));
    }

    [Fact]
    public async Task Handle_Dismiss_StoresStatusAndReason()
    {
        ReportFeedbackItem item = await SeedItemAsync(ORG_A);

        ErrorOr<ReportFeedbackDto> result = await _handler.Handle(
            new UpdateReportFeedbackStatusCommand(ORG_A, EDITOR_ID, EXECUTION_ID, item.Id, new UpdateReportFeedbackStatusRequest
            {
                Status = ReportFeedbackStatusDto.Dismissed,
                DismissReason = "Figure matches the filing.",
            }),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(ReportFeedbackStatusDto.Dismissed, result.Value.Status);
        Assert.Equal("Figure matches the filing.", result.Value.DismissReason);

        ReportFeedbackItem stored = Assert.Single(_context.ReportFeedbackItems);
        Assert.Equal(ReportFeedbackStatus.Dismissed, stored.Status);
        Assert.Equal("Figure matches the filing.", stored.DismissReason);
        Assert.Equal(EDITOR_ID, stored.StatusChangedByUserId);
        Assert.Equal(EDITOR_ID, result.Value.StatusChangedByUserId);
    }

    [Fact]
    public async Task Handle_Reopen_ClearsDismissReason()
    {
        ReportFeedbackItem item = await SeedItemAsync(ORG_A, ReportFeedbackStatus.Dismissed, "old reason");

        ErrorOr<ReportFeedbackDto> result = await _handler.Handle(
            new UpdateReportFeedbackStatusCommand(ORG_A, EDITOR_ID, EXECUTION_ID, item.Id, new UpdateReportFeedbackStatusRequest
            {
                Status = ReportFeedbackStatusDto.Addressed,
                DismissReason = "should be ignored",
            }),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(ReportFeedbackStatusDto.Addressed, result.Value.Status);
        Assert.Null(result.Value.DismissReason);
    }

    [Fact]
    public async Task Handle_OtherOrganization_ReturnsNotFoundAndLeavesItemUntouched()
    {
        ReportFeedbackItem item = await SeedItemAsync(ORG_A);

        ErrorOr<ReportFeedbackDto> result = await _handler.Handle(
            new UpdateReportFeedbackStatusCommand(ORG_B, EDITOR_ID, EXECUTION_ID, item.Id, new UpdateReportFeedbackStatusRequest
            {
                Status = ReportFeedbackStatusDto.Addressed,
            }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);

        ReportFeedbackItem stored = Assert.Single(_context.ReportFeedbackItems);
        Assert.Equal(ReportFeedbackStatus.Open, stored.Status);
    }

    [Fact]
    public async Task Handle_SameOrganizationButOtherReport_ReturnsNotFound()
    {
        ReportFeedbackItem item = await SeedItemAsync(ORG_A);

        ErrorOr<ReportFeedbackDto> result = await _handler.Handle(
            new UpdateReportFeedbackStatusCommand(ORG_A, EDITOR_ID, "exec-other", item.Id, new UpdateReportFeedbackStatusRequest
            {
                Status = ReportFeedbackStatusDto.Addressed,
            }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(ReportFeedbackStatus.Open, Assert.Single(_context.ReportFeedbackItems).Status);
    }

    private async Task<ReportFeedbackItem> SeedItemAsync(
        string organizationId,
        ReportFeedbackStatus status = ReportFeedbackStatus.Open,
        string? dismissReason = null)
    {
        ReportFeedbackItem item = ReportFeedbackItem.Create(
            organizationId,
            EXECUTION_ID,
            "WT",
            null,
            ReportFeedbackCategory.WrongFigure,
            "Wrong number.",
            "author-1");
        item.Status = status;
        item.DismissReason = dismissReason;

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
