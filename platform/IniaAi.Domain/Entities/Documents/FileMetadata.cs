using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Common.Text;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// Represents file metadata entity - contains all file information except the binary data.
/// This entity is used for fast metadata queries while the File entity stores only blob data.
/// </summary>
public class FileMetadata : Entity<FileMetadata>
{
    public string OrganizationId { get; set; } = string.Empty;
    public string? DocumentId { get; set; }

    [ForeignKey("OrganizationId")]
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public Organization? Organization { get; set; }
    public Document? Document { get; set; }

    [Required]
    [StringLength(450)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string Extension { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>
    /// Storage provider used for this file
    /// </summary>
    public StorageProvider StorageProvider { get; set; } = StorageProvider.Database;

    /// <summary>
    /// Path/identifier for storage systems
    /// - For Database storage: Contains the File.Id to lookup blob data
    /// - For External storage (S3, MinIO, etc.): Contains the external path/key
    /// </summary>
    public string? ExternalStoragePath { get; set; }

    private byte[] _bytes = [];

    /// <summary>
    /// Gets or sets the file bytes. For external storage, this will be empty and should be loaded via FileContentService.
    /// </summary>

    [NotMapped]
    public byte[] Bytes
    {
        get => _bytes;
        private set
        {
            _bytes = value;
            AreBytesLoaded = true;
        }
    }

    public FileMetadata()
    {
    }

    /// <summary>
    /// The name without its extension. A name that arrives already carrying its extension more
    /// than once — a re-submitted document whose caller composed name plus extension — would
    /// otherwise leave the extension baked into the stem, and every consumer that reattaches it
    /// would then render it twice.
    /// </summary>
    private static string StemOf(string name, string extension)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        if (extension.Length == 0)
        {
            return stem;
        }

        while (stem.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^extension.Length];
        }

        return stem;
    }

    public FileMetadata(string organizationId, string name, byte[] bytes)
    {
        string oneLineName = ControlCharacters.ToSpaces(name);
        OrganizationId = organizationId;
        Extension = Path.GetExtension(oneLineName)
            ?? throw new ArgumentException("Name must have an extension");
        Name = StemOf(oneLineName, Extension);

        LoadBytes(bytes); // Load bytes using the method
        SizeBytes = bytes.Length;
        StorageProvider = StorageProvider.Database; // Default to database storage for existing constructor
        // ExternalStoragePath will be set to the FileStorage.Id during the migration
    }

    public FileMetadata(string organizationId, string name, long sizeBytes, StorageProvider storageProvider, string? externalStoragePath = null)
    {
        string oneLineName = ControlCharacters.ToSpaces(name);
        OrganizationId = organizationId;
        Extension = Path.GetExtension(oneLineName)
            ?? throw new ArgumentException("Name must have an extension");
        Name = StemOf(oneLineName, Extension);

        SizeBytes = sizeBytes;
        StorageProvider = storageProvider;
        ExternalStoragePath = externalStoragePath;
    }

    /// <summary>
    /// Gets the storage path for this file - ExternalStoragePath which contains File.Id for database storage or S3 key for external storage
    /// </summary>
    public string GetStoragePath()
    {
        return ExternalStoragePath ?? throw new InvalidOperationException("External storage path is null");
    }

    /// <summary>
    /// Manually loads bytes for external storage files. Used by FileContentService.
    /// </summary>
    /// <param name="bytes">The bytes to load</param>
    public void LoadBytes(byte[] bytes)
    {
        _bytes = bytes;
        AreBytesLoaded = true;
    }

    /// <summary>
    /// Checks if bytes have been loaded for this file
    /// </summary>
    [NotMapped]
    public bool AreBytesLoaded { get; private set; } = false;

    /// <summary>
    /// Gets the MIME content type: the document's detected type when the document is loaded,
    /// else the type the extension suggests.
    /// </summary>
    [NotMapped]
    public string ContentType
    {
        get
        {
            return (Document?.MediaType ?? Utils.DetermineMediaTypeFromExtension(Extension)).ToContentType();
        }
    }
}
