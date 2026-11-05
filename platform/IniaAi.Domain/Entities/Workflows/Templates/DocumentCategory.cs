using IniaAi.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace IniaAi.Domain.Entities.Workflows.Templates;

public partial class DocumentCategory : Entity<DocumentCategory>
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public int DisplayOrder { get; set; } = 0;

    public string? CreatedByUserId { get; set; }

    public static DocumentCategory Create(string name, string? createdByUserId = null, int displayOrder = 0)
    {
        return new DocumentCategory
        {
            Id = ToKebabCase(name),
            Name = name,
            DisplayOrder = displayOrder,
            CreatedByUserId = createdByUserId
        };
    }

    private static string ToKebabCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string result = GenerateWhitespaceAndUnderscoreRegex().Replace(input.Trim(), "-");
        result = GenerateNonAlphanumericCharacterRegex().Replace(result, "");
        result = GenerateDashPatternRegex().Replace(result, "-");
        return result.ToLowerInvariant().Trim('-');
    }

    [GeneratedRegex(@"[\s_]+")]
    private static partial Regex GenerateWhitespaceAndUnderscoreRegex();

    [GeneratedRegex(@"[^a-zA-Z0-9-]")]
    private static partial Regex GenerateNonAlphanumericCharacterRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex GenerateDashPatternRegex();
}
