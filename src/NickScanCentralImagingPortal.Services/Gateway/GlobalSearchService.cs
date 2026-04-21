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
        private readonly IcumDownloadsDbContext _downloadsDb;
        private readonly ILogger<GlobalSearchService> _logger;

        public GlobalSearchService(
            ApplicationDbContext context,
            IcumDownloadsDbContext downloadsDb,
            ILogger<GlobalSearchService> logger)
        {
            _context = context;
            _downloadsDb = downloadsDb;
            _logger = logger;
        }

        public async Task<GlobalSearchResponse> SearchAsync(GlobalSearchRequest request)
        {
            var sw = Stopwatch.StartNew();
            var response = new GlobalSearchResponse
            {
                Query = request.Query
            };

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
            var total = results.Count;
            results = results
                .OrderByDescending(r => r.RelevanceScore)
                .Skip(request.Skip)
                .Take(request.MaxResults)
                .ToList();

            response.Results = results;
            response.TotalResults = total;
            response.ResultsReturned = results.Count;
            response.ResultsByType = results
                .GroupBy(r => r.EntityType)
                .ToDictionary(g => g.Key, g => g.Count());

            sw.Stop();
            response.ResponseTimeMs = (int)sw.ElapsedMilliseconds;

            _logger.LogInformation("Global search completed in {Ms}ms, {Results} results (total matched {Total})",
                sw.ElapsedMilliseconds, results.Count, total);

            return response;
        }

        private async Task<List<GatewaySearchResult>> SearchContainersAsync(string query, int maxResults)
        {
            var results = new List<GatewaySearchResult>();
            var like = $"%{query}%";
            var perSource = Math.Max(1, maxResults / 3);

            // ASE scans — ContainerNumber or TruckPlate
            var aseResults = await _context.AseScans
                .Where(s =>
                    (s.ContainerNumber != null && EF.Functions.Like(s.ContainerNumber, like)) ||
                    (s.TruckPlate != null && EF.Functions.Like(s.TruckPlate, like)))
                .OrderByDescending(s => s.ScanTime)
                .Take(perSource)
                .Select(s => new GatewaySearchResult
                {
                    EntityType = "Container",
                    Id = s.ContainerNumber ?? s.InspectionUuid,
                    PrimaryField = s.ContainerNumber ?? "(no container#)",
                    SecondaryField = s.ScanTime.ToString("yyyy-MM-dd HH:mm"),
                    RelevanceScore = CalculateRelevance(s.ContainerNumber ?? s.TruckPlate ?? "", query),
                    Metadata = new Dictionary<string, object>
                    {
                        { "ScannerType", "ASE" },
                        { "InspectionId", s.InspectionId },
                        { "TruckPlate", s.TruckPlate ?? "Unknown" }
                    },
                    MatchedFields = new List<string> { "ContainerNumber", "TruckPlate" },
                    Preview = $"ASE Scan — {s.ContainerNumber} ({s.TruckPlate})"
                })
                .ToListAsync();
            results.AddRange(aseResults);

            // FS6000 scans — ContainerNumber, TruckPlate, PicNumber
            var fs6000Results = await _context.FS6000Scans
                .Where(s =>
                    EF.Functions.Like(s.ContainerNumber, like) ||
                    (s.TruckPlate != null && EF.Functions.Like(s.TruckPlate, like)) ||
                    EF.Functions.Like(s.PicNumber, like))
                .OrderByDescending(s => s.ScanTime)
                .Take(perSource)
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
                        { "PicNumber", s.PicNumber },
                        { "TruckPlate", s.TruckPlate ?? "Unknown" },
                        { "FilePath", s.FilePath ?? "Unknown" }
                    },
                    MatchedFields = new List<string> { "ContainerNumber", "TruckPlate", "PicNumber" },
                    Preview = $"FS6000 Scan — {s.ContainerNumber} ({s.TruckPlate})"
                })
                .ToListAsync();
            results.AddRange(fs6000Results);

            // BOE documents — ContainerNumber, SealNumber, TruckPlateNumber (treated as Container matches)
            var boeResults = await _downloadsDb.BOEDocuments
                .Where(b =>
                    EF.Functions.Like(b.ContainerNumber, like) ||
                    (b.SealNumber != null && EF.Functions.Like(b.SealNumber, like)) ||
                    (b.TruckPlateNumber != null && EF.Functions.Like(b.TruckPlateNumber, like)))
                .OrderByDescending(b => b.Id)
                .Take(perSource)
                .Select(b => new GatewaySearchResult
                {
                    EntityType = "Container",
                    Id = b.ContainerNumber,
                    PrimaryField = b.ContainerNumber,
                    SecondaryField = b.DeclarationNumber ?? "",
                    RelevanceScore = CalculateRelevance(b.ContainerNumber, query),
                    Metadata = new Dictionary<string, object>
                    {
                        { "Source", "BOE" },
                        { "Declaration", b.DeclarationNumber ?? "" },
                        { "SealNumber", b.SealNumber ?? "" },
                        { "TruckPlate", b.TruckPlateNumber ?? "" }
                    },
                    MatchedFields = new List<string> { "ContainerNumber", "SealNumber", "TruckPlateNumber" },
                    Preview = $"BOE Container — {b.ContainerNumber} (Decl {b.DeclarationNumber})"
                })
                .ToListAsync();
            results.AddRange(boeResults);

            return results;
        }

        private async Task<List<GatewaySearchResult>> SearchICUMSAsync(string query, int maxResults)
        {
            var like = $"%{query}%";

            // BOE-level ICUMS matches — BL / Declaration / Rotation / HouseBL / MasterBL
            var results = await _downloadsDb.BOEDocuments
                .Where(b =>
                    (b.BlNumber != null && EF.Functions.Like(b.BlNumber, like)) ||
                    (b.MasterBlNumber != null && EF.Functions.Like(b.MasterBlNumber, like)) ||
                    (b.HouseBl != null && EF.Functions.Like(b.HouseBl, like)) ||
                    (b.DeclarationNumber != null && EF.Functions.Like(b.DeclarationNumber, like)) ||
                    (b.RotationNumber != null && EF.Functions.Like(b.RotationNumber, like)))
                .OrderByDescending(b => b.Id)
                .Take(maxResults)
                .Select(b => new GatewaySearchResult
                {
                    EntityType = "ICUMS",
                    Id = b.Id.ToString(),
                    PrimaryField = b.DeclarationNumber ?? b.BlNumber ?? b.ContainerNumber,
                    SecondaryField = b.ContainerNumber,
                    RelevanceScore = CalculateRelevance(
                        b.DeclarationNumber ?? b.BlNumber ?? b.RotationNumber ?? "", query),
                    Metadata = new Dictionary<string, object>
                    {
                        { "Declaration", b.DeclarationNumber ?? "" },
                        { "BL", b.BlNumber ?? "" },
                        { "MasterBL", b.MasterBlNumber ?? "" },
                        { "HouseBL", b.HouseBl ?? "" },
                        { "Rotation", b.RotationNumber ?? "" },
                        { "Regime", b.RegimeCode ?? "" }
                    },
                    MatchedFields = new List<string>
                    {
                        "BlNumber", "MasterBlNumber", "HouseBl", "DeclarationNumber", "RotationNumber"
                    },
                    Preview = $"BOE Decl {b.DeclarationNumber} · BL {b.BlNumber} · {b.ContainerNumber}"
                })
                .ToListAsync();

            return results;
        }

        private async Task<List<GatewaySearchResult>> SearchVehiclesAsync(string query, int maxResults)
        {
            var like = $"%{query}%";

            var results = await _downloadsDb.VehicleImports
                .Where(v =>
                    EF.Functions.Like(v.VIN, like) ||
                    (v.ChassisNumber != null && EF.Functions.Like(v.ChassisNumber, like)) ||
                    (v.DeclarationNumber != null && EF.Functions.Like(v.DeclarationNumber, like)) ||
                    (v.BLNumber != null && EF.Functions.Like(v.BLNumber, like)) ||
                    (v.ContainerNumber != null && EF.Functions.Like(v.ContainerNumber, like)))
                .OrderByDescending(v => v.Id)
                .Take(maxResults)
                .Select(v => new GatewaySearchResult
                {
                    EntityType = "Vehicle",
                    Id = v.VIN,
                    PrimaryField = v.VIN,
                    SecondaryField = v.VehicleType ?? "",
                    RelevanceScore = CalculateRelevance(v.VIN, query),
                    Metadata = new Dictionary<string, object>
                    {
                        { "Make", v.Make ?? "" },
                        { "Model", v.Model ?? "" },
                        { "Year", v.VehicleYear ?? "" },
                        { "Chassis", v.ChassisNumber ?? "" },
                        { "Declaration", v.DeclarationNumber ?? "" },
                        { "BL", v.BLNumber ?? "" },
                        { "Container", v.ContainerNumber ?? "" }
                    },
                    MatchedFields = new List<string>
                    {
                        "VIN", "ChassisNumber", "DeclarationNumber", "BLNumber", "ContainerNumber"
                    },
                    Preview = $"Vehicle {v.VIN} — {v.VehicleType}"
                })
                .ToListAsync();

            return results;
        }

        private async Task<List<GatewaySearchResult>> SearchOperatorsAsync(string query, int maxResults)
        {
            var results = new List<GatewaySearchResult>();
            var like = $"%{query}%";
            var perSource = Math.Max(1, maxResults / 2);

            // Users
            var userResults = await _context.Users
                .Where(u =>
                    EF.Functions.Like(u.Username, like) ||
                    EF.Functions.Like(u.Email, like) ||
                    EF.Functions.Like(u.FirstName, like) ||
                    EF.Functions.Like(u.LastName, like) ||
                    (u.UserNumber != null && EF.Functions.Like(u.UserNumber, like)))
                .Take(perSource)
                .Select(u => new GatewaySearchResult
                {
                    EntityType = "Operator",
                    Id = u.Id.ToString(),
                    PrimaryField = u.Username,
                    SecondaryField = (u.FirstName + " " + u.LastName).Trim(),
                    RelevanceScore = CalculateRelevance(u.Username, query),
                    Metadata = new Dictionary<string, object>
                    {
                        { "Source", "User" },
                        { "Email", u.Email },
                        { "Department", u.Department ?? "" },
                        { "UserNumber", u.UserNumber ?? "" }
                    },
                    MatchedFields = new List<string>
                    {
                        "Username", "Email", "FirstName", "LastName", "UserNumber"
                    },
                    Preview = $"User {u.Username} — {u.FirstName} {u.LastName}"
                })
                .ToListAsync();
            results.AddRange(userResults);

            // Distinct FS6000 operator IDs
            var scannerOps = await _context.FS6000Scans
                .Where(s => s.OperatorId != null && EF.Functions.Like(s.OperatorId, like))
                .GroupBy(s => s.OperatorId)
                .Select(g => new
                {
                    OperatorId = g.Key!,
                    ScanCount = g.Count(),
                    LastScan = g.Max(x => x.ScanTime)
                })
                .OrderByDescending(x => x.LastScan)
                .Take(perSource)
                .ToListAsync();

            foreach (var op in scannerOps)
            {
                results.Add(new GatewaySearchResult
                {
                    EntityType = "Operator",
                    Id = op.OperatorId,
                    PrimaryField = op.OperatorId,
                    SecondaryField = $"Last scan {op.LastScan:yyyy-MM-dd HH:mm}",
                    RelevanceScore = CalculateRelevance(op.OperatorId, query),
                    Metadata = new Dictionary<string, object>
                    {
                        { "Source", "FS6000" },
                        { "ScanCount", op.ScanCount }
                    },
                    MatchedFields = new List<string> { "OperatorId" },
                    Preview = $"FS6000 Operator {op.OperatorId} — {op.ScanCount} scans"
                });
            }

            return results;
        }

        private static double CalculateRelevance(string text, string query)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
                return 0.0;

            text = text.ToLower();
            query = query.ToLower();

            if (text == query) return 1.0;
            if (text.StartsWith(query)) return 0.9;
            if (text.Contains(query)) return 0.7;

            var commonChars = query.Count(c => text.Contains(c));
            return (double)commonChars / query.Length * 0.5;
        }
    }
}
