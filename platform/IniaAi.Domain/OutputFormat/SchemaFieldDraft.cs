namespace IniaAi.Domain.OutputFormat;

public enum SchemaFieldType
{
    Text = 0,
    Number = 1,
    WholeNumber = 2,
    TrueFalse = 3,
    List = 4,
}

/// <summary>
/// A single row in the Simple Schema Builder UI. Drives serialisation to a
/// flat JSON Schema (top-level required keys + per-property type / enum / range).
/// Anything beyond that round-trips through the Advanced (raw JSON Schema) editor.
/// </summary>
public class SchemaFieldDraft
{
    public string Name { get; set; } = string.Empty;
    public SchemaFieldType Type { get; set; } = SchemaFieldType.Text;
    public bool Required { get; set; } = true;

    /// <summary>Comma-separated allowed values, only used when <see cref="Type"/> is Text.</summary>
    public string? EnumValues { get; set; }

    /// <summary>Lower bound, only used when <see cref="Type"/> is Number or WholeNumber.</summary>
    public double? Min { get; set; }

    /// <summary>Upper bound, only used when <see cref="Type"/> is Number or WholeNumber.</summary>
    public double? Max { get; set; }

    /// <summary>Item type, only used when <see cref="Type"/> is List.</summary>
    public SchemaFieldType ListItemType { get; set; } = SchemaFieldType.Text;
}
