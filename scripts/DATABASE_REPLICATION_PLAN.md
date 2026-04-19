# Database Replication Plan: NS_CIS Instance → MSSQLSERVER Instance

**Date:** 2026-01-01  
**Status:** ⏳ **AWAITING APPROVAL**  
**Prepared by:** AI Assistant

---

## Executive Summary

This plan outlines the complete replication strategy for migrating 3 databases from the source SQL Server 2022 instance (`localhost\NS_CIS`) to the target SQL Server 2014 default instance (`localhost` / MSSQLSERVER).

### Current State
- **Source Instance:** `localhost\NS_CIS` (SQL Server 2022 Developer Edition)
- **Target Instance:** `localhost` (SQL Server 2014 Enterprise Edition - Default Instance)
- **Databases to Replicate:** NS_CIS, ICUMS, ICUMS_Downloads
- **Current Status:** Partial replication exists (15 incomplete tables identified)

---

## 1. Pre-Replication Assessment

### 1.1 Source Instance Status
✅ **Verified:**
- All 3 databases exist and are ONLINE
- NS_CIS: 46 tables
- ICUMS: 4 tables  
- ICUMS_Downloads: 10 tables
- Total: 60 tables across 3 databases

### 1.2 Target Instance Status
✅ **Current State:**
- NS_CIS database exists (created 2026-01-01)
- ICUMS database: **MISSING** - needs to be created
- ICUMS_Downloads database: **MISSING** - needs to be created
- 15 tables have data mismatches (from previous verification)

### 1.3 Known Issues
⚠️ **Incomplete Tables (15 total):**
- **NS_CIS (9 tables):** AseScans, AseSyncLogs, ContainerBOERelations, ContainerCompletenessStatuses, EndpointUsageLog, FS6000Images, FS6000Scans, FS6000SyncLogs, RolePermissions
- **ICUMS (1 table):** IcumManifestItems
- **ICUMS_Downloads (5 tables):** BOEDocuments, CMRValidationMetrics, DownloadedFiles, ManifestItems, VehicleImports

---

## 2. Replication Strategy

### 2.1 Approach: Full Schema + Data Replication

**Method:** Use existing comprehensive replication script with enhancements

**Phases:**
1. **Database Creation** - Create missing databases (ICUMS, ICUMS_Downloads)
2. **Schema Replication** - Copy all schemas, tables, columns, data types
3. **Data Transfer** - Bulk copy all data using SqlBulkCopy
4. **Objects Replication** - Copy indexes, foreign keys, views, procedures, functions, triggers
5. **Verification** - Verify schema and data completeness
6. **Resync Incomplete Tables** - Fix the 15 identified incomplete tables

### 2.2 Replication Order

**Recommended Sequence:**
1. **ICUMS** (smallest, least dependencies)
2. **ICUMS_Downloads** (medium size)
3. **NS_CIS** (largest, most complex, has dependencies on other databases)

**Rationale:**
- Start with smaller databases to validate process
- NS_CIS may have foreign key dependencies that reference other databases
- Allows for early detection of issues

---

## 3. Detailed Execution Plan

### Phase 1: Pre-Replication Checks ✅

**Tasks:**
- [x] Verify source instance connectivity
- [x] Verify target instance connectivity  
- [x] Check database existence on both instances
- [x] Identify incomplete tables
- [ ] **Backup source databases** (recommended)
- [ ] **Backup target databases** (if they exist)
- [ ] Check disk space on target instance
- [ ] Verify SQL Server version compatibility (2022 → 2014)

**Estimated Time:** 15-30 minutes

**Scripts:**
- `Verify-DatabaseReplication.ps1` (already exists)

---

### Phase 2: Database Creation

**Tasks:**
- [ ] Create ICUMS database on target
- [ ] Create ICUMS_Downloads database on target
- [ ] Verify database creation
- [ ] Set recovery model (match source: FULL)
- [ ] Set compatibility level (SQL Server 2014 = 120)

