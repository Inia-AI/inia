using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace IniaAi.Services.CsvImport;

public class UserCsvMapper : DefaultCsvEntityMapper<User>
{
    public const string PASSWORD_COLUMN = "Password";

    private static readonly HashSet<string> _recognizedColumns = new(StringComparer.Ordinal)
    {
        nameof(User.Email),
        PASSWORD_COLUMN,
        nameof(User.FirstName),
        nameof(User.LastName),
        nameof(User.MiddleName),
        nameof(User.UserName),
        nameof(User.PhoneNumber),
    };

    public override bool TryCreateEntity(out User entity, object[] parameters)
    {
        entity = default!;
        try
        {
            string organizationId = GetParameterValue<string>(parameters, 0);
            entity = new User(organizationId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public override IEnumerable<ColumnPropertyMap> MapColumns(string[] columns, PropertyInfo[] properties)
    {
        var columnMaps = new List<ColumnPropertyMap>();
        foreach (string column in columns)
        {
            if (!_recognizedColumns.Contains(column))
            {
                continue;
            }

            PropertyInfo? property = column == PASSWORD_COLUMN
                ? properties.FirstOrDefault(p => p.Name == nameof(User.PasswordHash))
                : properties.FirstOrDefault(p => p.Name == column);

            if (property is not null)
            {
                columnMaps.Add(new ColumnPropertyMap(column, property));
            }
        }

        return columnMaps;
    }

    public override void MapValues(User instance, string[] values, List<ColumnPropertyMap> columnMaps)
    {
        for (int i = 0; i < Math.Min(values.Length, columnMaps.Count); i++)
        {
            string val = values[i].Trim();

            if (string.IsNullOrWhiteSpace(val) || columnMaps[i].Name == PASSWORD_COLUMN)
            {
                continue;
            }

            PropertyInfo propertyInfo = columnMaps[i].PropertyInfo;
            if (propertyInfo.PropertyType == typeof(string))
            {
                propertyInfo.SetValue(instance, val);
            }
            else if (propertyInfo.PropertyType == typeof(int))
            {
                propertyInfo.SetValue(instance, int.Parse(val));
            }
            else if (propertyInfo.PropertyType == typeof(DateTime))
            {
                propertyInfo.SetValue(instance, DateTime.Parse(val));
            }
        }

        instance.UserName ??= instance.Email;
        instance.NormalizedUserName = instance.UserName?.ToUpperInvariant();
        instance.NormalizedEmail = instance.Email?.ToUpperInvariant();
        instance.EmailConfirmed = true;
        instance.SecurityStamp ??= Guid.NewGuid().ToString();

        instance.UserOrganizations.Add(new UserOrganization
        {
            UserId = instance.Id,
            OrganizationId = instance.OrganizationId,
            IsDefault = true,
            IsActive = true,
            JoinedAt = DateTime.UtcNow
        });
    }

    public override async Task<List<CsvImportError>> ValidateEntitiesAsync(
        List<(User entity, string[] values, string[] columnNames, int rowNumber)> entities,
        IServiceProvider serviceProvider)
    {
        var errors = new List<CsvImportError>();

        if (entities.Count == 0)
        {
            return errors;
        }

        IUserDataDbContext dbContext = serviceProvider.GetRequiredService<IUserDataDbContext>();

        bool userRoleExists = await dbContext.Set<IdentityRole>().AnyAsync(r => r.Name == Roles.USER);
        if (!userRoleExists)
        {
            errors.Add(new CsvImportError
            {
                RowNumber = 0,
                ErrorType = CsvImportErrorType.InvalidReference,
                ErrorMessage = $"Default role '{Roles.USER}' was not found in the database",
                SuggestedAction = "Seed the default roles before importing users"
            });
        }

        var emailValidator = new EmailAddressAttribute();
        string[] headerColumns = entities[0].columnNames;
        int passwordIndex = Array.IndexOf(headerColumns, PASSWORD_COLUMN);

        foreach ((User entity, string[] values, string[] _, int rowNumber) in entities)
        {
            if (string.IsNullOrWhiteSpace(entity.Email))
            {
                errors.Add(new CsvImportError
                {
                    RowNumber = rowNumber,
                    EntityId = entity.Id,
                    EntityName = entity.GetFullName(),
                    ErrorType = CsvImportErrorType.ValidationError,
                    ErrorMessage = "Email is required",
                    SuggestedAction = "Provide an email address for this row"
                });
            }
            else if (!emailValidator.IsValid(entity.Email))
            {
                errors.Add(new CsvImportError
                {
                    RowNumber = rowNumber,
                    EntityId = entity.Id,
                    EntityName = entity.Email,
                    ErrorType = CsvImportErrorType.ValidationError,
                    ErrorMessage = $"Email '{entity.Email}' is not a valid email address",
                    SuggestedAction = "Correct the email address"
                });
            }

            if (entity.UserName is not null && !IdentityUserNames.Accepts(entity.UserName))
            {
                errors.Add(new CsvImportError
                {
                    RowNumber = rowNumber,
                    EntityId = entity.Id,
                    EntityName = entity.Email ?? entity.UserName,
                    ErrorType = CsvImportErrorType.ValidationError,
                    ErrorMessage = $"User name '{entity.UserName}' may hold only letters, digits and - . _ @ +",
                    SuggestedAction = "Remove the user name to use the email, or remove its spaces and other characters"
                });
            }

            if (passwordIndex >= 0)
            {
                string rawPassword = passwordIndex < values.Length ? values[passwordIndex].Trim() : string.Empty;
                foreach (string issue in PasswordPolicy.Problems(rawPassword))
                {
                    errors.Add(new CsvImportError
                    {
                        RowNumber = rowNumber,
                        EntityId = entity.Id,
                        EntityName = entity.Email ?? entity.GetFullName(),
                        ErrorType = CsvImportErrorType.ValidationError,
                        ErrorMessage = issue,
                        SuggestedAction = "Provide a password satisfying the password rules"
                    });
                }
            }
        }

        IEnumerable<IGrouping<string, (User entity, string[] values, string[] columnNames, int rowNumber)>> duplicateEmailGroups = entities
            .Where(e => !string.IsNullOrWhiteSpace(e.entity.Email))
            .GroupBy(e => e.entity.Email!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (IGrouping<string, (User entity, string[] values, string[] columnNames, int rowNumber)> group in duplicateEmailGroups)
        {
            foreach ((User entity, string[] _, string[] _, int rowNumber) in group)
            {
                errors.Add(new CsvImportError
                {
                    RowNumber = rowNumber,
                    EntityId = entity.Id,
                    EntityName = entity.Email,
                    ErrorType = CsvImportErrorType.DuplicateInCsv,
                    ErrorMessage = $"Email '{entity.Email}' appears multiple times in the CSV file",
                    SuggestedAction = "Ensure each email is unique in the import file"
                });
            }
        }

        IEnumerable<IGrouping<string, (User entity, string[] values, string[] columnNames, int rowNumber)>> duplicateUserNameGroups = entities
            .Where(e => !string.IsNullOrWhiteSpace(e.entity.NormalizedUserName))
            .GroupBy(e => e.entity.NormalizedUserName!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1 && g.Select(e => e.entity.NormalizedEmail).Distinct().Count() > 1);

        foreach (IGrouping<string, (User entity, string[] values, string[] columnNames, int rowNumber)> group in duplicateUserNameGroups)
        {
            foreach ((User entity, string[] _, string[] _, int rowNumber) in group)
            {
                errors.Add(new CsvImportError
                {
                    RowNumber = rowNumber,
                    EntityId = entity.Id,
                    EntityName = entity.UserName,
                    ErrorType = CsvImportErrorType.DuplicateInCsv,
                    ErrorMessage = $"User name '{entity.UserName}' appears multiple times in the CSV file",
                    SuggestedAction = "Ensure each user name is unique in the import file"
                });
            }
        }

        List<string> normalizedEmails = entities
            .Where(e => !string.IsNullOrWhiteSpace(e.entity.NormalizedEmail))
            .Select(e => e.entity.NormalizedEmail!)
            .Distinct()
            .ToList();

        List<string> normalizedUserNames = entities
            .Where(e => !string.IsNullOrWhiteSpace(e.entity.NormalizedUserName))
            .Select(e => e.entity.NormalizedUserName!)
            .Distinct()
            .ToList();

        var existingUsers = await dbContext.Set<User>()
            .IgnoreQueryFilters()
            .Where(u => (u.NormalizedEmail != null && normalizedEmails.Contains(u.NormalizedEmail))
                || (u.NormalizedUserName != null && normalizedUserNames.Contains(u.NormalizedUserName)))
            .Select(u => new { u.NormalizedEmail, u.NormalizedUserName })
            .ToListAsync();

        var existingEmails = existingUsers
            .Where(u => u.NormalizedEmail is not null)
            .Select(u => u.NormalizedEmail!)
            .ToHashSet(StringComparer.Ordinal);

        var existingUserNames = existingUsers
            .Where(u => u.NormalizedUserName is not null)
            .Select(u => u.NormalizedUserName!)
            .ToHashSet(StringComparer.Ordinal);

        foreach ((User entity, string[] _, string[] _, int rowNumber) in entities)
        {
            if (entity.NormalizedEmail is not null && existingEmails.Contains(entity.NormalizedEmail))
            {
                errors.Add(new CsvImportError
                {
                    RowNumber = rowNumber,
                    EntityId = entity.Id,
                    EntityName = entity.Email,
                    ErrorType = CsvImportErrorType.NameConflict,
                    ErrorMessage = $"A user with email '{entity.Email}' already exists",
                    SuggestedAction = "Remove this row — updating existing users via CSV import is not supported"
                });
            }
            else if (entity.NormalizedUserName is not null && existingUserNames.Contains(entity.NormalizedUserName))
            {
                errors.Add(new CsvImportError
                {
                    RowNumber = rowNumber,
                    EntityId = entity.Id,
                    EntityName = entity.UserName,
                    ErrorType = CsvImportErrorType.NameConflict,
                    ErrorMessage = $"A user with user name '{entity.UserName}' already exists",
                    SuggestedAction = "Remove this row — updating existing users via CSV import is not supported"
                });
            }
        }

        // Hashing waits until the batch is accepted so a rejected import never pays PBKDF2 per row.
        if (errors.Count == 0 && passwordIndex >= 0)
        {
            foreach ((User entity, string[] values, string[] _, int _) in entities)
            {
                entity.SetPassword(values[passwordIndex].Trim());
            }
        }

        return errors;
    }

    public override async Task ProcessReferences(
        IEnumerable<(User entity, string[] values, string[] columnNames)> entities,
        IServiceProvider serviceProvider)
    {
        IUserDataDbContext dbContext = serviceProvider.GetRequiredService<IUserDataDbContext>();

        IdentityRole? userRole = await dbContext.Set<IdentityRole>().FirstOrDefaultAsync(r => r.Name == Roles.USER);
        if (userRole is null)
        {
            throw new InvalidOperationException($"Default role '{Roles.USER}' was not found in the database.");
        }

        foreach ((User entity, string[] _, string[] _) in entities)
        {
            _ = dbContext.Set<IdentityUserRole<string>>().Add(new IdentityUserRole<string>
            {
                RoleId = userRole.Id,
                UserId = entity.Id
            });
        }
    }
}
