using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Services.CsvImport;

public static class EntityMapperRegistry
{
    private static readonly Dictionary<Type, object> _entityMappers = new()
    {
        { typeof(User), new UserCsvMapper() }
    };

    public static ICsvEntityMapper<T> GetMapper<T>() where T : class
    {
        return _entityMappers.TryGetValue(typeof(T), out object? mapper)
            ? (ICsvEntityMapper<T>)mapper
            : new DefaultCsvEntityMapper<T>();
    }
}
