namespace IniaAi.Common.Interfaces;

/// <summary>
/// Opens chart sessions, so that one export draws all its charts with one Chrome.
/// </summary>
public interface IChartImageSessions
{
    /// <summary>A session that draws charts one after another. It starts Chrome for its first chart.</summary>
    IChartImageSession Open();
}

/// <summary>
/// Draws the charts of one export one after another with one Chrome, and frees that Chrome when disposed.
/// </summary>
public interface IChartImageSession : IChartImageRenderService, IAsyncDisposable;
