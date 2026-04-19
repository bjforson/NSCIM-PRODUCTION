# Background Services Optimization Implementation Plan

**Date**: January 4, 2026  
**Objective**: Optimize 30 background services to reduce resource overhead and improve efficiency  
**Current State**: 30 concurrent services, ~3-4 GB memory, connection pool pressure  
**Target State**: 15-18 services, ~2-3 GB memory, optimized connection usage

---

## 📋 **PLAN OVERVIEW**

### **Phases**
1. **Phase 1: Quick Wins** (1-2 days) - Low risk, immediate benefits
2. **Phase 2: Service Consolidation** (3-5 days) - Medium risk, significant benefits
3. **Phase 3: Advanced Optimizations** (2-3 days) - Low risk, incremental benefits

### **Total Estimated Time**: 6-10 days
### **Expected Benefits**:
- **Memory**: Reduce from 3-4 GB to 2-3 GB (25-33% reduction)
- **Connections**: Reduce peak usage by 40-50%
- **Query Efficiency**: Reduce duplicate queries by 60-80%
- **Maintainability**: Simpler architecture with fewer services

---

## 🚀 **PHASE 1: QUICK WINS** (1-2 days)

**Priority**: 🔴 **HIGH**  
**Risk**: 🟢 **LOW**  
**Impact**: 🟡 **MEDIUM**  
**Dependencies**: None

### **Task 1.1: Increase Connection Pool Sizes**

**Objective**: Prevent connection pool exhaustion  
**Time Estimate**: 30 minutes  
**Files to Modify**:
- `src/NickScanCentralImagingPortal.API/appsettings.json`

**Changes**:
```json
"ConnectionStrings": {
  "NS_CIS_Connection": "...Max Pool Size=100;Min Pool Size=10;...",  // Was: 50/5
  "ICUMS_Connection": "...Max Pool Size=50;Min Pool Size=5;...",    // Was: 25/2
  "ICUMS_Downloads_Connection": "...Max Pool Size=50;Min Pool Size=5;..."  // Was: 25/2
}
```

**Expected Benefit**: Eliminate connection pool exhaustion errors  
**Risk**: Low - SQL Server can handle 100+ connections easily  
**Testing**: Monitor connection pool usage after deployment

---

### **Task 1.2: Implement Shared Caching for Ready Groups**

**Objective**: Reduce duplicate queries for ready groups  
**Time Estimate**: 4-6 hours  
**Files to Create**:
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ReadyGroupsCacheService.cs`

**Files to Modify**:
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/AssignmentWorker.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/IntakeWorker.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/HousekeepingWorker.cs`
- `src/NickScanCentralImagingPortal.Services/ServiceConfiguration.cs`

**Implementation**:
1. Create `ReadyGroupsCacheService` (singleton)
   - Cache ready groups query results
   - 30-second cache expiration
   - Invalidate on group status changes
2. Inject into workers that query ready groups
3. Replace direct queries with cache lookups

**Expected Benefit**: 
- Reduce "ready groups" queries by 70-80%
- Reduce database load during peak times

**Risk**: Low - Cache invalidation is straightforward  
**Testing**: 
- Verify cache hits in logs
- Monitor database query reduction

---

### **Task 1.3: Implement Shared Caching for User Readiness**

**Objective**: Reduce duplicate queries for user readiness  
**Time Estimate**: 2-3 hours  
**Files to Modify**:
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/UserReadinessStateProvider.cs` (already exists)
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/AssignmentWorker.cs`

**Implementation**:
1. Enhance `UserReadinessStateProvider` to cache database queries
2. Add 15-second cache expiration
3. Invalidate on heartbeat updates

**Expected Benefit**: 
- Reduce user readiness queries by 60-70%
- Faster assignment decisions

**Risk**: Low - SignalR state already provides real-time updates  
**Testing**: Verify cache usage in assignment logs

---

### **Task 1.4: Optimize Polling Intervals**

