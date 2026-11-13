using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Services.CsvImport;
using IniaAi.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceProvider = IniaAi.Testing.DependencyInjection.ServiceProvider;

namespace IniaAi.Services.Tests.CsvImport;

public class UserCsvImportTests : IClassFixture<CsvImportFixture>
{
    private const string VALID_PASSWORD = "QuietMaple42";

    private const string USERS_WITH_PASSWORDS_CSV = @"
        Email,                  FirstName,  LastName,   Password
        jane.doe@example.com,   Jane,       Doe,        QuietMaple42
        john.smith@example.com, John,       Smith,      Secret42x
    ";

    private const string USERS_WITHOUT_PASSWORD_COLUMN_CSV = @"
        Email,                  FirstName,  LastName
        sso.user@example.com,   Single,     SignOn
    ";

    private const string USER_WITH_EMPTY_PASSWORD_CSV = @"
        Email,                  FirstName,  LastName,   Password
        jane.doe@example.com,   Jane,       Doe,
    ";

    private const string USER_WITH_WEAK_PASSWORD_CSV = @"
        Email,                  FirstName,  LastName,   Password
        jane.doe@example.com,   Jane,       Doe,        abc
    ";

    private const string USER_WITH_SEVEN_CHARACTER_PASSWORD_CSV = @"
        Email,                  FirstName,  LastName,   Password
        jane.doe@example.com,   Jane,       Doe,        Abcdef1
    ";

    private const string DUPLICATE_EMAILS_CSV = @"
        Email,                  FirstName,  LastName,   Password
        jane.doe@example.com,   Jane,       Doe,        QuietMaple42
        JANE.DOE@example.com,   Janet,      Doeh,       Password2
    ";

    private const string USER_WITHOUT_EMAIL_CSV = @"
        Email,  FirstName,  LastName,   Password
        ,       Jane,       Doe,        QuietMaple42
    ";

    private const string USER_WITH_EXPLICIT_USERNAME_CSV = @"
        Email,                  UserName,   FirstName,  LastName,   Password
        jane.doe@example.com,   jdoe,       Jane,       Doe,        QuietMaple42
    ";

    private const string USER_NAME_WITH_A_SPACE_CSV = @"
        Email,                  UserName,   FirstName,  LastName,   Password
        jane.doe@example.com,   Jane Doe,   Jane,       Doe,        QuietMaple42
    ";

    private const string DUPLICATE_USERNAMES_CSV = @"
        Email,                  UserName,   FirstName,  LastName,   Password
        jane.doe@example.com,   shared,     Jane,       Doe,        QuietMaple42
        john.smith@example.com, SHARED,     John,       Smith,      Secret42x
    ";

    private const string ROGUE_ORGANIZATION_ID_CSV = @"
        Email,                  FirstName,  LastName,   OrganizationId
        jane.doe@example.com,   Jane,       Doe,        other-org
    ";

    private const string ROGUE_PASSWORD_HASH_CSV = @"
        Email,                  FirstName,  LastName,   PasswordHash
        jane.doe@example.com,   Jane,       Doe,        attacker-controlled-hash
    ";

    private readonly CsvImportService _csvImportService;
    private readonly ServiceProvider _serviceProvider;

    public UserCsvImportTests(CsvImportFixture _, ITestOutputHelper output)
    {
        _serviceProvider = new ServiceProvider();
        _csvImportService = new CsvImportService(_serviceProvider, TestsLogger.CreateLogger<CsvImportService>(output));
    }

    [Fact]
    public async Task ImportAsync_UsersWithPasswords_CreatesLoginReadyUsersAsync()
    {
        await SeedUserRoleAsync();

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            USERS_WITH_PASSWORDS_CSV, _serviceProvider.TestOrganization.Id);

