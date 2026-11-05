using ErrorOr;
using IniaAi.Application.Commands.ReportFeedback.CreateReportFeedback;
using IniaAi.Application.Common.Services;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Requests.ReportFeedback;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DomainWorkflowState = IniaAi.Domain.Entities.WorkflowExecutions.WorkflowState;

namespace IniaAi.Application.UnitTests.Commands.ReportFeedback;

public class CreateReportFeedbackCommandHandlerTests : IDisposable
{
    private const string ORG_A = "org-a";
    private const string ORG_B = "org-b";
    private const string EXECUTION_ID = "exec-1";
    private const string WORKFLOW_TYPE_ID = "WT";
    private const string AUTHOR_ID = "author-1";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly CreateReportFeedbackCommandHandler _handler;

    public CreateReportFeedbackCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new CreateReportFeedbackCommandHandler(
            NullLogger<CreateReportFeedbackCommandHandler>.Instance,
            unitOfWork,
            new UserDisplayNameResolver(unitOfWork));
    }

    [Fact]
    public async Task Handle_AnchorsItemToExecutionAndWorkflowType()
    {
        await SeedExecutionAsync();

        ErrorOr<ReportFeedbackDto> result = await _handler.Handle(
            new CreateReportFeedbackCommand(ORG_A, AUTHOR_ID, EXECUTION_ID, new CreateReportFeedbackRequest
            {
                QuotedText = "Revenue 120",
                Category = ReportFeedbackCategoryDto.WrongFigure,
                Comment = "Revenue should be 125.",
            }),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        ReportFeedbackDto dto = result.Value;
        Assert.Equal(EXECUTION_ID, dto.WorkflowExecutionId);
        Assert.Equal(WORKFLOW_TYPE_ID, dto.WorkflowTypeId);
        Assert.Equal("Revenue 120", dto.QuotedText);
        Assert.Equal(ReportFeedbackCategoryDto.WrongFigure, dto.Category);
        Assert.Equal(ReportFeedbackStatusDto.Open, dto.Status);
        Assert.Equal(AUTHOR_ID, dto.AuthorUserId);
        Assert.Equal("Grace Hopper", dto.AuthorDisplayName);
        Assert.Null(dto.StatusChangedByUserId);
        Assert.Null(dto.OrganizationId);

        ReportFeedbackItem stored = Assert.Single(_context.ReportFeedbackItems);
        Assert.Equal(ORG_A, stored.OrganizationId);
        Assert.Equal(ReportFeedbackStatus.Open, stored.Status);
    }

    [Fact]
    public async Task Handle_AllowsNoQuotedText()
    {
        await SeedExecutionAsync();

        ErrorOr<ReportFeedbackDto> result = await _handler.Handle(
            new CreateReportFeedbackCommand(ORG_A, AUTHOR_ID, EXECUTION_ID, new CreateReportFeedbackRequest
            {
                Category = ReportFeedbackCategoryDto.WordingOrStyle,
                Comment = "Too long.",
            }),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.QuotedText);
    }

    [Fact]
    public async Task Handle_OtherOrganization_ReturnsNotFound()
    {
        await SeedExecutionAsync();

        ErrorOr<ReportFeedbackDto> result = await _handler.Handle(
            new CreateReportFeedbackCommand(ORG_B, AUTHOR_ID, EXECUTION_ID, new CreateReportFeedbackRequest
            {
                Category = ReportFeedbackCategoryDto.WrongFigure,
                Comment = "x",
            }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Empty(_context.ReportFeedbackItems);
    }

    private async Task SeedExecutionAsync()
    {
        _ = _context.Users.Add(new User(ORG_A) { Id = AUTHOR_ID, FirstName = "Grace", LastName = "Hopper" });

        _ = _context.WorkflowExecutions.Add(new WorkflowExecution
        {
            Id = EXECUTION_ID,
            OrganizationId = ORG_A,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            WorkflowState = DomainWorkflowState.Draft,
        });

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
