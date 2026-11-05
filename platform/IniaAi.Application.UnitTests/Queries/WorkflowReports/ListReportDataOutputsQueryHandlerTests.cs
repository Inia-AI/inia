using ErrorOr;
using IniaAi.Application.Queries.WorkflowReports.ListReportDataOutputs;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Dto.Citations;
using IniaAi.WebApi.Contracts.Responses.WorkflowReports;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace IniaAi.Application.UnitTests.Queries.WorkflowReports;

public class ListReportDataOutputsQueryHandlerTests : IDisposable
{
    private const string FACILITIES_JSON = """{ "facilities": [ { "limit": 250000 } ] }""";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly ListReportDataOutputsQueryHandler _handler;

    public ListReportDataOutputsQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        _handler = new ListReportDataOutputsQueryHandler(_serviceProvider.GetRequiredService<IUserDataUnitOfWork>());
    }

    [Fact]
    public async Task Handle_ReturnsEachOutputAsJson_ByName_WithItsCitations()
    {
        await SeedReportAsync("exec-data", "org-data");

        ErrorOr<List<ReportDataOutputDto>> result = await _handler.Handle(
            new ListReportDataOutputsQuery("exec-data", "org-data"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(["facilities", "rating"], result.Value.Select(output => output.Name));

        ReportDataOutputDto facilities = result.Value[0];
        Assert.Equal(JsonValueKind.Object, facilities.Data.ValueKind);
        Assert.Equal(250000, facilities.Data.GetProperty("facilities")[0].GetProperty("limit").GetInt32());

        DataOutputCitationDto citation = Assert.Single(facilities.Citations);
        Assert.Equal("/facilities/0/limit", citation.Path);
        Assert.Equal("250000", citation.Value);
        DocumentCitationSourceDto source = Assert.IsType<DocumentCitationSourceDto>(Assert.Single(citation.Sources));
        Assert.Equal(2, source.PageNumber);
    }

    [Fact]
    public async Task Handle_AnotherOrganizationsExecution_ReturnsNothing()
    {
        await SeedReportAsync("exec-other", "org-owner");

        ErrorOr<List<ReportDataOutputDto>> result = await _handler.Handle(
            new ListReportDataOutputsQuery("exec-other", "org-intruder"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    private async Task SeedReportAsync(string executionId, string organizationId)
    {
        var report = new WorkflowReport
        {
            WorkflowExecutionId = executionId,
            OrganizationId = organizationId,
            Title = "Report",
        };

        _ = _context.Add(report);
        _ = _context.Add(new ReportDataOutput
        {
            WorkflowReportId = report.Id,
            WorkflowReport = report,
            Name = "rating",
            Json = """{ "rating": "BB+" }""",
        });
        _ = _context.Add(new ReportDataOutput
        {
            WorkflowReportId = report.Id,
            WorkflowReport = report,
            Name = "facilities",
            Json = FACILITIES_JSON,
            Citations =
            [
                new DataOutputCitation
                {
                    Id = "c0",
                    Path = "/facilities/0/limit",
                    Value = "250000",
                    IsNumerical = true,
                    Sources = [new DocumentSource { CitedText = "Limit 250,000", PageId = "page-2", PageNumber = 2, DocumentName = "Approval.pdf" }],
                },
            ],
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
