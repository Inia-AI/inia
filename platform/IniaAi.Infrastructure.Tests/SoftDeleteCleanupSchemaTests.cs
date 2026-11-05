using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace IniaAi.Infrastructure.Tests;

/// <summary>
/// Detects schema drift by ensuring that every foreign key relationship pointing to
/// a soft-deletable entity is handled by the SoftDeleteCleanupService.
/// If someone adds a new FK to a soft-deletable entity without updating the cleanup
/// service, this test will fail.
/// </summary>
public partial class SoftDeleteCleanupSchemaTests
{
    private const string TENANT_ROOT_ENTITY = "Organization";
    private const string MONITORING_MARKER = "Monitoring";

    [Fact]
    public void AllForeignKeysToSoftDeletableEntities_AreHandledByCleanupService()
    {
        using var context = new UserDataDbContext(SqliteOptions());
        IModel model = context.Model;

        HashSet<string> softDeletableTypes = [.. model.GetEntityTypes()
            .Where(et => typeof(IAuditableEntity).IsAssignableFrom(et.ClrType)
                && et.ClrType.Name != TENANT_ROOT_ENTITY
                && !et.ClrType.Name.Contains(MONITORING_MARKER))
            .Select(et => et.ClrType.Name)];

        // Find all FK relationships where the principal (parent) is soft-deletable
        var unhandledRelationships = new List<string>();

        foreach (IEntityType entityType in model.GetEntityTypes())
        {
            foreach (IForeignKey fk in entityType.GetForeignKeys())
            {
                string principalName = fk.PrincipalEntityType.ClrType.Name;

                // Only care about FKs pointing to soft-deletable entities
                if (!softDeletableTypes.Contains(principalName))
                {
                    continue;
                }

                string dependentName = entityType.ClrType.Name;

                // Monitoring entities are excluded from cleanup scope
                if (dependentName.Contains(MONITORING_MARKER))
                {
                    continue;
                }

                string fkProperties = string.Join(", ", fk.Properties.Select(p => p.Name));
                string relationship = $"{dependentName}.{fkProperties} -> {principalName}";

                // Check if this relationship is handled by the cleanup service.
                // Match on exact entity names with word-boundary awareness:
                // - "DependentName.FkProp -> PrincipalName" (explicit FK handling)
                // - "DependentName (self-deleted)" (entity is cleaned up as self-deleted)
                // - "PrincipalName (self-deleted)" when dependent is also soft-deletable
                bool isHandled = SoftDeleteCleanupService.HandledRelationships
                    .Any(hr =>
                    {
                        // Exact FK property match: "Dependent.FkProp -> Principal"
                        if (hr.StartsWith($"{dependentName}.") && hr.Contains($"-> {principalName}"))
                        {
                            return true;
                        }

                        // Dependent handled as self-deleted or by-parent pattern
                        if (hr.StartsWith($"{dependentName} ") || hr.StartsWith($"{dependentName}."))
                        {
                            if (hr.Contains(principalName))
                            {
                                return true;
                            }
                        }

                        return false;
                    });

                // Self-referential FKs on soft-deletable entities are handled by
                // deleting the entity itself (children first pattern or natural ordering)
                bool isSelfReferential = dependentName == principalName;

                if (isHandled || isSelfReferential)
                {
                    continue;
                }

                // Check if this is a case where the dependent is deleted as part of
                // its own self-deleted cleanup (e.g., TokenUsageComponent -> TokenUsage
                // is handled by cascade delete)
                DeleteBehavior deleteBehavior = fk.DeleteBehavior;
                if (deleteBehavior is DeleteBehavior.Cascade)
                {
                    // Cascade delete means the DB handles this automatically
                    continue;
                }

                unhandledRelationships.Add(relationship);
            }
        }

        // Assert
        if (unhandledRelationships.Count <= 0)
        {
            return;
        }

        string message = "The following FK relationships to soft-deletable entities are not handled " +
            "by SoftDeleteCleanupService. Update the cleanup service and its HandledRelationships set:\n" +
            string.Join("\n", unhandledRelationships.Select(r => $"  - {r}"));
        Assert.Fail(message);
    }

    [Fact]
    public void EveryForeignKeyFreeEntityCarryingAPurgeKey_DeclaresItsRetentionDisposition()
    {
        using var context = new UserDataDbContext(SqliteOptions());

        string? drift = DescribeDispositionDrift(context.Model, DeclaredDispositions(context));

        Assert.True(drift is null, drift);
    }

    [Fact]
    public void AnUndeclaredForeignKeyFreeEntityWithADocumentId_IsReportedByName()
    {
        using var context = new ContextWithAnUndeclaredLooseReference(SqliteOptions());

        string? drift = DescribeDispositionDrift(context.Model, DeclaredDispositions(context));

        Assert.NotNull(drift);
        Assert.Contains(nameof(UndeclaredDocumentNote), drift);
    }

    [Fact]
    public void EveryNulledColumn_IsANullableColumnOfItsEntity()
    {
        using var context = new UserDataDbContext(SqliteOptions());

        List<string> wrong = [.. DeclaredDispositions(context)
            .SelectMany(d => d.NulledColumns
                .Where(column => context.Model.FindEntityType(d.Entity)?.FindProperty(column) is not { IsNullable: true })
                .Select(column => $"{d.Entity.Name}.{column}"))];

        Assert.True(wrong.Count == 0, "Declared nulled columns that are not nullable columns of their entity: " + string.Join(", ", wrong));
    }

