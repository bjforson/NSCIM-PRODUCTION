using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Models;
using Xunit;

namespace NickScanCentralImagingPortal.Core.Tests.Helpers;

/// <summary>
/// Phase B / B5 (2026-05-09): unit tests for AnalysisStatusValidator. The validator
/// became load-bearing on 2026-05-07 (Sprint 5G2 / Bridge B1) when the AnalysisGroup
/// state-machine facade made it the single source of truth for legal transitions.
/// Prior to this suite the validator had zero unit coverage; any regression in the
/// transition table would only surface when production traffic hit the bad edge.
/// </summary>
public class AnalysisStatusValidatorTests
{
    // ─── IsValidTransition: legitimate edges ───────────────────────────────

    [Theory]
    // Forward "happy path"
    [InlineData(AnalysisStatuses.Ready, AnalysisStatuses.AnalystAssigned)]
    [InlineData(AnalysisStatuses.AnalystAssigned, AnalysisStatuses.AnalystCompleted)]
    [InlineData(AnalysisStatuses.AnalystCompleted, AnalysisStatuses.AuditAssigned)]
    [InlineData(AnalysisStatuses.AuditAssigned, AnalysisStatuses.AuditCompleted)]
    [InlineData(AnalysisStatuses.AuditCompleted, AnalysisStatuses.Submitted)]
    [InlineData(AnalysisStatuses.Submitted, AnalysisStatuses.Completed)]
    // Decision Agent path (Ready → AgentProcessing → ...)
    [InlineData(AnalysisStatuses.Ready, AnalysisStatuses.AgentProcessing)]
    [InlineData(AnalysisStatuses.AgentProcessing, AnalysisStatuses.Ready)]            // DA release without decision
    [InlineData(AnalysisStatuses.AgentProcessing, AnalysisStatuses.AnalystCompleted)] // DA auto-decided
    [InlineData(AnalysisStatuses.AgentProcessing, AnalysisStatuses.AuditCompleted)]   // DA processing-depth=Audit
    // Direct-to-audit path (admin "audit-only" review)
    [InlineData(AnalysisStatuses.Ready, AnalysisStatuses.AuditAssigned)]
    [InlineData(AnalysisStatuses.Ready, AnalysisStatuses.AnalystCompleted)]           // DA bypasses analyst
    // Wave-progression / housekeeping shortcuts
    [InlineData(AnalysisStatuses.Ready, AnalysisStatuses.Completed)]
    [InlineData(AnalysisStatuses.AnalystAssigned, AnalysisStatuses.Completed)]
    [InlineData(AnalysisStatuses.AnalystCompleted, AnalysisStatuses.Completed)]
    [InlineData(AnalysisStatuses.AuditAssigned, AnalysisStatuses.Completed)]
    // Reverse / re-assign
    [InlineData(AnalysisStatuses.AnalystAssigned, AnalysisStatuses.Ready)]            // Expired-lease revert
    [InlineData(AnalysisStatuses.AnalystCompleted, AnalysisStatuses.Ready)]           // ReverseAgentDecision
    [InlineData(AnalysisStatuses.AnalystCompleted, AnalysisStatuses.AnalystAssigned)] // Admin re-assign
    [InlineData(AnalysisStatuses.AuditAssigned, AnalysisStatuses.AnalystCompleted)]   // Bounce back
    [InlineData(AnalysisStatuses.AuditAssigned, AnalysisStatuses.Ready)]              // Stuck-group sweep
    // PartiallyCompleted edges (added 2026-05-05)
    [InlineData(AnalysisStatuses.Ready, AnalysisStatuses.PartiallyCompleted)]
    [InlineData(AnalysisStatuses.AnalystAssigned, AnalysisStatuses.PartiallyCompleted)]
    [InlineData(AnalysisStatuses.AuditCompleted, AnalysisStatuses.PartiallyCompleted)]
    [InlineData(AnalysisStatuses.PartiallyCompleted, AnalysisStatuses.Ready)]
    [InlineData(AnalysisStatuses.PartiallyCompleted, AnalysisStatuses.Completed)]
    // Cancelled / Archived sweep edges
    [InlineData(AnalysisStatuses.AnalystAssigned, AnalysisStatuses.Cancelled)]
    [InlineData(AnalysisStatuses.AuditAssigned, AnalysisStatuses.Cancelled)]
    [InlineData(AnalysisStatuses.AnalystCompleted, AnalysisStatuses.Archived)]
    public void IsValidTransition_LegalEdges_ReturnsTrue(string from, string to)
    {
        Assert.True(AnalysisStatusValidator.IsValidTransition(from, to),
            $"Expected legal edge {from} → {to}");
    }

