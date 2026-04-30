# Shared Npgsql helper for the postgres-equivalent diagnostic scripts.
# Dot-source this from any sibling script:   . "$PSScriptRoot\_NpgsqlHelper.ps1"
#
# Why this exists:
#   - Production migrated from SQL Server to Postgres long ago. The legacy scripts
#     in scripts/*.ps1 use Invoke-Sqlcmd against port 1433 and AspNetUsers tables;
#     none of them work against current prod (Postgres on port 5432, lowercase
#     unquoted columns, users/roles instead of AspNet*).
#   - Npgsql.dll has runtime dependencies (Microsoft.Extensions.Logging.Abstractions
#     and DependencyInjection.Abstractions) that are not next to publish/API/Npgsql.dll
#     in a way PowerShell's Add-Type can resolve. The migration-runner bin/Debug
#     folder happens to ship all three side-by-side, which is what we use here.
#   - Requires PowerShell 7+ on .NET 10 (matching the deployed runtime). Windows
#     PowerShell 5.1 will fail to load .NET 10 Npgsql.

$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSEdition -ne "Core") {
    throw "These scripts require PowerShell 7+ (pwsh). Windows PowerShell 5.1 cannot load .NET 10 Npgsql."
}

# Load Npgsql + its two .NET-10-versioned dependencies once per session.
if (-not ([System.Management.Automation.PSTypeName]'Npgsql.NpgsqlConnection').Type) {
    $npgsqlBase = 'C:\Shared\NSCIM_PRODUCTION\tools\migration-runner\bin\Debug\net10.0'
    $required = @(
        'Microsoft.Extensions.Logging.Abstractions.dll',
        'Microsoft.Extensions.DependencyInjection.Abstractions.dll',
        'Npgsql.dll'
    )
    foreach ($dll in $required) {
        $full = Join-Path $npgsqlBase $dll
        if (-not (Test-Path $full)) {
            throw "Required DLL not found: $full. Ensure migration-runner has been built (dotnet build tools/migration-runner)."
        }
        Add-Type -Path $full
    }
}

function Open-NscimConnection {
    <#
    .SYNOPSIS
        Open a Postgres connection to the NSCIM application DB and set the RLS tenant.
    .DESCRIPTION
        - Uses NICKSCAN_DB_PASSWORD env var (the nscim_app non-superuser app role).
        - Use $UseSuperuser switch to connect as `postgres` with NICKHR_DB_PASSWORD —
          required ONLY for DDL or DML that nscim_app cannot execute (e.g. ALTER, GRANT,
          some UPDATEs that bypass RLS). All Diagnose-* scripts can use the default.
        - Sets `app.tenant_id` for the session via SET LOCAL inside a transaction.
          RLS is fail-closed since 2026-04-25; without this, every SELECT returns 0 rows.
    .PARAMETER PgHost
        Postgres host (default 127.0.0.1).
    .PARAMETER Port
        Port (default 5432).
    .PARAMETER Database
        DB name (default nickscan_production).
    .PARAMETER TenantId
        RLS tenant id (default '1' — the production single-tenant).
    .PARAMETER UseSuperuser
        Use postgres / NICKHR_DB_PASSWORD instead of nscim_app / NICKSCAN_DB_PASSWORD.
    .OUTPUTS
        A hashtable @{ Connection = NpgsqlConnection; Transaction = NpgsqlTransaction }
        Caller is responsible for committing/rolling back the transaction and disposing
        the connection (use Close-NscimConnection).
    #>
    [CmdletBinding()]
    param(
        [string]$PgHost = "127.0.0.1",
        [int]$Port = 5432,
        [string]$Database = "nickscan_production",
        [string]$TenantId = "1",
        [switch]$UseSuperuser
    )

    if ($UseSuperuser) {
        $user = 'postgres'
        $pwd  = $env:NICKHR_DB_PASSWORD
        if (-not $pwd) { throw "NICKHR_DB_PASSWORD is not set (postgres superuser password required for -UseSuperuser)" }
    } else {
        $user = 'nscim_app'
        $pwd  = $env:NICKSCAN_DB_PASSWORD
        if (-not $pwd) { throw "NICKSCAN_DB_PASSWORD is not set (set the env var first)" }
    }

    $cs = "Host=$PgHost;Port=$Port;Database=$Database;Username=$user;Password=$pwd;Timeout=10;Pooling=false"
    $conn = New-Object Npgsql.NpgsqlConnection($cs)
    $conn.Open()

    # SET LOCAL only takes effect inside an explicit transaction. Every diagnostic
    # query in this script family must run inside this transaction or RLS clamps it.
    $tx = $conn.BeginTransaction()
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx
    $cmd.CommandText = "SET LOCAL app.tenant_id = '$TenantId'"
    $null = $cmd.ExecuteNonQuery()

    return @{ Connection = $conn; Transaction = $tx }
}

function Close-NscimConnection {
    <# Commit (read-only — nothing to commit) and dispose. #>
    param([Parameter(Mandatory=$true)][hashtable]$Handle)
    try { if ($Handle.Transaction.Connection) { $Handle.Transaction.Commit() } } catch { }
    try { $Handle.Connection.Close() } catch { }
    try { $Handle.Connection.Dispose() } catch { }
}

function Invoke-NscimQuery {
    <#
    .SYNOPSIS
        Run a SELECT and return a list of PSCustomObject rows (drop-in for Invoke-Sqlcmd).
    .DESCRIPTION
        Each row exposes columns by their lowercase Postgres name (consistent with
        EF's unquoted-identifier landing). Use the original SQL Server CamelCase only
        in display formatters; access fields by the actual Postgres column name.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][hashtable]$Handle,
        [Parameter(Mandatory=$true)][string]$Sql,
        [hashtable]$Parameters
    )
    $cmd = $Handle.Connection.CreateCommand()
    $cmd.Transaction = $Handle.Transaction
    $cmd.CommandText = $Sql
    if ($Parameters) {
        foreach ($k in $Parameters.Keys) {
            $null = $cmd.Parameters.AddWithValue($k, $Parameters[$k])
        }
    }
    $rdr = $cmd.ExecuteReader()
    $rows = New-Object System.Collections.Generic.List[object]
    try {
        while ($rdr.Read()) {
            $row = [ordered]@{}
            for ($i = 0; $i -lt $rdr.FieldCount; $i++) {
                $name = $rdr.GetName($i)
                $val = if ($rdr.IsDBNull($i)) { $null } else { $rdr.GetValue($i) }
                $row[$name] = $val
            }
            $rows.Add([pscustomobject]$row) | Out-Null
        }
    } finally { $rdr.Close() }
    return ,$rows
}

function Invoke-NscimNonQuery {
    <#
    .SYNOPSIS
        Run a non-query (UPDATE / INSERT / DDL). Returns affected-row count.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][hashtable]$Handle,
        [Parameter(Mandatory=$true)][string]$Sql,
        [hashtable]$Parameters
    )
    $cmd = $Handle.Connection.CreateCommand()
    $cmd.Transaction = $Handle.Transaction
    $cmd.CommandText = $Sql
    if ($Parameters) {
        foreach ($k in $Parameters.Keys) {
            $null = $cmd.Parameters.AddWithValue($k, $Parameters[$k])
        }
    }
    return $cmd.ExecuteNonQuery()
}