**Estimated Time:** 5-10 minutes

**Scripts:**
- `Replicate_Databases_Comprehensive.ps1` (Phase 1)

**SQL Compatibility Notes:**
- Source: SQL Server 2022 (compatibility level 160)
- Target: SQL Server 2014 (compatibility level 120)
- Some SQL Server 2022 features may not be available
- Scripts will need to handle version differences

---

### Phase 3: Schema Replication

**Tasks:**
- [ ] Copy all schemas (non-system schemas)
- [ ] Create all tables with correct data types
- [ ] Set identity columns
- [ ] Set default constraints
- [ ] Set check constraints
- [ ] Set unique constraints
- [ ] Set primary keys

**Estimated Time:** 10-20 minutes per database

**Scripts:**
- `Replicate_Databases_Comprehensive.ps1` (Phases 2-3)

**Considerations:**
- Handle SQL Server 2022 → 2014 compatibility
- Some newer data types may need conversion
- Identity seed values must match

---

### Phase 4: Data Transfer

**Tasks:**
- [ ] Transfer data for ICUMS database
- [ ] Transfer data for ICUMS_Downloads database  
- [ ] Transfer data for NS_CIS database
- [ ] Monitor progress for large tables
- [ ] Handle identity columns correctly

**Estimated Time:** 
- ICUMS: 30-60 minutes (5.5M+ rows in IcumManifestItems)
- ICUMS_Downloads: 1-2 hours (10M+ rows in ManifestItems)
- NS_CIS: 1-2 hours (varies by table size)

**Scripts:**
- `Replicate_Databases_Comprehensive.ps1` (Phase 5)
- `Transfer_Table_Simple.ps1` (used by comprehensive script)

**Method:**
- Uses SqlBulkCopy for efficient bulk transfer
- Batch size: 10,000 rows
- Disables constraints during transfer
- Re-enables constraints after transfer

**Large Tables (Special Attention):**
- `IcumManifestItems`: ~5.5M rows
- `ManifestItems`: ~10.2M rows
- `EndpointUsageLog`: ~257K rows
- `ContainerCompletenessStatuses`: ~34K rows

---

### Phase 5: Objects Replication

**Tasks:**
- [ ] Create indexes (after data transfer for performance)
- [ ] Create foreign keys (after data transfer to avoid constraint violations)
- [ ] Copy views
- [ ] Copy stored procedures
- [ ] Copy functions
- [ ] Copy triggers

**Estimated Time:** 15-30 minutes per database

**Scripts:**
- `Replicate_Databases_Comprehensive.ps1` (Phases 4, 6-10)

**Order Matters:**
1. Data first (to avoid FK violations)
2. Indexes second (for performance)
3. Foreign keys third (to validate data integrity)
4. Views/Procs/Functions/Triggers last (depend on tables)

---

### Phase 6: Resync Incomplete Tables

**Tasks:**
- [ ] Run verification to identify current incomplete tables
- [ ] Resync each incomplete table
- [ ] Verify row counts match
- [ ] Handle any errors

**Estimated Time:** 30-60 minutes (depends on table sizes)

**Scripts:**
- `Verify-DatabaseReplication.ps1`
- `Resync-IncompleteTables.ps1`

**Known Incomplete Tables:**
- 15 tables identified from previous verification
- Will re-verify before resync

---

### Phase 7: Final Verification

**Tasks:**
- [ ] Verify all databases exist
- [ ] Verify all tables exist
- [ ] Verify row counts match for all tables
- [ ] Verify schema matches (column counts, data types)
- [ ] Check for any errors or warnings
- [ ] Generate verification report

**Estimated Time:** 10-15 minutes

**Scripts:**
- `Verify-DatabaseReplication.ps1`

**Success Criteria:**
- All 3 databases exist on target
- All 60 tables exist on target
- All row counts match source
- All schema matches source
- Zero incomplete tables
- Zero schema mismatches

---

## 4. Risk Assessment & Mitigation

