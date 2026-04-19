# Database Sync Status Check and Todo Generation

## Overview
This set of scripts helps you check the progress of database synchronization from the `localhost\NS_CIS` instance to the `(local)` MSSQLSERVER instance, and generates a todo list for completing the remainder.

## Databases Checked
- **NS_CIS** (Main application database)
- **ICUMS**
- **ICUMS_Downloads**

## Scripts

### 1. Check_Database_Sync_Status.ps1
Comprehensive status check script that compares row counts between source and target databases.

**Usage:**
```powershell
.\Check_Database_Sync_Status.ps1
```

**Parameters:**
- `-SourceInstance`: Source SQL Server instance (default: "localhost\NS_CIS")
- `-TargetInstance`: Target SQL Server instance (default: "(local)")
- `-Databases`: Array of database names (default: @("NS_CIS", "ICUMS", "ICUMS_Downloads"))
- `-OutputFile`: Output file name (default: "sync_status_report.txt")

**Output:**
- Console output with progress and summary
- `sync_status_report.txt`: Detailed text report
- `sync_status_report.csv`: CSV file for analysis

**Table Status Categories:**
- **Complete**: Source and target row counts match
- **Missing**: Table doesn't exist on target
- **Incomplete**: Table exists but row counts don't match
- **Empty**: Table exists on target but has 0 rows (source may have data)
- **Error**: Error occurred during status check

### 2. Generate_Sync_Todo_List.ps1
Generates a todo list and transfer script based on status check results.

**Usage:**
```powershell
.\Generate_Sync_Todo_List.ps1
```

**Parameters:**
- `-StatusCsvFile`: Path to CSV file from status check (default: "sync_status_report.csv")
- `-TodoListFile`: Output todo list file (default: "sync_todo_list.txt")
- `-TodoScriptFile`: Output transfer script file (default: "sync_transfer_remaining_tables.ps1")

**Output:**
- `sync_todo_list.txt`: Human-readable todo list
- `sync_transfer_remaining_tables.ps1`: PowerShell script to transfer remaining tables

### 3. Check_Sync_And_Generate_Todos.ps1 (Recommended)
Master script that runs both the status check and todo generation in one step.

**Usage:**
```powershell
.\Check_Sync_And_Generate_Todos.ps1
```

**Parameters:**
- `-SourceInstance`: Source SQL Server instance (default: "localhost\NS_CIS")
- `-TargetInstance`: Target SQL Server instance (default: "(local)")
- `-Databases`: Array of database names (default: @("NS_CIS", "ICUMS", "ICUMS_Downloads"))

**Output:**
- All files from both scripts (status report, CSV, todo list, transfer script)

## Workflow

### Step 1: Check Current Status
```powershell
cd scripts\sql
.\Check_Sync_And_Generate_Todos.ps1
```

This will:
1. Check all tables in all three databases
2. Compare row counts between source and target
3. Generate a detailed status report
4. Create a todo list of tables that need transfer
5. Generate a PowerShell script to transfer remaining tables

### Step 2: Review Results
- Open `sync_todo_list.txt` to see what needs to be transferred
- Review `sync_status_report.txt` for detailed information
- Check `sync_status_report.csv` for analysis in Excel

### Step 3: Transfer Remaining Tables
```powershell
.\sync_transfer_remaining_tables.ps1
```

**Note:** This script uses `Transfer_Table_Simple.ps1` which may require a linked server named `NS_CIS_SOURCE` to be configured. If you prefer direct connections, you may need to modify the transfer approach.

### Step 4: Re-check Status
After transferring, re-run the status check to verify completion:
```powershell
.\Check_Sync_And_Generate_Todos.ps1
```

## Notes

### Linked Server Consideration
The generated transfer script (`sync_transfer_remaining_tables.ps1`) references `Transfer_Table_Simple.ps1`, which uses a linked server `[NS_CIS_SOURCE]` in its SQL queries. 

If you need to use direct connections instead of linked servers, you may need to:
1. Modify `Transfer_Table_Simple.ps1` to use direct connections (OPENROWSET or separate queries)
2. Or create a new transfer script that uses direct connections

### System Tables
The scripts automatically exclude:
- `__EFMigrationsHistory` (Entity Framework migration history)
- Other system/metadata tables

Only application tables are checked and transferred.

### Timeout Handling
If transfers timeout:
- The status check will identify which tables failed or are incomplete
- You can re-run the transfer script to retry failed tables
- Large tables may need to be transferred in batches

## Example Output

```
========================================
Database Sync Status Check & Todo Generation
========================================

Step 1: Running sync status check...

Processing: NS_CIS
  Found 45 tables
  [1/45] Checking [dbo].[Users]... Complete (12 rows)
  [2/45] Checking [dbo].[Containers]... Incomplete (Source: 15234, Target: 8901)
  ...

Overall Summary
========================================
Total Tables:      135
Complete:          98
Needs Transfer:    37
  - Missing:       5
  - Incomplete:    28
  - Empty:         4
Errors:            0

Step 2: Generating todo list...

Todo list saved to: sync_todo_list.txt
Transfer script saved to: sync_transfer_remaining_tables.ps1
```

