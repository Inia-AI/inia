using IniaAi.Domain.Entities.UserManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

/// <summary>
/// Configures the schema for the UserOrganization entity for many-to-many relationship.
/// </summary>
public class UserOrganizationConfiguration : IEntityTypeConfiguration<UserOrganization>
{
    private readonly DbContext? _context;

    public UserOrganizationConfiguration()
    {
    }

    public UserOrganizationConfiguration(DbContext context)
    {
        _context = context;
    }

    public void Configure(EntityTypeBuilder<UserOrganization> builder)
    {
        // Configure composite primary key
        _ = builder.HasKey(uo => new { uo.UserId, uo.OrganizationId });

        // Configure relationship with User
        _ = builder.HasOne(uo => uo.User)
            .WithMany(u => u.UserOrganizations)
            .HasForeignKey(uo => uo.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure relationship with Organization
        _ = builder.HasOne(uo => uo.Organization)
            .WithMany(o => o.UserOrganizations)
            .HasForeignKey(uo => uo.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure indexes
        _ = builder.HasIndex(uo => uo.UserId);
        _ = builder.HasIndex(uo => uo.OrganizationId);
        _ = builder.HasIndex(uo => new { uo.UserId, uo.IsActive });
        _ = builder.HasIndex(uo => new { uo.OrganizationId, uo.IsActive });

        // Configure default values
        _ = builder.Property(uo => uo.IsActive)
            .HasDefaultValue(true);

        _ = builder.Property(uo => uo.IsDefault)
            .HasDefaultValue(false);

        // Configure JoinedAt default value based on database provider
        string? providerName = _context?.Database.ProviderName?.ToLower();
        _ = builder.Property(uo => uo.JoinedAt)
            .HasDefaultValueSql(providerName?.Contains("npgsql") == true ? "NOW()" : "GETUTCDATE()");
    }
}