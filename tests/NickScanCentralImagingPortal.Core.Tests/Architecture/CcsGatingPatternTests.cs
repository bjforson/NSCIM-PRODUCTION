using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace NickScanCentralImagingPortal.Core.Tests.Architecture;

/// <summary>
/// Phase B / B6 (2026-05-09): pattern-test that locks in the
/// "AG.Status authoritative; never gate on CCS.WorkflowStage" rule
/// established by Sprint 5G2 / Bridge B1 (state-machine facade live 2026-05-07).
///
/// CCS.WorkflowStage is INFORMATIONAL/ASPIRATIONAL — it lags AG.Status, can be
/// stale, and was the root cause of multiple "audit queue empty" /
/// "stuck in ImageAnalysis" / "MSBU3196923-class" parallel-state-surface bugs.
/// All routing decisions (queue assignment, who works what next, whether a
/// group is auditable) MUST read AG.Status. CCS.WorkflowStage is acceptable
/// only for display/diagnostic counts and as a write target by code that
/// keeps the legacy column in sync.
///
/// This test is a regex-based scanner — not a Roslyn analyzer — to keep the
/// guardrail cheap. It walks every <c>*.cs</c> file under <c>src/</c>, looks
/// for forbidden equality comparisons against <c>WorkflowStage</c>, and
/// asserts every match is in <see cref="AllowlistedFiles"/>.
///
/// If a future PR adds a NEW <c>CCS.WorkflowStage == "..."</c> gate in a file
/// not on the allowlist, this test fails in CI with a pointer to the canonical
/// alternative (<c>AnalysisGroup.Status</c>).
///
/// Adding to the allowlist requires reviewer pushback: the rule is "don't add
/// new gates", and the existing list documents the legacy surface that
/// pre-dates B1. Allowlist entries are filenames (basenames), not paths, so
/// the test does not break on cross-machine path differences.
/// </summary>
public class CcsGatingPatternTests
{
    private readonly ITestOutputHelper _output;

    public CcsGatingPatternTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Files containing legitimate, pre-existing CCS.WorkflowStage equality
    /// reads. The rule we enforce going forward is "don't ADD NEW gates",
    /// not "remove these". Each entry below is justified — see the comment
    /// for the line range and reason.
    ///
    /// Keep this list in sync with the regex matchers in
    /// <see cref="ForbiddenPatterns"/>: anything that fires AND is not on
    /// this list fails the test.
    /// </summary>
    private static readonly HashSet<string> AllowlistedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // ─── Display / count surfaces (read-only, not routing) ─────────────
        // ImageAnalysisController.cs:1578-1579 — AuditContainers / CompletedContainers
        //   summary counts displayed on the IA dashboard. Drives no routing.
        "ImageAnalysisController.cs",
        // ImageAnalysisDecisionController.cs:114-116, 388, 596-597, 2007-2017 —
        //   display counts + DA-side-effects writes (NOT gates). Diagnostic
        //   warning at :2017 explicitly logs when CCS.WorkflowStage drifts
        //   from AG.Status, which is exactly the parallel-state-surface bug.
        "ImageAnalysisDecisionController.cs",
        // ContainerCompletenessController.cs:557-558, :666 — legacy display
        //   filter + raw-SQL write to bring stale rows back to "ImageAnalysis".
        "ContainerCompletenessController.cs",
        // ReadyGroupsCacheService.cs:174-177, 232-235, 280, 283, 288 — display
        //   tile counts (the "OK CCS read" example referenced in the rule
        //   note). Line 280 is the relaxed-in-B2'-B filter that no longer
        //   gates audit; it remains as a count contributor only.
        "ReadyGroupsCacheService.cs",
        // WorkflowStageStatusHelper.cs — pure helper that converts a tuple of
        //   per-stage counts into a derived status string. No DB access; can't
        //   be a gate by construction.
        "WorkflowStageStatusHelper.cs",
        // RecordCompletenessBuilder.cs:188 — assignment (`record.WorkflowStage = "Audit"`),
        //   not a comparison. Listed for completeness; the regex below
        //   intentionally only fires on equality (==) and IN clauses.
        "RecordCompletenessBuilder.cs",
        // AuditReviewController.cs:37, 57, 58, 229-244, 431, 444, 1277, 1386,
        //   1427 — legitimate fallback chain post-B2'-C (three CCS-GroupIdentifier
        //   filters + raw-SQL audit-queue read). See the rule note: the rule
        //   is "don't ADD new gates", these existed before B1.
        "AuditReviewController.cs",

