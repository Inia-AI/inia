using IniaAi.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.RegularExpressions;

namespace IniaAi.Services.CsvImport;

public partial class CsvImportService : ICsvImportService
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;

    [GeneratedRegex(@"(?:^|,)(?:""([^""]*)""|([^,]*))")]
    private static partial Regex SplitColumnsRegex();

    public CsvImportService(IServiceProvider serviceProvider, ILogger<ICsvImportService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<CsvImportResult> ImportAsync<T>(string csvContents, params object[] parameters)
        where T : class
    {
        var rows = GetRows(csvContents).ToList();

        if (rows.Count <= 1)
        {
            _logger.LogWarning(
                "CSV import for {EntityType} skipped: {Reason}",
                typeof(T).Name,
                rows.Count == 0
                    ? "CSV string is empty"
                    : "CSV only contains a header row");
            return new CsvImportResult(0, 0);
        }

        string header = rows[0];
        rows.RemoveAt(0);

        PropertyInfo[] properties = typeof(T).GetProperties();
        string[] columns = [.. GetColumns(header)];

        // Get the appropriate mapper for the entity type
        ICsvEntityMapper<T> mapper = EntityMapperRegistry.GetMapper<T>();
        var columnMaps = mapper.MapColumns(columns, properties).ToList();

        // Validate all columns are mapped
        var unmappedColumns = columns.Except(columnMaps.Select(m => m.Name)).ToList();
        if (unmappedColumns.Count != 0)
        {
            _logger.LogError(
                "CSV import for {EntityType} failed: unmapped columns [{UnmappedColumns}]. CSV headers: [{AllColumns}]",
                typeof(T).Name,
                string.Join(", ", unmappedColumns),
                string.Join(", ", columns));
            throw new InvalidDataException($"CSV string contains columns that cannot be mapped to type {typeof(T)}: {string.Join(',', unmappedColumns)}");
        }

        IUserDataDbContext dbContext = _serviceProvider.GetRequiredService<IUserDataDbContext>();
        var entitiesToValidate = new List<(T entity, string[] values, string[] columnNames, int rowNumber)>();
        var entities = new List<(T entity, string[] values, string[] columnNames)>();
        int successCount = 0;
        int updatedCount = 0;

        PropertyInfo? keyProperty = properties.FirstOrDefault(p =>
            p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.KeyAttribute), true).Length != 0);

        _logger.LogInformation(
            "CSV import for {EntityType}: processing {RowCount} data rows with columns [{Columns}]",
            typeof(T).Name,
            rows.Count,
            string.Join(", ", columns));

        // First: Collect all entities for validation
        int rowNumber = 2; // Start at 2 (1 is header, 2 is first data row)
        foreach (string row in rows)
        {
            if (!mapper.TryCreateEntity(out T instance, parameters))
            {
                _logger.LogError(
                    "CSV import for {EntityType} failed: could not instantiate entity. Parameters: [{Parameters}]",
                    typeof(T).Name,
                    string.Join(", ", parameters.Select(p => p?.ToString() ?? "null")));
                throw new ArgumentException($"Failed to instantiate entity {typeof(T)}, invalid parameters for constructor.");
            }

            string[] values = [.. GetColumns(row)];
            mapper.MapValues(instance, values, columnMaps);

            entitiesToValidate.Add((instance, values, columns, rowNumber));
            rowNumber++;
        }

        // Validate all entities before processing
        List<CsvImportError> validationErrors = await mapper.ValidateEntitiesAsync(entitiesToValidate, _serviceProvider);
        if (validationErrors.Count != 0)
        {
            _logger.LogWarning(
                "CSV import for {EntityType} rejected with {ErrorCount} validation errors: {Errors}",
                typeof(T).Name,
                validationErrors.Count,
                string.Join("; ", validationErrors.Select(e => $"Row {e.RowNumber}: [{e.ErrorType}] {e.ErrorMessage}")));
            return new CsvImportResult(validationErrors);
        }

        // Second pass: Process parent entities (those without ParentId) or update existing entities
        PropertyInfo? parentIdProp = properties.FirstOrDefault(p => p.Name == "ParentId");

        foreach ((T? instance, string[]? values, string[]? columnNames, _) in entitiesToValidate)
        {
            string? parentIdValue = parentIdProp?.GetValue(instance)?.ToString();

            // Skip child entities in first pass
            if (!string.IsNullOrEmpty(parentIdValue))
            {
                continue;
            }

            if (keyProperty is not null)
            {
                string? keyValue = keyProperty.GetValue(instance)?.ToString();
                if (!string.IsNullOrEmpty(keyValue))
                {
                    // Check if entity with same key already exists (including soft-deleted)
                    T? existingEntity = await dbContext.Set<T>()
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(e => EF.Property<string>(e, keyProperty.Name) == keyValue);
                    if (existingEntity is not null)
                    {
                        // Update existing entity with new values
                        foreach (CsvImport.ColumnPropertyMap? columnMap in columnMaps)
                        {
                            object? value = columnMap.PropertyInfo.GetValue(instance);
                            columnMap.PropertyInfo.SetValue(existingEntity, value);
                        }

                        // Handle soft-delete resurrection if applicable
                        PropertyInfo? isDeletedProperty = typeof(T).GetProperty("IsDeleted");
                        PropertyInfo? deletedAtProperty = typeof(T).GetProperty("DeletedAt");
                        if (isDeletedProperty is not null && deletedAtProperty is not null)
                        {
                            // Check if entity is soft-deleted and resurrect it
                            bool isDeleted = (bool)(isDeletedProperty.GetValue(existingEntity) ?? false);
                            if (isDeleted)
                            {
                                isDeletedProperty.SetValue(existingEntity, false);
                                deletedAtProperty.SetValue(existingEntity, null);
                            }
                        }

                        dbContext.Entry(existingEntity).State = EntityState.Modified;
                        updatedCount++;
                        entities.Add((existingEntity, values, columnNames));
                        continue;
                    }
                }
            }

            entities.Add((instance, values, columnNames));
            _ = dbContext.Add(instance);
            successCount++;
        }

        // Save parent entities first
        try
        {
            _ = await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "CSV import for {EntityType}: failed to save parent entities. {Imported} new, {Updated} updated",
                typeof(T).Name,
                successCount,
                updatedCount);
            throw;
        }

        // Third pass: Process child entities
        foreach ((T? instance, string[]? values, string[]? columnNames, _) in entitiesToValidate)
        {
            string? parentIdValue = parentIdProp?.GetValue(instance)?.ToString();

            // Only process child entities in second pass
            if (string.IsNullOrEmpty(parentIdValue))
            {
                continue;
            }

            if (keyProperty is not null)
            {
                string? keyValue = keyProperty.GetValue(instance)?.ToString();
                if (!string.IsNullOrEmpty(keyValue))
                {
                    // Check if entity with same key already exists (including soft-deleted)
                    T? existingEntity = await dbContext.Set<T>()
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(e => EF.Property<string>(e, keyProperty.Name) == keyValue);
                    if (existingEntity is not null)
                    {
                        // Update existing entity with new values
                        foreach (CsvImport.ColumnPropertyMap? columnMap in columnMaps)
                        {
                            object? value = columnMap.PropertyInfo.GetValue(instance);
                            columnMap.PropertyInfo.SetValue(existingEntity, value);
                        }

                        // Handle soft-delete resurrection if applicable
                        PropertyInfo? isDeletedProperty = typeof(T).GetProperty("IsDeleted");
                        PropertyInfo? deletedAtProperty = typeof(T).GetProperty("DeletedAt");
                        if (isDeletedProperty is not null && deletedAtProperty is not null)
                        {
                            // Check if entity is soft-deleted and resurrect it
                            bool isDeleted = (bool)(isDeletedProperty.GetValue(existingEntity) ?? false);
                            if (isDeleted)
                            {
                                isDeletedProperty.SetValue(existingEntity, false);
                                deletedAtProperty.SetValue(existingEntity, null);
                            }
                        }

                        dbContext.Entry(existingEntity).State = EntityState.Modified;
                        updatedCount++;
                        entities.Add((existingEntity, values, columnNames));
                        continue;
                    }
                }
            }

            entities.Add((instance, values, columnNames));
            _ = dbContext.Add(instance);
            successCount++;
        }

        // Save child entities
        try
        {
            _ = await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "CSV import for {EntityType}: failed to save child entities",
                typeof(T).Name);
            throw;
        }

        // Process any entity-specific references
        await mapper.ProcessReferences(entities, _serviceProvider);

        // Save again in case references were updated
        try
        {
            _ = await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "CSV import for {EntityType}: failed to save entity references",
                typeof(T).Name);
            throw;
        }

        _logger.LogInformation(
            "CSV import for {EntityType} completed: {Imported} imported, {Updated} updated",
            typeof(T).Name,
            successCount,
            updatedCount);
        return new CsvImportResult(successCount, updatedCount, 0);
    }

    #region Private

    private static IEnumerable<string> GetRows(string csvContents)
    {
        string[] rows = csvContents.Split('\n');
        return rows.Select(r => r.Trim()).Where(r => !string.IsNullOrWhiteSpace(r));
    }

    private static IEnumerable<string> GetColumns(string line)
    {
        MatchCollection matches = SplitColumnsRegex().Matches(line);
        return matches.Select(m => (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim());
    }

    #endregion
}
