using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.Infrastructure.Persistence.UserData.Contexts;

public class UserDataDbInitializer
{
    private const string INIA_ORG_ID = "IniaAI";
    private const string INIA_ORG_NAME = "Inia AI";
    private const string SUPER_ADMIN_USER_ID = "1234";
    public const string SUPER_ADMIN_EMAIL_KEY = "Seeding:SuperAdminEmail";
    public const string DEFAULT_SUPER_ADMIN_EMAIL = "superadmin@inia.ai";
    private const string CUSTOMER_ADMIN_USER_ID = "6789";
    private const string CUSTOMER_ADMIN_EMAIL = "admin@example.com";
    private const int MAX_ADDRESS_LENGTH = 256;

    private readonly ILogger _logger;
    private readonly UserDataDbContext _context;
    private readonly string _customerOrgId;
    private readonly SeedAccountPasswords _passwords;
    private readonly RoleGrants _roles;
    private readonly BuiltInCounterpartyTypes _counterpartyTypes;
    private readonly StaffSeed _staff;
    private readonly string _superAdminEmail;

    public UserDataDbInitializer(
        ILogger logger,
        UserDataDbContext context,
        string customerOrgId,
        SeedAccountPasswords passwords,
        RoleGrants roles,
        BuiltInCounterpartyTypes counterpartyTypes,
        StaffSeed staff,
        string superAdminEmail = DEFAULT_SUPER_ADMIN_EMAIL)
    {
        _logger = logger;

        if (string.IsNullOrWhiteSpace(customerOrgId))
        {
            _logger.LogError("Customer organization ID is not set");

            throw new InvalidOperationException("Customer organization ID is not set");
        }

        _context = context;
        _customerOrgId = customerOrgId;
        _passwords = passwords;
        _roles = roles;
        _counterpartyTypes = counterpartyTypes;
        _staff = staff;
        _superAdminEmail = superAdminEmail;

        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        foreach (KeyValuePair<string, bool> tableStatus in context.GetTableStatuses())
        {
            _logger.LogDebug(
                "Table {TableStatusKey} has entries: {TableStatusValue}",
                tableStatus.Key,
                tableStatus.Value);
        }
    }

    public void Initialize()
    {
        _logger.LogDebug("Initializing user data database");

        SeedUserManagement();

        _ = _context.SaveChanges();

        _logger.LogInformation("User data database initialized");
    }

    private void SeedUserManagement()
    {
        RefuseUnusableStaff();

        SeedOrganizations();

        SeedRoles();

        SeedUsers();

        RepairUserNamesIdentityRejects();

        RepairLockoutSwitchedOff();

        SeedUserRoles();

        SeedStaff();

        DisableSuperAdminWhenAsked();

        SeedCounterpartyTypes();

        _ = _context.SaveChanges();
    }

    private void SeedUserRoles()
    {
        _logger.LogDebug("Seeding user roles");

        IdentityRole superAdminRole = _context.Roles.Single(r => r.Name == Roles.SUPER_ADMIN);
        IdentityRole adminRole = _context.Roles.Single(r => r.Name == Roles.ADMIN);
        IdentityRole userRole = _context.Roles.Single(r => r.Name == Roles.USER);
        IdentityRole workflowEditor = _context.Roles.Single(r => r.Name == Roles.WORKFLOW_EDITOR);
        List<IdentityRole> customerAdminModuleRoles = [.. _context.Roles.Where(r => _roles.CustomerAdminModuleRoles.Contains(r.Name!))];

        User superAdmin = _context.Users.Single(u => u.Id == SUPER_ADMIN_USER_ID);
        User? admin = _context.Users.SingleOrDefault(u => u.Id == CUSTOMER_ADMIN_USER_ID);

        List<IdentityUserRole<string>> userRoles = [.. _context.UserRoles];
        IdentityRole[] superAdminGrants = [superAdminRole, adminRole, userRole, workflowEditor];
        IdentityRole[] customerAdminGrants = [adminRole, userRole, workflowEditor, .. customerAdminModuleRoles];

        foreach (IdentityRole role in superAdminGrants)
        {
            GrantIfMissing(superAdmin, role, userRoles);
        }

        if (admin is not null)
        {
            foreach (IdentityRole role in customerAdminGrants)
            {
                GrantIfMissing(admin, role, userRoles);
            }
        }

        _ = _context.SaveChanges();

        _logger.LogInformation("User roles seeded");
    }

