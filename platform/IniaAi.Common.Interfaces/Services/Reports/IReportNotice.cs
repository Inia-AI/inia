namespace IniaAi.Common.Interfaces.Services.Reports;

/// <summary>
/// The line an organization's exported reports carry at their foot, on every page of a paged
/// format. The platform adds none; a closed module may.
/// </summary>
public interface IReportNotice
{
    /// <summary>The line for the organization's exports, one line of plain text, or null for none.</summary>
    Task<string?> FooterAsync(string organizationId, CancellationToken cancellationToken);
}
