using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// Pins the shared-component model configuration across providers and contexts. The bindings
/// column is especially fragile: UserData's model-level Ignore of Dictionary&lt;string,string&gt;
/// makes a missed per-property mapping silently unmapped (executed snapshots would drop bindings
/// with no error), and a missed Core mapping diverges per provider. A failure here means one of
/// the four mapping sites was lost.
/// </summary>
public class SharedComponentMappingTests
{
    private const string SQL_SERVER = "sqlserver";
    private const string NPGSQL = "npgsql";

    [Fact]
    public void ParentVersion_ForeignKey_IsOptionalAndNoActionOnDelete()
    {
        using CoreDbContext context = CoreContext(SQL_SERVER);

        IForeignKey foreignKey = context.Model
            .FindEntityType(typeof(SharedComponentVersion))!
            .GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(SharedComponentVersion));

        Assert.False(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Chain_HasUniqueSequenceIndex_AndNonUniqueContentHashIndex()
    {
        using CoreDbContext context = CoreContext(SQL_SERVER);

        IEntityType versionType = context.Model.FindEntityType(typeof(SharedComponentVersion))!;
        List<IIndex> indexes = [.. versionType.GetIndexes()];

        IIndex chainIndex = indexes.Single(index =>
            string.Join(",", index.Properties.Select(p => p.Name)) ==
            $"{nameof(SharedComponentVersion.SharedComponentId)},{nameof(SharedComponentVersion.SequenceNumber)}");
        Assert.True(chainIndex.IsUnique);

        IIndex hashIndex = indexes.Single(index =>
            index.Properties.Count == 1
            && index.Properties[0].Name == nameof(SharedComponentVersion.ContentHash));
        Assert.False(hashIndex.IsUnique);
    }

    [Fact]
    public void Payload_IsUnboundedNullable_WithAbsenceMarker_AndUnmappedAccessor()
    {
        using CoreDbContext context = CoreContext(SQL_SERVER);

        IEntityType versionType = context.Model.FindEntityType(typeof(SharedComponentVersion))!;

        IProperty? compressed = versionType.FindProperty(nameof(SharedComponentVersion.ContentJsonCompressed));
        Assert.NotNull(compressed);
        Assert.Null(compressed.GetMaxLength());
        Assert.True(compressed.IsNullable);

        IProperty? absentAt = versionType.FindProperty(nameof(SharedComponentVersion.PayloadAbsentAt));
        Assert.NotNull(absentAt);
        Assert.True(absentAt.IsNullable);

        Assert.Null(versionType.FindProperty(nameof(SharedComponentVersion.ContentJson)));
    }

    [Theory]
    [InlineData(SQL_SERVER, "nvarchar(max)")]
    [InlineData(NPGSQL, "jsonb")]
    public void Bindings_MappedOnBlockAndSection_InCore_WithProviderStoreType(string provider, string storeType)
    {
        using CoreDbContext context = CoreContext(provider);

        AssertBindingsMapped(context.Model, storeType);
    }

    [Theory]
    [InlineData(SQL_SERVER, "nvarchar(max)")]
    [InlineData(NPGSQL, "jsonb")]
    public void Bindings_MappedOnBlockAndSection_InUserData_WithProviderStoreType(string provider, string storeType)
    {
        using UserDataDbContext context = UserDataContext(provider);

        AssertBindingsMapped(context.Model, storeType);
    }

    [Fact]
    public void SharedComponentIdIndexes_ExistInCoreOnly()
    {
        using CoreDbContext core = CoreContext(SQL_SERVER);
        using UserDataDbContext userData = UserDataContext(SQL_SERVER);

        foreach (Type entityClrType in new[] { typeof(Block), typeof(Section) })
        {
            Assert.Contains(
                core.Model.FindEntityType(entityClrType)!.GetIndexes(),
                index => index.Properties.Count == 1
                    && index.Properties[0].Name == nameof(Block.SharedComponentId));
            Assert.DoesNotContain(
                userData.Model.FindEntityType(entityClrType)!.GetIndexes(),
                index => index.Properties.Any(p => p.Name == nameof(Block.SharedComponentId)));
        }
    }

    [Fact]
    public void RegistryEntities_AreAbsentFromTheUserDataModel()
    {
        using UserDataDbContext userData = UserDataContext(SQL_SERVER);

        Assert.Null(userData.Model.FindEntityType(typeof(SharedComponent)));
        Assert.Null(userData.Model.FindEntityType(typeof(SharedComponentVersion)));
        Assert.Null(userData.Model.FindEntityType(typeof(SharedComponentCategory)));
    }

    [Fact]
    public void CategoryEntity_MappedInCoreOnly_WithExpectedColumns()
    {
        using CoreDbContext context = CoreContext(SQL_SERVER);

        IEntityType categoryType = context.Model.FindEntityType(typeof(SharedComponentCategory))!;
        Assert.NotNull(categoryType);
        Assert.Equal(
            nameof(SharedComponentCategory.Id),
            categoryType.FindPrimaryKey()!.Properties.Single().Name);

        IProperty name = categoryType.FindProperty(nameof(SharedComponentCategory.Name))!;
        Assert.False(name.IsNullable);
        Assert.Equal(250, name.GetMaxLength());

        IProperty description = categoryType.FindProperty(nameof(SharedComponentCategory.Description))!;
        Assert.True(description.IsNullable);
        Assert.Equal(1000, description.GetMaxLength());

        Assert.True(categoryType.FindProperty(nameof(SharedComponentCategory.DisplayOrder))!.IsNullable);
        Assert.Empty(categoryType.GetForeignKeys());
    }

    [Fact]
    public void Component_CategoryId_IsNullableUnindexedString_WithoutForeignKey()
    {
        using CoreDbContext context = CoreContext(SQL_SERVER);

        IEntityType componentType = context.Model.FindEntityType(typeof(SharedComponent))!;
        IProperty categoryId = componentType.FindProperty(nameof(SharedComponent.CategoryId))!;

        Assert.True(categoryId.IsNullable);
        Assert.Equal(450, categoryId.GetMaxLength());
        Assert.Empty(componentType.GetForeignKeys());
        Assert.DoesNotContain(
            componentType.GetIndexes(),
            index => index.Properties.Any(p => p.Name == nameof(SharedComponent.CategoryId)));
    }

    [Fact]
    public void BindingsComparer_TreatsNullAndEmptyAsEqual()
    {
        using CoreDbContext context = CoreContext(SQL_SERVER);
        ValueComparer comparer = context.Model
            .FindEntityType(typeof(Block))!
            .FindProperty(nameof(Block.SharedComponentBindings))!
            .GetValueComparer();

        Assert.True(comparer.Equals(null, new Dictionary<string, string>()));
        Assert.True(comparer.Equals(new Dictionary<string, string>(), null));
        Assert.Equal(comparer.GetHashCode(new Dictionary<string, string>()), comparer.GetHashCode(null!));
        Assert.False(comparer.Equals(new Dictionary<string, string>(), new Dictionary<string, string> { ["slot"] = "block1" }));
        Assert.True(comparer.Equals(
            new Dictionary<string, string> { ["slot"] = "block1" },
            new Dictionary<string, string> { ["slot"] = "block1" }));
    }

    [Fact]
    public void BindingsConverter_RoundTripsMixedCaseKeys_AndStoresNullForEmptyMaps()
    {
        using CoreDbContext context = CoreContext(SQL_SERVER);
        ValueConverter converter = context.Model
            .FindEntityType(typeof(Block))!
            .FindProperty(nameof(Block.SharedComponentBindings))!
            .GetValueConverter()!;

        var bindings = new Dictionary<string, string> { ["PeerCompany"] = "peer_company_1", ["slot"] = "block1" };
        var stored = (string?)converter.ConvertToProvider(bindings);
        var roundTripped = (Dictionary<string, string>?)converter.ConvertFromProvider(stored);

        Assert.NotNull(stored);
        Assert.Contains("PeerCompany", stored);
        Assert.NotNull(roundTripped);
        Assert.Equal(bindings, roundTripped);

        Assert.Null(converter.ConvertToProvider(new Dictionary<string, string>()));
        Assert.Null(converter.ConvertFromProvider(string.Empty));
    }

    private static void AssertBindingsMapped(IModel model, string storeType)
    {
        foreach (Type entityClrType in new[] { typeof(Block), typeof(Section) })
        {
            IProperty? bindings = model
                .FindEntityType(entityClrType)!
                .FindProperty(nameof(Block.SharedComponentBindings));

            Assert.NotNull(bindings);
            Assert.Equal(storeType, bindings.GetColumnType());
        }
    }

    private static CoreDbContext CoreContext(string provider)
    {
        return new CoreDbContext(BuildOptions<CoreDbContext>(provider));
    }

    private static UserDataDbContext UserDataContext(string provider)
    {
        return new UserDataDbContext(BuildOptions<UserDataDbContext>(provider));
    }

    private static DbContextOptions<TContext> BuildOptions<TContext>(string provider)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        _ = provider == NPGSQL
            ? builder.UseNpgsql("Host=unused")
            : builder.UseSqlServer("Server=unused");

        return builder.Options;
    }
}
