using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.RecordCompleteness;

var options = PilotOptions.Parse(args);
if (options.ShowHelp)
{
    PilotOptions.PrintHelp();
    return 0;
}

if (options.Mode == RunMode.Apply && !options.ConfirmApply)
{
    Console.Error.WriteLine("Refusing to write without --confirm-apply. Run dry-run first, then pass --confirm-apply for apply mode.");
    return 2;
}

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:NS_CIS_Connection"] = options.AppConnection,
        ["ConnectionStrings:ICUMS_Downloads_Connection"] = options.DownloadsConnection,
        ["CmrCompositeProgression:Enabled"] = "true"
    })
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(builder =>
{
    builder.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
    builder.SetMinimumLevel(options.Verbose ? LogLevel.Information : LogLevel.Warning);
});
services.AddDbContext<ApplicationDbContext>(db =>
{
    db.UseNpgsql(options.AppConnection);
    db.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});
services.AddDbContext<IcumDownloadsDbContext>(db =>
{
    db.UseNpgsql(options.DownloadsConnection);
    db.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});
services.AddSingleton<IRecordBuildingService, RecordBuildingService>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var downloadsDb = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
var recordBuilder = scope.ServiceProvider.GetRequiredService<IRecordBuildingService>();

var runId = options.RunId ?? $"cmr-pilot-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
Console.WriteLine($"run_id={runId}");
Console.WriteLine($"mode={options.Mode.ToString().ToLowerInvariant()} limit={options.Limit} create_analysis_group={options.CreateAnalysisGroup}");

var candidates = await LoadCandidatesAsync(appDb, downloadsDb, options, CancellationToken.None);
Console.WriteLine($"candidate_count={candidates.Count}");

var summary = new RunSummary();
foreach (var candidate in candidates)
{
    summary.Candidates++;
    PrintCandidate(candidate);

    if (!candidate.IsReadyCandidate)
    {
        summary.SkippedNotReady++;
        continue;
    }

    if (candidate.HasUnsafeDuplicates)
    {
        summary.SkippedDuplicates++;
        continue;
    }

    if (options.Mode == RunMode.DryRun)
    {
        summary.DryRunReady++;
        continue;
    }

    try
    {
        await recordBuilder.BuildOrUpdateCmrRecordAsync(
            candidate.Key.RotationNumber,
            candidate.Key.ContainerNumber,
            candidate.Key.BlNumber,
            includeCmrCompositeRecords: true);

        summary.RecordsBuiltOrUpdated++;

        if (options.CreateAnalysisGroup)
        {
            var intakeResult = await CreateTargetedAnalysisGroupAsync(
                appDb,
                candidate.Key,
                runId,
                CancellationToken.None);

            summary.GroupsCreated += intakeResult.GroupsCreated;
            summary.GroupsAlreadyPresent += intakeResult.GroupsAlreadyPresent;
            summary.AnalysisRecordsCreated += intakeResult.AnalysisRecordsCreated;
            summary.CompletenessRowsStamped += intakeResult.CompletenessRowsStamped;
        }
    }
    catch (Exception ex)
    {
        summary.Errors++;
        Console.WriteLine($"error container={candidate.Key.ContainerNumber} key={candidate.Key.OperationalKey} message={ex.Message}");
    }
}

Console.WriteLine("summary_begin");
Console.WriteLine($"candidates={summary.Candidates}");
Console.WriteLine($"dry_run_ready={summary.DryRunReady}");
Console.WriteLine($"records_built_or_updated={summary.RecordsBuiltOrUpdated}");
Console.WriteLine($"groups_created={summary.GroupsCreated}");
Console.WriteLine($"groups_already_present={summary.GroupsAlreadyPresent}");
Console.WriteLine($"analysis_records_created={summary.AnalysisRecordsCreated}");
Console.WriteLine($"completeness_rows_stamped={summary.CompletenessRowsStamped}");
Console.WriteLine($"skipped_not_ready={summary.SkippedNotReady}");
Console.WriteLine($"skipped_duplicates={summary.SkippedDuplicates}");
Console.WriteLine($"errors={summary.Errors}");
Console.WriteLine("summary_end");

return summary.Errors == 0 ? 0 : 1;

