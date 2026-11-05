using System.Reflection;

namespace IniaAi.Services.CsvImport;

public interface ICsvEntityMapper<T> where T : class
{
    bool TryCreateEntity(out T entity, object[] parameters);
    IEnumerable<ColumnPropertyMap> MapColumns(string[] columns, PropertyInfo[] properties);
    void MapValues(T instance, string[] values, List<ColumnPropertyMap> columnMaps);
    Task ProcessReferences(IEnumerable<(T entity, string[] values, string[] columnNames)> entities, IServiceProvider serviceProvider);

    /// <summary>
    /// Validates entities before import. Default implementation returns no errors.
    /// </summary>
    Task<List<CsvImportError>> ValidateEntitiesAsync(
        List<(T entity, string[] values, string[] columnNames, int rowNumber)> entities,
        IServiceProvider serviceProvider)
    {
        return Task.FromResult(new List<CsvImportError>());
    }
}

public record ColumnPropertyMap(string Name, PropertyInfo PropertyInfo);
