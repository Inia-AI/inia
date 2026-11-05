using IniaAi.Application.Common.Services;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces;
using IniaAi.Common.Interfaces.Services.Reports;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.FileFormats.ContractTests.Reports;
using IniaAi.FileFormats.Word;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Export;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Services.Tests.Export;

public sealed class ExportServiceChartSessionTests : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;

    public ExportServiceChartSessionTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task ExportAsync_DrawsTheReportsChartsInOneChartSession_AndClosesItAfterTheFileIsWritten()
    {
        var sessions = new CountingChartSessions();
        var writer = new RecordingWordWriter();
        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        var export = new ExportService(NullLogger<ExportService>.Instance, unitOfWork, new UserDisplayNameResolver(unitOfWork), [writer], sessions, new NoReportNotice());

        _ = await export.ExportAsync(NewsMarketDataFixture.Build(), ExportFormat.Word);

        ChartSession session = Assert.Single(sessions.Opened);
        Assert.Same(session, writer.Charts);
        Assert.False(writer.ChartsWereClosedWhileWriting);
        Assert.True(session.Closed);
    }

    /// <summary>Chrome, which draws each chart picture, is an outside process; these sessions draw nothing.</summary>
    private sealed class CountingChartSessions : IChartImageSessions
    {
        public List<ChartSession> Opened { get; } = [];

        public IChartImageSession Open()
        {
            var session = new ChartSession();
            Opened.Add(session);
            return session;
        }
    }

    private sealed class ChartSession : IChartImageSession
    {
        public bool Closed { get; private set; }

        public Task<byte[]> RenderChartToImageAsync(object chartConfig, int width, int height, string? reportContext = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Chrome did not start.");
        }

        public ValueTask DisposeAsync()
        {
            Closed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingWordWriter : IReportFileWriter
    {
        public IChartImageRenderService? Charts { get; private set; }

        public bool ChartsWereClosedWhileWriting { get; private set; }

        public ExportFormat Format => ExportFormat.Word;

        public string Extension => ".docx";

        public string ContentType => "application/octet-stream";

        public Task<BinaryContent> WriteAsync(IDocumentElement document, ReportFileOptions options)
        {
            Charts = options.ChartRenderService;
            ChartsWereClosedWhileWriting = (options.ChartRenderService as ChartSession)?.Closed ?? false;
            return Task.FromResult(new BinaryContent(BinaryData.FromString("docx"), MediaType.ApplicationOctetStream));
        }
    }
}
