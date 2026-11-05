using ErrorOr;
using IniaAi.Application.Commands.WorkflowReports.DeleteReportParagraph;
using IniaAi.Application.Commands.WorkflowReports.DeleteReportSection;
using IniaAi.Application.Commands.WorkflowReports.UpdateReportParagraph;
using IniaAi.Application.Commands.WorkflowReports.UpdateReportSection;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Requests.WorkflowReports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.WorkflowReports;

public class ReportTreeCommandHandlersTests : IDisposable
{
    private const string EXECUTION_ID = "exec-1";
    private const string ORG_ID = "org-1";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly IUserDataUnitOfWork _uow;

    private string _sectionId = default!;
    private string _paragraphOneId = default!;
    private string _paragraphTwoId = default!;

    public ReportTreeCommandHandlersTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _uow = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    [Fact]
    public async Task UpdateReportSection_UpdatesEditableFields()
    {
        await SeedAsync();
        var handler = new UpdateReportSectionCommandHandler(
            NullLogger<UpdateReportSectionCommandHandler>.Instance, _uow);

        ErrorOr<Success> result = await handler.Handle(
            new UpdateReportSectionCommand(ORG_ID, "u", EXECUTION_ID, _sectionId,
                new UpdateReportSectionRequest { Title = "Updated" }),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _context.ChangeTracker.Clear();
        ReportSection? reloaded = await _uow.Repository<ReportSection>()
            .GetFirstOrDefaultAsync(new ReportSectionByIdSpec(_sectionId, EXECUTION_ID, ORG_ID), TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal("Updated", reloaded!.Title);
    }

    [Fact]
    public async Task UpdateReportSection_CrossOrganization_ReturnsNotFound()
    {
        await SeedAsync();
        var handler = new UpdateReportSectionCommandHandler(
            NullLogger<UpdateReportSectionCommandHandler>.Instance, _uow);

        ErrorOr<Success> result = await handler.Handle(
            new UpdateReportSectionCommand("intruder", "u", EXECUTION_ID, _sectionId,
                new UpdateReportSectionRequest { Title = "X" }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task UpdateReportParagraph_UpdatesContent()
    {
        await SeedAsync();
        var handler = new UpdateReportParagraphCommandHandler(
            NullLogger<UpdateReportParagraphCommandHandler>.Instance, _uow);

        ErrorOr<Success> result = await handler.Handle(
            new UpdateReportParagraphCommand(ORG_ID, "u", EXECUTION_ID, _paragraphOneId,
                new UpdateReportParagraphRequest { Content = "fresh", Confidence = 0.9 }),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _context.ChangeTracker.Clear();
        ReportParagraph? reloaded = await _uow.Repository<ReportParagraph>()
            .GetFirstOrDefaultAsync(new ReportParagraphByIdSpec(_paragraphOneId, EXECUTION_ID, ORG_ID), TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal("fresh", reloaded!.Content);
        Assert.Equal(0.9, reloaded.Confidence);
    }

    [Fact]
    public async Task DeleteReportSection_RemovesSectionAndParagraphs()
    {
        await SeedAsync();
        var handler = new DeleteReportSectionCommandHandler(
            NullLogger<DeleteReportSectionCommandHandler>.Instance, _uow);

        ErrorOr<Success> result = await handler.Handle(
            new DeleteReportSectionCommand(ORG_ID, "u", EXECUTION_ID, _sectionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _context.ChangeTracker.Clear();
        ReportSection? section = await _uow.Repository<ReportSection>()
            .GetFirstOrDefaultAsync(new ReportSectionByIdSpec(_sectionId, EXECUTION_ID, ORG_ID), TestContext.Current.CancellationToken);
        Assert.Null(section);
        ReportParagraph? paragraph = await _uow.Repository<ReportParagraph>()
            .GetFirstOrDefaultAsync(new ReportParagraphByIdSpec(_paragraphOneId, EXECUTION_ID, ORG_ID), TestContext.Current.CancellationToken);
        Assert.Null(paragraph);
    }

    [Fact]
    public async Task DeleteReportParagraph_RemovesOnlyTargetParagraph()
    {
        await SeedAsync();
        var handler = new DeleteReportParagraphCommandHandler(
            NullLogger<DeleteReportParagraphCommandHandler>.Instance, _uow);

        ErrorOr<Success> result = await handler.Handle(
            new DeleteReportParagraphCommand(ORG_ID, "u", EXECUTION_ID, _paragraphOneId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _context.ChangeTracker.Clear();
        ReportParagraph? deleted = await _uow.Repository<ReportParagraph>()
            .GetFirstOrDefaultAsync(new ReportParagraphByIdSpec(_paragraphOneId, EXECUTION_ID, ORG_ID), TestContext.Current.CancellationToken);
        ReportParagraph? survivor = await _uow.Repository<ReportParagraph>()
            .GetFirstOrDefaultAsync(new ReportParagraphByIdSpec(_paragraphTwoId, EXECUTION_ID, ORG_ID), TestContext.Current.CancellationToken);
        Assert.Null(deleted);
        Assert.NotNull(survivor);
    }

    [Fact]
    public async Task DeleteReportParagraph_CrossOrganization_ReturnsNotFound()
    {
        await SeedAsync();
        var handler = new DeleteReportParagraphCommandHandler(
            NullLogger<DeleteReportParagraphCommandHandler>.Instance, _uow);

        ErrorOr<Success> result = await handler.Handle(
            new DeleteReportParagraphCommand("intruder", "u", EXECUTION_ID, _paragraphOneId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    private async Task SeedAsync()
    {
        var report = new WorkflowReport
        {
            WorkflowExecutionId = EXECUTION_ID,
            OrganizationId = ORG_ID,
            Title = "Report",
        };
        var section = new ReportSection
        {
            WorkflowReportId = report.Id,
            WorkflowReport = report,
            Order = 0,
            Title = "Original",
        };
        var paragraphOne = new ReportParagraph
        {
            SectionId = section.Id,
            Section = section,
            Kind = ReportParagraphKind.Paragraph,
            Order = 0,
            Content = "old",
        };
        var paragraphTwo = new ReportParagraph
        {
            SectionId = section.Id,
            Section = section,
            Kind = ReportParagraphKind.Paragraph,
            Order = 1,
            Content = "keep",
        };
        section.Paragraphs.Add(paragraphOne);
        section.Paragraphs.Add(paragraphTwo);
        report.Sections.Add(section);

        _sectionId = section.Id;
        _paragraphOneId = paragraphOne.Id;
        _paragraphTwoId = paragraphTwo.Id;

        _ = await _context.Set<WorkflowReport>().AddAsync(report, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _context?.Dispose();
        _serviceProvider?.Dispose();
    }
}
