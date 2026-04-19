using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.LooseCargo
{
    /// <summary>
    /// Service for loose cargo business logic
    /// </summary>
    public class LooseCargoService : ILooseCargoService
    {
        private readonly ILooseCargoRepository _repository;
        private readonly IcumDownloadsDbContext _context;
        private readonly ILogger<LooseCargoService> _logger;

        public LooseCargoService(
            ILooseCargoRepository repository,
            IcumDownloadsDbContext context,
            ILogger<LooseCargoService> logger)
        {
            _repository = repository;
            _context = context;
            _logger = logger;
        }

        public async Task<LooseCargoSearchResponse> SearchAsync(LooseCargoSearchRequest request)
        {
            try
            {
                _logger.LogInformation("Searching loose cargo records with filters: {Request}", request);

                // Validate request
                if (request.PageNumber < 1)
                    request.PageNumber = 1;

                if (request.PageSize < 1 || request.PageSize > 1000)
                    request.PageSize = 100;

                // Get records from repository
                var (records, totalCount) = await _repository.GetLooseCargoRecordsAsync(
                    clearanceType: request.ClearanceType,
                    crmsLevel: request.CrmsLevel,
                    searchTerm: request.SearchTerm,
                    pageNumber: request.PageNumber,
                    pageSize: request.PageSize,
                    sortBy: request.SortBy,
                    sortDescending: request.SortDescending);

                // Apply additional date filters if provided
                if (request.FromDate.HasValue || request.ToDate.HasValue)
                {
                    var filteredRecords = records.Where(r =>
                    {
                        if (request.FromDate.HasValue && r.CreatedAt < request.FromDate.Value)
                            return false;

                        if (request.ToDate.HasValue && r.CreatedAt > request.ToDate.Value)
                            return false;

                        return true;
                    }).ToList();

                    // Adjust total count if date filtering was applied
                    if (request.FromDate.HasValue || request.ToDate.HasValue)
                    {
                        var (allRecords, _) = await _repository.GetLooseCargoRecordsAsync(
                            clearanceType: request.ClearanceType,
                            crmsLevel: request.CrmsLevel,
                            searchTerm: request.SearchTerm,
                            pageNumber: 1,
                            pageSize: int.MaxValue);

                        totalCount = allRecords.Count(r =>
                        {
                            if (request.FromDate.HasValue && r.CreatedAt < request.FromDate.Value)
                                return false;

                            if (request.ToDate.HasValue && r.CreatedAt > request.ToDate.Value)
                                return false;

                            return true;
                        });
                    }

                    records = filteredRecords;
                }

                // Apply additional filters
                if (!string.IsNullOrEmpty(request.CountryOfOrigin))
                {
                    records = records.Where(r =>
                        r.CountryOfOrigin != null &&
                        r.CountryOfOrigin.Contains(request.CountryOfOrigin, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (!string.IsNullOrEmpty(request.RegimeCode))
                {
                    records = records.Where(r => r.RegimeCode == request.RegimeCode).ToList();
                }

                return new LooseCargoSearchResponse
                {
                    Records = records,
                    TotalCount = totalCount,
                    PageNumber = request.PageNumber,
                    PageSize = request.PageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching loose cargo records");
                throw;
            }
        }

        public async Task<LooseCargoStatistics> GetStatisticsAsync()
        {
            try
            {
                return await _repository.GetStatisticsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting loose cargo statistics");
                throw;
            }
        }

        public async Task<LooseCargoDetailDto?> GetDetailAsync(int id)
        {
            try
            {
                var document = await _repository.GetByIdAsync(id);
                if (document == null)
                    return null;

                var manifestItems = await _repository.GetManifestItemsAsync(id);

                var sourceFile = await _context.DownloadedFiles
                    .FirstOrDefaultAsync(f => f.Id == document.DownloadedFileId);

                return new LooseCargoDetailDto
                {
                    Document = document,
                    ManifestItems = manifestItems,
                    SourceFile = sourceFile
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting loose cargo detail for ID: {Id}", id);
                throw;
            }
        }

        public async Task<LooseCargoDetailDto?> GetDetailByDeclarationNumberAsync(string declarationNumber)
        {
            try
            {
                var document = await _repository.GetByDeclarationNumberAsync(declarationNumber);
                if (document == null)
                    return null;

                var manifestItems = await _repository.GetManifestItemsAsync(document.Id);

                var sourceFile = await _context.DownloadedFiles
                    .FirstOrDefaultAsync(f => f.Id == document.DownloadedFileId);

                return new LooseCargoDetailDto
                {
                    Document = document,
                    ManifestItems = manifestItems,
                    SourceFile = sourceFile
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting loose cargo detail for declaration number: {DeclarationNumber}",
                    declarationNumber);
                throw;
            }
        }

        public async Task<List<BOEDocument>> GetRecentRecordsAsync(int days = 7)
        {
            try
            {
                return await _repository.GetRecentRecordsAsync(days);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent loose cargo records");
                throw;
            }
        }

        public Task<(bool isValid, List<string> errors)> ValidateRecordAsync(BOEDocument document)
        {
            var errors = new List<string>();

            try
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(document.DeclarationNumber))
                {
                    errors.Add("Declaration number is required");
                }

                if (string.IsNullOrWhiteSpace(document.ClearanceType))
                {
                    errors.Add("Clearance type is required");
                }

                // Validate clearance type values
                if (!string.IsNullOrWhiteSpace(document.ClearanceType) &&
                    !new[] { "IM", "EX", "CMR" }.Contains(document.ClearanceType))
                {
                    errors.Add($"Invalid clearance type: {document.ClearanceType}. Must be IM, EX, or CMR");
                }

                // Validate CRMS level values
                if (!string.IsNullOrWhiteSpace(document.CrmsLevel) &&
                    !new[] { "Red", "Yellow", "Green" }.Contains(document.CrmsLevel))
                {
                    errors.Add($"Invalid CRMS level: {document.CrmsLevel}. Must be Red, Yellow, or Green");
                }

                // Validate that it's actually loose cargo (no container number)
                if (!string.IsNullOrWhiteSpace(document.ContainerNumber) &&
                    document.ContainerNumber != "N/A")
                {
                    errors.Add("Record has container number - this is not loose cargo");
                }

                return Task.FromResult<(bool isValid, List<string> errors)>((errors.Count == 0, errors));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating loose cargo record");
                errors.Add($"Validation error: {ex.Message}");
                return Task.FromResult<(bool isValid, List<string> errors)>((false, errors));
            }
        }
    }
}

