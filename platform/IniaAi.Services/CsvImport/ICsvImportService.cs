namespace IniaAi.Services.CsvImport;

public interface ICsvImportService
{
    /// <summary>
    /// Import entities from a CSV string.
    /// <para>
    /// In order to import correctly:
    /// <list type="bullet">
    /// <item>The CSV string must contain a header row.</item>
    /// <item>The names of the columns must match the names used for the entity in order to be mapped properly.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <typeparam name="T">The target entity type that is instantiated for each row in CSV file.</typeparam>
    /// <param name="csvContents">A CSV string containing the rows to import.</param>
    /// <param name="parameters">Default parameters required to contruct entity <see cref="T"/>.</param>
    /// <returns>Result containing the count of imported and skipped entities.</returns>
    Task<CsvImportResult> ImportAsync<T>(string csvContents, params object[] parameters)
        where T : class;
}