static async Task<List<CmrPilotCandidate>> LoadCandidatesAsync(
    ApplicationDbContext appDb,
    IcumDownloadsDbContext downloadsDb,
    PilotOptions options,
    CancellationToken ct)
{
    var requestedContainers = options.Containers
        .Select(Normalize)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    List<string> seedContainers;
    if (requestedContainers.Count > 0)
    {
        seedContainers = requestedContainers;
    }
    else
    {
        seedContainers = await appDb.ContainerCompletenessStatuses
            .AsNoTracking()
            .Where(c => c.ClearanceType == "CMR"
                     && c.HasScannerData
                     && c.HasICUMSData
                     && c.HasImageData)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => c.ContainerNumber.ToUpper())
            .Distinct()
            .Take(options.Limit)
            .ToListAsync(ct);
    }

    if (seedContainers.Count == 0)
        return new List<CmrPilotCandidate>();

    var boeRows = await downloadsDb.BOEDocuments
        .AsNoTracking()
        .Where(b => b.ClearanceType == "CMR"
                 && b.ContainerNumber != null
                 && seedContainers.Contains(b.ContainerNumber.ToUpper()))
        .ToListAsync(ct);

    var ccsRows = await appDb.ContainerCompletenessStatuses
        .AsNoTracking()
        .Where(c => seedContainers.Contains(c.ContainerNumber.ToUpper()))
        .ToListAsync(ct);

    var candidates = new List<CmrPilotCandidate>();
    foreach (var boe in boeRows
                 .OrderByDescending(b => b.UpdatedAt)
                 .ThenByDescending(b => b.CreatedAt)
                 .ThenByDescending(b => b.Id))
    {
        if (!CmrCompositeKeyHelper.TryCreate(boe.RotationNumber, boe.ContainerNumber, boe.BlNumber, out var key))
            continue;

        if (candidates.Any(c => string.Equals(c.Key.OperationalKey, key.OperationalKey, StringComparison.OrdinalIgnoreCase)))
            continue;

        var rowsForContainer = ccsRows
            .Where(c => string.Equals(c.ContainerNumber, key.ContainerNumber, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.UpdatedAt)
            .ToList();

        var readyRows = rowsForContainer
            .Where(c => c.HasScannerData && c.HasICUMSData && c.HasImageData)
            .ToList();

        var duplicateCounts = await LoadDuplicateCountsAsync(appDb, key, ct);
        candidates.Add(new CmrPilotCandidate(
            key,
            boe.Id,
            rowsForContainer.Count,
            readyRows.Count,
            readyRows.FirstOrDefault()?.Status,
            readyRows.FirstOrDefault()?.WorkflowStage,
            readyRows.FirstOrDefault()?.GroupIdentifier,
            duplicateCounts));

        if (candidates.Count >= options.Limit)
            break;
    }

    return candidates;
}

static async Task<DuplicateCounts> LoadDuplicateCountsAsync(
    ApplicationDbContext appDb,
    CmrCompositeKey key,
    CancellationToken ct)
{
    var rcsCount = await appDb.RecordCompletenessStatuses
        .AsNoTracking()
        .CountAsync(r => r.DeclarationNumber == key.OperationalKey, ct);

    var groups = await appDb.AnalysisGroups
        .AsNoTracking()
        .Where(g => g.GroupIdentifier == key.OperationalKey)
        .Select(g => new { g.Id })
        .ToListAsync(ct);

    var groupIds = groups.Select(g => g.Id).ToList();
    var arCount = groupIds.Count == 0
        ? 0
        : await appDb.AnalysisRecords
            .AsNoTracking()
            .CountAsync(r => groupIds.Contains(r.GroupId)
                          && r.ContainerNumber == key.ContainerNumber, ct);

    var activeAssignmentCount = groupIds.Count == 0
        ? 0
        : await appDb.AnalysisAssignments
            .AsNoTracking()
            .CountAsync(a => groupIds.Contains(a.GroupId)
                          && a.State == "Active", ct);

    return new DuplicateCounts(rcsCount, groups.Count, arCount, activeAssignmentCount);
}

