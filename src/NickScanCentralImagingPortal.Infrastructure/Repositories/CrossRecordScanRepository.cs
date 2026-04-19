using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class CrossRecordScanRepository : ICrossRecordScanRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CrossRecordScanRepository> _logger;

        public CrossRecordScanRepository(ApplicationDbContext context, ILogger<CrossRecordScanRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<CrossRecordScan?> GetByIdAsync(int id)
        {
            return await _context.CrossRecordScans
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<CrossRecordScan?> GetByContainerAsync(string containerNumber)
        {
            return await _context.CrossRecordScans
                .AsNoTracking()
                .Where(s => s.Container1 == containerNumber || s.Container2 == containerNumber)
                .OrderByDescending(s => s.ScanDateTime)
                .FirstOrDefaultAsync();
        }

        public async Task<CrossRecordScan?> FindByContainerPairAsync(string container1, string container2)
        {
            return await _context.CrossRecordScans
                .AsNoTracking()
                .Where(crs =>
                    (crs.Container1 == container1 && crs.Container2 == container2) ||
                    (crs.Container1 == container2 && crs.Container2 == container1))
                .FirstOrDefaultAsync();
        }

        public async Task<(List<CrossRecordScan> Items, int TotalCount)> GetPagedListAsync(
            int page = 1, int pageSize = 50,
            string? severity = null, string? scannerType = null,
            DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = _context.CrossRecordScans.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(severity))
                query = query.Where(s => s.Severity == severity);

            if (!string.IsNullOrEmpty(scannerType))
                query = query.Where(s => s.ScannerType == scannerType);

            if (startDate.HasValue)
            {
                var start = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(s => s.ScanDateTime >= start);
            }

            if (endDate.HasValue)
            {
                var end = DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Utc);
                query = query.Where(s => s.ScanDateTime < end);
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(s => s.ScanDateTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<CrossRecordAnalytics> GetAnalyticsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = _context.CrossRecordScans.AsNoTracking().AsQueryable();

            if (startDate.HasValue)
            {
                var start = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc);
                query = query.Where(s => s.ScanDateTime >= start);
            }

            if (endDate.HasValue)
            {
                var end = DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Utc);
                query = query.Where(s => s.ScanDateTime < end);
            }

            var totalCount = await query.CountAsync();
            var differentImportersCount = await query.CountAsync(s => s.CrossRecordType == "DifferentImporters");
            var differentRiskLevelsCount = await query.CountAsync(s => s.CrossRecordType == "DifferentRiskLevels");
            var differentClearanceTypesCount = await query.CountAsync(s => s.CrossRecordType == "DifferentClearanceTypes");
            var differentBOEsOnlyCount = await query.CountAsync(s => s.CrossRecordType == "DifferentBOEs");
            var fs6000Count = await query.CountAsync(s => s.ScannerType == "FS6000");
            var aseCount = await query.CountAsync(s => s.ScannerType == "ASE");

            var dailyBreakdown = await query
                .GroupBy(s => s.ScanDateTime.Date)
                .Select(g => new DailyBreakdown { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date)
                .ToListAsync();

            var records = await query
                .OrderByDescending(s => s.ScanDateTime)
                .Take(1000)
                .ToListAsync();

            return new CrossRecordAnalytics
            {
                TotalCrossRecordScans = totalCount,
                DifferentImportersCount = differentImportersCount,
                DifferentRiskLevelsCount = differentRiskLevelsCount,
                DifferentClearanceTypesCount = differentClearanceTypesCount,
                DifferentBOEsOnlyCount = differentBOEsOnlyCount,
                FS6000Count = fs6000Count,
                ASECount = aseCount,
                DailyBreakdown = dailyBreakdown,
                Records = records.Select(s => new CrossRecordScanDto
                {
                    Id = s.Id,
                    OriginalScanRecord = s.OriginalScanRecord,
                    ScannerRecordId = s.ScannerRecordId,
                    ScannerType = s.ScannerType,
                    ScanDateTime = s.ScanDateTime,
                    Container1 = s.Container1,
                    Container1_BOE = s.Container1_BOE,
                    Container1_Consignee = s.Container1_Consignee,
                    Container1_CRMS = s.Container1_CRMS,
                    Container1_ClearanceType = s.Container1_ClearanceType,
                    Container1_MasterBL = s.Container1_MasterBL,
                    Container1_Rotation = s.Container1_Rotation,
                    Container2 = s.Container2,
                    Container2_BOE = s.Container2_BOE,
                    Container2_Consignee = s.Container2_Consignee,
                    Container2_CRMS = s.Container2_CRMS,
                    Container2_ClearanceType = s.Container2_ClearanceType,
                    Container2_MasterBL = s.Container2_MasterBL,
                    Container2_Rotation = s.Container2_Rotation,
                    CrossRecordType = s.CrossRecordType,
                    Severity = s.Severity,
                    RequiresReview = s.RequiresReview,
                    SameDeclaration = s.SameDeclaration,
                    SameConsignee = s.SameConsignee,
                    SameMasterBL = s.SameMasterBL,
                    SameRotation = s.SameRotation,
                    SameCRMS = s.SameCRMS,
                    SameClearanceType = s.SameClearanceType,
                    ReviewStatus = s.ReviewStatus,
                    ReviewedAt = s.ReviewedAt,
                    ReviewedBy = s.ReviewedBy
                }).ToList()
            };
        }

        public async Task MarkAsReviewedAsync(int id, string reviewedBy, string? notes = null)
        {
            var scan = await _context.CrossRecordScans.AsTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (scan == null)
                throw new KeyNotFoundException($"Cross-record scan with ID {id} not found");

            scan.ReviewStatus = "Reviewed";
            scan.ReviewedAt = DateTime.UtcNow;
            scan.ReviewedBy = reviewedBy;
            scan.ReviewNotes = notes;

            await _context.SaveChangesAsync();
        }
    }
}
