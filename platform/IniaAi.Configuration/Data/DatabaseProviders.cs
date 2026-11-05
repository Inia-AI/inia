namespace IniaAi.Configuration.Data;

public struct DatabaseProviders
{
    public const string MSSQL = "MSSQL";
    // public const string MY_SQL = "MySQL";
    public const string POSTGRE_SQL = "PostgreSQL";
    // public const string SQLITE = "SQLite";
    // public const string ORACLE = "Oracle";

    public static readonly List<string> All =
    [
        MSSQL,
        // MY_SQL,
        POSTGRE_SQL,
        // SQLITE,
        // ORACLE
    ];

    public static bool IsValid(string? serverVendor)
    {
        return All.Contains(serverVendor, StringComparer.OrdinalIgnoreCase);
    }
}
