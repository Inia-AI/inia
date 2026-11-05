using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Services.Documents;

public enum WorksheetLayoutTypes
{
    [EnumMember(Value = nameof(Unidentified))]
    [Description("The layout of the worksheet could not be identified.")]
    Unidentified = -1,

    [EnumMember(Value = nameof(Database))]
    [Description("A single uninterrupted block of columns and rows forming one " +
        "logical table - even if there are multiple header rows - without any " +
        "empty columns separating different data regions.")]
    Database,

    [EnumMember(Value = nameof(MultiNoSharedColumns))]
    [Description("Multiple distinct tables arranged side by side, each separated " +
        "from the next by at least one empty column. Each table repeats its own " +
        "row titles, so there is no shared row title spanning across tables.")]
    MultiNoSharedColumns,

    [EnumMember(Value = nameof(MultiSharedColumns))]
    [Description("Multiple distinct tables arranged side by side, each separated " +
        "from the next by at least one empty column. However, these tables share " +
        "a single set of row titles, typically shown once on the left side, " +
        "rather than repeating them for each table.")]
    MultiSharedColumns,
}
