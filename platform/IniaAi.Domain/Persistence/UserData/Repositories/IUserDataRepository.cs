namespace IniaAi.Domain.Persistence.UserData.Repositories;

/// <summary>
/// Repository interface specifically for UserData context entities
/// </summary>
public interface IUserDataRepository<T> : IRepository<T> where T : class
{
}