static async Task<TargetedIntakeResult> CreateTargetedAnalysisGroupAsync(
    ApplicationDbContext appDb,
    CmrCompositeKey key,
    string runId,
    CancellationToken ct)
{
    var record = await appDb.RecordCompletenessStatuses
        .AsTracking()
        .SingleOrDefaultAsync(r => r.DeclarationNumber == key.OperationalKey, ct);

    if (record == null)
        throw new InvalidOperationException($"CMR record {key.OperationalKey} was not created.");

    var readyChildren = await appDb.RecordExpectedContainers
        .AsNoTracking()
        .Where(c => c.RecordId == record.Id && c.Status == "Ready")
        .ToListAsync(ct);

    if (readyChildren.Count == 0)
        throw new InvalidOperationException($"CMR record {key.OperationalKey} has no ready children.");

    var scannerType = record.ScannerType ?? readyChildren.First().ScannerType;
    if (string.IsNullOrWhiteSpace(scannerType))
    {
        scannerType = await appDb.ContainerCompletenessStatuses
            .AsNoTracking()
            .Where(c => c.ContainerNumber == key.ContainerNumber && c.ScannerType != null)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => c.ScannerType)
            .FirstOrDefaultAsync(ct);
    }

    var existing = await appDb.AnalysisGroups
        .AsTracking()
        .FirstOrDefaultAsync(g => g.GroupIdentifier == key.OperationalKey
                               && g.ScannerType == scannerType, ct);
    if (existing != null)
    {
        if (!existing.RecordCompletenessStatusId.HasValue)
        {
            existing.RecordCompletenessStatusId = record.Id;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await appDb.SaveChangesAsync(ct);
        }

        var stampedExisting = await StampCompletenessRowsAsync(appDb, key, readyChildren, ct);
        return new TargetedIntakeResult(0, 1, 0, stampedExisting);
    }

    await using var tx = await appDb.Database.BeginTransactionAsync(ct);

    var group = new AnalysisGroup
    {
        GroupIdentifier = key.OperationalKey,
        NormalizedGroupIdentifier = key.OperationalKey,
        GroupType = "CMR",
        ScannerType = scannerType,
        Priority = 0,
        RecordCompletenessStatusId = record.Id,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        WaveNumber = 1,
        WaveCreatedReason = Truncate($"Pilot:{runId}", 50)
    };
    appDb.AnalysisGroups.Add(group);
    await appDb.SaveChangesAsync(ct);

    var createdRecords = 0;
    foreach (var child in readyChildren)
    {
        var exists = await appDb.AnalysisRecords
            .AsNoTracking()
            .AnyAsync(r => r.GroupId == group.Id && r.ContainerNumber == child.ContainerNumber, ct);
        if (exists)
            continue;

        appDb.AnalysisRecords.Add(new AnalysisRecord
        {
            GroupId = group.Id,
            ContainerNumber = child.ContainerNumber,
            ScannerType = child.ScannerType ?? scannerType,
            Status = "Ready",
            CreatedAtUtc = DateTime.UtcNow
        });
        createdRecords++;
    }
    await appDb.SaveChangesAsync(ct);

#pragma warning disable CS0618
    var parentGroup = new AnalysisParentGroup
    {
        GroupIdentifier = key.OperationalKey,
        ScannerType = scannerType,
        TotalExpectedContainers = record.TotalExpectedContainers,
        Status = "Active",
        CreatedAtUtc = DateTime.UtcNow
    };
    appDb.AnalysisParentGroups.Add(parentGroup);
    await appDb.SaveChangesAsync(ct);

    group.ParentGroupId = parentGroup.Id;
#pragma warning restore CS0618
    group.UpdatedAtUtc = DateTime.UtcNow;
    await appDb.SaveChangesAsync(ct);

    var stamped = await StampCompletenessRowsAsync(appDb, key, readyChildren, ct);
    await tx.CommitAsync(ct);

    return new TargetedIntakeResult(1, 0, createdRecords, stamped);
}

