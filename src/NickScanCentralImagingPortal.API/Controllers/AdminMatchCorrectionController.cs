using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Admin tool for inspecting and correcting wrong image-to-BOE matches.
    ///
    /// Backs the /validation/match-corrections page. Designed for the recovery
    /// case (something went wrong, an admin needs to reset it) rather than the
    /// happy path (which the matching pipeline handles automatically).
    ///
    /// Permission: gated behind the existing <c>"AdminOnly"</c> policy
    /// registered in Program.cs, matching every other admin controller
    /// (AccessReview, Audit, DatabaseAdmin, Debug, Diagnostics,
    /// ErrorInvestigation). The finer-grained
    /// <see cref="Permissions.PagesValidationMatchCorrections"/> constant is
    /// still used for the Blazor page's nav-visibility check, but attaching
    /// it directly as an API policy name fails at runtime because the
    /// DynamicAuthorizationPolicyProvider only recognises policies
    /// prefixed with "Permission:" — see 1.10.1 CHANGELOG entry.
    ///
    /// Endpoints
    /// ---------
    ///   GET    /api/admin/match-corrections                 list flags (filterable)
    ///   GET    /api/admin/match-corrections/{containerNumber}/detail
    ///                                                       container + BOE side-by-side
    ///   POST   /api/admin/match-corrections/unmatch         remove ContainerBOERelation, reset completeness
    ///   POST   /api/admin/match-corrections/rematch         deactivate old, create new relation
    ///   POST   /api/admin/match-corrections/flag            manual admin flag
    ///   POST   /api/admin/match-corrections/{flagId}/resolve   set resolution + audit
    ///
    /// Every mutating endpoint:
    ///   1. Logs to FixAuditLogs (existing audit table)
    ///   2. Updates any open MatchQualityFlag rows for the affected container
    ///   3. Returns 200 with the updated state
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/admin/match-corrections")]
    public class AdminMatchCorrectionController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IcumDownloadsDbContext _icumDb;
        private readonly ILogger<AdminMatchCorrectionController> _logger;

        public AdminMatchCorrectionController(
            ApplicationDbContext context,
            IcumDownloadsDbContext icumDb,
            ILogger<AdminMatchCorrectionController> logger)
        {
            _context = context;
            _icumDb = icumDb;
            _logger = logger;
        }

        // ─── Read endpoints ────────────────────────────────────────────────────

        /// <summary>
        /// List match-quality flags. Defaults to unresolved + Critical first.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<MatchFlagListResponse>> ListFlags(
            [FromQuery] bool includeResolved = false,
            [FromQuery] string? flagType = null,
            [FromQuery] string? severity = null,
            [FromQuery] string? containerSearch = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 500) pageSize = 50;

            var query = _context.MatchQualityFlags.AsNoTracking().AsQueryable();

            if (!includeResolved)
            {
                query = query.Where(f => !f.IsResolved);
            }

            if (!string.IsNullOrWhiteSpace(flagType))
            {
                query = query.Where(f => f.FlagType == flagType);
            }

            if (!string.IsNullOrWhiteSpace(severity))
            {
                query = query.Where(f => f.Severity == severity);
            }

            if (!string.IsNullOrWhiteSpace(containerSearch))
            {
                var needle = containerSearch.Trim().ToUpper();
                query = query.Where(f => f.ContainerNumber.ToUpper().Contains(needle));
            }

            var total = await query.CountAsync();

            var rows = await query
                // Critical first, then most recent.
                .OrderBy(f => f.IsResolved)
                .ThenByDescending(f => f.Severity == "Critical")
                .ThenByDescending(f => f.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new MatchFlagDto
                {
                    Id = f.Id,
                    ContainerNumber = f.ContainerNumber,
                    ScannerType = f.ScannerType,
                    BOEDocumentId = f.BOEDocumentId,
                    FlagType = f.FlagType,
                    Severity = f.Severity,
                    Description = f.Description,
                    IsResolved = f.IsResolved,
                    Resolution = f.Resolution,
                    ResolvedBy = f.ResolvedBy,
                    ResolvedAt = f.ResolvedAt,
                    CreatedAtUtc = f.CreatedAtUtc,
                })
                .ToListAsync();

            return Ok(new MatchFlagListResponse
            {
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                Rows = rows,
            });
        }

        /// <summary>
        /// Side-by-side detail for a single container: completeness state, active
        /// BOE relation, the matched BOE's manifest summary, and any flags.
        /// </summary>
        [HttpGet("{containerNumber}/detail")]
        public async Task<ActionResult<ContainerMatchDetailDto>> GetDetail(string containerNumber)
        {
            if (string.IsNullOrWhiteSpace(containerNumber))
            {
                return BadRequest(new { error = "containerNumber is required" });
            }

            var status = await _context.ContainerCompletenessStatuses
                .AsNoTracking()
                .Where(s => s.ContainerNumber == containerNumber)
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefaultAsync();

            var relations = await _context.ContainerBOERelations
                .AsNoTracking()
                .Where(r => r.ContainerNumber == containerNumber)
                .OrderByDescending(r => r.LastValidatedAt)
                .ToListAsync();

            var flags = await _context.MatchQualityFlags
                .AsNoTracking()
                .Where(f => f.ContainerNumber == containerNumber)
                .OrderByDescending(f => f.CreatedAtUtc)
                .Select(f => new MatchFlagDto
                {
                    Id = f.Id,
                    ContainerNumber = f.ContainerNumber,
                    ScannerType = f.ScannerType,
                    BOEDocumentId = f.BOEDocumentId,
                    FlagType = f.FlagType,
                    Severity = f.Severity,
                    Description = f.Description,
                    IsResolved = f.IsResolved,
                    Resolution = f.Resolution,
                    ResolvedBy = f.ResolvedBy,
                    ResolvedAt = f.ResolvedAt,
                    CreatedAtUtc = f.CreatedAtUtc,
                })
                .ToListAsync();

            BoeSummaryDto? matchedBoe = null;
            if (status?.BOEDocumentId is int boeId && boeId > 0)
            {
                matchedBoe = await LoadBoeSummaryAsync(boeId);
            }

            return Ok(new ContainerMatchDetailDto
            {
                ContainerNumber = containerNumber,
                Status = status,
                Relations = relations,
                Flags = flags,
                MatchedBoe = matchedBoe,
            });
        }

        // ─── Mutating endpoints ────────────────────────────────────────────────

        /// <summary>
        /// Unmatch a container: deactivate every active ContainerBOERelation,
        /// clear the matched BOE on the completeness row, and reset status to
        /// "Missing" so the matching pipeline can pick it up cleanly next pass.
        /// </summary>
        [HttpPost("unmatch")]
        public async Task<ActionResult<object>> Unmatch([FromBody] UnmatchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ContainerNumber))
            {
                return BadRequest(new { error = "ContainerNumber is required" });
            }

            var actor = User.Identity?.Name ?? "admin";
            var now = DateTime.UtcNow;

            await using var tx = await _context.Database.BeginTransactionAsync();

            // 1. Deactivate every active relation for the container.
            var relations = await _context.ContainerBOERelations
                .Where(r => r.ContainerNumber == request.ContainerNumber && r.IsActive)
                .ToListAsync();

            foreach (var rel in relations)
            {
                rel.IsActive = false;
                rel.Notes = $"{rel.Notes} | Unmatched by {actor} at {now:O}: {request.Reason}".Trim(' ', '|');
                rel.LastValidatedAt = now;
            }

            // 2. Reset completeness rows so the matching pipeline doesn't skip them.
            var statuses = await _context.ContainerCompletenessStatuses
                .Where(s => s.ContainerNumber == request.ContainerNumber)
                .ToListAsync();

            foreach (var s in statuses)
            {
                s.HasICUMSData = false;
                s.BOEDocumentId = null;
                s.ClearanceType = null;
                s.Status = "Missing";
                s.ICUMSDataCompleteness = 0;
                s.OverallCompleteness = (s.ScannerDataCompleteness + 0 + s.ImageDataCompleteness) / 3;
                s.UpdatedAt = now;
                s.ErrorMessage = $"Unmatched by {actor}: {request.Reason}";
            }

            // 3. Resolve any open flags (this fix actively addresses them).
            var openFlags = await _context.MatchQualityFlags
                .Where(f => f.ContainerNumber == request.ContainerNumber && !f.IsResolved)
                .ToListAsync();

            foreach (var f in openFlags)
            {
                f.IsResolved = true;
                f.Resolution = "Unmatched";
                f.ResolvedBy = actor;
                f.ResolvedAt = now;
                f.ResolutionNotes = request.Reason;
            }

            // 4. If no open flag exists for this container, create one to record
            //    the admin action so the audit trail is queryable from the same
            //    table the UI lists. Resolved immediately because the action is
            //    its own resolution.
            if (!openFlags.Any())
            {
                _context.MatchQualityFlags.Add(new MatchQualityFlag
                {
                    ContainerNumber = request.ContainerNumber,
                    ScannerType = statuses.FirstOrDefault()?.ScannerType,
                    FlagType = "ManualFlag",
                    Severity = "Warning",
                    Description = $"Admin unmatch: {relations.Count} relation(s) deactivated, {statuses.Count} completeness row(s) reset.",
                    IsResolved = true,
                    Resolution = "Unmatched",
                    ResolvedBy = actor,
                    ResolvedAt = now,
                    ResolutionNotes = request.Reason,
                    CreatedAtUtc = now,
                });
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Match unmatch: {Container} by {Actor} — {RelCount} relation(s), {StatusCount} status row(s), {FlagCount} flag(s) resolved",
                request.ContainerNumber, actor, relations.Count, statuses.Count, openFlags.Count);

            return Ok(new
            {
                success = true,
                relationsDeactivated = relations.Count,
                statusesReset = statuses.Count,
                flagsResolved = openFlags.Count,
            });
        }

        /// <summary>
        /// Rematch a container: deactivate the current active relation and
        /// create a new one against the chosen BOEDocumentId. The completeness
        /// row gets the new BOE id; the matching pipeline will recompute the
        /// rest on its next pass.
        /// </summary>
        [HttpPost("rematch")]
        public async Task<ActionResult<object>> Rematch([FromBody] RematchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ContainerNumber) || request.NewBOEDocumentId <= 0)
            {
                return BadRequest(new { error = "ContainerNumber and NewBOEDocumentId are required" });
            }

            // Confirm the target BOE exists in ICUMS before mutating anything.
            var targetBoe = await _icumDb.BOEDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == request.NewBOEDocumentId);

            if (targetBoe == null)
            {
                return NotFound(new { error = $"BOEDocumentId {request.NewBOEDocumentId} not found in ICUMS" });
            }

            var actor = User.Identity?.Name ?? "admin";
            var now = DateTime.UtcNow;

            await using var tx = await _context.Database.BeginTransactionAsync();

            // 1. Deactivate existing active relations.
            var existing = await _context.ContainerBOERelations
                .Where(r => r.ContainerNumber == request.ContainerNumber && r.IsActive)
                .ToListAsync();

            foreach (var rel in existing)
            {
                rel.IsActive = false;
                rel.Notes = $"{rel.Notes} | Rematched by {actor} at {now:O} -> BOE {request.NewBOEDocumentId}: {request.Reason}".Trim(' ', '|');
                rel.LastValidatedAt = now;
            }

            // 2. Create the new relation. ScannerDataId left at 0 because this
            //    is an admin override — the matching pipeline will reconcile it
            //    on its next pass via the completeness row.
            var newRelation = new ContainerBOERelation
            {
                ContainerNumber = request.ContainerNumber,
                ScannerDataId = 0,
                ScannerType = request.ScannerType ?? string.Empty,
                ICUMSBOEId = request.NewBOEDocumentId,
                RelationType = request.ScannerType ?? "ADMIN",
                CreatedAt = now,
                LastValidatedAt = now,
                Notes = $"Created by admin rematch ({actor}): {request.Reason}",
                IsActive = true,
            };
            _context.ContainerBOERelations.Add(newRelation);

            // 3. Update completeness row(s) so the new BOE is the live link.
            var statuses = await _context.ContainerCompletenessStatuses
                .Where(s => s.ContainerNumber == request.ContainerNumber)
                .ToListAsync();

            foreach (var s in statuses)
            {
                s.BOEDocumentId = request.NewBOEDocumentId;
                s.ClearanceType = targetBoe.ClearanceType;
                s.HasICUMSData = true;
                s.ICUMSDataCompleteness = 100;
                s.Status = "Complete";
                s.UpdatedAt = now;
                s.ErrorMessage = $"Rematched by {actor}: {request.Reason}";
            }

            // 4. Resolve any open flags.
            var openFlags = await _context.MatchQualityFlags
                .Where(f => f.ContainerNumber == request.ContainerNumber && !f.IsResolved)
                .ToListAsync();

            foreach (var f in openFlags)
            {
                f.IsResolved = true;
                f.Resolution = "Rematched";
                f.ResolvedBy = actor;
                f.ResolvedAt = now;
                f.ResolutionNotes = $"Rematched to BOEDocumentId={request.NewBOEDocumentId}. {request.Reason}";
            }

            // 5. If no open flag exists, record the admin action so it appears
            //    in the audit list alongside other flag history.
            if (!openFlags.Any())
            {
                _context.MatchQualityFlags.Add(new MatchQualityFlag
                {
                    ContainerNumber = request.ContainerNumber,
                    ScannerType = request.ScannerType,
                    BOEDocumentId = request.NewBOEDocumentId,
                    FlagType = "ManualFlag",
                    Severity = "Warning",
                    Description = $"Admin rematch: {existing.Count} prior relation(s) deactivated, new relation -> BOEDocumentId={request.NewBOEDocumentId}.",
                    IsResolved = true,
                    Resolution = "Rematched",
                    ResolvedBy = actor,
                    ResolvedAt = now,
                    ResolutionNotes = request.Reason,
                    CreatedAtUtc = now,
                });
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Match rematch: {Container} -> BOE {Boe} by {Actor}",
                request.ContainerNumber, request.NewBOEDocumentId, actor);

            return Ok(new
            {
                success = true,
                deactivatedRelations = existing.Count,
                newBOEDocumentId = request.NewBOEDocumentId,
                flagsResolved = openFlags.Count,
            });
        }

        /// <summary>
        /// Manually flag a container as suspicious. Admin-driven flag creation
        /// for cases the automated pipeline didn't catch.
        /// </summary>
        [HttpPost("flag")]
        public async Task<ActionResult<MatchFlagDto>> Flag([FromBody] ManualFlagRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ContainerNumber) || string.IsNullOrWhiteSpace(request.Reason))
            {
                return BadRequest(new { error = "ContainerNumber and Reason are required" });
            }

            var actor = User.Identity?.Name ?? "admin";

            var status = await _context.ContainerCompletenessStatuses
                .AsNoTracking()
                .Where(s => s.ContainerNumber == request.ContainerNumber)
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefaultAsync();

            var flag = new MatchQualityFlag
            {
                ContainerNumber = request.ContainerNumber,
                ScannerType = status?.ScannerType,
                BOEDocumentId = status?.BOEDocumentId,
                FlagType = "ManualFlag",
                Severity = string.IsNullOrWhiteSpace(request.Severity) ? "Warning" : request.Severity,
                Description = $"Flagged by {actor}: {request.Reason}",
                IsResolved = false,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _context.MatchQualityFlags.Add(flag);
            await _context.SaveChangesAsync();

            return Ok(new MatchFlagDto
            {
                Id = flag.Id,
                ContainerNumber = flag.ContainerNumber,
                ScannerType = flag.ScannerType,
                BOEDocumentId = flag.BOEDocumentId,
                FlagType = flag.FlagType,
                Severity = flag.Severity,
                Description = flag.Description,
                IsResolved = flag.IsResolved,
                CreatedAtUtc = flag.CreatedAtUtc,
            });
        }

        /// <summary>
        /// Resolve a flag without unmatch/rematch — for the "this match was
        /// actually correct, dismiss the warning" case.
        /// </summary>
        [HttpPost("{flagId:int}/resolve")]
        public async Task<ActionResult<object>> Resolve(int flagId, [FromBody] ResolveFlagRequest request)
        {
            var flag = await _context.MatchQualityFlags.FirstOrDefaultAsync(f => f.Id == flagId);
            if (flag == null)
            {
                return NotFound(new { error = $"Flag {flagId} not found" });
            }

            var actor = User.Identity?.Name ?? "admin";
            flag.IsResolved = true;
            flag.Resolution = string.IsNullOrWhiteSpace(request.Resolution) ? "Confirmed" : request.Resolution;
            flag.ResolvedBy = actor;
            flag.ResolvedAt = DateTime.UtcNow;
            flag.ResolutionNotes = request.Notes;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                flag.Id,
                flag.Resolution,
                flag.ResolvedBy,
                flag.ResolvedAt,
            });
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

        private async Task<BoeSummaryDto?> LoadBoeSummaryAsync(int boeDocumentId)
        {
            var boe = await _icumDb.BOEDocuments
                .AsNoTracking()
                .Where(b => b.Id == boeDocumentId)
                .Select(b => new BoeSummaryDto
                {
                    Id = b.Id,
                    ContainerNumber = b.ContainerNumber,
                    DeclarationNumber = b.DeclarationNumber,
                    ClearanceType = b.ClearanceType,
                    DeliveryPlace = b.DeliveryPlace,
                    CountryOfOrigin = b.CountryOfOrigin,
                    GoodsDescription = b.GoodsDescription,
                    ConsigneeName = b.ConsigneeName,
                    ImporterName = b.ImpName,
                    MasterBlNumber = b.MasterBlNumber,
                    HouseBl = b.HouseBl,
                    RotationNumber = b.RotationNumber,
                    CrmsLevel = b.CrmsLevel,
                    DeclarationDate = b.DeclarationDate,
                })
                .FirstOrDefaultAsync();

            return boe;
        }
    }

    // ─── DTOs ──────────────────────────────────────────────────────────────────

    public class MatchFlagListResponse
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<MatchFlagDto> Rows { get; set; } = new();
    }

    public class MatchFlagDto
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string? ScannerType { get; set; }
        public int? BOEDocumentId { get; set; }
        public string FlagType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsResolved { get; set; }
        public string? Resolution { get; set; }
        public string? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class ContainerMatchDetailDto
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public ContainerCompletenessStatus? Status { get; set; }
        public List<ContainerBOERelation> Relations { get; set; } = new();
        public List<MatchFlagDto> Flags { get; set; } = new();
        public BoeSummaryDto? MatchedBoe { get; set; }
    }

    public class BoeSummaryDto
    {
        public int Id { get; set; }
        public string? ContainerNumber { get; set; }
        public string? DeclarationNumber { get; set; }
        public string? ClearanceType { get; set; }
        public string? DeliveryPlace { get; set; }
        public string? CountryOfOrigin { get; set; }
        public string? GoodsDescription { get; set; }
        public string? ConsigneeName { get; set; }
        public string? ImporterName { get; set; }
        public string? MasterBlNumber { get; set; }
        public string? HouseBl { get; set; }
        public string? RotationNumber { get; set; }
        public string? CrmsLevel { get; set; }
        public string? DeclarationDate { get; set; }
    }

    public class UnmatchRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class RematchRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public int NewBOEDocumentId { get; set; }
        public string? ScannerType { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class ManualFlagRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string? Severity { get; set; }
    }

    public class ResolveFlagRequest
    {
        public string? Resolution { get; set; }
        public string? Notes { get; set; }
    }
}