**Objective**: Reduce idle polling overhead  
**Time Estimate**: 2 hours  
**Files to Modify**:
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/AssignmentWorker.cs` (already optimized)
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessService.cs`
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/IntakeWorker.cs`

**Changes**:
- Review current intervals
- Increase intervals for services that rarely have work
- Document rationale for each interval

**Expected Benefit**: 
- Reduce CPU usage during idle periods
- Reduce unnecessary database connections

**Risk**: Low - Can be adjusted per service  
**Testing**: Monitor service execution frequency

---

## 🔧 **PHASE 2: SERVICE CONSOLIDATION** (3-5 days)

**Priority**: 🟡 **MEDIUM**  
**Risk**: 🟡 **MEDIUM**  
**Impact**: 🔴 **HIGH**  
**Dependencies**: Phase 1 complete

### **Task 2.1: Consolidate Image Analysis Workers**

**Objective**: Merge 5 workers into 1 coordinated service  
**Time Estimate**: 1-2 days  
**Services to Consolidate**:
- `IntakeWorker`
- `AssignmentWorker`
- `SubmissionWorker`
- `HousekeepingWorker`
- `ImageAnalysisBootstrapper`

**Files to Create**:
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs`

**Files to Modify**:
- `src/NickScanCentralImagingPortal.Services/ServiceConfiguration.cs`
- Remove individual worker registrations

**Implementation Strategy**:
1. Create `ImageAnalysisOrchestratorService` that coordinates all workflows
2. Maintain existing worker logic as internal methods
3. Execute workflows sequentially within single service:
   - Bootstrapper (once on startup)
   - Intake (every 10s)
   - Assignment (every 5s)
   - Submission (every 30s)
   - Housekeeping (every 1 min)
4. Use single DbContext scope per cycle
5. Share cached data between workflows

**Expected Benefit**: 
- Reduce from 5 services to 1 (80% reduction)
- Reduce memory: ~250 MB → ~50 MB
- Reduce connections: 5 concurrent → 1 sequential
- Better coordination between workflows

**Risk**: Medium - Need to ensure workflow order is correct  
**Testing**:
- Verify all workflows still execute correctly
- Monitor execution times
- Compare before/after metrics

**Rollback Plan**: Keep old worker files, can revert registration if needed

---

### **Task 2.2: Consolidate ICUMS Pipeline Services**

**Objective**: Merge 5 ICUMS services into 1 coordinated service  
**Time Estimate**: 1-2 days  
**Services to Consolidate**:
- `IcumBackgroundService`
- `IcumFileScannerService`
- `IcumJsonIngestionService`
- `IcumDataTransferService`
- `ICUMSDownloadBackgroundService`

**Files to Create**:
- `src/NickScanCentralImagingPortal.Services/IcumApi/IcumPipelineOrchestratorService.cs`

**Files to Modify**:
- `src/NickScanCentralImagingPortal.Services/ServiceConfiguration.cs`
- Remove individual service registrations

**Implementation Strategy**:
1. Create `IcumPipelineOrchestratorService` that coordinates ICUMS workflows
2. Execute in pipeline order:
   - File Scanner (detect new files)
   - Download Background (download missing data)
   - JSON Ingestion (process downloaded files)
   - Data Transfer (sync to main database)
   - Background Service (batch operations)
3. Use single DbContext scope per pipeline cycle
4. Share state between pipeline stages

**Expected Benefit**: 
- Reduce from 5 services to 1 (80% reduction)
- Reduce memory: ~200 MB → ~40 MB
- Reduce connections: 5 concurrent → 1 sequential
- Better pipeline coordination

**Risk**: Medium - Pipeline order is critical  
**Testing**:
- Verify all ICUMS operations still work
- Monitor pipeline execution
- Check for data consistency

**Rollback Plan**: Keep old service files, can revert if needed

---

### **Task 2.3: Consolidate Container Completeness Services**

**Objective**: Merge 4 services into 1 coordinated service  
**Time Estimate**: 1 day  
**Services to Consolidate**:
- `ContainerCompletenessService`
- `ContainerDataMapperService`
- `ManualBOESelectivityService`
- `PostICUMSValidationService`