    private void RefuseUnusableStaff()
    {
        for (int index = 0; index < _staff.Accounts.Count; index++)
        {
            StaffAccount account = _staff.Accounts[index];
            string key = account.Key ?? $"{StaffSeed.STAFF_KEY}:{index}";
            if (!IdentityUserNames.Accepts(account.Email) || account.Email.Count(c => c == '@') != 1 || account.Email.Length > MAX_ADDRESS_LENGTH)
            {
                throw new SeedConfigurationException(
                    $"{key}:Email is not an address the platform can use as a user name: one @, letters, digits and - . _ @ + only, at most {MAX_ADDRESS_LENGTH} characters.");
            }

            if (account.Roles.Count == 0)
            {
                throw new SeedConfigurationException($"{key}:Roles lists no role. Give the entry at least one.");
            }

            string? undefined = account.Roles.FirstOrDefault(role => !_roles.DefinedRoles.Contains(role));
            if (undefined is not null)
            {
                throw new SeedConfigurationException(
                    $"{key} asks for the role {undefined}, which this deployment does not define. Use one of: {string.Join(", ", _roles.DefinedRoles)}.");
            }
        }
    }

    private void SeedStaff()
    {
        if (_staff.Accounts.Count == 0)
        {
            return;
        }

        Organization iniaAiOrg = _context.Organizations.Single(o => o.Name == INIA_ORG_NAME);
        Dictionary<string, IdentityRole> roles = _context.Roles.AsEnumerable().ToDictionary(r => r.Name!, StringComparer.Ordinal);
        List<string> addresses = [.. _staff.Accounts.Select(account => account.Email.ToUpperInvariant()).Distinct(StringComparer.Ordinal)];
        List<User> existing = [.. _context.Users.Where(u => addresses.Contains(u.NormalizedEmail!) || addresses.Contains(u.NormalizedUserName!))];
        List<string> existingIds = [.. existing.Select(u => u.Id)];
        List<IdentityUserRole<string>> granted = [.. _context.UserRoles.Where(ur => existingIds.Contains(ur.UserId))];
        Dictionary<string, User> staffByAddress = new(StringComparer.Ordinal);

        for (int index = 0; index < _staff.Accounts.Count; index++)
        {
            StaffAccount account = _staff.Accounts[index];
            string normalized = account.Email.ToUpperInvariant();
            if (!staffByAddress.TryGetValue(normalized, out User? staff))
            {
                staff = ExistingStaff(existing, normalized, account.Key ?? $"{StaffSeed.STAFF_KEY}:{index}") ?? NewStaff(iniaAiOrg, account.Email);
                staffByAddress[normalized] = staff;
            }

            foreach (string role in account.Roles)
            {
                GrantIfMissing(staff, roles[role], granted);
            }
        }

        _ = _context.SaveChanges();

        _logger.LogInformation("Staff seeded: {Count} accounts", staffByAddress.Count);
    }

    private static User? ExistingStaff(List<User> existing, string normalized, string key)
    {
        List<User> matches = [.. existing.Where(u => u.NormalizedEmail == normalized || u.NormalizedUserName == normalized)];
        if (matches.Count > 1 || matches.Any(u => u.NormalizedEmail != normalized))
        {
            throw new SeedConfigurationException(
                $"{key}:Email belongs to more than one account, or is the user name of an account with another address. Resolve it in the admin app first.");
        }

        return matches.SingleOrDefault();
    }

    private User NewStaff(Organization iniaAiOrg, string email)
    {
        User staff = NewSeededUser(iniaAiOrg, email);
        _ = _context.Users.Add(staff);

        _logger.LogDebug("Adding staff user {UserId}", staff.Id);

        return staff;
    }

