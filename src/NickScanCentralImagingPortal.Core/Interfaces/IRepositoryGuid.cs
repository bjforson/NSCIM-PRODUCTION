namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Base repository interface for entities with Guid primary keys
    /// </summary>
    public interface IRepositoryGuid<T> where T : class
    {
        Task<T?> GetByIdAsync(Guid id);
        Task<IEnumerable<T>> GetAllAsync();
        Task<T> AddAsync(T entity);
        Task UpdateAsync(T entity);
        Task DeleteAsync(Guid id);
        Task<bool> ExistsAsync(Guid id);
    }
}

