using IniaAi.Domain.Entities.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;

/// <summary>
/// Configures the schema for the FileMetadata entity.
/// </summary>
public class FileMetadataConfiguration : IEntityTypeConfiguration<FileMetadata>
{
    public void Configure(EntityTypeBuilder<FileMetadata> builder)
    {
        // Configure primary key
        _ = builder.HasKey(fm => fm.Id);

        // Configure Id property
        //builder.Property(fm => fm.Id)
        //    .IsRequired()
        //    .HasMaxLength(36); // GUID without hyphens

        //// Configure OrganizationId
        //builder.Property(fm => fm.OrganizationId)
        //    .IsRequired()
        //    .HasMaxLength(36);

        //// Configure DocumentId
        //builder.Property(fm => fm.DocumentId)
        //    .HasMaxLength(36);

        // Configure Name
        _ = builder.Property(fm => fm.Name)
            .IsRequired()
            .HasMaxLength(450);

        // Configure Extension
        _ = builder.Property(fm => fm.Extension)
            .IsRequired()
            .HasMaxLength(10);

        // Configure SizeBytes
        _ = builder.Property(fm => fm.SizeBytes)
            .IsRequired();

        // Configure StorageProvider enum as int
        _ = builder.Property(fm => fm.StorageProvider)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValue(Domain.Enums.StorageProvider.NotDefined);

        // Configure ExternalStoragePath
        _ = builder.Property(fm => fm.ExternalStoragePath)
            .HasMaxLength(36); // For S3 paths, etc.
    }
}
