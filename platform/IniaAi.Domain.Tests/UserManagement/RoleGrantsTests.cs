using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Domain.Tests.UserManagement;

public class RoleGrantsTests
{
    private const string ORPHAN_ROLE = "RealMoneyFunds";
    private const string RUNNER_ROLE = "RegressionRunner";
    private const string MODULE_ROLE = "AuditReviewer";
    private const string OWN_ORG = "org-own";
    private const string SECOND_OWN_ORG = "org-own-second";
    private const string OTHER_ORG = "org-other";

    private static readonly HashSet<string> _managerOrganizations = [OWN_ORG, SECOND_OWN_ORG];
    private static readonly RoleGrants _grants = new([]);

    [Theory]
    [InlineData(Roles.USER)]
    [InlineData(Roles.ADMIN)]
    [InlineData(Roles.SIGNATURE_CONTRIBUTOR)]
    public void AnAdmin_GrantsAnOrganizationRole(string role)
    {
        Assert.True(_grants.CanGrant(granterIsSuperAdmin: false, role));
    }

    [Theory]
    [InlineData(Roles.SUPER_ADMIN)]
    [InlineData(Roles.WORKFLOW_EDITOR)]
    [InlineData(Roles.OPERATOR)]
    [InlineData(ORPHAN_ROLE)]
    public void AnAdmin_DoesNotGrantADeploymentWideOrUndefinedRole(string role)
    {
        Assert.False(_grants.CanGrant(granterIsSuperAdmin: false, role));
    }

    [Theory]
    [InlineData(Roles.SUPER_ADMIN)]
    [InlineData(Roles.WORKFLOW_EDITOR)]
    [InlineData(Roles.OPERATOR)]
    [InlineData(Roles.USER)]
    public void ASuperAdmin_GrantsEveryDefinedRole(string role)
    {
        Assert.True(_grants.CanGrant(granterIsSuperAdmin: true, role));
    }

    [Fact]
    public void ASuperAdmin_DoesNotGrantARoleTheCodeDoesNotDefine()
    {
        Assert.False(_grants.CanGrant(granterIsSuperAdmin: true, ORPHAN_ROLE));
    }

    [Theory]
    [InlineData(Roles.SUPER_ADMIN)]
    [InlineData(Roles.WORKFLOW_EDITOR)]
    [InlineData(Roles.OPERATOR)]
    public void AnAdmin_DoesNotManageAUserWhoHoldsADeploymentWideRole(string role)
    {
        Assert.False(_grants.CanManage(managerIsSuperAdmin: false, _managerOrganizations, [Roles.USER, role], [OWN_ORG]));
    }

    [Fact]
    public void AnAdmin_ManagesAUserWhoHoldsOrganizationAndUndefinedRolesOnly()
    {
        Assert.True(_grants.CanManage(managerIsSuperAdmin: false, _managerOrganizations, [Roles.USER, Roles.ADMIN, Roles.SIGNATURE_CONTRIBUTOR, ORPHAN_ROLE], [OWN_ORG]));
    }

    [Fact]
    public void AnAdmin_DoesNotManageAUserWhoAlsoBelongsToAnotherOrganization()
    {
        Assert.False(_grants.CanManage(managerIsSuperAdmin: false, _managerOrganizations, [Roles.USER], [OWN_ORG, OTHER_ORG]));
    }

    [Fact]
    public void AnAdmin_ManagesAUserWhoseOrganizationsAreAllTheirs()
    {
        Assert.True(_grants.CanManage(managerIsSuperAdmin: false, _managerOrganizations, [Roles.USER], [OWN_ORG, SECOND_OWN_ORG]));
    }

    [Fact]
    public void ASuperAdmin_ManagesAUserWhoHoldsSuperAdminInAnyOrganization()
    {
        Assert.True(_grants.CanManage(managerIsSuperAdmin: true, new HashSet<string>(), [Roles.SUPER_ADMIN], [OTHER_ORG]));
    }