    private void DisableSuperAdminWhenAsked()
    {
        if (!_staff.DisablesSuperAdmin)
        {
            return;
        }

        User superAdmin = _context.Users.Single(u => u.Id == SUPER_ADMIN_USER_ID);
        if (superAdmin.IsDisabled)
        {
            return;
        }

        string superAdminRoleId = _context.Roles.Single(r => r.Name == Roles.SUPER_ADMIN).Id;
        List<string> holders = [.. _context.UserRoles
            .Where(ur => ur.RoleId == superAdminRoleId && ur.UserId != SUPER_ADMIN_USER_ID)
            .Select(ur => ur.UserId)];
        if (!_context.Users.Where(u => holders.Contains(u.Id)).AsEnumerable().Any(u => !u.IsDisabled))
        {
            throw new SeedConfigurationException(
                $"{StaffSeed.DISABLE_SUPER_ADMIN_KEY} turns the seeded super admin off, but no other enabled account has the role {Roles.SUPER_ADMIN}, so nobody could grant roles. Give a {StaffSeed.STAFF_KEY} entry that role.");
        }

        superAdmin.Disable();
        superAdmin.SecurityStamp = Guid.NewGuid().ToString();
        _ = _context.SaveChanges();
        int revoked = _context.Tokens
            .Where(token => token.Subject == SUPER_ADMIN_USER_ID && token.Status == Statuses.Valid)
            .ExecuteUpdate(setters => setters.SetProperty(token => token.Status, Statuses.Revoked));

        _logger.LogInformation("Disabled the seeded super admin and revoked {Count} of its tokens, as {Key} asks", revoked, StaffSeed.DISABLE_SUPER_ADMIN_KEY);
    }

