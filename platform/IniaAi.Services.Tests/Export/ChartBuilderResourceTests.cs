using IniaAi.Services.Export;
using System.Reflection;

namespace IniaAi.Services.Tests.Export;

public class ChartBuilderResourceTests
{
    private const string RESOURCE_NAME = "IniaAi.Services.Resources.chartBuilder.charts.global.js";

    private static readonly Assembly _services = typeof(ChartImageRenderService).Assembly;

    [Fact]
    public void TheChartBundle_IsEmbedded_AndDrawsForTheExport()
    {
        using Stream? stream = _services.GetManifestResourceStream(RESOURCE_NAME);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        string contents = reader.ReadToEnd();

        Assert.StartsWith("/*!", contents, StringComparison.Ordinal);
        Assert.Contains("Apache ECharts", contents, StringComparison.Ordinal);
        Assert.Contains("var IniaCharts", contents, StringComparison.Ordinal);
        Assert.Contains("drawForExport", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChartBundle_IsTheOnlyChartCodeTheServicesAssemblyEmbeds()
    {
        string[] chartResources = [.. _services.GetManifestResourceNames().Where(name => name.Contains("chart", StringComparison.OrdinalIgnoreCase))];

        Assert.Equal([RESOURCE_NAME], chartResources);
    }
}
