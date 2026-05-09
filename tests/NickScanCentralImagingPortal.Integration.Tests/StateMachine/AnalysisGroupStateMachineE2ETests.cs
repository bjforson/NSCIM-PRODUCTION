using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using Xunit;

namespace NickScanCentralImagingPortal.Integration.Tests.StateMachine;

/// <summary>
/// Phase B / B7-D (2026-05-09): end-to-end audit-pipeline state-machine test.
///
/// Walks a single AnalysisGroup through the canonical 6-edge flow:
///   Ready → AnalystAssigned → AnalystCompleted → AuditAssigned →
///   AuditCompleted → Submitted → Completed
/// using <see cref="AnalysisGroupStateMachine.TransitionAsync"/> (the sole-writer
/// facade since Sprint 5G2 / Bridge B1, 2026-05-07). Asserts that:
/// <list type="number">
///   <item>Each transition succeeds (Applied, not Idempotent).</item>
///   <item>Each transition writes one row to <c>analysis_group_status_transitions</c>.</item>
///   <item>The validator allows every edge (no <see cref="InvalidOperationException"/>).</item>
///   <item>The final group reaches Completed.</item>
/// </list>
///
/// Catches the three-hop drift class identified in the 2026-05-09 audit-queue
/// dead-mans-switch memory: any future regression that breaks one of these six
/// edges (or the audit-row write) fails this test loudly in CI.
///
/// <para>
/// Backed by EF Core InMemory — see csproj banner. The facade exercises
/// <c>DbContext.Entry</c>, <c>DbSet&lt;T&gt;.Add</c>, <c>SaveChangesAsync</c>; all
/// satisfied by InMemory. The single PG-specific feature it touches is the
/// post-save subscriber event, which is null in tests and skipped by guard.
/// </para>
/// </summary>
public class AnalysisGroupStateMachineE2ETests
{
    private static ApplicationDbContext NewInMemoryDb()
    {
        // Per-test database name so concurrent xUnit collections don't bleed state.
        var dbName = $"AGStateMachineE2E_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            // Service-provider caching off so per-test InMemory DBs don't share warnings/state.
            .EnableServiceProviderCaching(false)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AnalysisGroup SeedReadyGroup(ApplicationDbContext db)
    {
        var group = new AnalysisGroup
        {
            Id = Guid.NewGuid(),
            GroupIdentifier = "TEST-BL-E2E-001",
            GroupType = "BL",
            ScannerType = "FS6000",
            // Status defaults to "Ready" via the initializer; we don't (and can't)
            // assign Status here because the setter is `internal` since Sprint 5G2.
        };
        db.AnalysisGroups.Add(group);
        db.SaveChanges();
        return group;
    }

    /// <summary>
    /// Reload the entity from a fresh tracking query — InMemory keeps a stable
    /// reference, but reloading mirrors what real callers do (avoids accidentally
    /// hitting the test-author bias of a stale local).
    /// </summary>
    private static async Task<AnalysisGroup> ReloadTrackedAsync(ApplicationDbContext db, Guid id)
    {
        var group = await db.AnalysisGroups.AsTracking().FirstAsync(g => g.Id == id);
        return group;
    }

    [Fact]
    public async Task FullAuditPipeline_SixTransitions_AllAppliedAndAudited()
    {
        await using var db = NewInMemoryDb();
        var seeded = SeedReadyGroup(db);
        var groupId = seeded.Id;

        Assert.Equal(AnalysisStatuses.Ready, seeded.Status);
        Assert.Empty(db.AnalysisGroupStatusTransitions);

        // ── Transition 1: Ready → AnalystAssigned ──────────────────────────
        {
            var g = await ReloadTrackedAsync(db, groupId);
            var result = await AnalysisGroupStateMachine.TransitionAsync(
                db, g,
                toStatus: AnalysisStatuses.AnalystAssigned,
                triggerName: "AnalystClaimedAssignment",
                actor: "test-analyst-user-001",
                reason: "E2E test: claim from ready queue");
            Assert.Equal(AnalysisGroupTransitionResult.Applied, result);
            Assert.Equal(AnalysisStatuses.AnalystAssigned, g.Status);
        }

        // ── Transition 2: AnalystAssigned → AnalystCompleted ───────────────
        {
            var g = await ReloadTrackedAsync(db, groupId);
            var result = await AnalysisGroupStateMachine.TransitionAsync(
                db, g,
                toStatus: AnalysisStatuses.AnalystCompleted,
                triggerName: "AnalystSubmittedFindings",
                actor: "test-analyst-user-001",
                reason: "E2E test: analyst submitted decisions for all containers");
            Assert.Equal(AnalysisGroupTransitionResult.Applied, result);
            Assert.Equal(AnalysisStatuses.AnalystCompleted, g.Status);
        }

        // ── Transition 3: AnalystCompleted → AuditAssigned ─────────────────
        {
            var g = await ReloadTrackedAsync(db, groupId);
            var result = await AnalysisGroupStateMachine.TransitionAsync(
                db, g,
                toStatus: AnalysisStatuses.AuditAssigned,
                triggerName: "AssignmentToAudit",
                actor: "test-audit-user-001",
                reason: "E2E test: auditor claimed for second-tier review");
            Assert.Equal(AnalysisGroupTransitionResult.Applied, result);
            Assert.Equal(AnalysisStatuses.AuditAssigned, g.Status);
        }

        // ── Transition 4: AuditAssigned → AuditCompleted ───────────────────
        {
            var g = await ReloadTrackedAsync(db, groupId);
            var result = await AnalysisGroupStateMachine.TransitionAsync(
                db, g,
                toStatus: AnalysisStatuses.AuditCompleted,
                triggerName: "AuditSubmittedDecisions",
                actor: "test-audit-user-001",
                reason: "E2E test: auditor finalised all container reviews");
            Assert.Equal(AnalysisGroupTransitionResult.Applied, result);
            Assert.Equal(AnalysisStatuses.AuditCompleted, g.Status);
        }

        // ── Transition 5: AuditCompleted → Submitted ───────────────────────
        {
            var g = await ReloadTrackedAsync(db, groupId);
            var result = await AnalysisGroupStateMachine.TransitionAsync(
                db, g,
                toStatus: AnalysisStatuses.Submitted,
                triggerName: "SubmittedToICUMS",
                actor: "ICUMS-SUBMISSION-WORKER",
                reason: "E2E test: outbox flushed to ICUMS");
            Assert.Equal(AnalysisGroupTransitionResult.Applied, result);
            Assert.Equal(AnalysisStatuses.Submitted, g.Status);
        }

        // ── Transition 6: Submitted → Completed ────────────────────────────
        {
            var g = await ReloadTrackedAsync(db, groupId);
            var result = await AnalysisGroupStateMachine.TransitionAsync(
                db, g,
                toStatus: AnalysisStatuses.Completed,
                triggerName: "SubmissionWorkflowCompleted",
                actor: "ICUMS-SUBMISSION-WORKER",
                reason: "E2E test: ICUMS acknowledged + outbox cleared");
            Assert.Equal(AnalysisGroupTransitionResult.Applied, result);
            Assert.Equal(AnalysisStatuses.Completed, g.Status);
        }

        // ── Audit-trail assertions ────────────────────────────────────────
        var transitions = await db.AnalysisGroupStatusTransitions
            .Where(t => t.GroupId == groupId)
            .OrderBy(t => t.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(6, transitions.Count);

        // Walk the persisted from→to chain — must match the canonical pipeline edge-for-edge.
        var expectedChain = new[]
        {
            (AnalysisStatuses.Ready,             AnalysisStatuses.AnalystAssigned),
            (AnalysisStatuses.AnalystAssigned,   AnalysisStatuses.AnalystCompleted),
            (AnalysisStatuses.AnalystCompleted,  AnalysisStatuses.AuditAssigned),
            (AnalysisStatuses.AuditAssigned,     AnalysisStatuses.AuditCompleted),
            (AnalysisStatuses.AuditCompleted,    AnalysisStatuses.Submitted),
            (AnalysisStatuses.Submitted,         AnalysisStatuses.Completed),
        };

        for (int i = 0; i < expectedChain.Length; i++)
        {
            Assert.Equal(expectedChain[i].Item1, transitions[i].FromStatus);
            Assert.Equal(expectedChain[i].Item2, transitions[i].ToStatus);
            Assert.False(string.IsNullOrEmpty(transitions[i].TriggerName),
                $"Transition {i} ({transitions[i].FromStatus}→{transitions[i].ToStatus}) missing trigger name");
            Assert.False(string.IsNullOrEmpty(transitions[i].Actor),
                $"Transition {i} ({transitions[i].FromStatus}→{transitions[i].ToStatus}) missing actor");
            Assert.False(string.IsNullOrEmpty(transitions[i].Reason),
                $"Transition {i} ({transitions[i].FromStatus}→{transitions[i].ToStatus}) missing reason");
        }

        // Final state — group is Completed, no further transitions queued.
        var finalGroup = await db.AnalysisGroups.AsNoTracking().FirstAsync(g => g.Id == groupId);
        Assert.Equal(AnalysisStatuses.Completed, finalGroup.Status);
    }

    [Fact]
    public async Task IllegalTransition_AnalystAssignedToSubmitted_Throws()
    {
        await using var db = NewInMemoryDb();
        var seeded = SeedReadyGroup(db);

        // Walk to AnalystAssigned legitimately first.
        var g = await ReloadTrackedAsync(db, seeded.Id);
        await AnalysisGroupStateMachine.TransitionAsync(
            db, g,
            toStatus: AnalysisStatuses.AnalystAssigned,
            triggerName: "AnalystClaimedAssignment",
            actor: "test-analyst",
            reason: "setup");

        // Skip-stage forward — must throw and write nothing.
        var trackedG = await ReloadTrackedAsync(db, seeded.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AnalysisGroupStateMachine.TransitionAsync(
                db, trackedG,
                toStatus: AnalysisStatuses.Submitted,
                triggerName: "ImpossibleSkip",
                actor: "test",
                reason: "should reject"));

        // The ONE legitimate transition (Ready→AnalystAssigned) is the only audit row.
        var rowCount = await db.AnalysisGroupStatusTransitions.CountAsync(t => t.GroupId == seeded.Id);
        Assert.Equal(1, rowCount);

        var still = await db.AnalysisGroups.AsNoTracking().FirstAsync(x => x.Id == seeded.Id);
        Assert.Equal(AnalysisStatuses.AnalystAssigned, still.Status);
    }

    [Fact]
    public async Task IdempotentTransition_NoAuditRowWritten()
    {
        await using var db = NewInMemoryDb();
        var seeded = SeedReadyGroup(db);

        var g = await ReloadTrackedAsync(db, seeded.Id);
        // Group is Ready; ask for Ready again.
        var result = await AnalysisGroupStateMachine.TransitionAsync(
            db, g,
            toStatus: AnalysisStatuses.Ready,
            triggerName: "SelfTransition",
            actor: "test",
            reason: "should be idempotent");

        Assert.Equal(AnalysisGroupTransitionResult.Idempotent, result);
        Assert.Equal(AnalysisStatuses.Ready, g.Status);

        var rowCount = await db.AnalysisGroupStatusTransitions.CountAsync(t => t.GroupId == seeded.Id);
        Assert.Equal(0, rowCount); // No audit row for a no-op
    }

    [Fact]
    public async Task DetachedEntity_Throws()
    {
        await using var db = NewInMemoryDb();
        var seeded = SeedReadyGroup(db);
        var groupId = seeded.Id;

        // Load with NoTracking — facade must reject this and tell the caller to .AsTracking().
        var detached = await db.AnalysisGroups.AsNoTracking().FirstAsync(x => x.Id == groupId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AnalysisGroupStateMachine.TransitionAsync(
                db, detached,
                toStatus: AnalysisStatuses.AnalystAssigned,
                triggerName: "AttemptedDetached",
                actor: "test",
                reason: "should throw"));

        Assert.Contains("detached", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
