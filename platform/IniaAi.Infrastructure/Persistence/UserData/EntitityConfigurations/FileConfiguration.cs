using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

/// <summary>
/// Configures the schema for the File entity - now just blob storage (Id + Bytes).
/// </summary>
public class FileConfiguration : IEntityTypeConfiguration<File>
{
    public void Configure(EntityTypeBuilder<File> builder)
    {
        // Configure primary key
        _ = builder.HasKey(f => f.Id);
    }
}