    // ─── IsValidTransition: illegal edges ──────────────────────────────────

    [Theory]
    // Skipping stages forward
    [InlineData(AnalysisStatuses.AnalystAssigned, AnalysisStatuses.AuditAssigned)]
    [InlineData(AnalysisStatuses.AnalystAssigned, AnalysisStatuses.Submitted)]
    // Terminal-state breakouts
    [InlineData(AnalysisStatuses.Completed, AnalysisStatuses.Ready)]
    [InlineData(AnalysisStatuses.Completed, AnalysisStatuses.AnalystAssigned)]
    [InlineData(AnalysisStatuses.Cancelled, AnalysisStatuses.Ready)]
    [InlineData(AnalysisStatuses.Archived, AnalysisStatuses.Ready)]
    // Submitted is forward-only
    [InlineData(AnalysisStatuses.Submitted, AnalysisStatuses.Ready)]
    [InlineData(AnalysisStatuses.Submitted, AnalysisStatuses.AnalystAssigned)]
    // Unknown source
    [InlineData("BogusStatus", AnalysisStatuses.Ready)]
    public void IsValidTransition_IllegalEdges_ReturnsFalse(string from, string to)
    {
        Assert.False(AnalysisStatusValidator.IsValidTransition(from, to),
            $"Expected illegal edge {from} → {to} to be rejected");
    }

    // ─── IsValidTransition: edge cases ─────────────────────────────────────

    [Theory]
    [InlineData(AnalysisStatuses.Ready, AnalysisStatuses.Ready)]
    [InlineData(AnalysisStatuses.Completed, AnalysisStatuses.Completed)]
    [InlineData(AnalysisStatuses.AnalystAssigned, AnalysisStatuses.AnalystAssigned)]
    public void IsValidTransition_SameStateIsIdempotent_ReturnsTrue(string status, string sameStatus)
    {
        Assert.True(AnalysisStatusValidator.IsValidTransition(status, sameStatus));
    }

    [Theory]
    [InlineData(null, AnalysisStatuses.Ready)]
    [InlineData("", AnalysisStatuses.Ready)]
    [InlineData("   ", AnalysisStatuses.Ready)]
    [InlineData(AnalysisStatuses.Ready, null)]
    [InlineData(AnalysisStatuses.Ready, "")]
    public void IsValidTransition_NullOrWhitespace_ReturnsFalse(string? from, string? to)
    {
        Assert.False(AnalysisStatusValidator.IsValidTransition(from!, to!));
    }

    [Theory]
    // Case-insensitive on the SAME status (idempotent shortcut)
    [InlineData("ready", "READY")]
    [InlineData("AnalystCompleted", "ANALYSTCOMPLETED")]
    public void IsValidTransition_SameStateCaseInsensitive_ReturnsTrue(string from, string to)
    {
        Assert.True(AnalysisStatusValidator.IsValidTransition(from, to));
    }

    // ─── ValidateTransition: throws on illegal ─────────────────────────────

