using IniaAi.Domain.Entities.UserManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

/// <summary>
/// Configures the schema for the User entity with name fields and constraints.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // Configure FirstName property
        _ = builder.Property(u => u.FirstName)
            .HasMaxLength(100)
            .IsRequired(false);

        // Configure LastName property  
        _ = builder.Property(u => u.LastName)
            .HasMaxLength(100)
            .IsRequired(false);

        // Configure MiddleName property (optional)
        _ = builder.Property(u => u.MiddleName)
            .HasMaxLength(100)
            .IsRequired(false);
    }
}