### 4.1 High Risk Items

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Data loss during transfer** | Critical | Low | Full backup before replication |
| **SQL Server version incompatibility** | High | Medium | Test schema scripts, handle version differences |
| **Large table timeouts** | Medium | Medium | Increase timeouts, use batch processing |
| **Disk space exhaustion** | High | Low | Check disk space before starting |
| **Connection failures** | Medium | Low | Retry logic, connection pooling |
| **Identity column mismatches** | Medium | Low | Handle identity insert correctly |

### 4.2 Rollback Plan

**If replication fails:**
1. Stop replication process
2. Drop target databases (if created)
3. Restore from backup (if backups were taken)
4. Investigate and fix issues
5. Re-run replication

**If partial replication:**
1. Use `Resync-IncompleteTables.ps1` to fix incomplete tables
2. Re-verify after resync
3. Document any remaining issues

---

## 5. Timeline Estimate

### Conservative Estimate
- **Pre-checks:** 30 minutes
- **Database Creation:** 10 minutes
- **Schema Replication:** 60 minutes (3 databases × 20 min)
- **Data Transfer:** 3-4 hours (large tables)
- **Objects Replication:** 60 minutes (3 databases × 20 min)
- **Resync Incomplete:** 60 minutes
- **Final Verification:** 15 minutes

**Total Estimated Time:** **5-7 hours**

### Optimistic Estimate (if no issues)
- **Total Estimated Time:** **3-4 hours**

### Factors Affecting Duration
- Network speed between instances
- Disk I/O performance
- Table sizes (especially large tables)
- Number of indexes/constraints
- SQL Server version compatibility issues

---

## 6. Prerequisites & Requirements

### 6.1 System Requirements
- ✅ Source instance accessible (`localhost\NS_CIS`)
- ✅ Target instance accessible (`localhost`)
- ✅ Sufficient disk space on target (estimate: 2-3x source database size)
- ✅ SQL Server permissions (sysadmin or db_owner on both instances)
- ✅ PowerShell execution policy (Bypass)

### 6.2 Scripts Required
- ✅ `Replicate_Databases_Comprehensive.ps1`
- ✅ `Transfer_Table_Simple.ps1`
- ✅ `Verify-DatabaseReplication.ps1`
- ✅ `Resync-IncompleteTables.ps1`

### 6.3 Pre-Replication Checklist
- [ ] **Backup source databases** (strongly recommended)
- [ ] **Backup target databases** (if they exist)
- [ ] Verify disk space on target (minimum 50GB free recommended)
- [ ] Check SQL Server service status on both instances
- [ ] Verify network connectivity between instances
- [ ] Test connection strings
- [ ] Review and understand the replication scripts
- [ ] Schedule maintenance window (if needed)
- [ ] Notify stakeholders

---

## 7. Execution Steps (After Approval)

### Step 1: Pre-Flight Checks
```powershell
# Verify connectivity
sqlcmd -S "localhost\NS_CIS" -E -Q "SELECT @@VERSION"
sqlcmd -S "localhost" -E -Q "SELECT @@VERSION"

# Check disk space
# (Run appropriate disk space check command)

# Run verification
.\scripts\Verify-DatabaseReplication.ps1
```

### Step 2: Create Missing Databases
```powershell
# This will be handled by Replicate_Databases_Comprehensive.ps1
# But can be done manually if needed
```

### Step 3: Run Comprehensive Replication
```powershell
# Full replication for all 3 databases
.\scripts\sql\Replicate_Databases_Comprehensive.ps1 `
    -SourceInstance "localhost\NS_CIS" `
    -TargetInstance "localhost" `
    -Databases @("ICUMS", "ICUMS_Downloads", "NS_CIS")
```

### Step 4: Resync Incomplete Tables
```powershell
# Fix any incomplete tables
.\scripts\Resync-IncompleteTables.ps1 `
    -SourceInstance "localhost\NS_CIS" `
    -TargetInstance "localhost"
