using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Domain.Tests.UserManagement;

public class RolesDisplayNameTests
{
    private static readonly RoleGrants _grants = new([new ModuleRole("AuditReviewer", "Audit Reviewer", ActsWithinOrganizations: true)]);

    [Fact]
    public void AModuleRole_DisplaysItsDisplayName()
    {
        Assert.Equal("Audit Reviewer", _grants.DisplayName("AuditReviewer"));
    }

    [Theory]
    [InlineData(Roles.SUPER_ADMIN)]
    [InlineData(Roles.ADMIN)]
    [InlineData(Roles.USER)]
    [InlineData(Roles.WORKFLOW_EDITOR)]
    [InlineData(Roles.SIGNATURE_CONTRIBUTOR)]
    [InlineData(Roles.OPERATOR)]
    public void OtherRoles_DisplayTheirStoredName(string role)
    {
        Assert.Equal(role, _grants.DisplayName(role));
    }

    [Fact]
    public void UnknownRole_DisplaysItsStoredName()
    {
        Assert.Equal("Auditor", _grants.DisplayName("Auditor"));
    }
}
