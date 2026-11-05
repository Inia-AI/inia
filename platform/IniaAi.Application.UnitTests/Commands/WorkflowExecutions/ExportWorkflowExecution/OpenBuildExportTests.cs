using System.Text;
using DocumentFormat.OpenXml.Packaging;
using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution;
using IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution.Models;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Configuration;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.FileFormats;
using IniaAi.FileFormats.Word;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Export;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.ExportWorkflowExecution;

/// <summary>A build without the paid file formats registers only the open ones.</summary>
public sealed class OpenBuildExportTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-id";
    private const string EXECUTION_ID = "open-build-execution";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public OpenBuildExportTests()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddTestDatabaseServices();
        _ = services.AddApplication();
        _ = services.AddOpenFileFormats();
        _ = services.AddSingleton(Options.Create(new PlatformSettings()));
        _ = services.AddScoped<IUserDisplayNameResolver, UserDisplayNameResolver>();
        _ = services.AddReportExport();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public async Task Export_Markdown_WritesTheReport()
    {
        await ArrangeReportAsync();

        ErrorOr<WorkflowExecutionExportResult> result = await SendAsync(ExportFormat.Markdown);

        Assert.False(result.IsError);
        Assert.EndsWith(".md", result.Value.FileName, StringComparison.Ordinal);
        Assert.Contains("## Credit Summary", Encoding.UTF8.GetString(result.Value.Content.BinaryData.ToArray()));
    }

    [Fact]
    public async Task Export_Word_WritesTheReportAsADocx()
    {
        await ArrangeReportAsync();

        ErrorOr<WorkflowExecutionExportResult> result = await SendAsync(ExportFormat.Word);

        Assert.False(result.IsError);
        Assert.EndsWith(".docx", result.Value.FileName, StringComparison.Ordinal);
        using var stream = new MemoryStream(result.Value.Content.BinaryData.ToArray());
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, isEditable: false);
        Assert.Contains("Leverage stays inside the covenant.", document.MainDocumentPart!.Document!.Body!.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddOpenFileFormats_Twice_RegistersOneWriterPerFormat()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging().AddOpenFileFormats().AddOpenFileFormats();
        await using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal([ExportFormat.Word, ExportFormat.Markdown], provider.GetServices<IReportFileWriter>().Select(writer => writer.Format).Order());
    }

    private Task<ErrorOr<WorkflowExecutionExportResult>> SendAsync(ExportFormat format)
    {
        return _scope.ServiceProvider.GetRequiredService<ISender>().Send(
            new ExportWorkflowExecutionCommand(EXECUTION_ID, format, "user-id", ORGANIZATION_ID),
            TestContext.Current.CancellationToken);
    }

    private async Task ArrangeReportAsync()
    {
        UserDataDbContext userData = _scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = userData.WorkflowExecutions.Add(new WorkflowExecution { Id = EXECUTION_ID, OrganizationId = ORGANIZATION_ID, Title = "Open build run" });
        var report = new WorkflowReport { WorkflowExecutionId = EXECUTION_ID };
        var section = new ReportSection { Title = "Credit Summary" };
        report.Add(section);
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = "Leverage stays inside the covenant." });
        _ = userData.WorkflowReports.Add(report);
        _ = await userData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
