using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// Represents a file entity.
/// </summary>
public class File
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    private byte[] _bytes = [];

    /// <summary>
    /// Gets or sets the file bytes. For external storage, this will be empty and should be loaded via FileContentService.
    /// </summary>
    public byte[] Bytes
    {
        get => _bytes;
        set
        {
            _bytes = value;
            AreBytesLoaded = true;
        }
    }

    public File()
    {
    }

    public File(byte[] bytes)
    {
        LoadBytes(bytes);
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

    #region Not Mapped - To be removed in future version

    public string? OrganizationId { get; set; }

    public string? DocumentId { get; set; }

    [StringLength(450)]
    public string? Name { get; set; }

    [StringLength(10)]
    public string? Extension { get; set; }

    public long? SizeBytes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }

    #endregion
}