    [Fact]
    public void ValidateTransition_LegalEdge_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            AnalysisStatusValidator.ValidateTransition(AnalysisStatuses.Ready, AnalysisStatuses.AnalystAssigned));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateTransition_IllegalEdge_ThrowsWithValidTargetsListed()
    {
        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            AnalysisStatusValidator.ValidateTransition(AnalysisStatuses.Submitted, AnalysisStatuses.Ready, "test-group-1"));

        Assert.Contains("Submitted", ex.Message);
        Assert.Contains("Ready", ex.Message);
        Assert.Contains("test-group-1", ex.Message);
        // The exception lists the legitimate targets — Submitted → Completed only
        Assert.Contains(AnalysisStatuses.Completed, ex.Message);
    }

    [Fact]
    public void ValidateTransition_TerminalState_ListsNoneAsTarget()
    {
        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            AnalysisStatusValidator.ValidateTransition(AnalysisStatuses.Completed, AnalysisStatuses.Ready));

        Assert.Contains("none (terminal state)", ex.Message);
    }

    // ─── IsTerminalState ───────────────────────────────────────────────────

    [Theory]
    [InlineData(AnalysisStatuses.Completed)]
    [InlineData(AnalysisStatuses.Cancelled)]
    [InlineData(AnalysisStatuses.Archived)]
    public void IsTerminalState_TerminalStatuses_ReturnsTrue(string status)
    {
        Assert.True(AnalysisStatusValidator.IsTerminalState(status));
    }

    [Theory]
    [InlineData(AnalysisStatuses.Ready)]
    [InlineData(AnalysisStatuses.AnalystAssigned)]
    [InlineData(AnalysisStatuses.AnalystCompleted)]
    [InlineData(AnalysisStatuses.AuditAssigned)]
    [InlineData(AnalysisStatuses.AuditCompleted)]
    [InlineData(AnalysisStatuses.Submitted)]
    [InlineData(AnalysisStatuses.AgentProcessing)]
    [InlineData(AnalysisStatuses.PartiallyCompleted)]
    public void IsTerminalState_NonTerminalStatuses_ReturnsFalse(string status)
    {
        Assert.False(AnalysisStatusValidator.IsTerminalState(status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UnknownStatus")]
    public void IsTerminalState_NullOrUnknown_ReturnsFalse(string? status)
    {
        Assert.False(AnalysisStatusValidator.IsTerminalState(status!));
    }

    // ─── GetValidTargetStatuses ────────────────────────────────────────────

    [Fact]
    public void GetValidTargetStatuses_FromReady_ReturnsAllReadyTargets()
    {
        var targets = AnalysisStatusValidator.GetValidTargetStatuses(AnalysisStatuses.Ready);

        Assert.Contains(AnalysisStatuses.AnalystAssigned, targets);
        Assert.Contains(AnalysisStatuses.AuditAssigned, targets);
        Assert.Contains(AnalysisStatuses.AgentProcessing, targets);
        Assert.Contains(AnalysisStatuses.PartiallyCompleted, targets);
        Assert.Contains(AnalysisStatuses.Completed, targets);
    }

    [Fact]
    public void GetValidTargetStatuses_FromTerminalState_ReturnsEmpty()
    {
        Assert.Empty(AnalysisStatusValidator.GetValidTargetStatuses(AnalysisStatuses.Completed));
        Assert.Empty(AnalysisStatusValidator.GetValidTargetStatuses(AnalysisStatuses.Cancelled));
        Assert.Empty(AnalysisStatusValidator.GetValidTargetStatuses(AnalysisStatuses.Archived));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UnknownStatus")]
    public void GetValidTargetStatuses_InvalidInput_ReturnsEmpty(string? status)
    {
        Assert.Empty(AnalysisStatusValidator.GetValidTargetStatuses(status!));
    }

    // ─── GetTransitionPath ─────────────────────────────────────────────────

    [Fact]
    public void GetTransitionPath_HappyPath_ReturnsShortestPath()
    {
        var path = AnalysisStatusValidator.GetTransitionPath(
            AnalysisStatuses.Ready, AnalysisStatuses.Completed);

        // Ready has a direct edge to Completed (intake wave-progression),
        // so the shortest path is exactly two states.
        Assert.Equal(2, path.Count);
        Assert.Equal(AnalysisStatuses.Ready, path[0]);
        Assert.Equal(AnalysisStatuses.Completed, path[1]);
    }

    [Fact]
    public void GetTransitionPath_RequiresMultipleHops_ReturnsFullPath()
    {
        var path = AnalysisStatusValidator.GetTransitionPath(
            AnalysisStatuses.AnalystAssigned, AnalysisStatuses.Submitted);

        // AnalystAssigned has no direct edge to Submitted; shortest is via
        // AnalystCompleted → AuditAssigned → AuditCompleted → Submitted, OR
        // AnalystCompleted → AuditCompleted → Submitted (DA bypass shortcut).
        // Either way, the path starts at AnalystAssigned and ends at Submitted.
        Assert.NotEmpty(path);
        Assert.Equal(AnalysisStatuses.AnalystAssigned, path[0]);
        Assert.Equal(AnalysisStatuses.Submitted, path[^1]);
    }

    [Fact]
    public void GetTransitionPath_SameSource_ReturnsSingleElement()
    {
        var path = AnalysisStatusValidator.GetTransitionPath(
            AnalysisStatuses.Ready, AnalysisStatuses.Ready);

        Assert.Single(path);
        Assert.Equal(AnalysisStatuses.Ready, path[0]);
    }

    [Fact]
    public void GetTransitionPath_NoPathFromTerminal_ReturnsEmpty()
    {
        var path = AnalysisStatusValidator.GetTransitionPath(
            AnalysisStatuses.Completed, AnalysisStatuses.Ready);

        Assert.Empty(path);
    }

    [Theory]
    [InlineData(null, AnalysisStatuses.Ready)]
    [InlineData(AnalysisStatuses.Ready, null)]
    [InlineData("", "")]
    public void GetTransitionPath_NullOrEmpty_ReturnsEmpty(string? from, string? to)
    {
        Assert.Empty(AnalysisStatusValidator.GetTransitionPath(from!, to!));
    }

    // ─── Regression guards (specific 2026-05-05 + 5G2 additions) ───────────

    [Fact]
    public void Regression_PartiallyCompletedReachableFromAuditCompleted()
    {
        // Added 2026-05-05: AuditCompleted → PartiallyCompleted is a NEW forward edge.
        Assert.True(AnalysisStatusValidator.IsValidTransition(
            AnalysisStatuses.AuditCompleted, AnalysisStatuses.PartiallyCompleted));
    }

    [Fact]
    public void Regression_AgentProcessingRevertToReadyIsLegal()
    {
        // DA shadow mode releases without decision: AgentProcessing → Ready must work
        // mid-flow or DA can't roll back when uncertain.
        Assert.True(AnalysisStatusValidator.IsValidTransition(
            AnalysisStatuses.AgentProcessing, AnalysisStatuses.Ready));
    }

    [Fact]
    public void Regression_TerminalStatesRejectAllOutbound()
    {
        // 2026-05-05 (Sprint 2C, audit 4.04): Cancelled + Archived used to silently
        // accept any outbound transition via the unknown-from fallthrough. Now they
        // must reject.
        Assert.False(AnalysisStatusValidator.IsValidTransition(
            AnalysisStatuses.Cancelled, AnalysisStatuses.Ready));
        Assert.False(AnalysisStatusValidator.IsValidTransition(
            AnalysisStatuses.Archived, AnalysisStatuses.Ready));
        Assert.False(AnalysisStatusValidator.IsValidTransition(
            AnalysisStatuses.Completed, AnalysisStatuses.Ready));
    }

    [Fact]
    public void Regression_UnknownFromStatusRejected()
    {
        // 2026-05-05: pre-fix, unknown-from used to silently return true. Now rejects.
        Assert.False(AnalysisStatusValidator.IsValidTransition(
            "TypoedStatus", AnalysisStatuses.Ready));
    }
}