    private static User NewSeededUser(Organization organization, string email)
    {
        return new User(organization)
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            UserOrganizations =
            [
                new UserOrganization
                {
                    OrganizationId = organization.Id,
                    IsDefault = true
                }
            ]
        };
    }

    private void GrantIfMissing(User user, IdentityRole role, List<IdentityUserRole<string>> granted)
    {
        if (granted.Any(ur => ur.UserId == user.Id && ur.RoleId == role.Id))
        {
            return;
        }

        _logger.LogDebug("Adding {RoleName} role to user {UserId}", role.Name, user.Id);

        var grant = new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id };
        _ = _context.UserRoles.Add(grant);
        granted.Add(grant);
    }

    /// <summary>
    /// The first admin's address: the setting when a deployment sets one, else Inia's own.
    /// </summary>
    public static string SuperAdminEmailFrom(IConfiguration configuration)
    {
        string? configured = configuration[SUPER_ADMIN_EMAIL_KEY];

        return string.IsNullOrWhiteSpace(configured) ? DEFAULT_SUPER_ADMIN_EMAIL : configured;
    }

    private void SeedUsers()
    {
        _logger.LogDebug("Seeding users");

        if (_customerOrgId is not INIA_ORG_ID && string.Equals(_superAdminEmail, CUSTOMER_ADMIN_EMAIL, StringComparison.OrdinalIgnoreCase))
        {
            throw new SeedConfigurationException(
                $"{SUPER_ADMIN_EMAIL_KEY} is {CUSTOMER_ADMIN_EMAIL}, the address of the customer admin this start creates. Give the first admin another address.");
        }

        if (!_context.Users.Any(u => u.Id == SUPER_ADMIN_USER_ID || u.NormalizedEmail == _superAdminEmail.ToUpperInvariant()))
        {
            _logger.LogDebug("Adding SuperAdmin user");

            Organization iniaAiOrg = _context.Organizations.Single(o => o.Name == INIA_ORG_NAME);
            User superAdmin = NewSeededUser(iniaAiOrg, _superAdminEmail);
            superAdmin.Id = SUPER_ADMIN_USER_ID;
            superAdmin.UserName = Roles.SUPER_ADMIN;

            if (_passwords.PasswordSignInOn)
            {
                superAdmin.SetPassword(RequirePassword(_passwords.SuperAdmin, SeedAccountPasswords.SUPER_ADMIN_PASSWORD_KEY, _superAdminEmail));
            }

            _ = _context.Users.Add(superAdmin);
        }

        if (_customerOrgId is not INIA_ORG_ID &&
            !_context.Users.Any(u => u.Id == CUSTOMER_ADMIN_USER_ID || u.NormalizedEmail == CUSTOMER_ADMIN_EMAIL.ToUpperInvariant()))
        {
            _logger.LogDebug("Adding Admin user");

            Organization org = _context.Organizations.Single(o => o.Id == _customerOrgId);
            User admin = NewSeededUser(org, CUSTOMER_ADMIN_EMAIL);
            admin.Id = CUSTOMER_ADMIN_USER_ID;

            if (_passwords.PasswordSignInOn)
            {
                admin.SetPassword(RequirePassword(_passwords.CustomerAdmin, SeedAccountPasswords.CUSTOMER_ADMIN_PASSWORD_KEY, CUSTOMER_ADMIN_EMAIL));
            }

            _ = _context.Users.Add(admin);
        }

        _ = _context.SaveChanges();

        _logger.LogInformation("Users seeded");
    }

    /// <summary>
    /// Identity refuses every update to an account whose user name breaks its character rule, so such
    /// an account never counts a failed sign-in toward its lockout and cannot set up two-factor. Earlier
    /// builds seeded the customer admin as "&lt;org&gt; Admin", and the CSV import took any user name.
    /// Each such account takes its email as user name, unless another account already holds that name.
    /// </summary>
    private void RepairUserNamesIdentityRejects()
    {
        var rejected = _context.Users
            .AsNoTracking()
            .Where(u => u.Email != null && u.UserName != null)
            .Select(u => new { u.Id, u.UserName, u.Email })
            .AsEnumerable()
            .Where(u => !IdentityUserNames.Accepts(u.UserName) && IdentityUserNames.Accepts(u.Email))
            .ToList();

        int repaired = 0;
        foreach (var account in rejected)
        {
            string normalized = account.Email!.ToUpperInvariant();
            if (_context.Users.Any(u => u.Id != account.Id && u.NormalizedUserName == normalized))
            {
                continue;
            }

            repaired += _context.Users
                .Where(u => u.Id == account.Id)
                .ExecuteUpdate(update => update
                    .SetProperty(u => u.UserName, account.Email)
                    .SetProperty(u => u.NormalizedUserName, normalized));
        }

        if (repaired > 0)
        {
            _logger.LogInformation("Gave {Count} accounts their email as user name, because Identity rejects their old one", repaired);
        }
    }

    /// <summary>
    /// Accounts that earlier builds created outside UserManager (the seeded admins, CSV imports, the
    /// admin app's Create user) started with lockout off, so failed sign-ins never locked them. Every
    /// person's account gets it on.
    /// </summary>
    private void RepairLockoutSwitchedOff()
    {
        int switchedOn = _context.Users
            .Where(u => !u.IsSystem && !u.LockoutEnabled)
            .ExecuteUpdate(update => update.SetProperty(u => u.LockoutEnabled, true));

        if (switchedOn > 0)
        {
            _logger.LogInformation("Switched sign-in lockout on for {Count} accounts that were created without it", switchedOn);
        }
    }

    private static string RequirePassword(string? password, string key, string email)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new SeedConfigurationException(
                $"{key} is required to create {email} on a new database. Set it in configuration, for example the environment variable {key.Replace(":", "__")}.");
        }

        if (!PasswordPolicy.Accepts(password))
        {
            throw new SeedConfigurationException($"{key} must have {PasswordPolicy.Description}.");
        }

        return password;
    }

    private void SeedRoles()
    {
        _logger.LogDebug("Seeding roles");

        var roles = _context.Roles.ToList();

        foreach (string roleName in _roles.DefinedRoles)
        {
            if (roles.Any(r => r.Name == roleName))
            {
                continue;
            }

            _logger.LogDebug("Adding {RoleName} role", roleName);

            IdentityRole role = new(roleName) { NormalizedName = roleName.ToUpperInvariant() };

            _ = _context.Roles.Add(role);
        }

        _ = _context.SaveChanges();

        _logger.LogInformation("Roles seeded");
    }

    private void SeedOrganizations()
    {
        _logger.LogDebug("Seeding organizations");

        if (!_context.Organizations.Any(o => o.Id == INIA_ORG_ID))
        {
            _logger.LogDebug("Adding Inia AI organization");

            _ = _context.Organizations.Add(new Organization(INIA_ORG_NAME, INIA_ORG_ID));
        }

        if (_customerOrgId is not INIA_ORG_ID && !_context.Organizations.Any(o => o.Id == _customerOrgId))
        {
            _logger.LogDebug("Adding customer organization");

            _ = _context.Organizations.Add(new Organization(_customerOrgId, _customerOrgId));
        }

        _ = _context.SaveChanges();

        _logger.LogInformation("Organizations seeded");
    }

    private void SeedCounterpartyTypes()
    {
        var existingNames = _context.CounterpartyTypes.Select(t => t.Name).ToHashSet();

        foreach (BuiltInCounterpartyType builtIn in _counterpartyTypes.All.Where(t => !existingNames.Contains(t.Name)))
        {
            _logger.LogDebug("Adding {CounterpartyTypeName} counterparty type", builtIn.Name);

            _ = _context.Add(new CounterpartyType(builtIn.Name) { Description = builtIn.Description });
        }

        _ = _context.SaveChanges();

        _logger.LogInformation("Counterparty types seeded");
    }
}
