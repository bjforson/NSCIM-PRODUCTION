# ✅ Post-Restart Verification Checklist

**Date**: Current  
**Purpose**: Verify all orchestrators and optimizations are working after API restart

---

## 🚀 **IMMEDIATE CHECKS** (First 2 Minutes)

### **1. API Startup**
- [ ] API starts without errors
- [ ] No exceptions in startup logs
- [ ] All database connections successful

### **2. Orchestrator Startup Messages**
Check API logs for these messages:

#### **ImageAnalysisOrchestratorService**
- [ ] `[IMAGE-ANALYSIS-ORCHESTRATOR] Starting Image Analysis Orchestrator Service`
- [ ] `[BOOTSTRAPPER] Image Analysis system initialized successfully`
- [ ] No errors during bootstrapper execution

#### **IcumPipelineOrchestratorService**
- [ ] `[ICUMS-PIPELINE-ORCHESTRATOR] ICUMS Pipeline Orchestrator Service started`
- [ ] No errors during initialization

#### **ContainerCompletenessOrchestratorService**
- [ ] `[CONTAINER-COMPLETENESS-ORCHESTRATOR] Container Completeness Orchestrator Service started`
- [ ] No errors during initialization

### **3. Old Services NOT Running**
Verify these services are **NOT** in logs (they should be commented out):
- [ ] No `IntakeWorker` logs
- [ ] No `AssignmentWorker` logs (except from orchestrator)
- [ ] No `SubmissionWorker` logs
- [ ] No `HousekeepingWorker` logs
- [ ] No `IcumBackgroundService` logs (standalone)
- [ ] No `IcumFileScannerService` logs (standalone)
- [ ] No `IcumJsonIngestionService` logs (standalone)
- [ ] No `IcumDataTransferService` logs (standalone)
- [ ] No `ICUMSDownloadBackgroundService` logs (standalone)

---

## 🔍 **FUNCTIONAL VERIFICATION** (First 5-10 Minutes)

### **4. API Endpoints**
Test these endpoints return 200 OK:

- [ ] `GET /api/image-analysis-management/service-state` → Returns settings
- [ ] `GET /api/image-analysis-management/assignments` → Returns list (may be empty)
- [ ] `GET /api/image-analysis/my-assignments?role=Analyst` → Returns list (may be empty)
- [ ] `GET /api/image-analysis-management/stats` → Returns statistics

### **5. Image Analysis Workflow**
- [ ] Check logs for `[INTAKE]` messages (from orchestrator)
- [ ] Check logs for `[ASSIGNMENT]` messages (from orchestrator)
- [ ] Check logs for `[SUBMISSION]` messages (from orchestrator)
- [ ] Check logs for `[HOUSEKEEPING]` messages (from orchestrator)

### **6. ICUMS Pipeline Workflow**
- [ ] Check logs for `[FILE-SCANNER]` messages
- [ ] Check logs for `[DOWNLOAD-QUEUE]` messages
- [ ] Check logs for `[JSON-INGESTION]` messages
- [ ] Check logs for `[DATA-TRANSFER]` messages

### **7. Container Completeness Workflow**
- [ ] Check logs for `[COMPLETENESS-CHECK]` messages
- [ ] Check logs for `[DATA-MAPPING]` messages
- [ ] Check logs for `[BOE-SELECTIVITY]` messages
- [ ] Check logs for `[POST-ICUMS-VALIDATION]` messages

---

## ⚡ **ADAPTIVE POLLING VERIFICATION** (First 15-30 Minutes)

### **8. Adaptive Polling Logs**
Look for these messages in API logs:

- [ ] `[ADAPTIVE-POLLING] Work count: X, Level: HIGH, Interval: 5s` (when work > 50)
- [ ] `[ADAPTIVE-POLLING] Work count: X, Level: MEDIUM, Interval: 30s` (when work 10-49)
- [ ] `[ADAPTIVE-POLLING] Work count: X, Level: LOW, Interval: 120s` (when work 1-9)
- [ ] `[ADAPTIVE-POLLING] Work count: 0, Level: NONE, Interval: 300s` (when no work)

### **9. Polling Behavior**
- [ ] When work is available, polling happens every 5 seconds
- [ ] When work is low, polling slows to 2 minutes
- [ ] When no work, polling slows to 5 minutes

---

## 📊 **HEALTH MONITORING VERIFICATION** (First 30 Minutes)

### **10. Health Monitor Logs**
Look for these messages:

- [ ] `[HEALTH-MONITOR]` messages appear
- [ ] Execution times are logged for workflows
- [ ] No `[HEALTH-MONITOR] Slow workflow detected` warnings (unless actually slow)

### **11. Service Metrics**
- [ ] Memory usage is being tracked
- [ ] Execution counts are incrementing
- [ ] Success/failure counts are tracked

---

## 🐛 **ERROR CHECKING** (Ongoing)

### **12. No Critical Errors**
- [ ] No 500 errors in API logs
- [ ] No database connection errors
- [ ] No unhandled exceptions
- [ ] No service crashes

### **13. Warning Messages**
- [ ] Review any warnings (may be expected)
- [ ] Document any new warnings
- [ ] Check if warnings are related to new orchestrators

---

## 📈 **PERFORMANCE OBSERVATION** (24-48 Hours)

### **14. Memory Usage**
- [ ] Monitor memory usage over time
- [ ] Compare to baseline (should be 25-33% lower)
- [ ] Check for memory leaks

### **15. Database Connections**
- [ ] Monitor connection pool usage
- [ ] Verify connections reduced by 40-50%
- [ ] Check for connection exhaustion

### **16. Query Performance**
- [ ] Monitor query counts
- [ ] Verify duplicate queries reduced
- [ ] Check cache hit rates

---

## ✅ **SUCCESS CRITERIA**

### **Immediate Success** (First Hour)
- ✅ All 3 orchestrators start successfully
- ✅ No startup errors
- ✅ All API endpoints return 200 OK
- ✅ Workflows execute correctly
- ✅ Adaptive polling logs appear

### **Short-term Success** (24 Hours)
- ✅ No service crashes
- ✅ No performance degradation
- ✅ Memory usage stable or reduced
- ✅ Database connections within limits

### **Long-term Success** (48-72 Hours)
- ✅ Measured improvements match targets
- ✅ No regressions in functionality
- ✅ System stability maintained
- ✅ Health metrics provide insights

---

## 🚨 **ROLLBACK PROCEDURE** (If Needed)

If critical issues arise:

1. **Stop API**
2. **Edit** `src/NickScanCentralImagingPortal.Services/ServiceConfiguration.cs`
3. **Uncomment** old services (lines 343-347, 313-317, etc.)
4. **Comment out** new orchestrators (lines 350, 320, etc.)
5. **Restart API**
6. **Verify** old services start correctly

---

## 📝 **NOTES SECTION**

Use this space to document any issues or observations:

```
Date/Time: _______________
Issue: 
Resolution: 

Date/Time: _______________
Issue: 
Resolution: 
```

---

## 🎯 **QUICK REFERENCE**

### **Expected Log Patterns**

**Good Signs:**
- ✅ Orchestrator startup messages
- ✅ Adaptive polling messages
- ✅ Health monitor messages
- ✅ Workflow execution messages

**Warning Signs:**
- ⚠️ Repeated errors
- ⚠️ Service crashes
- ⚠️ Database connection errors
- ⚠️ Memory spikes

**Critical Issues:**
- 🚨 Unhandled exceptions
- 🚨 Service not starting
- 🚨 Data corruption
- 🚨 Complete system failure

---

**Status**: Ready for Verification  
**Next**: Restart API and begin checking items above