```

### Step 5: Final Verification
```powershell
# Verify everything is complete
.\scripts\Verify-DatabaseReplication.ps1 `
    -SourceInstance "localhost\NS_CIS" `
    -TargetInstance "localhost"
```

---

## 8. Post-Replication Tasks

### 8.1 Application Configuration
- [ ] Update connection strings in `appsettings.json` if needed
- [ ] Test application connectivity to target instance
- [ ] Verify all application features work with target instance
- [ ] Update any hardcoded connection strings

### 8.2 Monitoring
- [ ] Monitor application logs for connection issues
- [ ] Monitor database performance
- [ ] Check for any data inconsistencies
- [ ] Verify background services are working

### 8.3 Documentation
- [ ] Document replication completion
- [ ] Document any issues encountered
- [ ] Document any manual fixes applied
- [ ] Update runbook with lessons learned

---

## 9. Alternative Approaches Considered

### Option 1: Full Backup/Restore
**Pros:**
- Fastest method
- Preserves everything exactly

**Cons:**
- Requires SQL Server version compatibility
- May not work across different SQL Server versions (2022 → 2014)
- Requires backup file storage

**Decision:** ❌ Not viable due to version incompatibility

### Option 2: Transactional Replication
**Pros:**
- Real-time synchronization
- Automatic conflict resolution

**Cons:**
- Complex setup
- Requires ongoing maintenance
- May have version compatibility issues

**Decision:** ❌ Too complex for one-time migration

### Option 3: Schema + Data Scripts (Current Plan)
**Pros:**
- Works across SQL Server versions
- Full control over process
- Can handle version differences
- Can verify at each step

**Cons:**
- Takes longer
- More manual steps

**Decision:** ✅ **SELECTED** - Best fit for this scenario

---

## 10. Approval & Sign-Off

### Approval Required For:
- [ ] Overall replication strategy
- [ ] Execution timeline
- [ ] Risk mitigation approach
- [ ] Rollback plan

### Questions for Approval:
1. **Timing:** When should this replication be executed? (Maintenance window?)
2. **Backups:** Should we take full backups before starting?
3. **Application Downtime:** Does the application need to be stopped during replication?
4. **Testing:** Should we test on a staging environment first?
5. **Monitoring:** Who should be notified during execution?

---

## 11. Next Steps (After Approval)

1. **Review this plan** with stakeholders
2. **Schedule execution window** (if needed)
3. **Take backups** (if approved)
4. **Execute Phase 1** (Pre-checks)
5. **Execute Phases 2-5** (Full replication)
6. **Execute Phase 6** (Resync incomplete)
7. **Execute Phase 7** (Final verification)
8. **Update application configuration** (if needed)
9. **Document completion**

---

## Appendix A: Table Size Estimates

Based on current row counts:

| Database | Table | Estimated Rows | Priority |
|----------|-------|----------------|----------|
| ICUMS | IcumManifestItems | 5,555,863 | High |
| ICUMS_Downloads | ManifestItems | 10,261,511 | High |
| NS_CIS | EndpointUsageLog | 256,991 | Medium |
| NS_CIS | ContainerCompletenessStatuses | 33,907 | Medium |
| NS_CIS | AseScans | 33,278 | Medium |
| NS_CIS | AnalysisAssignments | 43,526 | Medium |

---

## Appendix B: SQL Server Version Compatibility Notes

### Source: SQL Server 2022
- Compatibility Level: 160
- Newer features available

### Target: SQL Server 2014
- Compatibility Level: 120
- Limited to SQL Server 2014 features

### Potential Issues:
- Some newer data types may not be supported
- Some newer T-SQL syntax may not work
- Identity columns should work fine
- Foreign keys should work fine

### Mitigation:
- Scripts will use standard SQL Server 2014 compatible syntax
- Data types will be mapped to compatible versions
- Test schema scripts before full replication

---

**END OF PLAN**

**Status:** ⏳ **AWAITING YOUR APPROVAL**

Please review and approve this plan before implementation begins.

