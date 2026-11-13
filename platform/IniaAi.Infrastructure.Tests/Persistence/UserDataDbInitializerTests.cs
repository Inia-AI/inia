using IniaAi.Configuration;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.Seed;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using System.Text.Json;
using SqliteUserDataDbContext = IniaAi.Testing.DependencyInjection.SqliteUserDataDbContext;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// The seeder runs on every boot against whatever an earlier boot left, and a local database is
/// booted under more than one customer organization: the AWS launch profiles read it from the dev
/// parameter store, the others from appsettings.json.
/// </summary>
public class UserDataDbInitializerTests : IDisposable
{
    private const string SUPER_ADMIN_ID = "1234";
    private const string CUSTOMER_ADMIN_ID = "6789";
    private const string INIA_ORG_ID = "IniaAI";
    private const string FIRST_CUSTOMER = "ExampleBank";
    private const string SECOND_CUSTOMER = "Inia";
    private const string NEUTRAL_TENANT = "Default";
    private const string CUSTOMER_ADMIN_EMAIL = "admin@example.com";
    private const string SUPER_ADMIN_PASSWORD = "First-admin-password-1";
    private const string CUSTOMER_ADMIN_PASSWORD = "Customer-admin-password-1";
    private const string FIRST_STAFF = "operator@inia.ai";
    private const string SECOND_STAFF = "editor@inia.ai";

    private static readonly SeedAccountPasswords _configuredPasswords = new(SUPER_ADMIN_PASSWORD, CUSTOMER_ADMIN_PASSWORD);
    private static readonly SeedAccountPasswords _noPasswords = new(null, null);

