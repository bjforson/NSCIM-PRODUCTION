using Microsoft.EntityFrameworkCore;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    /// <summary>
    /// Extension methods for DbContext to improve memory management
    /// </summary>
    public static class DbContextExtensions
    {
        /// <summary>
        /// Saves changes and immediately clears the change tracker to release memory
        /// This prevents Entity Framework from holding references to thousands of tracked entities
        /// </summary>
        public static async Task<int> SaveChangesAndClearAsync(this DbContext context)
        {
            var result = await context.SaveChangesAsync();

            // ✅ CRITICAL MEMORY FIX: Release ALL tracked entities immediately
            context.ChangeTracker.Clear();

            return result;
        }

        /// <summary>
        /// Clears the change tracker and optionally logs the number of tracked entities
        /// </summary>
        public static void ClearChangeTracker(this DbContext context, bool logClearedCount = false)
        {
            if (logClearedCount)
            {
                var trackedCount = context.ChangeTracker.Entries().Count();
                Console.WriteLine($"Clearing {trackedCount} tracked entities from change tracker");
            }

            context.ChangeTracker.Clear();
        }
    }
}

