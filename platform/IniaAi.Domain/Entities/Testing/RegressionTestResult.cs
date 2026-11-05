using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Testing;

/// <summary>
/// Represents a regression test execution result.
/// </summary>
public class RegressionTestResult : Entity<RegressionTestResult>, IOrganizationScoped, IIdentifiable
{
    /// <summary>
    /// Gets or sets the organization ID that owns this test result.
    /// </summary>
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of regression test (e.g., "Performance", "Integration").
    /// </summary>
    [StringLength(100)]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the application version that was tested (e.g., "0.9.681").
    /// </summary>
    [StringLength(50)]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date and time when the test was executed.
    /// </summary>
    public DateTime TestDate { get; set; }

    /// <summary>
    /// Gets or sets the test execution status.
    /// </summary>
    public RegressionTestStatus Status { get; set; } = RegressionTestStatus.Unknown;

    /// <summary>
    /// Gets or sets the test report content (HTML or other format).
    /// Can be null if report generation failed.
    /// </summary>
    public string? Report { get; set; }

    /// <summary>
    /// Gets or sets the test results data (e.g., TSV, JSON).
    /// Can be null if results capture failed.
    /// </summary>
    public string? Results { get; set; }
}
