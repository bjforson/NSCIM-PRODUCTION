// Check User Readiness Heartbeats - C# Script
// Run with: dotnet script CheckUserReadiness.csx

#r "nuget: System.Data.SqlClient, 4.8.6"

using System;
using System.Data;
using System.Data.SqlClient;

var connectionString = "Server=127.0.0.1,1433;Database=NS_CIS;Trusted_Connection=true;MultipleActiveResultSets=true;Encrypt=true;TrustServerCertificate=true;";

Console.WriteLine("========================================");
Console.WriteLine("User Readiness Heartbeat Checker");
Console.WriteLine("========================================");
Console.WriteLine();

try
{
    using (var connection = new SqlConnection(connectionString))
    {
        connection.Open();
        Console.WriteLine("✅ Connected to database");
        Console.WriteLine();

        // Check current status
        Console.WriteLine("=== CURRENT USER READINESS STATUS ===");
        Console.WriteLine();

        var checkQuery = @"
SELECT 
    Username,
    Role,
    IsReady,
    LastHeartbeat,
    DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) AS MinutesSinceHeartbeat,
    CASE 
        WHEN IsReady = 0 THEN 'NOT READY'
        WHEN DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) > 60 THEN 'HEARTBEAT EXPIRED (>60 min)'
        WHEN DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) <= 60 THEN 'READY'
        ELSE 'UNKNOWN'
    END AS Status
FROM UserReadiness
WHERE Role IN ('Analyst', 'Audit')
ORDER BY Role, LastHeartbeat DESC;
";

        using (var command = new SqlCommand(checkQuery, connection))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.HasRows)
            {
                Console.WriteLine("⚠️  No UserReadiness records found for Analyst or Audit roles");
            }
            else
            {
                Console.WriteLine($"{"Username",-16} | {"Role",-7} | {"IsReady",-7} | {"LastHeartbeat",-22} | {"MinutesSince",-12} | Status");
                Console.WriteLine(new string('-', 100));

                var expiredCount = 0;
                var readyCount = 0;

                while (reader.Read())
                {
                    var username = reader["Username"].ToString();
                    var role = reader["Role"].ToString();
                    var isReady = reader["IsReady"].ToString();
                    var lastHeartbeat = reader["LastHeartbeat"].ToString();
                    var minutesSince = reader["MinutesSinceHeartbeat"].ToString();
                    var status = reader["Status"].ToString();

                    Console.WriteLine($"{username,-16} | {role,-7} | {isReady,-7} | {lastHeartbeat,-22} | {minutesSince,-12} | {status}");

                    if (status == "HEARTBEAT EXPIRED (>60 min)") expiredCount++;
                    if (status == "READY") readyCount++;
                }

                Console.WriteLine();
                Console.WriteLine("=== SUMMARY ===");
                Console.WriteLine();

                // Summary query
                var summaryQuery = @"
SELECT 
    Role,
    COUNT(*) AS TotalUsers,
    SUM(CASE WHEN IsReady = 1 THEN 1 ELSE 0 END) AS ReadyUsers,
    SUM(CASE WHEN IsReady = 1 AND DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) <= 60 THEN 1 ELSE 0 END) AS ReadyWithin60Min,
    SUM(CASE WHEN IsReady = 1 AND DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) > 60 THEN 1 ELSE 0 END) AS ReadyButExpired
FROM UserReadiness
WHERE Role IN ('Analyst', 'Audit')
GROUP BY Role;
";

                using (var summaryCommand = new SqlCommand(summaryQuery, connection))
                using (var summaryReader = summaryCommand.ExecuteReader())
                {
                    while (summaryReader.Read())
                    {
                        Console.WriteLine($"Role: {summaryReader["Role"]}");
                        Console.WriteLine($"  Total Users: {summaryReader["TotalUsers"]}");
                        Console.WriteLine($"  Ready Users: {summaryReader["ReadyUsers"]}");
                        Console.WriteLine($"  Ready (within 60 min): {summaryReader["ReadyWithin60Min"]}");
                        Console.WriteLine($"  Ready (expired >60 min): {summaryReader["ReadyButExpired"]}");
                        Console.WriteLine();
                    }
                }

                // Update if needed
                if (expiredCount > 0)
                {
                    Console.WriteLine($"⚠️  Found {expiredCount} user(s) with expired heartbeats");
                    Console.WriteLine();
                    Console.WriteLine("Updating heartbeats...");

                    var updateQuery = @"
UPDATE UserReadiness
SET LastHeartbeat = GETUTCDATE(),
    LastChangedAt = GETUTCDATE()
WHERE IsReady = 1 
    AND Role IN ('Analyst', 'Audit')
    AND LastHeartbeat < DATEADD(MINUTE, -60, GETUTCDATE());
";

                    using (var updateCommand = new SqlCommand(updateQuery, connection))
                    {
                        var rowsAffected = updateCommand.ExecuteNonQuery();
                        Console.WriteLine($"✅ Updated {rowsAffected} user readiness record(s)");
                        Console.WriteLine();
                        Console.WriteLine("Heartbeats updated! Assignments should be created on the next assignment cycle.");
                    }
                }
                else if (readyCount > 0)
                {
                    Console.WriteLine($"✅ {readyCount} user(s) are ready for assignment (within 60 minutes)");
                }
                else
                {
                    Console.WriteLine("⚠️  No users are currently ready for assignment");
                }
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Environment.Exit(1);
}

Console.WriteLine();
Console.WriteLine("========================================");
Console.WriteLine("Check Complete");
Console.WriteLine("========================================");

