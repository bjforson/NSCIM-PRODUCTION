using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageAnalysis;
using NickScanCentralImagingPortal.Services.RecordCompleteness;

var containerNumber = args.Length > 0 ? args[0] : "PIDU4444900";
var appConnection = args.Length > 1
    ? args[1]
    : "Host=localhost;Port=55432;Database=nickscan_cmr_staging_app;Username=postgres;Pooling=false;Options=-c app.tenant_id=1";
var downloadsConnection = args.Length > 2
    ? args[2]
    : "Host=localhost;Port=55432;Database=nickscan_cmr_staging_downloads;Username=postgres;Pooling=false;Options=-c app.tenant_id=1";

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:NS_CIS_Connection"] = appConnection,
        ["ConnectionStrings:ICUMS_Downloads_Connection"] = downloadsConnection,
        ["CmrCompositeProgression:Enabled"] = "true",
        ["ImageAnalysis:MaxIdleMinutesForReadiness"] = "60",
        ["ReadyGroupsCache:ExpirationSeconds"] = "1",
        ["ReadyGroupsCache:MaxGroups"] = "25"
    })
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
    builder.SetMinimumLevel(LogLevel.Information);
});
services.AddMemoryCache();
services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(appConnection);
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});
services.AddDbContext<IcumDownloadsDbContext>(options =>
{
    options.UseNpgsql(downloadsConnection);
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});
services.AddSingleton<IRecordBuildingService, RecordBuildingService>();
services.AddSingleton<ReadyGroupsCacheService>();
services.AddSingleton<ImageAnalysisOrchestratorService>();

await using var provider = services.BuildServiceProvider();
await using var setupScope = provider.CreateAsyncScope();
var appDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var icumDb = setupScope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

await SeedAssignmentPrerequisitesAsync(appDb);

var boe = await icumDb.BOEDocuments
    .AsNoTracking()
    .SingleAsync(b => b.ContainerNumber != null && b.ContainerNumber.ToUpper() == containerNumber.ToUpper());

if (!CmrCompositeKeyHelper.TryCreate(boe.RotationNumber, boe.ContainerNumber, boe.BlNumber, out var cmrKey))
{
    Console.WriteLine($"cmr_key_valid=false container={containerNumber}");
    return 2;
}

Console.WriteLine($"cmr_key={cmrKey.OperationalKey}");
Console.WriteLine($"cmr_label={cmrKey.DisplayLabel}");

var builder = provider.GetRequiredService<IRecordBuildingService>();
await builder.BuildOrUpdateCmrRecordAsync(boe.RotationNumber, boe.ContainerNumber, boe.BlNumber, includeCmrCompositeRecords: true);

await using (var intakeScope = provider.CreateAsyncScope())
{
    var intakeDb = intakeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var orchestrator = provider.GetRequiredService<ImageAnalysisOrchestratorService>();
    await InvokePrivateTaskAsync(
        orchestrator,
        "RunIntakeWorkflowAsync",
        intakeDb,
        CancellationToken.None);
}

await using (var assignmentScope = provider.CreateAsyncScope())
{
    var assignmentDb = assignmentScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var settings = await assignmentDb.AnalysisSettings.AsTracking().FirstAsync();
    settings.Enabled = true;
    settings.AssignmentMode = "Auto";
    settings.AutoAssignStrategy = "RoundRobin";
    settings.MaxConcurrentPerUser = 5;
    settings.LeaseMinutes = 30;
    settings.UpdatedAtUtc = DateTime.UtcNow;
    await assignmentDb.SaveChangesAsync();

    var orchestrator = provider.GetRequiredService<ImageAnalysisOrchestratorService>();
    await InvokePrivateTaskAsync(
        orchestrator,
        "RunAssignmentWorkflowAsync",
        assignmentDb,
        settings,
        DateTime.UtcNow,
        CancellationToken.None);
}

await using var verifyScope = provider.CreateAsyncScope();
var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

var record = await verifyDb.RecordCompletenessStatuses
    .AsNoTracking()
    .SingleOrDefaultAsync(r => r.DeclarationNumber == cmrKey.OperationalKey);
var child = record == null
    ? null
    : await verifyDb.RecordExpectedContainers
        .AsNoTracking()
        .SingleOrDefaultAsync(c => c.RecordId == record.Id && c.ContainerNumber == cmrKey.ContainerNumber);
var group = await verifyDb.AnalysisGroups
    .AsNoTracking()
    .SingleOrDefaultAsync(g => g.GroupIdentifier == cmrKey.OperationalKey);
var analysisRecord = group == null
    ? null
    : await verifyDb.AnalysisRecords
        .AsNoTracking()
        .SingleOrDefaultAsync(r => r.GroupId == group.Id && r.ContainerNumber == cmrKey.ContainerNumber);
var assignment = group == null
    ? null
    : await verifyDb.AnalysisAssignments
        .AsNoTracking()
        .SingleOrDefaultAsync(a => a.GroupId == group.Id && a.State == "Active");
var queueEntry = assignment == null
    ? null
    : await verifyDb.AnalysisQueueEntries
        .AsNoTracking()
        .SingleOrDefaultAsync(q => q.AssignmentId == assignment.Id);
var completeness = await verifyDb.ContainerCompletenessStatuses
    .AsNoTracking()
    .SingleOrDefaultAsync(c => c.ContainerNumber == cmrKey.ContainerNumber);

