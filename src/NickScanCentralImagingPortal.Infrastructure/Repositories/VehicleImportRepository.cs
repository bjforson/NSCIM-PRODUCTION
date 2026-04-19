using Npgsql;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    /// <summary>
    /// Repository implementation for VehicleImport data access operations
    /// </summary>
    public class VehicleImportRepository : IVehicleImportRepository
    {
        private readonly IcumDownloadsDbContext _context;

        public VehicleImportRepository(IcumDownloadsDbContext context)
        {
            _context = context;
        }

        public async Task<int> SaveVehicleImportAsync(VehicleImport vehicleImport)
        {
            // Ensure FK references an existing BOE document; resolve if necessary to avoid FK violations
            if (vehicleImport.BOEDocumentId > 0)
            {
                var boeExists = await _context.BOEDocuments.AsNoTracking().AnyAsync(b => b.Id == vehicleImport.BOEDocumentId);
                if (!boeExists)
                {
                    // Try to resolve by declaration number first, then by container+declaration
                    BOEDocument? resolved = null;
                    if (!string.IsNullOrWhiteSpace(vehicleImport.DeclarationNumber))
                    {
                        resolved = await _context.BOEDocuments
                            .AsNoTracking()
                            .Where(b => b.DeclarationNumber == vehicleImport.DeclarationNumber)
                            .OrderByDescending(b => b.Id)
                            .FirstOrDefaultAsync();
                    }
                    if (resolved == null && !string.IsNullOrWhiteSpace(vehicleImport.ContainerNumber))
                    {
                        var container = vehicleImport.ContainerNumber.Trim().ToUpper();
                        var decl = vehicleImport.DeclarationNumber?.Trim();
                        resolved = await _context.BOEDocuments
                            .AsNoTracking()
                            .FirstOrDefaultAsync(b => b.ContainerNumber == container && b.DeclarationNumber == decl);
                    }

                    if (resolved != null)
                    {
                        vehicleImport.BOEDocumentId = resolved.Id;
                    }
                    else
                    {
                        throw new InvalidOperationException($"BOEDocument not found for VehicleImport (VIN={vehicleImport.VIN}, Decl={vehicleImport.DeclarationNumber}, Cntr={vehicleImport.ContainerNumber})");
                    }
                }
            }

            vehicleImport.CreatedAt = DateTime.UtcNow;
            vehicleImport.UpdatedAt = DateTime.UtcNow;

            _context.VehicleImports.Add(vehicleImport);
            await _context.SaveChangesAsync();

            // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
            _context.ChangeTracker.Clear();

            return vehicleImport.Id;
        }

        public async Task UpdateVehicleImportAsync(VehicleImport vehicleImport)
        {
            // ✅ DEADLOCK FIX: Add retry logic for transient SQL deadlock errors
            // Deadlocks are transient and can be safely retried
            const int maxRetries = 3;
            var retryDelay = TimeSpan.FromMilliseconds(100);

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    vehicleImport.UpdatedAt = DateTime.UtcNow;
                    _context.VehicleImports.Update(vehicleImport);
                    await _context.SaveChangesAsync();

                    // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                    _context.ChangeTracker.Clear();
                    return; // Success - exit retry loop
                }
                catch (Npgsql.PostgresException sqlEx) when (sqlEx.SqlState == "40P01") // Deadlock victim (Error 1205)
                {
                    if (attempt < maxRetries - 1)
                    {
                        // Clear change tracker before retry to avoid stale entity state
                        _context.ChangeTracker.Clear();

                        // Exponential backoff: 100ms, 200ms, 400ms
                        await Task.Delay(retryDelay);
                        retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2);

                        // Retry - deadlocks are transient
                        continue;
                    }
                    else
                    {
                        // Final attempt failed - rethrow (service layer will log)
                        throw;
                    }
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException sqlEx && sqlEx.SqlState == "40P01")
                {
                    // Handle deadlock wrapped in DbUpdateException
                    if (attempt < maxRetries - 1)
                    {
                        _context.ChangeTracker.Clear();
                        await Task.Delay(retryDelay);
                        retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2);

                        // Retry - deadlocks are transient
                        continue;
                    }
                    else
                    {
                        // Final attempt failed - rethrow (service layer will log)
                        throw;
                    }
                }
            }
        }

        public async Task<VehicleImport?> GetVehicleImportByVINAsync(string vin)
        {
            return await _context.VehicleImports
                .Include(v => v.BOEDocument)
                .FirstOrDefaultAsync(v => v.VIN == vin);
        }

        public async Task<VehicleImport?> GetVehicleImportByIdAsync(int id)
        {
            return await _context.VehicleImports
                .Include(v => v.BOEDocument)
                .FirstOrDefaultAsync(v => v.Id == id);
        }

        public async Task<(List<VehicleImport> Items, int TotalCount)> GetVehicleImportsAsync(
            int page = 1,
            int pageSize = 50,
            string? searchTerm = null,
            VehicleImportType? importType = null,
            string? processingStatus = null)
        {
            var query = _context.VehicleImports
                .Include(v => v.BOEDocument)
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var searchLower = searchTerm.ToLower();
                // ✅ FIX: Use case-insensitive search by converting both sides to lowercase
                // EF Core will translate ToLower() to SQL Server LOWER() function
                query = query.Where(v =>
                    (v.VIN != null && v.VIN.ToLower().Contains(searchLower)) ||
                    (v.ChassisNumber != null && v.ChassisNumber.ToLower().Contains(searchLower)) ||
                    (v.VehicleType != null && v.VehicleType.ToLower().Contains(searchLower)) ||
                    (v.Make != null && v.Make.ToLower().Contains(searchLower)) ||
                    (v.Model != null && v.Model.ToLower().Contains(searchLower)) ||
                    (v.DeclarationNumber != null && v.DeclarationNumber.ToLower().Contains(searchLower)) ||
                    (v.ImporterName != null && v.ImporterName.ToLower().Contains(searchLower)) ||
                    (v.ShipperName != null && v.ShipperName.ToLower().Contains(searchLower)) ||
                    (v.ConsigneeName != null && v.ConsigneeName.ToLower().Contains(searchLower))
                );
            }

            if (importType.HasValue)
            {
                query = query.Where(v => v.ImportType == importType.Value);
            }

            if (!string.IsNullOrWhiteSpace(processingStatus))
            {
                query = query.Where(v => v.ProcessingStatus == processingStatus);
            }

            // Get total count
            var totalCount = await query.CountAsync();

            // Apply pagination
            var items = await query
                .OrderByDescending(v => v.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<List<VehicleImport>> GetVehicleImportsByDeclarationNumberAsync(string declarationNumber)
        {
            return await _context.VehicleImports
                .Include(v => v.BOEDocument)
                .Where(v => v.DeclarationNumber == declarationNumber)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<VehicleImport>> GetVehicleImportsByContainerNumberAsync(string containerNumber)
        {
            return await _context.VehicleImports
                .Include(v => v.BOEDocument)
                .Where(v => v.ContainerNumber == containerNumber)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<VehicleImport>> SearchVehicleImportsAsync(
            string? vin = null,
            string? chassisNumber = null,
            string? vehicleType = null,
            string? make = null,
            string? model = null,
            string? declarationNumber = null,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            var query = _context.VehicleImports
                .Include(v => v.BOEDocument)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(vin))
            {
                query = query.Where(v => v.VIN.Contains(vin));
            }

            if (!string.IsNullOrWhiteSpace(chassisNumber))
            {
                query = query.Where(v => v.ChassisNumber != null && v.ChassisNumber.Contains(chassisNumber));
            }

            if (!string.IsNullOrWhiteSpace(vehicleType))
            {
                query = query.Where(v => v.VehicleType != null && v.VehicleType.Contains(vehicleType));
            }

            if (!string.IsNullOrWhiteSpace(make))
            {
                query = query.Where(v => v.Make != null && v.Make.Contains(make));
            }

            if (!string.IsNullOrWhiteSpace(model))
            {
                query = query.Where(v => v.Model != null && v.Model.Contains(model));
            }

            if (!string.IsNullOrWhiteSpace(declarationNumber))
            {
                query = query.Where(v => v.DeclarationNumber == declarationNumber);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(v => v.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(v => v.CreatedAt <= toDate.Value);
            }

            return await query
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> VINExistsAsync(string vin)
        {
            return await _context.VehicleImports
                .AnyAsync(v => v.VIN == vin);
        }

        public async Task<List<VehicleImport>> GetVehicleImportsByStatusAsync(string processingStatus)
        {
            return await _context.VehicleImports
                .Include(v => v.BOEDocument)
                .Where(v => v.ProcessingStatus == processingStatus)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();
        }

        public async Task UpdateProcessingStatusAsync(int vehicleImportId, string status, string? errorMessage = null)
        {
            var vehicleImport = await _context.VehicleImports.AsTracking().FirstOrDefaultAsync(v => v.Id == vehicleImportId);
            if (vehicleImport != null)
            {
                vehicleImport.ProcessingStatus = status;
                vehicleImport.ErrorMessage = errorMessage;
                vehicleImport.UpdatedAt = DateTime.UtcNow;

                if (status == "Completed")
                {
                    vehicleImport.ProcessedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                _context.ChangeTracker.Clear();
            }
        }

        public async Task DeleteVehicleImportAsync(int vehicleImportId)
        {
            var vehicleImport = await _context.VehicleImports.AsTracking().FirstOrDefaultAsync(v => v.Id == vehicleImportId);
            if (vehicleImport != null)
            {
                _context.VehicleImports.Remove(vehicleImport);
                await _context.SaveChangesAsync();

                // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                _context.ChangeTracker.Clear();
            }
        }

        public async Task<VehicleImportStatistics> GetVehicleImportStatisticsAsync()
        {
            var stats = new VehicleImportStatistics();

            // Basic counts
            stats.TotalVehicleImports = await _context.VehicleImports.CountAsync();
            stats.DirectVINCount = await _context.VehicleImports.CountAsync(v => v.ImportType == VehicleImportType.DirectVIN);
            stats.VINInContainerCount = await _context.VehicleImports.CountAsync(v => v.ImportType == VehicleImportType.VINInContainer);
            stats.PendingCount = await _context.VehicleImports.CountAsync(v => v.ProcessingStatus == "Pending");
            stats.ProcessedCount = await _context.VehicleImports.CountAsync(v => v.ProcessingStatus == "Completed");
            stats.FailedCount = await _context.VehicleImports.CountAsync(v => v.ProcessingStatus == "Failed");

            // Unique counts
            stats.UniqueVINs = await _context.VehicleImports.Select(v => v.VIN).Distinct().CountAsync();
            stats.UniqueMakes = await _context.VehicleImports
                .Where(v => !string.IsNullOrEmpty(v.Make))
                .Select(v => v.Make)
                .Distinct()
                .CountAsync();

            // Last import date
            stats.LastImportDate = await _context.VehicleImports
                .MaxAsync(v => (DateTime?)v.CreatedAt);

            // Make distribution
            var makeGroups = await _context.VehicleImports
                .Where(v => !string.IsNullOrEmpty(v.Make))
                .GroupBy(v => v.Make)
                .Select(g => new { Make = g.Key!, Count = g.Count() })
                .ToListAsync();

            stats.MakeDistribution = makeGroups.ToDictionary(g => g.Make, g => g.Count);

            // Country distribution
            var countryGroups = await _context.VehicleImports
                .Where(v => !string.IsNullOrEmpty(v.CountryOfOrigin))
                .GroupBy(v => v.CountryOfOrigin)
                .Select(g => new { Country = g.Key!, Count = g.Count() })
                .ToListAsync();

            stats.CountryDistribution = countryGroups.ToDictionary(g => g.Country, g => g.Count);

            return stats;
        }

        public async Task<List<VehicleImport>> GetVehicleImportsByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            return await _context.VehicleImports
                .Include(v => v.BOEDocument)
                .Where(v => v.CreatedAt >= fromDate && v.CreatedAt <= toDate)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();
        }
    }
}
