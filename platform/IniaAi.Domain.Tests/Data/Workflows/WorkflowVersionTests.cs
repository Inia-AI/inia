using IniaAi.Domain.Entities.Workflows;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class WorkflowVersionTests
{
    [Fact]
    public void Validate_MissingRequiredFields_FailsValidation()
    {
        var version = new WorkflowVersion();
        var results = new List<ValidationResult>();

        bool valid = Validator.TryValidateObject(
            version,
            new ValidationContext(version),
            results,
            validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(WorkflowVersion.ContentHash)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(WorkflowVersion.WorkflowTypeId)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(WorkflowVersion.CreatedByUserId)));
    }

    [Fact]
    public void Validate_PopulatedRequiredFields_Passes()
    {
        var version = new WorkflowVersion
        {
            WorkflowTypeId = "WT",
            ContentHash = new string('a', 64),
            WorkflowJson = "{}",
            CreatedByUserId = "user",
        };
        var results = new List<ValidationResult>();

        bool valid = Validator.TryValidateObject(
            version,
            new ValidationContext(version),
            results,
            validateAllProperties: true);

        Assert.True(valid);
    }

    [Fact]
    public void ParentVersionId_IsOptional()
    {
        var version = new WorkflowVersion
        {
            WorkflowTypeId = "WT",
            ContentHash = new string('a', 64),
            WorkflowJson = "{}",
            CreatedByUserId = "user",
            ParentVersionId = null,
        };
        var results = new List<ValidationResult>();

        bool valid = Validator.TryValidateObject(
            version,
            new ValidationContext(version),
            results,
            validateAllProperties: true);

        Assert.True(valid);
    }
}