    [Fact]
    public void EveryHandledRelationship_StartsWithAnEntityOfTheModel()
    {
        using var context = new UserDataDbContext(SqliteOptions());
        HashSet<string> entityNames = [.. context.Model.GetEntityTypes()
            .SelectMany(et => new[] { et.ClrType.Name, et.ShortName() })];

        List<string> stale = [.. SoftDeleteCleanupService.HandledRelationships
            .Where(hr => !entityNames.Contains(LeadingEntity().Match(hr).Value))
            .Order(StringComparer.Ordinal)];

        Assert.True(
            stale.Count == 0,
            "These handled relationships name an entity the model no longer has; remove them:\n  "
            + string.Join("\n  ", stale));
    }

    [Fact]
    public void HandledRelationships_IsNotEmpty()
    {
        // Sanity check — make sure we're actually testing something
        Assert.NotEmpty(SoftDeleteCleanupService.HandledRelationships);
        Assert.True(SoftDeleteCleanupService.HandledRelationships.Count > 20,
            "Expected at least 20 handled relationships in the cleanup service");
    }

    private static DbContextOptions<UserDataDbContext> SqliteOptions()
    {
        return new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
    }

    /// <summary>
    /// The entities a purge deletes, read from the queryables <see cref="PurgeSelection"/> hands the
    /// ordered delete. Counterparty types are left out: the sweep drops a type only once no
    /// counterparty uses it, and a "Type" id elsewhere names a workflow or usage type, not this one.
    /// </summary>
    private static List<string> PurgedEntityNames()
    {
        return [.. typeof(PurgeSelection).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(IQueryable<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .Where(type => type != typeof(CounterpartyType))
            .Select(type => type.Name)
            .Distinct()];
    }

    /// <summary>
    /// The declarations the host runs with: the cleanup service's own, and those of the kept
    /// tables' purge steps.
    /// </summary>
    private static List<LooseReferenceDisposition> DeclaredDispositions(UserDataDbContext context)
    {
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton(context)
            .AddKeptTablePurgeSteps()
            .BuildServiceProvider();
        using IServiceScope scope = services.CreateScope();

        return [
            .. SoftDeleteCleanupService.LooseReferenceDispositions,
            .. scope.ServiceProvider.GetServices<IDocumentPurgeStep>().SelectMany(step => step.Dispositions),
        ];
    }

    /// <summary>
    /// Null when the declared loose-id columns are exactly the string columns, on foreign-key-free
    /// entities, whose names end in the id of a purged entity; otherwise the message that names each
    /// side's surplus as Entity.Column.
    /// </summary>
    private static string? DescribeDispositionDrift(IModel model, IEnumerable<LooseReferenceDisposition> declared)
    {
        List<string> purgedEntityNames = PurgedEntityNames();

        HashSet<string> derived = [.. model.GetEntityTypes()
            .Where(et => !et.IsOwned() && !et.GetForeignKeys().Any())
            .SelectMany(et => et.GetProperties()
                .Where(p => p.ClrType == typeof(string) && NamesAPurgedEntity(p.Name, purgedEntityNames))
                .Select(p => $"{et.ClrType.Name}.{p.Name}"))];

        HashSet<string> declaredColumns = [.. declared.SelectMany(d => d.LooseIdColumns.Select(column => $"{d.Entity.Name}.{column}"))];

        List<string> undeclared = [.. derived.Except(declaredColumns).Order()];
        List<string> notInModel = [.. declaredColumns.Except(derived).Order()];

        if (undeclared.Count == 0 && notInModel.Count == 0)
        {
            return null;
        }

        return "Foreign-key-free entities that carry the id of a purged entity must declare what the soft-delete sweep does with them, "
            + "in SoftDeleteCleanupService.LooseReferenceDispositions or on an IDocumentPurgeStep that AddKeptTablePurgeSteps registers.\n"
            + (undeclared.Count > 0 ? "  Undeclared: " + string.Join(", ", undeclared) + "\n" : string.Empty)
            + (notInModel.Count > 0 ? "  Declared but not a purge-key column of a foreign-key-free entity: " + string.Join(", ", notInModel) + "\n" : string.Empty);
    }

    private static bool NamesAPurgedEntity(string propertyName, IEnumerable<string> purgedEntityNames)
    {
        const string ID_SUFFIX = "Id";
        if (!propertyName.EndsWith(ID_SUFFIX, StringComparison.Ordinal) || propertyName.Length == ID_SUFFIX.Length)
        {
            return false;
        }

        string stem = propertyName[..^ID_SUFFIX.Length];

        return purgedEntityNames.Any(entity => stem.EndsWith(entity, StringComparison.Ordinal)
            || entity.EndsWith(stem, StringComparison.Ordinal)
            || stem.EndsWith(HeadNoun(entity), StringComparison.Ordinal));
    }

    private static string HeadNoun(string pascalCase)
    {
        int secondWord = 1;
        while (secondWord < pascalCase.Length && !char.IsUpper(pascalCase[secondWord]))
        {
            secondWord++;
        }

        return secondWord < pascalCase.Length ? pascalCase[secondWord..] : pascalCase;
    }

    public sealed class UndeclaredDocumentNote
    {
        public string Id { get; set; } = string.Empty;

        public string DocumentId { get; set; } = string.Empty;
    }

    private sealed class ContextWithAnUndeclaredLooseReference(DbContextOptions<UserDataDbContext> options) : UserDataDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            _ = modelBuilder.Entity<UndeclaredDocumentNote>();
        }
    }

    [GeneratedRegex(@"^\w+")]
    private static partial Regex LeadingEntity();
}
