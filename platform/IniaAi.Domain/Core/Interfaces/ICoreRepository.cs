using IniaAi.Domain.Persistence.UserData.Repositories;

namespace IniaAi.Domain.Core.Interfaces;

/// <summary>
/// Repository interface specifically for Core context entities
/// </summary>
public interface ICoreRepository<T> : IRepository<T> where T : class
{
}