Console.WriteLine($"rcs_status={record?.Status ?? "missing"} workflow={record?.WorkflowStage ?? "missing"}");
Console.WriteLine($"rec_status={child?.Status ?? "missing"}");
Console.WriteLine($"ag_status={group?.Status ?? "missing"} grouptype={group?.GroupType ?? "missing"} linked_rcs={group?.RecordCompletenessStatusId?.ToString() ?? "missing"}");
Console.WriteLine($"ar_status={analysisRecord?.Status ?? "missing"} container={analysisRecord?.ContainerNumber ?? "missing"}");
Console.WriteLine($"assignment_state={assignment?.State ?? "missing"} assigned_to={assignment?.AssignedTo ?? "missing"}");
Console.WriteLine($"queue_entry={(queueEntry == null ? "missing" : "present")}");
Console.WriteLine($"ccs_groupidentifier={completeness?.GroupIdentifier ?? "missing"} ccs_status={completeness?.Status ?? "missing"} ccs_workflow={completeness?.WorkflowStage ?? "missing"}");

var duplicateSummary = new
{
    Rcs = await verifyDb.RecordCompletenessStatuses.CountAsync(r => r.DeclarationNumber == cmrKey.OperationalKey),
    Ag = await verifyDb.AnalysisGroups.CountAsync(g => g.GroupIdentifier == cmrKey.OperationalKey),
    Ar = group == null ? 0 : await verifyDb.AnalysisRecords.CountAsync(r => r.GroupId == group.Id && r.ContainerNumber == cmrKey.ContainerNumber),
    Assignments = group == null ? 0 : await verifyDb.AnalysisAssignments.CountAsync(a => a.GroupId == group.Id && a.State == "Active"),
    QueueEntries = assignment == null ? 0 : await verifyDb.AnalysisQueueEntries.CountAsync(q => q.AssignmentId == assignment.Id)
};

Console.WriteLine($"duplicate_counts rcs={duplicateSummary.Rcs} ag={duplicateSummary.Ag} ar={duplicateSummary.Ar} active_assignments={duplicateSummary.Assignments} queue_entries={duplicateSummary.QueueEntries}");

return record?.Status == "Ready"
    && child?.Status == "Ready"
    && group?.GroupType == "CMR"
    && analysisRecord?.ContainerNumber == cmrKey.ContainerNumber
    && assignment?.State == "Active"
    && queueEntry != null
    && duplicateSummary.Rcs == 1
    && duplicateSummary.Ag == 1
    && duplicateSummary.Ar == 1
    && duplicateSummary.Assignments == 1
    && duplicateSummary.QueueEntries == 1
        ? 0
        : 1;

static async Task SeedAssignmentPrerequisitesAsync(ApplicationDbContext db)
{
    var now = DateTime.UtcNow;

    var settings = await db.AnalysisSettings.AsTracking().FirstOrDefaultAsync();
    if (settings == null)
    {
        settings = new AnalysisSettings
        {
            Enabled = true,
            AssignmentMode = "Auto",
            AutoAssignStrategy = "RoundRobin",
            LeaseMinutes = 30,
            MaxConcurrentPerUser = 5,
            MinYearForIntake = 2026,
            CreatedAtUtc = now
        };
        db.AnalysisSettings.Add(settings);
    }
    else
    {
        settings.Enabled = true;
        settings.AssignmentMode = "Auto";
        settings.AutoAssignStrategy = "RoundRobin";
        settings.LeaseMinutes = 30;
        settings.MaxConcurrentPerUser = 5;
        settings.UpdatedAtUtc = now;
    }

    var analystRole = await db.Roles.AsTracking().FirstOrDefaultAsync(r => r.Name == "Analyst");
    if (analystRole == null)
    {
        analystRole = new Role
        {
            Name = "Analyst",
            DisplayName = "Analyst",
            Description = "CMR staging analyst",
            IsSystemRole = true,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = "cmr-staging-runner"
        };
        db.Roles.Add(analystRole);
        await db.SaveChangesAsync();
    }

    var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Username == "cmr.stage.analyst");
    if (user == null)
    {
        user = new User
        {
            Username = "cmr.stage.analyst",
            Email = "cmr.stage.analyst@example.invalid",
            PasswordHash = "staging-only",
            FirstName = "CMR",
            LastName = "Staging",
            RoleId = analystRole.Id,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = "cmr-staging-runner",
            UserNumber = "CMR-STAGE-01"
        };
        db.Users.Add(user);
    }
    else
    {
        user.RoleId = analystRole.Id;
        user.IsActive = true;
        user.UpdatedAt = now;
        user.UpdatedBy = "cmr-staging-runner";
    }

    var readiness = await db.UserReadiness.AsTracking()
        .FirstOrDefaultAsync(r => r.Username == "cmr.stage.analyst" && r.Role == "Analyst");
    if (readiness == null)
    {
        readiness = new UserReadiness
        {
            Username = "cmr.stage.analyst",
            Role = "Analyst",
            IsReady = true,
            LastHeartbeat = now,
            LastChangedAt = now,
            ChangedBy = "cmr-staging-runner",
            SessionId = "cmr-staging"
        };
        db.UserReadiness.Add(readiness);
    }
    else
    {
        readiness.IsReady = true;
        readiness.LastHeartbeat = now;
        readiness.LastChangedAt = now;
        readiness.ChangedBy = "cmr-staging-runner";
        readiness.SessionId = "cmr-staging";
    }

    await db.SaveChangesAsync();
}

static async Task InvokePrivateTaskAsync(object target, string methodName, params object[] args)
{
    var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Method {methodName} not found on {target.GetType().Name}");
    var result = method.Invoke(target, args)
        ?? throw new InvalidOperationException($"Method {methodName} returned null");
    await (Task)result;
}
