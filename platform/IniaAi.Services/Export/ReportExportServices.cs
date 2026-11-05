using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Services.Export;

public static class ReportExportServices
{
    /// <summary>Lets the platform export a run's report, drawing each chart as a picture. The file writers come from the file format registrations.</summary>
    public static IServiceCollection AddReportExport(this IServiceCollection services)
    {
        return services
            .AddScoped<IChartImageSessions, ChartImageRenderService>()
            .AddScoped<IExportService, ExportService>();
    }
}