        // ─── Orchestrator & service-layer pre-existing gates ───────────────
        // ImageAnalysisOrchestratorService.cs — the largest pre-existing
        //   surface: ~30 matches across intake filters (lines 651, 981, 2994,
        //   3309, 4122-4123) and per-stage display counts (lines 787-790,
        //   2919-2922, 3636-3638, 3995-3997). The intake filter is a real
        //   gate but pre-dates B1; rebuilding it is a separate sprint.
        //   Raw-SQL UPDATE statements (lines 1840, 1982, 2538) are write paths,
        //   not gates — they keep the legacy column in sync as the system
        //   transitions to AG.Status authoritative.
        "ImageAnalysisOrchestratorService.cs",
        // ContainerCompletenessService.cs:1275 — guarded-stage transition
        //   ("only promote to ImageAnalysis if currently Pending or
        //   Export-Hold"). Internal to the legacy completeness pipeline,
        //   pre-dates B1.
        "ContainerCompletenessService.cs",
        // ManualBOESelectivityService.cs:561 — admin-side completeness path
        //   for manual BOE creation; same pre-B1 surface.
        "ManualBOESelectivityService.cs",
        // DecisionSideEffectsService.cs:262 — DA-side-effects write that
        //   mirrors AG.Status into CCS.WorkflowStage. Write target, not gate.
        "DecisionSideEffectsService.cs",
        // ZombieAnalysisGroupSweeperService.cs — comment on line 26 documents
        //   the WorkflowStage='Audit' vacuous-truth gate; no live equality.
        //   Allowlisted so the documentation reference doesn't fire the test.
        "ZombieAnalysisGroupSweeperService.cs",
        // UserReadinessController.cs:378 — diagnostic message string includes
        //   the literal "WorkflowStage='Audit'" for the operator. Not a gate.
        "UserReadinessController.cs",
        // BusinessRulesController.cs:459 — seed data for a configurable
        //   business rule expression containing the literal "WorkflowStage IN
        //   ('Pending', 'ImageAnalysis', 'Audit', 'Completed')" string. Not a
        //   live gate; lives in a config row.
        "BusinessRulesController.cs",
        // ApplicationDbContext.cs:538 — comment describing a covering index
        //   on (Status, WorkflowStage). Documents the index, not a gate.
        "ApplicationDbContext.cs",
        // ModuleQueuesController.cs:99-110 — Sprint 5G2 / B1 sister endpoint
        //   (/api/_module/queues). Reads CCS.WorkflowStage specifically TO
        //   DETECT parallel-state-surface drift between AG.Status and
        //   CCS.WorkflowStage — i.e. it's the canary, not the gate.
        //   Removing this read would defeat the drift-detection.
        "ModuleQueuesController.cs",
    };

    /// <summary>
    /// Patterns that flag a CCS.WorkflowStage gate. Each match is reported
    /// with file basename + line number so triage is one click away.
    /// We deliberately do NOT match assignments (e.g. <c>x.WorkflowStage = "..."</c>):
    /// those are write paths that the legacy column still needs.
    /// </summary>
    private static readonly Regex[] ForbiddenPatterns =
    [
        // C# / LINQ equality: `<expr>.WorkflowStage == "..."` or `WorkflowStage == "..."`
        new(@"(?<!=)\bWorkflowStage\s*==\s*""[^""]*""", RegexOptions.Compiled),
        // C# / LINQ inequality: `<expr>.WorkflowStage != "..."`
        new(@"\bWorkflowStage\s*!=\s*""[^""]*""", RegexOptions.Compiled),
        // Raw-SQL inside a verbatim or interpolated string: `WorkflowStage = '...'`
        // (single-quoted SQL literal). We require the comparator to be `=`
        // (not `=`-inside-`<>=`) and a space before to avoid catching
        // `SET WorkflowStage = 'X'` UPDATE assignments — those have
        // `SET ... =` immediately preceding. Negative lookbehind for `SET `.
        new(@"(?<!SET\s)\bWorkflowStage\s*=\s*'[^']*'", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // Raw-SQL `WorkflowStage IN (...)` — case-insensitive, the gating
        // pattern raised in AuditReviewController.cs:1427 and elsewhere.
        new(@"\bWorkflowStage\s+IN\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

    /// <summary>
    /// File basenames that the scanner skips entirely. Migrations + Designer
    /// files are EF-generated and contain raw-SQL strings that touch
    /// WorkflowStage during schema evolution; designer files are mechanical.
    /// Any file ending in <c>.Tests.cs</c> or living under a <c>tests/</c>
    /// folder is also skipped — test fixtures may use the column for setup.
    /// </summary>
    private static bool ShouldSkipPath(string fullPath)
    {
        var lower = fullPath.Replace('\\', '/').ToLowerInvariant();
        if (lower.Contains("/migrations/")) return true;
        if (lower.Contains("/obj/")) return true;
        if (lower.Contains("/bin/")) return true;
        if (lower.Contains("/tests/")) return true;
        if (lower.EndsWith(".designer.cs")) return true;
        if (lower.EndsWith(".tests.cs")) return true;
        return false;
    }

    /// <summary>
    /// Resolves the repository's <c>src/</c> directory by walking up from this
    /// test source file's directory (provided at compile time via
    /// <see cref="CallerFilePathAttribute"/>). This is robust across CI / dev
    /// machines because it depends on the test source location, not the
    /// runtime working directory.
    /// </summary>
    private static string ResolveSrcRoot([CallerFilePath] string callerPath = "")
    {
        // callerPath is .../tests/NickScanCentralImagingPortal.Core.Tests/Architecture/CcsGatingPatternTests.cs
        var dir = Path.GetDirectoryName(callerPath);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "src");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            $"Could not resolve repository src/ from caller path {callerPath}.");
    }

    [Fact]
    public void NoNewCcsWorkflowStageGates_OutsideAllowlist()
    {
        var srcRoot = ResolveSrcRoot();
        Assert.True(Directory.Exists(srcRoot), $"src/ not found at {srcRoot}");

        var violations = new List<string>();
        var allowedHits = 0;

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (ShouldSkipPath(file)) continue;

            var basename = Path.GetFileName(file);
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (IOException)
            {
                // File locked by build, etc. — skip rather than flake.
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Cheap pre-filter — skip lines that don't even mention the column.
                if (!line.Contains("WorkflowStage", StringComparison.Ordinal)) continue;

                // Skip pure comment lines (// or /* ...) — the cheapest way to
                // dodge the "comment that quotes a forbidden pattern" false
                // positive without a full C# parser. Block comments spanning
                // multiple lines are a known limitation; allowlist the file
                // if it triggers.
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*"))
                    continue;

                foreach (var pattern in ForbiddenPatterns)
                {
                    var match = pattern.Match(line);
                    if (!match.Success) continue;

                    if (AllowlistedFiles.Contains(basename))
                    {
                        allowedHits++;
                    }
                    else
                    {
                        var rel = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
                        violations.Add(
                            $"  src/{rel}:{i + 1}  -->  {match.Value.Trim()}\n" +
                            $"      line: {line.Trim()}");
                    }
                    break; // one violation per line is enough
                }
            }
        }

        _output.WriteLine(
            $"[CcsGatingPatternTests] scanned src/ — {allowedHits} allowlisted hits, {violations.Count} new violations.");

        if (violations.Count > 0)
        {
            var msg =
                "New CCS.WorkflowStage gate(s) detected outside the allowlist.\n\n" +
                "RULE: AnalysisGroup.Status is the authoritative routing surface since\n" +
                "Sprint 5G2 / Bridge B1 (2026-05-07). CCS.WorkflowStage is informational\n" +
                "and lags AG.Status; gating on it has caused multiple parallel-state-surface\n" +
                "bugs (audit queue empty, MSBU3196923-class drift, etc.).\n\n" +
                "Use AnalysisGroup.Status for routing decisions. If this is a legitimate\n" +
                "display/diagnostic read, add the file basename to AllowlistedFiles in\n" +
                $"{nameof(CcsGatingPatternTests)} with a justification comment.\n\n" +
                "Violations:\n" +
                string.Join("\n", violations);
            Assert.Fail(msg);
        }

        // Sanity check: if the allowlisted-files list ever produces zero hits,
        // the regexes have rotted and the whole guard is silently pointless.
        // The rule is observed-as-of 2026-05-09; if a future cleanup removes
        // every legacy use, lower this bound to 0 in the same PR that does.
        Assert.True(allowedHits > 0,
            "Pattern test produced zero allowlisted hits — regex likely broken.");
    }

    /// <summary>
    /// Smoke test for the regex itself: keeps the test honest if a future
    /// edit silently breaks the pattern.
    /// </summary>
    [Theory]
    [InlineData("c.WorkflowStage == \"Audit\"", true)]
    [InlineData("ccs.WorkflowStage == \"ImageAnalysis\"", true)]
    [InlineData("WorkflowStage IN (@p0, @p1)", true)]
    [InlineData("WorkflowStage = 'Audit'", true)]               // raw-SQL gate
    [InlineData("SET WorkflowStage = 'Audit'", false)]          // raw-SQL write
    [InlineData("record.WorkflowStage = \"Audit\";", false)]    // C# assignment
    [InlineData("// WorkflowStage = 'Audit' (comment)", false)] // comment, but still scanned line-wise — caller-side check filters it
    public void ForbiddenPatterns_MatchExpectedShapes(string sample, bool shouldMatch)
    {
        var matched = ForbiddenPatterns.Any(p => p.IsMatch(sample));

        // The comment case is filtered by the line-prefix check in the main
        // test, not by the regex itself. Adjust expectation accordingly.
        if (sample.TrimStart().StartsWith("//"))
        {
            // The regex itself may match — we just don't report comments.
            // Skip strict equality on this case.
            return;
        }

        Assert.Equal(shouldMatch, matched);
    }
}
