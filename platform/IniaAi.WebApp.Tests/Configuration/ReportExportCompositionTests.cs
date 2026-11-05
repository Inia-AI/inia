using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces;
using IniaAi.Services.Export;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApp.Tests.Configuration;

/// <summary>
/// The admin app builds the export command's handler at startup, so it registers the export and the chart renderer
/// the export needs, the same way the API does.
/// </summary>
public class ReportExportCompositionTests
{
    [Fact]
    public void SetupServices_RegistersTheExportWithTheChartRenderer()
    {
        IServiceCollection services = WorkQueueCompositionTests.BuildWebAppServices();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExportService) && descriptor.ImplementationType == typeof(ExportService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IChartImageSessions) && descriptor.ImplementationType == typeof(ChartImageRenderService));
    }
}
