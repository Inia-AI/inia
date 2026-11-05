using System.Text;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Content;
using IniaAi.Common.Interfaces.Services.Reports;
using IniaAi.Configuration;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.Markdown;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Export;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Services.Tests.Export;

public class ExportServiceAnalystNameTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER_ID = "analyst-1";
    private const string ANALYST_EMAIL = "jordan.lee@example.com";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly IUserDataUnitOfWork _unitOfWork;

    public ExportServiceAnalystNameTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ExportAsync_ShowsTheAnalystsFullName_NotTheirEmail()
    {
        _ = _context.Users.Add(new User(ORG)
        {
            Id = USER_ID,
            FirstName = "Jordan",
            LastName = "Lee",
            UserName = ANALYST_EMAIL,
            Email = ANALYST_EMAIL,
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        string text = await ExportAsync(new WorkflowReport
        {
            Title = "Credit Analysis",
            UserId = USER_ID,
            AnalystName = ANALYST_EMAIL,
        });

        Assert.Contains("Jordan Lee", text);
        Assert.DoesNotContain(ANALYST_EMAIL, text);
    }

    [Fact]
    public async Task ExportAsync_FallsBackToTheStoredAnalystName_WhenTheUserIsNotFound()
    {
        string text = await ExportAsync(new WorkflowReport
        {
            Title = "Report",
            UserId = "unknown-user",
            AnalystName = "Stored Name",
        });

        Assert.Contains("Stored Name", text);
    }

    private async Task<string> ExportAsync(WorkflowReport report)
    {
        var exportService = new ExportService(
            NullLogger<ExportService>.Instance,
            _unitOfWork,
            new UserDisplayNameResolver(_unitOfWork),
            [new MarkdownReportWriter()],
            new ChartImageRenderService(Options.Create(new PlatformSettings()), NullLogger<ChartImageRenderService>.Instance),
            new NoReportNotice());

        BinaryContent result = await exportService.ExportAsync(report, ExportFormat.Markdown);
        return Encoding.UTF8.GetString(result.BinaryData.ToArray());
    }
}
