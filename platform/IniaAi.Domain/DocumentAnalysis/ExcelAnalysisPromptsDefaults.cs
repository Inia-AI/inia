using IniaAi.Domain.Entities.DocumentAnalysis;

namespace IniaAi.Domain.DocumentAnalysis;

/// <summary>
/// The Excel analysis prompts a deployment starts with, edited afterwards in Tools Settings.
/// </summary>
public static class ExcelAnalysisPromptsDefaults
{
    public static DateTime PromptsUpdateDate { get; } = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

    public const string EXCEL_LAYOUT_ANALYSIS_SYSTEM_PROMPT =
"""
Your task is to classify Excel worksheet layouts to determine optimal chunking strategy. Examine the sheet and classify it into one of three layout types.
""";

    public const string EXCEL_LAYOUT_ANALYSIS_USER_PROMPT =
"""
<Excel_worksheet>
{0}
</Excel_worksheet>

LAYOUT TYPES:

A. **Database** (Default): Single continuous table—one uninterrupted block of columns and rows. May have multiple header rows or grouped sections. No empty columns separating data regions.

B. **MultiNoSharedColumns**: Multiple distinct tables separated by empty column(s). Each table has its own row labels on its left side.

C. **MultiSharedColumns**: Multiple distinct tables separated by empty column(s), but sharing a common set of row labels (typically the leftmost columns).

KEY PRINCIPLE:

Empty columns are the signal for multiple tables. Continuous data across columns without empty separators = Database, regardless of how headers are grouped.

INSTRUCTIONS:

1. Identify whether empty columns separate distinct data regions.
2. If no empty separators exist → **Database**
3. If empty separators exist, determine whether tables share row labels or repeat them independently.
4. For **MultiSharedColumns** only: specify which column(s) contain(s) the shared row titles using standard Excel references (e.g., column A, column B). Otherwise use "NA".

5. Check your work by considering the following clarifications:
- Continuous Data is when the data spans across columns without breaks. Even if grouped under different headings, the table should be treated as one single table and classified under the Database category above.
- Shared Column Misidentification: Do not mark a column as "shared" unless there are multiple distinct tables separated by empty columns that explicitly reuse the same column(s). In a Database layout, all columns are part of one table.
- Visual Cues: Look for clear separations such as empty columns that indicate the presence of multiple tables.

6. Provide your final answer as exactly as per the format below wrapped in <answer> tags.

OUTPUT FORMAT:

<answer>
{{
    "WorksheetLayoutType": "[layout type]",
    "SharedColumns": ["A", "B" or "NA"]
}}
</answer>

Example:

<answer>
{{
    "WorksheetLayoutType": "Database",
    "SharedColumns": ["B"]
}}
</answer>
""";

    public const string EXCEL_HEADER_DATABASE_SYSTEM_PROMPT =
"""
Your task is to identify header rows to freeze for paginating an Excel worksheet. The worksheet has been pre-classified as a Database layout (single continuous table). Your output will determine which rows are preserved as headers during chunking.
""";

    public const string EXCEL_HEADER_DATABASE_USER_PROMPT =
"""
INPUTS:

<Excel_worksheet>
{0}
</Excel_worksheet>

TASK: Identify header rows to freeze for the PRIMARY table.

IDENTIFYING THE PRIMARY TABLE:

In financial models, the primary table is typically a time-series with dates, periods, quarters, or years as column headers, spanning many columns (often 10+).

DETERMINING HEADER ROWS:

1. Include the primary table's column header row(s)—may span multiple rows for hierarchical structures
2. If small reference tables exist above the primary table, include them to preserve context during pagination
3. Stop before the first row of actual data values

EDGE CASES:

- Data starts immediately with no column labels → return empty string: ""
- Table begins at row 1 with no content above → header rows start at 1
- Uncertain if row is header or first data row → include it as header

OUTPUT FORMAT:

Provide your final answer as exactly as per the format below wrapped in <answer> tags.

<answer>
{{
    "HeaderRows": "1-5"
}}
</answer>
""";

