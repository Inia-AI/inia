namespace IniaAi.Application.Common.Reports;

/// <summary>
/// Whether a report's content, its sections, data outputs and exports, may be read. The platform
/// lets every report be read; a closed module may hide some.
/// </summary>
public interface IReportContentRule
{
    /// <summary>Why the report's content is hidden, in words for the reader with the next step; null when it may be read.</summary>
    ValueTask<string?> RefusalAsync(string organizationId, string workflowExecutionId, CancellationToken cancellationToken);
}

public sealed class EveryReportContent : IReportContentRule
{
    public ValueTask<string?> RefusalAsync(string organizationId, string workflowExecutionId, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<string?>(null);
    }
}
