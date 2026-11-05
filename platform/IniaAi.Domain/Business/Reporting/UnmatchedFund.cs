namespace IniaAi.Domain.Business.Reporting;

/// <summary>
/// An observation whose fund name matched no counterparty stores no fund or manager id. Exported
/// files still write <see cref="EXPORT_ID"/> in those columns, the value their readers have always
/// received for such a fund.
/// </summary>
public static class UnmatchedFund
{
    public const string EXPORT_ID = "ERROR";
}
