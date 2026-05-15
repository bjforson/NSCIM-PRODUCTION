using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.RecordCompleteness
{
    /// <summary>
    /// 1.14.0 — Pure helper that converts a set of ICUMS BOE document rows into a
    /// <see cref="RecordCompletenessStatus"/> (plus its expected-container children).
    /// No database access, no side effects — just shape transformation. Used by the
    /// <see cref="RecordReconciliationWorker"/> and the backfill script.
    ///
    /// Encodes the Pattern A / B / C grouping rule we agreed on:
    /// - IM/EX record identity is the declaration number (globally unique, stable).
    /// - CMR record identity is the CMR composite operational key until BOE upgrade.
    /// - For the "used cars in one container" case where multiple declarations share
    ///   a single physical container, the records are still created per-declaration
    ///   but they share a <see cref="RecordCompletenessStatus.ContainerGroupKey"/>
    ///   so the UI can present them together for image-level decisions.
    /// - Master BL is recorded for display only, never used as an identifier, because
    ///   unrelated customers can share a shipping contract.
    /// </summary>
    public static class RecordCompletenessBuilder
    {
        /// <summary>
        /// Build a (record, expected containers) pair from the BOE rows that belong
        /// to one declaration.
        /// </summary>
        /// <param name="declarationRows">All BOE document rows with the same declarationnumber.</param>
        /// <param name="otherDeclarationsSharingAnyContainer">
        /// Rows from OTHER declarations whose containernumber matches one of the
        /// declarationRows. Used to detect Pattern A (sibling declarations on the same
        /// physical container) and to populate <see cref="RecordCompletenessStatus.DeclarationsJson"/>.
        /// Pass an empty list for non-Pattern-A cases.
        /// </param>
        /// <param name="nowUtc">Timestamp to stamp on the new record.</param>
        public static BuildResult Build(
            IReadOnlyList<BOEDocument> declarationRows,
            IReadOnlyList<BOEDocument> otherDeclarationsSharingAnyContainer,
            DateTime nowUtc)
        {
            if (declarationRows == null || declarationRows.Count == 0)
                throw new ArgumentException("declarationRows must not be empty", nameof(declarationRows));

            var first = declarationRows[0];
            var declarationNumber = (first.DeclarationNumber ?? string.Empty).Trim();

            // Distinct containers expected by this declaration, deduped by container number
            // (the same container can appear multiple times under different house BLs for
            // the consolidated case).
            var expectedContainers = declarationRows
                .Where(r => !string.IsNullOrWhiteSpace(r.ContainerNumber))
                .GroupBy(r => r.ContainerNumber.Trim().ToUpperInvariant())
                .Select(g =>
                {
                    var rep = g.First();
                    return new ExpectedContainerInput
                    {
                        ContainerNumber = g.Key,
                        BoeDocumentId = rep.Id,
                        HouseBl = rep.HouseBl,
                        ConsigneeName = rep.ConsigneeName,
                    };
                })
                .ToList();

            // Pattern A detection: exactly one expected container AND that container
            // appears on at least one OTHER declaration. If true, this is a "used cars in
            // one container" case and the ContainerGroupKey is set so the UI can collapse
            // sibling declarations for image-level decisions.
            string? containerGroupKey = null;
            string? declarationsJson = null;

            if (expectedContainers.Count == 1 && otherDeclarationsSharingAnyContainer.Count > 0)
            {
                var theContainer = expectedContainers[0].ContainerNumber;
                var siblingsOnSameContainer = otherDeclarationsSharingAnyContainer
                    .Where(o => string.Equals(
                        (o.ContainerNumber ?? string.Empty).Trim().ToUpperInvariant(),
                        theContainer,
                        StringComparison.Ordinal))
                    .ToList();

                if (siblingsOnSameContainer.Count > 0)
                {
                    containerGroupKey = theContainer;
                    var metadata = siblingsOnSameContainer
                        .Select(s => new
                        {
                            declarationNumber = s.DeclarationNumber,
                            consigneeName = s.ConsigneeName,
                            houseBl = s.HouseBl,
                            goodsDescription = Truncate(s.GoodsDescription, 200),
                        })
                        .ToList();
                    declarationsJson = JsonSerializer.Serialize(metadata);
                }
            }

            var record = new RecordCompletenessStatus
            {
                DeclarationNumber = declarationNumber,
                ClearanceType = first.ClearanceType,
                RegimeCode = first.RegimeCode,
                PrimaryBoeDocumentId = first.Id,
                RotationNumber = first.RotationNumber,
                BlNumber = first.BlNumber,
                ContainerGroupKey = containerGroupKey,
                ScannerType = null, // unknown until a scanner event arrives
                TotalExpectedContainers = expectedContainers.Count,
                ContainersAwaitingScan = expectedContainers.Count,
                ContainersScanned = 0,
                ContainersReady = 0,
                ContainersDecided = 0,
                ContainersSubmitted = 0,
                ContainersNoImage = 0,
                ContainersNoScan = 0,
                Status = "Pending",
                WorkflowStage = "Pending",
                FirstSeenUtc = nowUtc,
                LastNewContainerAtUtc = nowUtc,
                DeclarationsJson = declarationsJson,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            };

            var children = expectedContainers
                .Select(e => new RecordExpectedContainer
                {
                    ContainerNumber = e.ContainerNumber,
                    Status = "AwaitingScan",
                    BoeDocumentId = e.BoeDocumentId,
                    HouseBl = e.HouseBl,
                    ConsigneeName = e.ConsigneeName,
                    FirstSeenUtc = nowUtc,
                })
                .ToList();

            return new BuildResult(record, children);
        }

        /// <summary>
        /// Build a CMR pre-declaration record from a single valid CMR BOE row.
        /// CMR records are keyed by the CMR composite operational key because
        /// DeclarationNumber is not stable/available until the later BOE upgrade.
        /// </summary>
        public static BuildResult BuildCmr(BOEDocument cmrRow, DateTime nowUtc)
        {
            if (cmrRow == null)
                throw new ArgumentNullException(nameof(cmrRow));

            if (!string.Equals(cmrRow.ClearanceType?.Trim(), "CMR", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("CMR row must have ClearanceType CMR.", nameof(cmrRow));

            if (!CmrCompositeKeyHelper.TryCreate(
                    cmrRow.RotationNumber,
                    cmrRow.ContainerNumber,
                    cmrRow.BlNumber,
                    out var compositeKey))
            {
                throw new ArgumentException(
                    "CMR row must include rotation number, container number, and BL number.",
                    nameof(cmrRow));
            }

            var record = new RecordCompletenessStatus
            {
                DeclarationNumber = compositeKey.OperationalKey,
                ClearanceType = "CMR",
                RegimeCode = cmrRow.RegimeCode,
                PrimaryBoeDocumentId = cmrRow.Id,
                RotationNumber = compositeKey.RotationNumber,
                BlNumber = compositeKey.BlNumber,
                ContainerGroupKey = null,
                ScannerType = null,
                TotalExpectedContainers = 1,
                ContainersAwaitingScan = 1,
                ContainersScanned = 0,
                ContainersReady = 0,
                ContainersDecided = 0,
                ContainersSubmitted = 0,
                ContainersNoImage = 0,
                ContainersNoScan = 0,
                Status = "Pending",
                WorkflowStage = "Pending",
                FirstSeenUtc = nowUtc,
                LastNewContainerAtUtc = nowUtc,
                DeclarationsJson = null,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            };

            var children = new List<RecordExpectedContainer>
            {
                new()
                {
                    ContainerNumber = compositeKey.ContainerNumber,
                    Status = "AwaitingScan",
                    BoeDocumentId = cmrRow.Id,
                    HouseBl = cmrRow.HouseBl,
                    ConsigneeName = cmrRow.ConsigneeName,
                    FirstSeenUtc = nowUtc,
                }
            };

            return new BuildResult(record, children);
        }

        /// <summary>
        /// Recompute <see cref="RecordCompletenessStatus"/> rollup counts and derive
        /// <c>Status</c> and <c>WorkflowStage</c> from the current state of its children.
        /// Call this after any change to the child rows.
        /// </summary>
        public static void Recompute(RecordCompletenessStatus record, IReadOnlyList<RecordExpectedContainer> children)
        {
            var total = children.Count;
            var awaiting = children.Count(c => c.Status == "AwaitingScan");
            var scanned = children.Count(c => c.Status == "Pending");
            var ready = children.Count(c => c.Status == "Ready");
            var decided = children.Count(c => c.Status == "Decided");
            var submitted = children.Count(c => c.Status == "Submitted");
            var noImage = children.Count(c => c.Status == "NoImageAvailable");
            var noScan = children.Count(c => c.Status == "NoScanReceived");

            record.TotalExpectedContainers = total;
            record.ContainersAwaitingScan = awaiting;
            record.ContainersScanned = scanned;
            record.ContainersReady = ready;
            record.ContainersDecided = decided;
            record.ContainersSubmitted = submitted;
            record.ContainersNoImage = noImage;
            record.ContainersNoScan = noScan;

            // Derive status. Priority order: terminal > active > waiting.
            if (record.Status == "Archived" || record.Status == "Failed")
            {
                // Terminal — don't change
            }
            else if (submitted == total && total > 0)
            {
                record.Status = "Completed";
                record.WorkflowStage = "Completed";
            }
            else if (submitted > 0 && submitted + decided + noImage + noScan == total)
            {
                record.Status = "PendingSubmission";
                record.WorkflowStage = "PendingSubmission";
            }
            else if (decided > 0 && decided + submitted == total - noImage - noScan - awaiting)
            {
                // At least one decided, and everything that CAN be decided has been decided
                record.Status = "InAudit";
                record.WorkflowStage = "Audit";
            }
            else if (ready > 0)
            {
                if (awaiting == 0 && scanned == 0)
                {
                    record.Status = "Ready";
                    record.WorkflowStage = "ImageAnalysis";
                }
                else
                {
                    record.Status = "PartiallyReady";
                    record.WorkflowStage = "ImageAnalysis";
                }
                if (record.FirstReadyAtUtc == null)
                {
                    record.FirstReadyAtUtc = DateTime.UtcNow;
                }
            }
            else if (scanned > 0)
            {
                record.Status = "PartiallyReady";
                record.WorkflowStage = "Pending";
            }
            else
            {
                record.Status = "Pending";
                record.WorkflowStage = "Pending";
            }

            record.UpdatedAtUtc = DateTime.UtcNow;
            record.LastCheckedAtUtc = DateTime.UtcNow;
        }

        private static string? Truncate(string? value, int max)
            => string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value.Substring(0, max));

        public sealed record BuildResult(
            RecordCompletenessStatus Record,
            List<RecordExpectedContainer> Children);

        private sealed class ExpectedContainerInput
        {
            public string ContainerNumber { get; set; } = string.Empty;
            public int BoeDocumentId { get; set; }
            public string? HouseBl { get; set; }
            public string? ConsigneeName { get; set; }
        }
    }
}