    [Fact]
    public void Unmanageable_NamesOtherUsersWithADeploymentWideRoleOrAnotherOrganization_ButNeverTheManager()
    {
        (string UserId, string? RoleName)[] assignments =
        [
            ("manager", Roles.ADMIN),
            ("manager", Roles.WORKFLOW_EDITOR),
            ("editor", Roles.USER),
            ("editor", Roles.WORKFLOW_EDITOR),
            ("staff", Roles.SUPER_ADMIN),
            ("analyst", Roles.SIGNATURE_CONTRIBUTOR),
            ("legacy", ORPHAN_ROLE),
            ("shared", Roles.USER),
        ];
        (string UserId, string OrganizationId)[] memberships =
        [
            ("manager", OWN_ORG),
            ("manager", OTHER_ORG),
            ("editor", OWN_ORG),
            ("staff", OWN_ORG),
            ("analyst", OWN_ORG),
            ("legacy", SECOND_OWN_ORG),
            ("shared", OWN_ORG),
            ("shared", OTHER_ORG),
        ];

        Assert.Equal(
            ["editor", "shared", "staff"],
            _grants.Unmanageable(managerIsSuperAdmin: false, managerId: "manager", _managerOrganizations, assignments, memberships).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Unmanageable_IsEmptyForASuperAdmin()
    {
        Assert.Empty(_grants.Unmanageable(managerIsSuperAdmin: true, managerId: "root", new HashSet<string>(), [("staff", Roles.SUPER_ADMIN)], [("staff", OTHER_ORG)]));
    }

    [Fact]
    public void TheDefinedRoles_AreEveryRoleConstant()
    {
        Assert.Equal(
            [Roles.ADMIN, Roles.OPERATOR, Roles.SIGNATURE_CONTRIBUTOR, Roles.SUPER_ADMIN, Roles.USER, Roles.WORKFLOW_EDITOR],
            _grants.DefinedRoles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ABuildWithoutValidation_NeitherDefinesNorOffersTheRunnerRole()
    {
        Assert.DoesNotContain(RUNNER_ROLE, _grants.DefinedRoles);
        Assert.False(_grants.CanGrant(granterIsSuperAdmin: true, RUNNER_ROLE));
        Assert.False(_grants.CanGrant(granterIsSuperAdmin: false, RUNNER_ROLE));
    }

    [Fact]
    public void AModuleRoleThatActsWithinOrganizations_IsDefinedAndAnAdminGrantsIt()
    {
        RoleGrants grants = new([new ModuleRole(MODULE_ROLE, "Auditor", ActsWithinOrganizations: true)]);

        Assert.Contains(MODULE_ROLE, grants.DefinedRoles);
        Assert.True(grants.CanGrant(granterIsSuperAdmin: false, MODULE_ROLE));
        Assert.True(grants.CanManage(managerIsSuperAdmin: false, _managerOrganizations, [MODULE_ROLE], [OWN_ORG]));
    }

    [Fact]
    public void AModuleRoleThatReachesEveryOrganization_IsGrantedBySuperAdminOnly()
    {
        RoleGrants grants = new([new ModuleRole(MODULE_ROLE, "Auditor", ActsWithinOrganizations: false)]);

        Assert.True(grants.CanGrant(granterIsSuperAdmin: true, MODULE_ROLE));
        Assert.False(grants.CanGrant(granterIsSuperAdmin: false, MODULE_ROLE));
        Assert.False(grants.CanManage(managerIsSuperAdmin: false, _managerOrganizations, [MODULE_ROLE], [OWN_ORG]));
    }

    [Fact]
    public void TheCustomerAdmin_IsGrantedOnlyTheModuleRolesThatAskForIt()
    {
        RoleGrants grants = new(
        [
            new ModuleRole(MODULE_ROLE, "Auditor", ActsWithinOrganizations: true, GrantedToTheCustomerAdmin: true),
            new ModuleRole(RUNNER_ROLE, "Runner", ActsWithinOrganizations: true),
        ]);

        Assert.Equal([MODULE_ROLE], grants.CustomerAdminModuleRoles);
        Assert.Empty(_grants.CustomerAdminModuleRoles);
    }

    [Fact]
    public void OperatorSurfaces_AdmitOperatorAndSuperAdmin()
    {
        Assert.Equal([Roles.OPERATOR, Roles.SUPER_ADMIN], Roles.OPERATOR_ROLES.Split(','));
    }
}
