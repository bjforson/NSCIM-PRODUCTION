# Prioritized Implementation Plan - Incomplete Features

**Date:** 2026-01-03  
**Status:** 📋 Planning Complete  
**Total Features:** 21+ incomplete features

---

## 🎯 Executive Summary

This plan prioritizes 21+ incomplete features based on:
- **User Impact** (High/Medium/Low)
- **Business Value** (Critical/Important/Nice-to-Have)
- **Implementation Complexity** (Low/Medium/High)
- **Dependencies** (None/Some/Many)

---

## 📊 Priority Matrix

| Priority | Count | Time Est. | Description |
|----------|-------|-----------|-------------|
| **P0 - Critical** | 4 | 15-20h | Blocking user workflows |
| **P1 - High** | 6 | 18-24h | Important for core functionality |
| **P2 - Medium** | 7 | 12-16h | Enhances user experience |
| **P3 - Low** | 4+ | 10-12h | Nice-to-have features |
| **TOTAL** | **21+** | **55-72h** | ~7-9 days of focused work |

---

## 🔴 P0 - CRITICAL PRIORITY (15-20 hours)

### 1. Business Rules CRUD Operations ⭐⭐⭐
**Priority:** P0 - Critical  
**Time Estimate:** 6-8 hours  
**Dependencies:** None  
**Impact:** Users cannot manage business rules

**Tasks:**
1. Create database table `BusinessRules` (1h)
   - Entity model
   - DbContext configuration
   - Migration script
2. Implement POST `/api/BusinessRules` (1h)
   - Create endpoint
   - Validation
   - Save to database
3. Implement PUT `/api/BusinessRules/{id}` (1h)
   - Update endpoint
   - Validation
   - Update database
4. Implement DELETE `/api/BusinessRules/{id}` (0.5h)
   - Delete endpoint
   - Soft delete or hard delete
5. Implement PUT `/api/BusinessRules/{id}/status` (0.5h)
   - Toggle active status
6. Create Rule Dialog (Frontend) (1.5h)
   - MudDialog component
   - Form validation
   - API integration
7. Edit Rule Dialog (Frontend) (1h)
   - Reuse create dialog
   - Pre-populate form
8. Wire up API calls (Frontend) (1h)
   - Delete API call
   - Toggle status API call
   - Error handling

**Files to Create:**
- `src/NickScanCentralImagingPortal.Core/Entities/BusinessRule.cs`
- `src/NickScanCentralImagingPortal.Infrastructure/Data/Configurations/BusinessRuleConfiguration.cs`
- Database Migration: `AddBusinessRulesTable`

**Files to Modify:**
- `src/NickScanCentralImagingPortal.Infrastructure/Data/ApplicationDbContext.cs`
- `src/NickScanCentralImagingPortal.API/Controllers/BusinessRulesController.cs`
- `src/NickScanWebApp.New/Pages/Validation/BusinessRules.razor`

**Acceptance Criteria:**
- ✅ Users can create new business rules
- ✅ Users can edit existing rules
- ✅ Users can delete rules
- ✅ Users can toggle rule active status
- ✅ All changes persist to database
- ✅ Rules load from database (not sample data)

---

### 2. Export Functionality (Excel/PDF) ⭐⭐⭐
**Priority:** P0 - Critical  
**Time Estimate:** 5-6 hours  
**Dependencies:** NuGet packages  
**Impact:** Users cannot export data to standard formats

**Tasks:**
1. Install NuGet packages (0.5h)
   - ClosedXML (Excel)
   - QuestPDF (PDF)
2. Implement Excel Export (2h)
   - Extend ExportService
   - Create Excel workbook
   - Add formatting
   - Handle multiple sheets
3. Implement PDF Export (2h)
   - Extend ExportService
   - Create PDF document
   - Add branding/headers
   - Handle tables
4. Update ExportService (0.5h)
   - Remove NotImplementedException
   - Add error handling
   - Add logging
5. Test and integrate (1h)
   - Test with real data
   - Verify file downloads
   - Check formatting

**NuGet Packages:**
```xml
<PackageReference Include="ClosedXML" Version="0.102.2" />
<PackageReference Include="QuestPDF" Version="2024.7.0" />
```

**Files to Modify:**
- `src/NickScanWebApp.New/Services/ExportService.cs`
- `src/NickScanWebApp.Shared/Services/ExportService.cs`
- `src/NickScanWebApp.Mobile/Services/ExportService.cs`
- All pages using ExportService

**Acceptance Criteria:**
- ✅ Excel export works for all data types
- ✅ PDF export works with proper formatting
- ✅ Files download correctly
- ✅ No NotImplementedException errors
- ✅ Export works in all applications (New, Mobile, Shared)

