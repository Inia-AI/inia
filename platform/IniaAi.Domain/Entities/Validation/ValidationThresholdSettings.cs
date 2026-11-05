using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// An organization's saved grading thresholds. One row per organization; an organization with
/// no row grades under the platform defaults below. Runs scored after a save are graded under
/// the saved pair; runs scored before it keep the outcome their scorecard recorded.
/// </summary>
[Index(nameof(OrganizationId), IsUnique = true)]
public class ValidationThresholdSettings : Entity<ValidationThresholdSettings>, IOrganizationScoped, IIdentifiable
{
    public const double DEFAULT_SUCCESS_AT_OR_ABOVE_PERCENT = 95;
    public const double DEFAULT_WARNING_AT_OR_ABOVE_PERCENT = 80;

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    public double SuccessAtOrAbovePercent { get; set; } = DEFAULT_SUCCESS_AT_OR_ABOVE_PERCENT;

    public double WarningAtOrAbovePercent { get; set; } = DEFAULT_WARNING_AT_OR_ABOVE_PERCENT;
}
