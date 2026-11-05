using System.Globalization;

namespace IniaAi.FileFormats.Excel;

/// <summary>
/// The number formats a workbook names only by id, in the codes Excel shows for US English, except the short date and
/// the date with time, which follow the culture. An id the table lacks, such as one of the East Asian ones, shows as General.
/// </summary>
internal static class BuiltInNumberFormats
{
    private const string GENERAL = ExcelNumberText.GENERAL;
    private const int SHORT_DATE = 14;
    private const int DATE_AND_TIME = 22;
    private const string TIME_OF_DAY = " h:mm";

    private static readonly Dictionary<int, string> _codes = new()
    {
        [0] = GENERAL,
        [1] = "0",
        [2] = "0.00",
        [3] = "#,##0",
        [4] = "#,##0.00",
        [5] = "$#,##0_);($#,##0)",
        [6] = "$#,##0_);[Red]($#,##0)",
        [7] = "$#,##0.00_);($#,##0.00)",
        [8] = "$#,##0.00_);[Red]($#,##0.00)",
        [9] = "0%",
        [10] = "0.00%",
        [11] = "0.00E+00",
        [12] = "# ?/?",
        [13] = "# ??/??",
        [15] = "d-mmm-yy",
        [16] = "d-mmm",
        [17] = "mmm-yy",
        [18] = "h:mm AM/PM",
        [19] = "h:mm:ss AM/PM",
        [20] = "h:mm",
        [21] = "h:mm:ss",
        [37] = "#,##0 ;(#,##0)",
        [38] = "#,##0 ;[Red](#,##0)",
        [39] = "#,##0.00;(#,##0.00)",
        [40] = "#,##0.00;[Red](#,##0.00)",
        [45] = "mm:ss",
        [46] = "[h]:mm:ss",
        [47] = "mm:ss.0",
        [48] = "##0.0E+0",
        [49] = "@",
    };

    /// <summary>
    /// The code of a built-in format. Excel localizes formats 14 and 22, so they use the short date of the reader's
    /// culture, as the paid reader shows them, not the US English code: the invariant culture of a server shows 01/31/2020.
    /// </summary>
    public static string Code(int id, CultureInfo culture)
    {
        return id switch
        {
            SHORT_DATE => ShortDate(culture),
            DATE_AND_TIME => ShortDate(culture) + TIME_OF_DAY,
            _ => _codes.GetValueOrDefault(id, GENERAL),
        };
    }

    private static string ShortDate(CultureInfo culture)
    {
        return culture.DateTimeFormat.ShortDatePattern.Replace('M', 'm');
    }
}
