using System.Reflection;

namespace IniaAi.Services.CsvImport;

public class DefaultCsvEntityMapper<T> : ICsvEntityMapper<T> where T : class
{
    protected TParam GetParameterValue<TParam>(object[] parameters, int index)
    {
        return parameters.Length <= index || parameters[index] is not TParam value
            ? throw new ArgumentException($"Required parameter at index {index} of type {typeof(TParam)} was not found")
            : value;
    }

    protected TParam? GetParameterValueOrDefault<TParam>(object[] parameters, int index, TParam? defaultValue = default)
    {
        return parameters.Length <= index || parameters[index] is not TParam value
            ? defaultValue
            : value;
    }

    public virtual bool TryCreateEntity(out T entity, object[] parameters)
    {
        entity = default!;

        if (Activator.CreateInstance(typeof(T), parameters) is T instance)
        {
            entity = instance;
            return true;
        }

        return false;
    }

    public virtual IEnumerable<ColumnPropertyMap> MapColumns(string[] columns, PropertyInfo[] properties)
    {
        var columnMaps = new List<ColumnPropertyMap>();
        foreach (string column in columns)
        {
            PropertyInfo? property = properties.FirstOrDefault(p => p.Name == column);
            if (property is not null)
            {
                columnMaps.Add(new ColumnPropertyMap(column, property));
            }
        }

        return columnMaps;
    }

    public virtual void MapValues(T instance, string[] values, List<ColumnPropertyMap> columnMaps)
    {
        for (int i = 0; i < Math.Min(values.Length, columnMaps.Count); i++)
        {
            PropertyInfo propertyInfo = columnMaps[i].PropertyInfo;
            string val = values[i].Trim();

            // Skip empty values
            if (string.IsNullOrWhiteSpace(val))
            {
                continue;
            }

            if (propertyInfo.PropertyType == typeof(string))
            {
                propertyInfo.SetValue(instance, val);
            }
            else if (propertyInfo.PropertyType == typeof(int))
            {
                propertyInfo.SetValue(instance, int.Parse(val));
            }
            else if (propertyInfo.PropertyType == typeof(float))
            {
                propertyInfo.SetValue(instance, float.Parse(val));
            }
            else if (propertyInfo.PropertyType == typeof(double))
            {
                propertyInfo.SetValue(instance, double.Parse(val));
            }
            else if (propertyInfo.PropertyType == typeof(decimal))
            {
                propertyInfo.SetValue(instance, decimal.Parse(val));
            }
            else if (propertyInfo.PropertyType == typeof(DateTime))
            {
                propertyInfo.SetValue(instance, DateTime.Parse(val));
            }
            else if (propertyInfo.PropertyType == typeof(DateOnly))
            {
                propertyInfo.SetValue(instance, DateOnly.Parse(val));
            }
        }
    }

    public virtual Task ProcessReferences(IEnumerable<(T entity, string[] values, string[] columnNames)> entities, IServiceProvider serviceProvider)
    {
        return Task.CompletedTask;
    }

    public virtual Task<List<CsvImportError>> ValidateEntitiesAsync(
        List<(T entity, string[] values, string[] columnNames, int rowNumber)> entities,
        IServiceProvider serviceProvider)
    {
        return Task.FromResult(new List<CsvImportError>());
    }
}