static async Task<int> StampCompletenessRowsAsync(
    ApplicationDbContext appDb,
    CmrCompositeKey key,
    IReadOnlyList<RecordExpectedContainer> readyChildren,
    CancellationToken ct)
{
    var containers = readyChildren
        .Select(c => c.ContainerNumber)
        .Where(c => !string.IsNullOrWhiteSpace(c))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (containers.Length == 0)
        return 0;

    return await appDb.Database.ExecuteSqlInterpolatedAsync($@"
        UPDATE containercompletenessstatuses
        SET groupidentifier = {key.OperationalKey},
            updatedat = now() AT TIME ZONE 'UTC'
        WHERE containernumber = ANY({containers})
          AND (groupidentifier IS NULL OR btrim(groupidentifier) = '')", ct);
}

static void PrintCandidate(CmrPilotCandidate candidate)
{
    Console.WriteLine(
        "candidate container={0} key={1} label=\"{2}\" boe_id={3} ccs_rows={4} ready_ccs_rows={5} ccs_status={6} ccs_workflow={7} ccs_groupidentifier={8} rcs={9} ag={10} ar={11} active_assignments={12} ready={13} unsafe_duplicates={14}",
        candidate.Key.ContainerNumber,
        candidate.Key.OperationalKey,
        candidate.Key.DisplayLabel,
        candidate.BoeDocumentId,
        candidate.CompletenessRows,
        candidate.ReadyCompletenessRows,
        candidate.SampleCompletenessStatus ?? "missing",
        candidate.SampleWorkflowStage ?? "missing",
        string.IsNullOrWhiteSpace(candidate.SampleGroupIdentifier) ? "blank" : candidate.SampleGroupIdentifier,
        candidate.DuplicateCounts.Rcs,
        candidate.DuplicateCounts.AnalysisGroups,
        candidate.DuplicateCounts.AnalysisRecords,
        candidate.DuplicateCounts.ActiveAssignments,
        candidate.IsReadyCandidate,
        candidate.HasUnsafeDuplicates);
}

static string Normalize(string value) => value.Trim().ToUpperInvariant();

static string Truncate(string value, int maxLength) =>
    value.Length <= maxLength ? value : value[..maxLength];

internal enum RunMode
{
    DryRun,
    Apply
}

internal sealed record PilotOptions(
    RunMode Mode,
    bool ConfirmApply,
    bool ShowHelp,
    bool Verbose,
    int Limit,
    IReadOnlyList<string> Containers,
    string AppConnection,
    string DownloadsConnection,
    bool CreateAnalysisGroup,
    string? RunId)
{
    public static PilotOptions Parse(string[] args)
    {
        var mode = RunMode.DryRun;
        var confirmApply = false;
        var showHelp = false;
        var verbose = false;
        var limit = 25;
        var containers = new List<string>();
        var createAnalysisGroup = true;
        string? runId = null;

        var appConnection = "Host=localhost;Port=55432;Database=nickscan_cmr_staging_app;Username=postgres;Pooling=false;Options=-c app.tenant_id=1";
        var downloadsConnection = "Host=localhost;Port=55432;Database=nickscan_cmr_staging_downloads;Username=postgres;Pooling=false;Options=-c app.tenant_id=1";

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string NextValue()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Missing value after {arg}");
                return args[++i];
            }

            switch (arg.ToLowerInvariant())
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--mode":
                    var modeValue = NextValue();
                    mode = modeValue.Equals("apply", StringComparison.OrdinalIgnoreCase)
                        ? RunMode.Apply
                        : RunMode.DryRun;
                    break;
                case "--apply":
                    mode = RunMode.Apply;
                    break;
                case "--dry-run":
                    mode = RunMode.DryRun;
                    break;
                case "--confirm-apply":
                    confirmApply = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--limit":
                    limit = Math.Max(1, int.Parse(NextValue()));
                    break;
                case "--container":
                    containers.Add(NextValue());
                    break;
                case "--app":
                    appConnection = NextValue();
                    break;
                case "--downloads":
                    downloadsConnection = NextValue();
                    break;
                case "--record-only":
                    createAnalysisGroup = false;
                    break;
                case "--run-id":
                    runId = NextValue();
                    break;
                default:
                    if (!arg.StartsWith("-"))
                    {
                        containers.Add(arg);
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown argument: {arg}");
                    }
                    break;
            }
        }

        return new PilotOptions(
            mode,
            confirmApply,
            showHelp,
            verbose,
            limit,
            containers,
            appConnection,
            downloadsConnection,
            createAnalysisGroup,
            runId);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
CMR composite pilot runner

Defaults to disposable local staging DBs on localhost:55432.

Usage:
  dotnet run --project tools\cmr-composite-pilot-runner -- --dry-run --container PIDU4444900
  dotnet run --project tools\cmr-composite-pilot-runner -- --apply --confirm-apply --container PIDU4444900

Options:
  --dry-run                 Inspect candidates only. Default.
  --apply                   Build CMR record and targeted CMR analysis group.
  --confirm-apply           Required with --apply.
  --container <number>      Limit to one container. Can be repeated.
  --limit <n>               Max candidates when no container is supplied. Default 25.
  --record-only             Build/update CMR record only; do not create AnalysisGroup/AnalysisRecord.
  --app <connection>        Application DB connection string.
  --downloads <connection>  ICUMS downloads DB connection string.
  --run-id <id>             Correlation id printed in output and wave reason.
  --verbose                 Show service logs.
""");
    }
}

internal sealed record CmrPilotCandidate(
    CmrCompositeKey Key,
    int BoeDocumentId,
    int CompletenessRows,
    int ReadyCompletenessRows,
    string? SampleCompletenessStatus,
    string? SampleWorkflowStage,
    string? SampleGroupIdentifier,
    DuplicateCounts DuplicateCounts)
{
    public bool IsReadyCandidate => ReadyCompletenessRows > 0;
    public bool HasUnsafeDuplicates =>
        DuplicateCounts.Rcs > 1
        || DuplicateCounts.AnalysisGroups > 1
        || DuplicateCounts.AnalysisRecords > 1
        || DuplicateCounts.ActiveAssignments > 1;
}

internal sealed record DuplicateCounts(
    int Rcs,
    int AnalysisGroups,
    int AnalysisRecords,
    int ActiveAssignments);

internal sealed record TargetedIntakeResult(
    int GroupsCreated,
    int GroupsAlreadyPresent,
    int AnalysisRecordsCreated,
    int CompletenessRowsStamped);

internal sealed class RunSummary
{
    public int Candidates { get; set; }
    public int DryRunReady { get; set; }
    public int RecordsBuiltOrUpdated { get; set; }
    public int GroupsCreated { get; set; }
    public int GroupsAlreadyPresent { get; set; }
    public int AnalysisRecordsCreated { get; set; }
    public int CompletenessRowsStamped { get; set; }
    public int SkippedNotReady { get; set; }
    public int SkippedDuplicates { get; set; }
    public int Errors { get; set; }
}