        Assert.False(result.HasErrors, DescribeErrors(result));
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.UpdatedCount);

        User jane = await GetRequiredUserAsync("jane.doe@example.com");
        Assert.Equal("jane.doe@example.com", jane.UserName);
        Assert.Equal("JANE.DOE@EXAMPLE.COM", jane.NormalizedUserName);
        Assert.Equal("JANE.DOE@EXAMPLE.COM", jane.NormalizedEmail);
        Assert.True(jane.EmailConfirmed);
        Assert.False(string.IsNullOrEmpty(jane.SecurityStamp));
        Assert.Equal("Jane", jane.FirstName);
        Assert.Equal("Doe", jane.LastName);
        Assert.Equal(_serviceProvider.TestOrganization.Id, jane.OrganizationId);

        Assert.NotNull(jane.PasswordHash);
        Assert.NotEqual(VALID_PASSWORD, jane.PasswordHash);
        PasswordVerificationResult verification = new PasswordHasher<User>()
            .VerifyHashedPassword(jane, jane.PasswordHash!, VALID_PASSWORD);
        Assert.Equal(PasswordVerificationResult.Success, verification);

        await AssertUserProvisionedAsync(jane);

        User john = await GetRequiredUserAsync("john.smith@example.com");
        PasswordVerificationResult johnVerification = new PasswordHasher<User>()
            .VerifyHashedPassword(john, john.PasswordHash!, "Secret42x");
        Assert.Equal(PasswordVerificationResult.Success, johnVerification);
        await AssertUserProvisionedAsync(john);
    }

    [Fact]
    public async Task ImportAsync_UsersWithoutPasswordColumn_ImportsProvisionedUsersWithoutPasswordAsync()
    {
        await SeedUserRoleAsync();

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            USERS_WITHOUT_PASSWORD_COLUMN_CSV, _serviceProvider.TestOrganization.Id);

        Assert.False(result.HasErrors, DescribeErrors(result));
        Assert.Equal(1, result.ImportedCount);

        User user = await GetRequiredUserAsync("sso.user@example.com");
        Assert.Null(user.PasswordHash);
        Assert.Equal("sso.user@example.com", user.UserName);
        Assert.True(user.EmailConfirmed);
        await AssertUserProvisionedAsync(user);
    }

    [Fact]
    public async Task ImportAsync_UserRowWithEmptyPassword_ReturnsValidationErrorAsync()
    {
        await SeedUserRoleAsync();

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            USER_WITH_EMPTY_PASSWORD_CSV, _serviceProvider.TestOrganization.Id);

        Assert.True(result.HasErrors);
        Assert.Equal(0, result.ImportedCount);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvImportErrorType.ValidationError && e.RowNumber == 2);
        await AssertUserNotPersistedAsync("jane.doe@example.com");
    }

    [Fact]
    public async Task ImportAsync_UserRowWithWeakPassword_ReturnsValidationErrorAsync()
    {
        await SeedUserRoleAsync();

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            USER_WITH_WEAK_PASSWORD_CSV, _serviceProvider.TestOrganization.Id);

        Assert.True(result.HasErrors);
        Assert.Equal(0, result.ImportedCount);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvImportErrorType.ValidationError && e.RowNumber == 2);
        await AssertUserNotPersistedAsync("jane.doe@example.com");
    }

    [Fact]
    public async Task ImportAsync_UserRowWithASevenCharacterPassword_IsRefusedWithTheSharedRuleAsync()
    {
        await SeedUserRoleAsync();

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            USER_WITH_SEVEN_CHARACTER_PASSWORD_CSV, _serviceProvider.TestOrganization.Id);

        CsvImportError error = Assert.Single(result.Errors);
        Assert.Equal("Password must be at least 8 characters", error.ErrorMessage);
        Assert.Equal(PasswordPolicy.Problems("Abcdef1"), [error.ErrorMessage]);
        await AssertUserNotPersistedAsync("jane.doe@example.com");
    }

    [Fact]
    public async Task ImportAsync_DuplicateEmailsInCsv_ReturnsDuplicateErrorsAsync()
    {
        await SeedUserRoleAsync();

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            DUPLICATE_EMAILS_CSV, _serviceProvider.TestOrganization.Id);

        Assert.True(result.HasErrors);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(2, result.Errors.Count(e => e.ErrorType == CsvImportErrorType.DuplicateInCsv));
        await AssertUserNotPersistedAsync("jane.doe@example.com");
    }

    [Fact]
    public async Task ImportAsync_UserEmailAlreadyInDatabase_ReturnsConflictErrorAsync()
    {
        await SeedUserRoleAsync();
        await SeedExistingUserAsync("jane.doe@example.com");

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            USERS_WITH_PASSWORDS_CSV, _serviceProvider.TestOrganization.Id);

        Assert.True(result.HasErrors);
        Assert.Equal(0, result.ImportedCount);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvImportErrorType.NameConflict && e.RowNumber == 2);
        await AssertUserNotPersistedAsync("john.smith@example.com");
    }

    [Fact]
    public async Task ImportAsync_UserRowWithoutEmail_ReturnsValidationErrorAsync()
    {
        await SeedUserRoleAsync();

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            USER_WITHOUT_EMAIL_CSV, _serviceProvider.TestOrganization.Id);

        Assert.True(result.HasErrors);
        Assert.Equal(0, result.ImportedCount);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvImportErrorType.ValidationError && e.RowNumber == 2);
    }

    // ASP.NET Core Identity refuses every update to an account whose user name breaks its character
    // rule, so such an account would never count a failed sign-in toward its lockout.
    [Fact]
    public async Task ImportAsync_UserNameIdentityRejects_ReturnsValidationErrorAsync()
    {
        await SeedUserRoleAsync();

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            USER_NAME_WITH_A_SPACE_CSV, _serviceProvider.TestOrganization.Id);

        Assert.Equal(0, result.ImportedCount);
        CsvImportError error = Assert.Single(result.Errors);
        Assert.Equal(CsvImportErrorType.ValidationError, error.ErrorType);
        Assert.Equal(2, error.RowNumber);
    }

    [Fact]
    public async Task ImportAsync_UserRoleNotSeeded_ReturnsValidationErrorAsync()
    {
        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            USERS_WITH_PASSWORDS_CSV, _serviceProvider.TestOrganization.Id);

        Assert.True(result.HasErrors);
        Assert.Equal(0, result.ImportedCount);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvImportErrorType.InvalidReference);
        await AssertUserNotPersistedAsync("jane.doe@example.com");
    }

    [Fact]
    public async Task ImportAsync_DuplicateUserNamesInCsv_ReturnsDuplicateErrorsAsync()
    {
        await SeedUserRoleAsync();

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            DUPLICATE_USERNAMES_CSV, _serviceProvider.TestOrganization.Id);

        Assert.True(result.HasErrors);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(2, result.Errors.Count(e => e.ErrorType == CsvImportErrorType.DuplicateInCsv));
        await AssertUserNotPersistedAsync("jane.doe@example.com");
        await AssertUserNotPersistedAsync("john.smith@example.com");
    }

    [Fact]
    public async Task ImportAsync_RogueOrganizationIdColumn_IsRejectedAsync()
    {
        await SeedUserRoleAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => _csvImportService.ImportAsync<User>(
            ROGUE_ORGANIZATION_ID_CSV, _serviceProvider.TestOrganization.Id));

        await AssertUserNotPersistedAsync("jane.doe@example.com");
    }

    [Fact]
    public async Task ImportAsync_RoguePasswordHashColumn_IsRejectedAsync()
    {
        await SeedUserRoleAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => _csvImportService.ImportAsync<User>(
            ROGUE_PASSWORD_HASH_CSV, _serviceProvider.TestOrganization.Id));

        await AssertUserNotPersistedAsync("jane.doe@example.com");
    }

    [Fact]
    public async Task ImportAsync_UserWithExplicitUserName_KeepsProvidedUserNameAsync()
    {
        await SeedUserRoleAsync();

        CsvImportResult result = await _csvImportService.ImportAsync<User>(
            USER_WITH_EXPLICIT_USERNAME_CSV, _serviceProvider.TestOrganization.Id);

        Assert.False(result.HasErrors, DescribeErrors(result));

        User user = await GetRequiredUserAsync("jane.doe@example.com");
        Assert.Equal("jdoe", user.UserName);
        Assert.Equal("JDOE", user.NormalizedUserName);
        Assert.Equal("JANE.DOE@EXAMPLE.COM", user.NormalizedEmail);
    }

    #region Private

    private static string DescribeErrors(CsvImportResult result)
    {
        return string.Join("; ", result.Errors.Select(e => $"Row {e.RowNumber}: [{e.ErrorType}] {e.ErrorMessage}"));
    }

    private async Task SeedUserRoleAsync()
    {
        IUserDataUnitOfWork userDataUnitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        await userDataUnitOfWork.Repository<IdentityRole>().AddAsync(new IdentityRole(Roles.USER));
        _ = await userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedExistingUserAsync(string email)
    {
        IUserDataUnitOfWork userDataUnitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        User existing = new(_serviceProvider.TestOrganization.Id)
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true
        };
        await userDataUnitOfWork.Users.AddAsync(existing);
        _ = await userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
    }

    private async Task<User> GetRequiredUserAsync(string email)
    {
        IUserDataDbContext dbContext = _serviceProvider.GetRequiredService<IUserDataDbContext>();
        User? user = await dbContext.Set<User>()
            .FirstOrDefaultAsync(u => u.Email == email, TestContext.Current.CancellationToken);
        Assert.NotNull(user);
        return user;
    }

    private async Task AssertUserNotPersistedAsync(string email)
    {
        IUserDataDbContext dbContext = _serviceProvider.GetRequiredService<IUserDataDbContext>();
        bool exists = await dbContext.Set<User>()
            .AnyAsync(u => u.Email == email, TestContext.Current.CancellationToken);
        Assert.False(exists);
    }

    private async Task AssertUserProvisionedAsync(User user)
    {
        IUserDataDbContext dbContext = _serviceProvider.GetRequiredService<IUserDataDbContext>();

        UserOrganization? membership = await dbContext.Set<UserOrganization>()
            .FirstOrDefaultAsync(o => o.UserId == user.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(membership);
        Assert.Equal(_serviceProvider.TestOrganization.Id, membership.OrganizationId);
        Assert.True(membership.IsDefault);
        Assert.True(membership.IsActive);

        IdentityUserRole<string>? roleLink = await dbContext.Set<IdentityUserRole<string>>()
            .FirstOrDefaultAsync(l => l.UserId == user.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(roleLink);

        IdentityRole? role = await dbContext.Set<IdentityRole>()
            .FirstOrDefaultAsync(r => r.Id == roleLink.RoleId, TestContext.Current.CancellationToken);
        Assert.NotNull(role);
        Assert.Equal(Roles.USER, role.Name);
    }

    #endregion
}
