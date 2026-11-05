namespace IniaAi.Common.Interfaces.Services.Reports;

public sealed class NoReportNotice : IReportNotice
{
    public Task<string?> FooterAsync(string organizationId, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }
}
