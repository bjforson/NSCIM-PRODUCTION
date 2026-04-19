using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models.Gateway;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Gateway
{
    public class GlobalSearchService : IGlobalSearchService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<GlobalSearchService> _logger;

        public GlobalSearchService(
            ApplicationDbContext context,
            ILogger<GlobalSearchService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<GlobalSearchResponse> SearchAsync(GlobalSearchRequest request)
        {
            var sw = Stopwatch.StartNew();
            var response = new GlobalSearchResponse
            {
                Query = request.Query
            };

            try
            {
                _logger.LogInformation("Global search for: {Query}", request.Query);

                var results = new List<GatewaySearchResult>();

                // Search in all entity types if none specified
                var entityTypes = request.EntityTypes.Any()
                    ? request.EntityTypes
                    : new List<string> { "Container", "ICUMS", "Vehicle", "Operator" };

                foreach (var entityType in entityTypes)
                {
                    var entityResults = entityType.ToLower() switch
                    {
                        "container" => await SearchContainersAsync(request.Query, request.MaxResults),
                        "icums" => await SearchICUMSAsync(request.Query, request.MaxResults),
                        "vehicle" => await SearchVehiclesAsync(request.Query, request.MaxResults),
                        "operator" => await SearchOperatorsAsync(request.Query, request.MaxResults),
                        _ => new List<GatewaySearchResult>()
                    };

                    results.AddRange(entityResults);
                }

                // Sort by relevance score and apply pagination
                results = results
                    .OrderByDescending(r => r.RelevanceScore)
                    .Skip(request.Skip)
                    .Take(request.MaxResults)
                    .ToList();

                response.Results = results;
                response.TotalResults = results.Count;
                response.ResultsReturned = results.Count;
                response.ResultsByType = results
                    .GroupBy(r => r.EntityType)
                    .ToDictionary(g => g.Key, g => g.Count());

                sw.Stop();
                response.ResponseTimeMs = (int)sw.ElapsedMilliseconds;

                _logger.LogInformation("Global search completed in {Ms}ms, {Results} results",
                    sw.ElapsedMilliseconds, results.Count);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing global search");
                sw.Stop();
                response.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
                return response;
            }
        }

        private async Task<List<GatewaySearchResult>> SearchContainersAsync(string query, int maxResults)
        {
            var results = new List<GatewaySearchResult>();

            try
            {
                // Search ASE scans
                var aseResults = await _context.AseScans
                    .Where(s => s.ContainerNumber != null && EF.Functions.Like(s.ContainerNumber, $"%{query}%"))
                    .Take(maxResults / 2)
                    .Select(s => new GatewaySearchResult
                    {
                        EntityType = "Container",
                        Id = s.ContainerNumber ?? "",
                        PrimaryField = s.ContainerNumber ?? "",
                        SecondaryField = s.ScanTime.ToString("yyyy-MM-dd HH:mm"),
                        RelevanceScore = CalculateRelevance(s.ContainerNumber ?? "", query),
                        Metadata = new Dictionary<string, object>
                        {
                            { "ScannerType", "ASE" },
                            { "InspectionId", s.InspectionId },
                            { "TruckPlate", s.TruckPlate ?? "Unknown" }
                        },
                        MatchedFields = new List<string> { "ContainerNumber" },
                        Preview = $"ASE Scan - {s.ContainerNumber}"
                    })
                    .ToListAsync();

                results.AddRange(aseResults);

                // Search FS6000 scans
                var fs6000Results = await _context.FS6000Scans
                    .Where(s => EF.Functions.Like(s.ContainerNumber, $"%{query}%"))
                    .Take(maxResults / 2)
                    .Select(s => new GatewaySearchResult
                    {
                        EntityType = "Container",
                        Id = s.ContainerNumber,
                        PrimaryField = s.ContainerNumber,
                        SecondaryField = s.ScanTime.ToString("yyyy-MM-dd HH:mm"),
                        RelevanceScore = CalculateRelevance(s.ContainerNumber, query),
                        Metadata = new Dictionary<string, object>
                        {
                            { "ScannerType", "FS6000" },
                            { "FilePath", s.FilePath ?? "Unknown" }
                        },
                        MatchedFields = new List<string> { "ContainerNumber" },
                        Preview = $"FS6000 Scan - {s.ContainerNumber}"
                    })
                    .ToListAsync();

                results.AddRange(fs6000Results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching containers");
            }

            return results;
        }

        private async Task<List<GatewaySearchResult>> SearchICUMSAsync(string query, int maxResults)
        {
            var results = new List<GatewaySearchResult>();

            try
            {
                // ICUMS search would go here - requires IcumDownloadsDbContext
                // Placeholder for now
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching ICUMS");
            }

            return results;
        }

        private async Task<List<GatewaySearchResult>> SearchVehiclesAsync(string query, int maxResults)
        {
            var results = new List<GatewaySearchResult>();

            try
            {
                // Vehicle search would go here - requires VehicleImports DbSet
                // Placeholder for now
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching vehicles");
            }

            return results;
        }

        private async Task<List<GatewaySearchResult>> SearchOperatorsAsync(string query, int maxResults)
        {
            var results = new List<GatewaySearchResult>();

            try
            {
                // Operator search would go here - ASE doesn't have OperatorId currently
                // Placeholder for now
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching operators");
            }

            return results;
        }

        private double CalculateRelevance(string text, string query)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
                return 0.0;

            text = text.ToLower();
            query = query.ToLower();

            // Exact match gets highest score
            if (text == query)
                return 1.0;

            // Starts with query gets high score
            if (text.StartsWith(query))
                return 0.9;

            // Contains query gets medium score
            if (text.Contains(query))
                return 0.7;

            // Calculate simple similarity based on common characters
            var commonChars = query.Count(c => text.Contains(c));
            return (double)commonChars / query.Length * 0.5;
        }
    }
}