**Files to Create**:
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessOrchestratorService.cs`

**Files to Modify**:
- `src/NickScanCentralImagingPortal.Services/ServiceConfiguration.cs`
- Remove individual service registrations

**Implementation Strategy**:
1. Create `ContainerCompletenessOrchestratorService`
2. Execute workflows in order:
   - Completeness Check (every 5 min)
   - Data Mapping (when completeness changes)
   - BOE Selectivity (when needed)
   - Post-ICUMS Validation (after ICUMS updates)
3. Use single DbContext scope per cycle
4. Coordinate state between workflows

**Expected Benefit**: 
- Reduce from 4 services to 1 (75% reduction)
- Reduce memory: ~150 MB → ~40 MB
- Reduce connections: 4 concurrent → 1 sequential
- Better workflow coordination

**Risk**: Low-Medium - Workflows are relatively independent  
**Testing**:
- Verify completeness checks still work
- Monitor data mapping accuracy
- Check BOE selectivity logic

**Rollback Plan**: Keep old service files

---

## ⚡ **PHASE 3: ADVANCED OPTIMIZATIONS** (2-3 days)

**Priority**: 🟢 **LOW**  
**Risk**: 🟢 **LOW**  
**Impact**: 🟡 **MEDIUM**  
**Dependencies**: Phase 2 complete

### **Task 3.1: Implement Adaptive Polling**

**Objective**: Adjust polling frequency based on work availability  
**Time Estimate**: 1 day  
**Files to Modify**:
- `src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs`
- `src/NickScanCentralImagingPortal.Services/IcumApi/IcumPipelineOrchestratorService.cs`
- `src/NickScanCentralImagingPortal.Services/ContainerCompleteness/ContainerCompletenessOrchestratorService.cs`

**Implementation**:
1. Track work availability (count of pending items)
2. Adjust polling interval dynamically:
   - High work: Poll every 5 seconds
   - Medium work: Poll every 30 seconds
   - Low work: Poll every 2 minutes
   - No work: Poll every 5 minutes
3. Add configuration for thresholds

**Expected Benefit**: 
- Reduce idle polling by 60-80%
- Reduce CPU usage during low activity
- Faster response when work is available

**Risk**: Low - Can fall back to fixed intervals  
**Testing**: Monitor polling frequency changes

---

### **Task 3.2: Add Service Health Monitoring**

**Objective**: Monitor consolidated services for performance issues  
**Time Estimate**: 4-6 hours  
**Files to Create**:
- `src/NickScanCentralImagingPortal.Services/Monitoring/ServiceHealthMonitor.cs`

**Implementation**:
1. Track execution times per workflow
2. Track memory usage per service
3. Track database connection usage
4. Log metrics to monitoring system
5. Alert on performance degradation

**Expected Benefit**: 
- Better visibility into service performance
- Early detection of issues
- Data-driven optimization

**Risk**: Low - Monitoring only, no functional changes  
**Testing**: Verify metrics are collected correctly

---

### **Task 3.3: Optimize Database Query Patterns**

**Objective**: Reduce redundant queries in consolidated services  
**Time Estimate**: 1 day  
**Files to Modify**:
- All orchestrator services created in Phase 2

**Implementation**:
1. Review query patterns in each orchestrator
2. Identify duplicate queries within single cycle
3. Cache query results within cycle
4. Use batch queries where possible
5. Optimize LINQ queries (already done for CTE issues)

**Expected Benefit**: 
- Reduce database queries by 30-40%
- Faster service execution
- Lower database load

**Risk**: Low - Query optimization only  
**Testing**: Compare query counts before/after

---

## 📊 **IMPLEMENTATION TIMELINE**

### **Week 1**
- **Day 1-2**: Phase 1 (Quick Wins)
  - Task 1.1: Connection pools (30 min)
  - Task 1.2: Ready groups cache (4-6 hours)
  - Task 1.3: User readiness cache (2-3 hours)
  - Task 1.4: Polling optimization (2 hours)

### **Week 2**
- **Day 3-4**: Phase 2.1 (Image Analysis consolidation)
- **Day 5-6**: Phase 2.2 (ICUMS consolidation)
- **Day 7**: Phase 2.3 (Container Completeness consolidation)

### **Week 3**
- **Day 8**: Phase 3.1 (Adaptive polling)
- **Day 9**: Phase 3.2 (Health monitoring)
- **Day 10**: Phase 3.3 (Query optimization)

---

## ✅ **SUCCESS CRITERIA**

### **Phase 1 Success Metrics**
- ✅ Connection pool exhaustion errors eliminated
- ✅ Ready groups queries reduced by 70%+
- ✅ User readiness queries reduced by 60%+
- ✅ No functional regressions

### **Phase 2 Success Metrics**
- ✅ Service count reduced from 30 to 15-18
- ✅ Memory usage reduced by 25-33% (3-4 GB → 2-3 GB)
- ✅ Database connections reduced by 40-50%
- ✅ All workflows still function correctly
- ✅ No performance degradation

### **Phase 3 Success Metrics**
- ✅ Idle polling reduced by 60-80%
- ✅ Service health metrics available
- ✅ Query counts reduced by 30-40%
- ✅ Overall system responsiveness maintained or improved

---

## 🚨 **RISK MITIGATION**

### **High-Risk Areas**
1. **Service Consolidation** (Phase 2)
   - **Risk**: Breaking existing workflows
   - **Mitigation**: 
     - Keep old service files for rollback
     - Comprehensive testing before deployment
     - Gradual rollout (one consolidation at a time)
     - Feature flags to enable/disable new services

2. **Cache Invalidation** (Phase 1)
   - **Risk**: Stale cache data
   - **Mitigation**:
     - Short cache expiration (15-30 seconds)
     - Explicit invalidation on data changes
     - Fallback to direct query if cache miss

### **Rollback Strategy**
- Each phase can be rolled back independently
- Old service files kept in repository
- Configuration flags to switch between old/new services
- Database changes are minimal (connection strings only)

---

## 📝 **TESTING STRATEGY**

### **Unit Tests**
- Test cache invalidation logic
- Test orchestrator workflow coordination
- Test adaptive polling logic

### **Integration Tests**
- Test consolidated services end-to-end
- Verify all workflows still execute
- Check database query patterns

### **Performance Tests**
- Measure memory usage before/after
- Measure database connection usage
- Measure query counts
- Measure service execution times

### **Load Tests**
- Simulate peak load scenarios
- Verify connection pools handle load
- Verify cache performance under load

---

## 📋 **APPROVAL CHECKLIST**

Before starting implementation, confirm:

- [ ] **Phase 1 approved** (Quick Wins - 1-2 days)
- [ ] **Phase 2 approved** (Service Consolidation - 3-5 days)
- [ ] **Phase 3 approved** (Advanced Optimizations - 2-3 days)
- [ ] **Timeline acceptable** (6-10 days total)
- [ ] **Risk level acceptable** (Medium risk for Phase 2)
- [ ] **Rollback strategy understood**
- [ ] **Testing strategy approved**

---

## 🎯 **EXPECTED OUTCOMES**

### **Resource Reduction**
- **Services**: 30 → 15-18 (40-50% reduction)
- **Memory**: 3-4 GB → 2-3 GB (25-33% reduction)
- **Connections**: Peak usage reduced by 40-50%
- **Queries**: Duplicate queries reduced by 60-80%

### **Performance Improvements**
- Faster service execution (shared state, fewer connections)
- Better resource utilization
- Reduced database load
- Improved system responsiveness

### **Maintainability Improvements**
- Simpler architecture (fewer services)
- Better code organization (orchestrators)
- Easier debugging (consolidated logs)
- Better monitoring (health metrics)

---

## 📞 **NEXT STEPS**

1. **Review this plan** and provide feedback
2. **Approve phases** you want to proceed with
3. **Set priorities** if not doing all phases
4. **Schedule implementation** based on your timeline
5. **Begin Phase 1** once approved

---

**Ready for your approval!** 🚀