---

### 3. Container Details Actions ⭐⭐
**Priority:** P0 - Critical  
**Time Estimate:** 4-5 hours  
**Dependencies:** Some API endpoints may need to be created  
**Impact:** Multiple container actions are non-functional

**Tasks:**
1. Implement Submit to ICUMS (1h)
   - Create/verify API endpoint
   - Wire up API call
   - Add loading states
   - Handle errors
2. Implement Export Report (0.5h)
   - Use ExportService (depends on #2)
   - Generate PDF/Excel
   - Download file
3. Implement Trigger Validation (1h)
   - Create/verify API endpoint
   - Wire up API call
   - Show validation results
4. Implement Edit Container (1h)
   - Create edit dialog
   - Form with container fields
   - API integration
   - Validation
5. Implement Add Annotation (1h)
   - Create annotation interface
   - Drawing/selection tools
   - Save annotations
   - API integration
6. Implement Download Image (0.5h)
   - Get image URL
   - Download via browser
   - Or API endpoint

**Files to Create:**
- `src/NickScanWebApp.New/Components/Containers/EditContainerDialog.razor`
- `src/NickScanWebApp.New/Components/Containers/AnnotationTool.razor` (optional)

**Files to Modify:**
- `src/NickScanWebApp.New/Pages/Containers/ContainerDetails.razor`

**API Endpoints Needed:**
- POST `/api/containers/{id}/submit`
- POST `/api/containers/{id}/validate`
- PUT `/api/containers/{id}`
- POST `/api/containers/{id}/annotations`
- GET `/api/containers/{id}/image/download`

**Acceptance Criteria:**
- ✅ All container actions work
- ✅ Proper error handling
- ✅ Loading states shown
- ✅ Success/error messages displayed
- ✅ Actions persist changes

---

### 4. Container List Bulk Operations ⭐
**Priority:** P0 - Critical  
**Time Estimate:** 3-4 hours  
**Dependencies:** Individual operations must work  
**Impact:** Users cannot perform bulk operations

**Tasks:**
1. Implement Bulk Approve (0.75h)
   - Select multiple containers
   - API endpoint for bulk approve
   - Progress indicator
   - Error handling
2. Implement Bulk Reject (0.75h)
   - Select multiple containers
   - API endpoint for bulk reject
   - Reason dialog
   - Progress indicator
3. Implement Bulk Submit (0.75h)
   - Select multiple containers
   - API endpoint for bulk submit
   - Progress indicator
   - Batch processing
4. Implement Bulk Export (0.75h)
   - Select multiple containers
   - Use ExportService (depends on #2)
   - Generate combined file
   - Download

**Files to Modify:**
- `src/NickScanWebApp.New/Pages/Containers/ContainerList.razor`

**API Endpoints Needed:**
- POST `/api/containers/bulk/approve`
- POST `/api/containers/bulk/reject`
- POST `/api/containers/bulk/submit`

**Acceptance Criteria:**
- ✅ Users can select multiple containers
- ✅ Bulk operations process correctly
- ✅ Progress shown during processing
- ✅ Errors handled gracefully
- ✅ Partial failures reported

---

## 🟡 P1 - HIGH PRIORITY (18-24 hours)

### 5. Image Analysis Management Endpoints
**Priority:** P1 - High  
**Time Estimate:** 2-3 hours  
**Dependencies:** Background services

**Tasks:**
- Implement sync-stages endpoint
- Implement rebuild-intake endpoint
- Add progress tracking
- Add error handling

---

### 6. Container Completeness Service Status
**Priority:** P1 - High  
**Time Estimate:** 1-2 hours  
**Dependencies:** Background service tracking

**Tasks:**
- Get actual service status from background service
- Implement BOE request queueing
- Add status monitoring

---

### 7. ICUM Batch Controller Endpoints
**Priority:** P1 - High  
**Time Estimate:** 3-4 hours  
**Dependencies:** ICUM Batch service

**Tasks:**
- Implement log retrieval
- Implement service toggle
- Implement configuration update
- Implement manual trigger

---

### 8. Access Review Tracking
**Priority:** P1 - High  
**Time Estimate:** 4-5 hours  
**Dependencies:** Database schema

**Tasks:**
- Create database schema for review tracking
- Implement review status tracking
- Implement review date tracking
- Implement reviewer tracking
- Implement access revocation
- Implement CSV export

---

### 9. Image Processing - Resizing
**Priority:** P1 - High  
**Time Estimate:** 1-2 hours  
**Dependencies:** Image processing library

**Tasks:**
- Implement image resizing
- Add size parameter handling
- Test with various image sizes

---

### 10. Auth Debugging (Multiple Controllers)
**Priority:** P1 - High  
**Time Estimate:** 2-3 hours  
**Dependencies:** None

**Tasks:**
- Debug ContainerValidationController auth
- Debug ContainerDetailsController auth
- Fix token validation issues
- Enable auth on controllers

---

## 🟢 P2 - MEDIUM PRIORITY (12-16 hours)

### 11. Preventive Fixes Tracking
**Priority:** P2 - Medium  
**Time Estimate:** 2-3 hours

### 12. Daily Data Quality Reports
**Priority:** P2 - Medium  
**Time Estimate:** 2-3 hours

### 13. Notification Integrations
**Priority:** P2 - Medium  
**Time Estimate:** 3-4 hours

### 14. JS Interop Features
**Priority:** P2 - Medium  
**Time Estimate:** 1-2 hours

### 15. Missing API Calls (Various)
**Priority:** P2 - Medium  
**Time Estimate:** 2-3 hours

### 16. System Settings Confirmation Dialog
**Priority:** P2 - Medium  
**Time Estimate:** 0.5-1 hour

### 17. Image Viewer Fullscreen
**Priority:** P2 - Medium  
**Time Estimate:** 0.5-1 hour

---

## 🔵 P3 - LOW PRIORITY (10-12 hours)

### 18. Heimann Smith Detection
**Priority:** P3 - Low  
**Time Estimate:** 2-3 hours

### 19. CMR Redownload Integration
**Priority:** P3 - Low  
**Time Estimate:** 1-2 hours

### 20. Image Analysis Dashboard Enhancements
**Priority:** P3 - Low  
**Time Estimate:** 2-3 hours

### 21. Additional UI Enhancements
**Priority:** P3 - Low  
**Time Estimate:** 5-6 hours

---

## 📅 Recommended Implementation Timeline

### **Sprint 1 (Week 1) - Critical Features**
- ✅ Business Rules CRUD (6-8h)
- ✅ Export Functionality (5-6h)
- ✅ Container Details Actions (4-5h)
- **Total:** 15-19 hours

### **Sprint 2 (Week 2) - High Priority**
- ✅ Container List Bulk Operations (3-4h)
- ✅ Image Analysis Management (2-3h)
- ✅ Container Completeness Service Status (1-2h)
- ✅ ICUM Batch Controller (3-4h)
- ✅ Auth Debugging (2-3h)
- **Total:** 11-16 hours

### **Sprint 3 (Week 3) - Remaining High + Medium**
- ✅ Access Review Tracking (4-5h)
- ✅ Image Processing Resizing (1-2h)
- ✅ Preventive Fixes Tracking (2-3h)
- ✅ Daily Data Quality Reports (2-3h)
- ✅ Missing API Calls (2-3h)
- **Total:** 11-16 hours

### **Sprint 4 (Week 4) - Medium + Low Priority**
- ✅ Notification Integrations (3-4h)
- ✅ JS Interop Features (1-2h)
- ✅ System Settings Dialog (0.5-1h)
- ✅ Image Viewer Fullscreen (0.5-1h)
- ✅ Remaining enhancements (5-6h)
- **Total:** 10-14 hours

---

## 🎯 Quick Wins (High Impact, Low Effort)

1. **System Settings Confirmation Dialog** (0.5h)
2. **Image Viewer Fullscreen** (0.5h)
3. **Missing API Calls** (2-3h)
4. **Auth Debugging** (2-3h)
5. **Image Processing Resizing** (1-2h)

**Total Quick Wins:** 6-10 hours

---

## 📝 Implementation Notes

### **Dependencies:**
- **Export Functionality** (#2) is a dependency for:
  - Container Details Export (#3)
  - Container List Bulk Export (#4)
  - Access Review CSV Export (#8)

- **Business Rules CRUD** (#1) should be done first as it's user-facing and independent

### **Risk Mitigation:**
- Test export functionality thoroughly before deployment
- Implement database migrations carefully
- Add proper error handling for all new endpoints
- Include logging for debugging

### **Testing Requirements:**
- Unit tests for new services
- Integration tests for API endpoints
- UI tests for frontend features
- End-to-end tests for critical workflows

---

## ✅ Success Metrics

After implementation:
- ✅ 0 NotImplementedException errors
- ✅ 0 "Coming soon!" messages
- ✅ All user-facing features functional
- ✅ All API endpoints return real data
- ✅ Export functionality works for all formats
- ✅ Business rules fully manageable via UI

---

**Last Updated:** 2026-01-03  
**Next Review:** After Sprint 1 completion