    public const string EXCEL_HEADER_MULTI_NO_SHARED_SYSTEM_PROMPT =
"""
Your task is to identify header rows and table column boundaries for paginating an Excel worksheet. The worksheet has been pre-classified as MultiNoSharedColumns layout (multiple distinct tables arranged side by side, separated by empty columns). Your output will determine which rows are preserved as headers and how tables are segmented during chunking.
""";

    public const string EXCEL_HEADER_MULTI_NO_SHARED_USER_PROMPT =
"""
<Excel_worksheet>
{0}
</Excel_worksheet>

TASK: Identify header rows and column boundaries for multiple side-by-side tables.

DETERMINING HEADER ROWS:

1. Include column header row(s) for the tables—may span multiple rows for hierarchical structures
2. If reference tables or context exists above the main tables, include those rows
3. Stop before the first row of actual data values

DETERMINING TABLE COLUMNS:

1. Identify each distinct table separated by empty column(s)
2. For each table, specify the first and last column (e.g., "A-C", "E-G")
3. Single-column tables use same letter for both (e.g., "A-A")

EDGE CASES:

- Data starts immediately with no column labels → HeaderRows: ""
- Tables begin at row 1 → header rows start at 1

OUTPUT FORMAT:

<answer>
{{
    "HeaderRows": "1-5",
    "TableColumns": ["A-C", "E-G"]
}}
</answer>
""";

    public const string EXCEL_HEADER_MULTI_SHARED_SYSTEM_PROMPT =
"""
Your task is to identify header rows and table column boundaries for paginating an Excel worksheet. The worksheet has been pre-classified as MultiSharedColumns layout (multiple distinct tables arranged side by side, separated by empty columns, but sharing a common row index column). Your output will determine which rows are preserved as headers and how tables are segmented during chunking.
""";

    public const string EXCEL_HEADER_MULTI_SHARED_USER_PROMPT =
"""
<Excel_worksheet>
{0}
</Excel_worksheet>

<shared_columns>
{1}
</shared_columns>

TASK: Identify header rows and column boundaries for multiple side-by-side tables that share a common row index column.

DETERMINING HEADER ROWS:

1. Include column header row(s) for the tables—may span multiple rows for hierarchical structures
2. If reference tables or context exists above the main tables, include those rows
3. Stop before the first row of actual data values

DETERMINING TABLE COLUMNS:

1. Identify each distinct table separated by empty column(s)
2. EXCLUDE the shared column(s) from all table ranges—these apply to every table and will be handled separately
3. For each table, specify the first and last column of its unique data (e.g., "B-D", "F-H")
4. Single-column tables use same letter for both (e.g., "B-B")

EDGE CASES:

- Data starts immediately with no column labels → HeaderRows: ""
- Tables begin at row 1 → header rows start at 1

OUTPUT FORMAT:

<answer>
{{
    "HeaderRows": "1-5",
    "TableColumns": ["B-D", "F-H"]
}}
</answer>
""";

    public static ExcelAnalysisPrompts Create()
    {
        return new ExcelAnalysisPrompts
        {
            CreatedAt = PromptsUpdateDate,
            LayoutAnalysisSystemPrompt = EXCEL_LAYOUT_ANALYSIS_SYSTEM_PROMPT,
            LayoutAnalysisUserPrompt = EXCEL_LAYOUT_ANALYSIS_USER_PROMPT,
            HeaderDatabaseSystemPrompt = EXCEL_HEADER_DATABASE_SYSTEM_PROMPT,
            HeaderDatabaseUserPrompt = EXCEL_HEADER_DATABASE_USER_PROMPT,
            HeaderMultiNoSharedSystemPrompt = EXCEL_HEADER_MULTI_NO_SHARED_SYSTEM_PROMPT,
            HeaderMultiNoSharedUserPrompt = EXCEL_HEADER_MULTI_NO_SHARED_USER_PROMPT,
            HeaderMultiSharedSystemPrompt = EXCEL_HEADER_MULTI_SHARED_SYSTEM_PROMPT,
            HeaderMultiSharedUserPrompt = EXCEL_HEADER_MULTI_SHARED_USER_PROMPT,
        };
    }
}