    private readonly SqliteSeedDatabase _database = new();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _database.Dispose();
    }

    [Fact]
    public void AFreshCustomerDeployment_SeedsItsAdminWithRoles()
    {
        Seed(FIRST_CUSTOMER);

        using var context = new SqliteUserDataDbContext(_database.Options);
        User admin = context.Users.Single(u => u.Id == CUSTOMER_ADMIN_ID);
        Assert.Equal(CUSTOMER_ADMIN_EMAIL, admin.Email);
        Assert.True(HasRole(context, CUSTOMER_ADMIN_ID, Roles.ADMIN));
        Assert.True(HasRole(context, SUPER_ADMIN_ID, Roles.SUPER_ADMIN));
    }

    [Fact]
    public void AFreshInstallWithTheShippedDefault_SeedsTheNeutralTenantAndAnAdminAtAReservedDomain()
    {
        Seed(new PlatformSettings().CustomerOrgId);

        using var context = new SqliteUserDataDbContext(_database.Options);
        Organization tenant = context.Organizations.Single(o => o.Id != INIA_ORG_ID);
        Assert.Equal(NEUTRAL_TENANT, tenant.Id);
        Assert.Equal(CUSTOMER_ADMIN_EMAIL, context.Users.Single(u => u.Id == CUSTOMER_ADMIN_ID).Email);
    }

    [Theory]
    [InlineData("IniaAi.WebApi")]
    [InlineData("IniaAi.WebApp")]
    public void TheShippedSettings_NameTheCodeDefaultTenant(string app)
    {
        string path = Path.Combine(RepositoryRoot.Find(), app, "appsettings.json");
        using var settings = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        string? shipped = settings.RootElement.GetProperty("PlatformSettings").GetProperty("CustomerOrgId").GetString();

        Assert.Equal(new PlatformSettings().CustomerOrgId, shipped);
    }

    [Fact]
    public void AFreshDeploymentWithoutValidation_SeedsNoRunnerRole()
    {
        Seed(FIRST_CUSTOMER);

        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.DoesNotContain(context.Roles, role => role.Name == "RegressionRunner");
        Assert.Equal(
            new RoleGrants([]).DefinedRoles.Order(StringComparer.Ordinal),
            context.Roles.Select(role => role.Name!).AsEnumerable().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AFreshDeploymentWithoutClosedModules_SeedsOnlyThePlatformsRolesGrantsAndCounterpartyTypes()
    {
        Seed(FIRST_CUSTOMER);

        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.Equal(
            [Roles.ADMIN, Roles.OPERATOR, Roles.SIGNATURE_CONTRIBUTOR, Roles.SUPER_ADMIN, Roles.USER, Roles.WORKFLOW_EDITOR],
            context.Roles.Select(role => role.Name!).AsEnumerable().Order(StringComparer.Ordinal));
        Assert.Equal(
            [Roles.ADMIN, Roles.USER, Roles.WORKFLOW_EDITOR],
            SqliteSeedDatabase.RolesOf(context, CUSTOMER_ADMIN_ID).Order(StringComparer.Ordinal));
        Assert.Equal(
            [CounterpartyType.TYPE_CORPORATION, CounterpartyType.TYPE_REAL_MONEY_FUND, CounterpartyType.TYPE_REAL_MONEY_FUND_MANAGER],
            context.CounterpartyTypes.Select(type => type.Name).AsEnumerable().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ARestartUnderAnotherCustomerOrganization_FinishesSeeding()
    {
        Seed(FIRST_CUSTOMER);
        RemoveCounterpartyTypes();

        Seed(SECOND_CUSTOMER);

        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.Equal(CUSTOMER_ADMIN_EMAIL, context.Users.Single(u => u.Id == CUSTOMER_ADMIN_ID).Email);
        Assert.NotEmpty(context.CounterpartyTypes);
    }

    [Fact]
    public void ADeploymentWithoutACustomerAdmin_GivesTheSuperAdminItsRoles()
    {
        Seed(INIA_ORG_ID);

        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.DoesNotContain(context.Users, u => u.Id == CUSTOMER_ADMIN_ID);
        Assert.True(HasRole(context, SUPER_ADMIN_ID, Roles.SUPER_ADMIN));
        Assert.NotEmpty(context.CounterpartyTypes);
    }

    [Fact]
    public void AFreshCustomerDeployment_SignsBothAdminsInWithTheConfiguredPasswords()
    {
        Seed(FIRST_CUSTOMER);

        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.True(AcceptsPassword(context.Users.Single(u => u.Id == SUPER_ADMIN_ID), SUPER_ADMIN_PASSWORD));
        Assert.True(AcceptsPassword(context.Users.Single(u => u.Id == CUSTOMER_ADMIN_ID), CUSTOMER_ADMIN_PASSWORD));
    }

    [Fact]
    public void AFreshDeploymentWithASuperAdminEmail_SeedsTheFirstAdminAtThatAddress()
    {
        using (var context = new SqliteUserDataDbContext(_database.Options))
        {
            new UserDataDbInitializer(NullLogger.Instance, context, FIRST_CUSTOMER, _configuredPasswords, new RoleGrants([]), new BuiltInCounterpartyTypes([]), new StaffSeed([]), superAdminEmail: "superadmin@example.com").Initialize();
        }

        using var check = new SqliteUserDataDbContext(_database.Options);
        Assert.Equal("superadmin@example.com", check.Users.Single(u => u.Id == SUPER_ADMIN_ID).Email);
    }

    [Fact]
    public void ASuperAdminEmailThatIsTheCustomerAdminsAddress_RefusesNamingTheKey()
    {
        using var context = new SqliteUserDataDbContext(_database.Options);
        var initializer = new UserDataDbInitializer(NullLogger.Instance, context, FIRST_CUSTOMER, _configuredPasswords, new RoleGrants([]), new BuiltInCounterpartyTypes([]), new StaffSeed([]), superAdminEmail: "Admin@Example.com");

        SeedConfigurationException refusal = Assert.Throws<SeedConfigurationException>(initializer.Initialize);

        Assert.Contains(UserDataDbInitializer.SUPER_ADMIN_EMAIL_KEY, refusal.Message);
    }

    [Fact]
    public void TheSuperAdminEmail_ComesFromItsSetting_AndKeepsItsDefaultWithoutOne()
    {
        IConfiguration configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [UserDataDbInitializer.SUPER_ADMIN_EMAIL_KEY] = "admin@example.com" })
            .Build();

        Assert.Equal("admin@example.com", UserDataDbInitializer.SuperAdminEmailFrom(configured));
        Assert.Equal(UserDataDbInitializer.DEFAULT_SUPER_ADMIN_EMAIL, UserDataDbInitializer.SuperAdminEmailFrom(new ConfigurationBuilder().Build()));
    }

    [Fact]
    public void AFreshDeploymentWithoutTheSuperAdminPassword_RefusesNamingTheKey()
    {
        SeedConfigurationException refusal = Assert.Throws<SeedConfigurationException>(
            () => Seed(INIA_ORG_ID, new SeedAccountPasswords(null, CUSTOMER_ADMIN_PASSWORD)));

        Assert.Contains(SeedAccountPasswords.SUPER_ADMIN_PASSWORD_KEY, refusal.Message);
        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.DoesNotContain(context.Users, u => u.Id == SUPER_ADMIN_ID);
    }

    [Fact]
    public void AFreshCustomerDeploymentWithoutTheCustomerAdminPassword_RefusesNamingTheKey()
    {
        SeedConfigurationException refusal = Assert.Throws<SeedConfigurationException>(
            () => Seed(FIRST_CUSTOMER, new SeedAccountPasswords(SUPER_ADMIN_PASSWORD, " ")));

        Assert.Contains(SeedAccountPasswords.CUSTOMER_ADMIN_PASSWORD_KEY, refusal.Message);
        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.DoesNotContain(context.Users, u => u.Id == CUSTOMER_ADMIN_ID);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("password1")]
    public void AWeakFirstPassword_RefusesNamingTheKeyAndTheRule(string weak)
    {
        SeedConfigurationException refusal = Assert.Throws<SeedConfigurationException>(
            () => Seed(INIA_ORG_ID, new SeedAccountPasswords(weak, CUSTOMER_ADMIN_PASSWORD)));

        Assert.Contains(SeedAccountPasswords.SUPER_ADMIN_PASSWORD_KEY, refusal.Message);
        Assert.Contains("not one of the most commonly used passwords", refusal.Message);
        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.DoesNotContain(context.Users, u => u.Id == SUPER_ADMIN_ID);
    }

    [Fact]
    public void ARestartWithBothAdminsInPlace_NeedsNoPasswordAndKeepsTheirs()
    {
        Seed(FIRST_CUSTOMER);

        Seed(FIRST_CUSTOMER, _noPasswords);

        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.True(AcceptsPassword(context.Users.Single(u => u.Id == SUPER_ADMIN_ID), SUPER_ADMIN_PASSWORD));
    }

    [Fact]
    public void TheSeededAdmins_HaveUserNamesIdentityAccepts()
    {
        Seed(FIRST_CUSTOMER);

        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.All(
            context.Users.Where(u => u.Id == SUPER_ADMIN_ID || u.Id == CUSTOMER_ADMIN_ID).ToList(),
            user => Assert.True(IdentityUserNames.Accepts(user.UserName), $"{user.Id} has a user name Identity rejects"));
    }

    [Fact]
    public void AnAccountWhoseUserNameIdentityRejects_TakesItsEmailAtTheNextStart()
    {
        Seed(FIRST_CUSTOMER);
        using (var context = new SqliteUserDataDbContext(_database.Options))
        {
            User admin = context.Users.Single(u => u.Id == CUSTOMER_ADMIN_ID);
            admin.UserName = $"{FIRST_CUSTOMER} Admin";
            admin.NormalizedUserName = admin.UserName.ToUpperInvariant();
            _ = context.SaveChanges();
        }

        Seed(FIRST_CUSTOMER, _noPasswords);

        using var reloaded = new SqliteUserDataDbContext(_database.Options);
        User repaired = reloaded.Users.Single(u => u.Id == CUSTOMER_ADMIN_ID);
        Assert.Equal(repaired.Email, repaired.UserName);
        Assert.Equal(repaired.Email!.ToUpperInvariant(), repaired.NormalizedUserName);
    }

    [Fact]
    public void APersonWhoseLockoutIsOff_GetsItAtTheNextStart()
    {
        Seed(FIRST_CUSTOMER);
        using (var context = new SqliteUserDataDbContext(_database.Options))
        {
            context.Users.Single(u => u.Id == CUSTOMER_ADMIN_ID).LockoutEnabled = false;
            _ = context.SaveChanges();
        }

        Seed(FIRST_CUSTOMER, _noPasswords);

        using var reloaded = new SqliteUserDataDbContext(_database.Options);
        Assert.True(reloaded.Users.Single(u => u.Id == CUSTOMER_ADMIN_ID).LockoutEnabled);
    }

    [Fact]
    public void AStaffList_SeedsEachAddressInTheIniaOrganizationWithItsRolesAndNoPassword()
    {
        Seed(FIRST_CUSTOMER, new StaffSeed([new StaffAccount(FIRST_STAFF, [Roles.SUPER_ADMIN, Roles.ADMIN]), new StaffAccount(SECOND_STAFF, [Roles.WORKFLOW_EDITOR])]));

        using var context = new SqliteUserDataDbContext(_database.Options);
        User first = context.Users.Include(u => u.UserOrganizations).Single(u => u.Email == FIRST_STAFF);
        Assert.Equal(INIA_ORG_ID, Assert.Single(first.UserOrganizations).OrganizationId);
        Assert.True(first.EmailConfirmed);
        Assert.Null(first.PasswordHash);
        Assert.True(HasRole(context, first.Id, Roles.SUPER_ADMIN));
        Assert.True(HasRole(context, first.Id, Roles.ADMIN));
        User second = context.Users.Single(u => u.Email == SECOND_STAFF);
        Assert.True(HasRole(context, second.Id, Roles.WORKFLOW_EDITOR));
        Assert.False(HasRole(context, second.Id, Roles.SUPER_ADMIN));
    }

    [Fact]
    public void ARestartWithAWiderStaffList_AddsTheNewRoleAndKeepsOneAccount()
    {
        Seed(FIRST_CUSTOMER, new StaffSeed([new StaffAccount(FIRST_STAFF, [Roles.WORKFLOW_EDITOR])]));

        Seed(FIRST_CUSTOMER, _noPasswords, new StaffSeed([new StaffAccount(FIRST_STAFF.ToUpperInvariant(), [Roles.WORKFLOW_EDITOR, Roles.ADMIN])]));

        using var context = new SqliteUserDataDbContext(_database.Options);
        User staff = context.Users.Single(u => u.NormalizedEmail == FIRST_STAFF.ToUpperInvariant());
        Assert.True(HasRole(context, staff.Id, Roles.ADMIN));
        Assert.True(HasRole(context, staff.Id, Roles.WORKFLOW_EDITOR));
    }

    [Fact]
    public void TheSameAddressListedTwice_MakesOneAccountWithBothEntriesRoles()
    {
        Seed(FIRST_CUSTOMER, new StaffSeed([new StaffAccount(FIRST_STAFF, [Roles.ADMIN, Roles.ADMIN]), new StaffAccount(FIRST_STAFF.ToUpperInvariant(), [Roles.WORKFLOW_EDITOR])]));

        using var context = new SqliteUserDataDbContext(_database.Options);
        User staff = context.Users.Single(u => u.NormalizedEmail == FIRST_STAFF.ToUpperInvariant());
        Assert.True(HasRole(context, staff.Id, Roles.ADMIN));
        Assert.True(HasRole(context, staff.Id, Roles.WORKFLOW_EDITOR));
    }

    [Fact]
    public void AStaffRoleTheDeploymentDoesNotDefine_RefusesNamingTheEntry()
    {
        var unknownRole = new StaffSeed([new StaffAccount(FIRST_STAFF, ["Owner"])]);

        SeedConfigurationException refused = Assert.Throws<SeedConfigurationException>(() => Seed(FIRST_CUSTOMER, unknownRole));

        Assert.Contains($"{StaffSeed.STAFF_KEY}:0", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Owner", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("o'brien@inia.ai")]
    [InlineData("no-at-sign.inia.ai")]
    public void AStaffAddressThePlatformCannotUse_RefusesNamingTheEntryAndNotTheAddress(string address)
    {
        var rejected = new StaffSeed([new StaffAccount(FIRST_STAFF, [Roles.ADMIN]), new StaffAccount(address, [Roles.ADMIN])]);

        SeedConfigurationException refused = Assert.Throws<SeedConfigurationException>(() => Seed(FIRST_CUSTOMER, rejected));

        Assert.Contains($"{StaffSeed.STAFF_KEY}:1:Email", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(address, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaffEntryWithNoRoles_RefusesNamingTheEntry()
    {
        SeedConfigurationException refused = Assert.Throws<SeedConfigurationException>(
            () => Seed(FIRST_CUSTOMER, new StaffSeed([new StaffAccount(FIRST_STAFF, [])])));

        Assert.Contains($"{StaffSeed.STAFF_KEY}:0:Roles", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaffAddressThatIsAnotherAccountsUserName_RefusesNamingTheEntryAndNotTheAddress()
    {
        Seed(FIRST_CUSTOMER);
        using (var context = new SqliteUserDataDbContext(_database.Options))
        {
            User other = context.Users.Single(u => u.Id == CUSTOMER_ADMIN_ID);
            other.UserName = FIRST_STAFF;
            other.NormalizedUserName = FIRST_STAFF.ToUpperInvariant();
            _ = context.SaveChanges();
        }

        SeedConfigurationException refused = Assert.Throws<SeedConfigurationException>(
            () => Seed(FIRST_CUSTOMER, _noPasswords, new StaffSeed([new StaffAccount(FIRST_STAFF, [Roles.ADMIN])])));

        Assert.Contains($"{StaffSeed.STAFF_KEY}:0:Email", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FIRST_STAFF, refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AStaffAddressOfAnAccountInAnotherOrganization_GainsTheRolesAndKeepsItsOrganization()
    {
        Seed(FIRST_CUSTOMER);

        Seed(FIRST_CUSTOMER, _noPasswords, new StaffSeed([new StaffAccount(CUSTOMER_ADMIN_EMAIL, [Roles.WORKFLOW_EDITOR])]));

        using var context = new SqliteUserDataDbContext(_database.Options);
        User admin = context.Users.Include(u => u.UserOrganizations).Single(u => u.Id == CUSTOMER_ADMIN_ID);
        Assert.Equal(FIRST_CUSTOMER, Assert.Single(admin.UserOrganizations).OrganizationId);
        Assert.True(HasRole(context, CUSTOMER_ADMIN_ID, Roles.WORKFLOW_EDITOR));
    }

    [Fact]
    public void DisablingTheSuperAdmin_KeepsItDisabledAtEveryStartAndRevokesItsTokens()
    {
        var disables = new StaffSeed([new StaffAccount(FIRST_STAFF, [Roles.SUPER_ADMIN])], DisablesSuperAdmin: true);
        Seed(FIRST_CUSTOMER);
        string? stampBefore;
        using (var context = new SqliteUserDataDbContext(_database.Options))
        {
            stampBefore = context.Users.Single(u => u.Id == SUPER_ADMIN_ID).SecurityStamp;
            _ = context.Tokens.Add(new OpenIddictEntityFrameworkCoreToken { Subject = SUPER_ADMIN_ID, Status = Statuses.Valid, Type = TokenTypeHints.RefreshToken });
            _ = context.SaveChanges();
        }

        Seed(FIRST_CUSTOMER, _noPasswords, disables);
        using (var context = new SqliteUserDataDbContext(_database.Options))
        {
            User disabled = context.Users.Single(u => u.Id == SUPER_ADMIN_ID);
            Assert.True(disabled.IsDisabled);
            Assert.NotEqual(stampBefore, disabled.SecurityStamp);
            Assert.Equal(Statuses.Revoked, context.Tokens.Single(token => token.Subject == SUPER_ADMIN_ID).Status);
            disabled.LockoutEnd = null;
            _ = context.SaveChanges();
        }

        Seed(FIRST_CUSTOMER, _noPasswords, disables);

        using var reloaded = new SqliteUserDataDbContext(_database.Options);
        Assert.True(reloaded.Users.Single(u => u.Id == SUPER_ADMIN_ID).IsDisabled);
    }

    [Fact]
    public void DisablingTheSuperAdminWithNoStaffSuperAdmin_RefusesAndLeavesItEnabled()
    {
        Seed(FIRST_CUSTOMER);

        SeedConfigurationException refused = Assert.Throws<SeedConfigurationException>(
            () => Seed(FIRST_CUSTOMER, _noPasswords, new StaffSeed([new StaffAccount(FIRST_STAFF, [Roles.ADMIN])], DisablesSuperAdmin: true)));

        Assert.Contains(StaffSeed.DISABLE_SUPER_ADMIN_KEY, refused.Message, StringComparison.Ordinal);
        Assert.False(SuperAdminIsDisabled());
    }

    [Fact]
    public void ListingTheSuperAdminItselfAsTheStaffSuperAdmin_RefusesAndLeavesItEnabled()
    {
        Seed(FIRST_CUSTOMER);

        _ = Assert.Throws<SeedConfigurationException>(
            () => Seed(FIRST_CUSTOMER, _noPasswords, new StaffSeed([new StaffAccount("superadmin@inia.ai", [Roles.SUPER_ADMIN])], DisablesSuperAdmin: true)));

        Assert.False(SuperAdminIsDisabled());
    }

    [Fact]
    public void OneBadStaffEntry_StopsTheStartWithTheSuperAdminStillEnabled()
    {
        Seed(FIRST_CUSTOMER);
        var oneBad = new StaffSeed([new StaffAccount(FIRST_STAFF, [Roles.SUPER_ADMIN]), new StaffAccount(SECOND_STAFF, ["Owner"])], DisablesSuperAdmin: true);

        _ = Assert.Throws<SeedConfigurationException>(() => Seed(FIRST_CUSTOMER, _noPasswords, oneBad));

        Assert.False(SuperAdminIsDisabled());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("yes")]
    public void ADisableValueThatIsNotTrueOrFalse_RefusesNamingTheKey(string value)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [StaffSeed.DISABLE_SUPER_ADMIN_KEY] = value })
            .Build();

        SeedConfigurationException refused = Assert.Throws<SeedConfigurationException>(() => StaffSeed.From(configuration));

        Assert.Contains(StaffSeed.DISABLE_SUPER_ADMIN_KEY, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFreshDeploymentWithPasswordSignInOff_NeedsNoPasswordForEitherAdmin()
    {
        Seed(FIRST_CUSTOMER, new SeedAccountPasswords(null, null, PasswordSignInOn: false));

        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.Null(context.Users.Single(u => u.Id == SUPER_ADMIN_ID).PasswordHash);
        Assert.Null(context.Users.Single(u => u.Id == CUSTOMER_ADMIN_ID).PasswordHash);
    }

    [Fact]
    public void TheSeedPasswords_ReadWhetherPasswordSignInIsOn()
    {
        IConfiguration off = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Password:Enabled"] = "false" })
            .Build();

        Assert.False(SeedAccountPasswords.From(off).PasswordSignInOn);
        Assert.True(SeedAccountPasswords.From(new ConfigurationBuilder().Build()).PasswordSignInOn);
    }

    [Fact]
    public void TheStaffSeed_ReadsTheListAndTheSwitchFromConfiguration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seeding:Staff:0:Email"] = FIRST_STAFF,
                ["Seeding:Staff:0:Roles:0"] = Roles.SUPER_ADMIN,
                ["Seeding:Staff:0:Roles:1"] = Roles.ADMIN,
                ["Seeding:Staff:1:Email"] = SECOND_STAFF,
                ["Seeding:Staff:1:Roles:0"] = Roles.WORKFLOW_EDITOR,
                ["Seeding:DisableSuperAdmin"] = "true",
            })
            .Build();

        StaffSeed seed = StaffSeed.From(configuration);

        Assert.True(seed.DisablesSuperAdmin);
        Assert.Equal([FIRST_STAFF, SECOND_STAFF], seed.Accounts.Select(a => a.Email));
        Assert.Equal([Roles.SUPER_ADMIN, Roles.ADMIN], seed.Accounts[0].Roles);
    }

    [Fact]
    public void RolesSetAsOneValue_RefuseNamingTheKey()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seeding:Staff:0:Email"] = FIRST_STAFF,
                ["Seeding:Staff:0:Roles"] = Roles.SUPER_ADMIN,
            })
            .Build();

        SeedConfigurationException refused = Assert.Throws<SeedConfigurationException>(() => StaffSeed.From(configuration));

        Assert.Contains("Seeding:Staff:0:Roles", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoSeedingSettings_NoStaffAreSeededAndTheSuperAdminStaysOpen()
    {
        Seed(FIRST_CUSTOMER, StaffSeed.From(new ConfigurationBuilder().Build()));

        using var context = new SqliteUserDataDbContext(_database.Options);
        Assert.False(context.Users.Single(u => u.Id == SUPER_ADMIN_ID).IsDisabled);
        Assert.Equal(2, context.Users.Count(u => !u.IsSystem));
    }

    private void Seed(string customerOrgId)
    {
        Seed(customerOrgId, _configuredPasswords);
    }

    private void Seed(string customerOrgId, StaffSeed staff)
    {
        Seed(customerOrgId, _configuredPasswords, staff);
    }

    private void Seed(string customerOrgId, SeedAccountPasswords passwords, StaffSeed? staff = null)
    {
        using var context = new SqliteUserDataDbContext(_database.Options);
        new UserDataDbInitializer(NullLogger.Instance, context, customerOrgId, passwords, new RoleGrants([]), new BuiltInCounterpartyTypes([]), staff ?? new StaffSeed([])).Initialize();
    }

    private bool SuperAdminIsDisabled()
    {
        using var context = new SqliteUserDataDbContext(_database.Options);
        return context.Users.Single(u => u.Id == SUPER_ADMIN_ID).IsDisabled;
    }

    private static bool AcceptsPassword(User user, string password)
    {
        return new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash!, password)
            != PasswordVerificationResult.Failed;
    }

    private void RemoveCounterpartyTypes()
    {
        using var context = new SqliteUserDataDbContext(_database.Options);
        context.CounterpartyTypes.RemoveRange(context.CounterpartyTypes);
        _ = context.SaveChanges();
    }

    private static bool HasRole(UserDataDbContext context, string userId, string roleName)
    {
        string roleId = context.Roles.Single(r => r.Name == roleName).Id;
        return context.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == roleId);
    }
}
