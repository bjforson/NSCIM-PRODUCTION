using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    /// <summary>
    /// Base repository implementation for entities with Guid primary keys
    /// </summary>
    public class RepositoryGuid<T> : IRepositoryGuid<T> where T : class
    {
        protected readonly ApplicationDbContext _context;
        protected readonly DbSet<T> _dbSet;

        public RepositoryGuid(ApplicationDbContext context)
        {
            _context = context;
            _dbSet = context.Set<T>();
        }

        public virtual async Task<T?> GetByIdAsync(Guid id)
        {
            return await _dbSet.FindAsync(id);
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _dbSet.ToListAsync();
        }

        public virtual async Task<T> AddAsync(T entity)
        {
            await _dbSet.AddAsync(entity);
            await _context.SaveChangesAsync();

            // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
            _context.ChangeTracker.Clear();

            return entity;
        }

        public virtual async Task UpdateAsync(T entity)
        {
            _dbSet.Update(entity);
            await _context.SaveChangesAsync();

            // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
            _context.ChangeTracker.Clear();
        }

        public virtual async Task DeleteAsync(Guid id)
        {
            var entity = await GetByIdAsync(id);
            if (entity != null)
            {
                _dbSet.Remove(entity);
                await _context.SaveChangesAsync();

                // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                _context.ChangeTracker.Clear();
            }
        }

        public virtual async Task<bool> ExistsAsync(Guid id)
        {
            return await _dbSet.FindAsync(id) != null;
        }
    }
}

