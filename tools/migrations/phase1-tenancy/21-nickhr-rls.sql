-- =============================================================================
-- NICKSCAN ERP SOLUTION - Phase 1 - Enable Row-Level Security on nickhr
-- 
-- Each table gets a policy that filters by current_setting('app.tenant_id').
-- The TenantOwnedEntityInterceptor sets this session variable on every
-- connection. Bypass for the postgres superuser via the BYPASSRLS attribute
-- (already on by default for postgres role).
-- =============================================================================
BEGIN;
SET LOCAL search_path = public;

-- AchievementEntries
ALTER TABLE "AchievementEntries" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_achievemententries" ON "AchievementEntries";
CREATE POLICY "tenant_isolation_achievemententries" ON "AchievementEntries"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Announcements
ALTER TABLE "Announcements" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_announcements" ON "Announcements";
CREATE POLICY "tenant_isolation_announcements" ON "Announcements"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Applications
ALTER TABLE "Applications" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_applications" ON "Applications";
CREATE POLICY "tenant_isolation_applications" ON "Applications"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- AppraisalCycles
ALTER TABLE "AppraisalCycles" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_appraisalcycles" ON "AppraisalCycles";
CREATE POLICY "tenant_isolation_appraisalcycles" ON "AppraisalCycles"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- AppraisalForms
ALTER TABLE "AppraisalForms" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_appraisalforms" ON "AppraisalForms";
CREATE POLICY "tenant_isolation_appraisalforms" ON "AppraisalForms"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- ApprovalDelegations
ALTER TABLE "ApprovalDelegations" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_approvaldelegations" ON "ApprovalDelegations";
CREATE POLICY "tenant_isolation_approvaldelegations" ON "ApprovalDelegations"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Approvals
ALTER TABLE "Approvals" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_approvals" ON "Approvals";
CREATE POLICY "tenant_isolation_approvals" ON "Approvals"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- AssetAssignments
ALTER TABLE "AssetAssignments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_assetassignments" ON "AssetAssignments";
CREATE POLICY "tenant_isolation_assetassignments" ON "AssetAssignments"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Assets
ALTER TABLE "Assets" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_assets" ON "Assets";
CREATE POLICY "tenant_isolation_assets" ON "Assets"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- AttendanceRecords
ALTER TABLE "AttendanceRecords" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_attendancerecords" ON "AttendanceRecords";
CREATE POLICY "tenant_isolation_attendancerecords" ON "AttendanceRecords"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- AuditLogs
ALTER TABLE "AuditLogs" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_auditlogs" ON "AuditLogs";
CREATE POLICY "tenant_isolation_auditlogs" ON "AuditLogs"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Beneficiaries
ALTER TABLE "Beneficiaries" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_beneficiaries" ON "Beneficiaries";
CREATE POLICY "tenant_isolation_beneficiaries" ON "Beneficiaries"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Candidates
ALTER TABLE "Candidates" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_candidates" ON "Candidates";
CREATE POLICY "tenant_isolation_candidates" ON "Candidates"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- ClearanceItems
ALTER TABLE "ClearanceItems" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_clearanceitems" ON "ClearanceItems";
CREATE POLICY "tenant_isolation_clearanceitems" ON "ClearanceItems"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- CompanySettings
ALTER TABLE "CompanySettings" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_companysettings" ON "CompanySettings";
CREATE POLICY "tenant_isolation_companysettings" ON "CompanySettings"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Competencies
ALTER TABLE "Competencies" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_competencies" ON "Competencies";
CREATE POLICY "tenant_isolation_competencies" ON "Competencies"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- CompetencyFrameworks
ALTER TABLE "CompetencyFrameworks" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_competencyframeworks" ON "CompetencyFrameworks";
CREATE POLICY "tenant_isolation_competencyframeworks" ON "CompetencyFrameworks"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- ComplianceDeadlines
ALTER TABLE "ComplianceDeadlines" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_compliancedeadlines" ON "ComplianceDeadlines";
CREATE POLICY "tenant_isolation_compliancedeadlines" ON "ComplianceDeadlines"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Departments
ALTER TABLE "Departments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_departments" ON "Departments";
CREATE POLICY "tenant_isolation_departments" ON "Departments"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Dependents
ALTER TABLE "Dependents" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_dependents" ON "Dependents";
CREATE POLICY "tenant_isolation_dependents" ON "Dependents"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Designations
ALTER TABLE "Designations" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_designations" ON "Designations";
CREATE POLICY "tenant_isolation_designations" ON "Designations"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- DisciplinaryCases
ALTER TABLE "DisciplinaryCases" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_disciplinarycases" ON "DisciplinaryCases";
CREATE POLICY "tenant_isolation_disciplinarycases" ON "DisciplinaryCases"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- EmailTemplates
ALTER TABLE "EmailTemplates" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_emailtemplates" ON "EmailTemplates";
CREATE POLICY "tenant_isolation_emailtemplates" ON "EmailTemplates"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- EmergencyContacts
ALTER TABLE "EmergencyContacts" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_emergencycontacts" ON "EmergencyContacts";
CREATE POLICY "tenant_isolation_emergencycontacts" ON "EmergencyContacts"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- EmployeeDocuments
ALTER TABLE "EmployeeDocuments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_employeedocuments" ON "EmployeeDocuments";
CREATE POLICY "tenant_isolation_employeedocuments" ON "EmployeeDocuments"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- EmployeeQualifications
ALTER TABLE "EmployeeQualifications" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_employeequalifications" ON "EmployeeQualifications";
CREATE POLICY "tenant_isolation_employeequalifications" ON "EmployeeQualifications"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- EmployeeSalaryStructures
ALTER TABLE "EmployeeSalaryStructures" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_employeesalarystructures" ON "EmployeeSalaryStructures";
CREATE POLICY "tenant_isolation_employeesalarystructures" ON "EmployeeSalaryStructures"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- EmployeeSkills
ALTER TABLE "EmployeeSkills" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_employeeskills" ON "EmployeeSkills";
CREATE POLICY "tenant_isolation_employeeskills" ON "EmployeeSkills"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Employees
ALTER TABLE "Employees" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_employees" ON "Employees";
CREATE POLICY "tenant_isolation_employees" ON "Employees"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- EmployeesOfMonth
ALTER TABLE "EmployeesOfMonth" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_employeesofmonth" ON "EmployeesOfMonth";
CREATE POLICY "tenant_isolation_employeesofmonth" ON "EmployeesOfMonth"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- EmploymentHistoryRecords
ALTER TABLE "EmploymentHistoryRecords" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_employmenthistoryrecords" ON "EmploymentHistoryRecords";
CREATE POLICY "tenant_isolation_employmenthistoryrecords" ON "EmploymentHistoryRecords"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- ExcuseDuties
ALTER TABLE "ExcuseDuties" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_excuseduties" ON "ExcuseDuties";
CREATE POLICY "tenant_isolation_excuseduties" ON "ExcuseDuties"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- ExitInterviews
ALTER TABLE "ExitInterviews" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_exitinterviews" ON "ExitInterviews";
CREATE POLICY "tenant_isolation_exitinterviews" ON "ExitInterviews"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- ExpenseClaims
ALTER TABLE "ExpenseClaims" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_expenseclaims" ON "ExpenseClaims";
CREATE POLICY "tenant_isolation_expenseclaims" ON "ExpenseClaims"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- FinalSettlements
ALTER TABLE "FinalSettlements" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_finalsettlements" ON "FinalSettlements";
CREATE POLICY "tenant_isolation_finalsettlements" ON "FinalSettlements"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- GeneratedLetters
ALTER TABLE "GeneratedLetters" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_generatedletters" ON "GeneratedLetters";
CREATE POLICY "tenant_isolation_generatedletters" ON "GeneratedLetters"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Goals
ALTER TABLE "Goals" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_goals" ON "Goals";
CREATE POLICY "tenant_isolation_goals" ON "Goals"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Grades
ALTER TABLE "Grades" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_grades" ON "Grades";
CREATE POLICY "tenant_isolation_grades" ON "Grades"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Grievances
ALTER TABLE "Grievances" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_grievances" ON "Grievances";
CREATE POLICY "tenant_isolation_grievances" ON "Grievances"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Holidays
ALTER TABLE "Holidays" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_holidays" ON "Holidays";
CREATE POLICY "tenant_isolation_holidays" ON "Holidays"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Interviews
ALTER TABLE "Interviews" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_interviews" ON "Interviews";
CREATE POLICY "tenant_isolation_interviews" ON "Interviews"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- JobPostings
ALTER TABLE "JobPostings" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_jobpostings" ON "JobPostings";
CREATE POLICY "tenant_isolation_jobpostings" ON "JobPostings"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- JobRequisitions
ALTER TABLE "JobRequisitions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_jobrequisitions" ON "JobRequisitions";
CREATE POLICY "tenant_isolation_jobrequisitions" ON "JobRequisitions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- LeaveBalances
ALTER TABLE "LeaveBalances" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_leavebalances" ON "LeaveBalances";
CREATE POLICY "tenant_isolation_leavebalances" ON "LeaveBalances"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- LeavePolicies
ALTER TABLE "LeavePolicies" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_leavepolicies" ON "LeavePolicies";
CREATE POLICY "tenant_isolation_leavepolicies" ON "LeavePolicies"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- LeaveRequests
ALTER TABLE "LeaveRequests" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_leaverequests" ON "LeaveRequests";
CREATE POLICY "tenant_isolation_leaverequests" ON "LeaveRequests"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- LetterTemplates
ALTER TABLE "LetterTemplates" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_lettertemplates" ON "LetterTemplates";
CREATE POLICY "tenant_isolation_lettertemplates" ON "LetterTemplates"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- LoanApplications
ALTER TABLE "LoanApplications" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_loanapplications" ON "LoanApplications";
CREATE POLICY "tenant_isolation_loanapplications" ON "LoanApplications"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- LoanRepayments
ALTER TABLE "LoanRepayments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_loanrepayments" ON "LoanRepayments";
CREATE POLICY "tenant_isolation_loanrepayments" ON "LoanRepayments"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Loans
ALTER TABLE "Loans" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_loans" ON "Loans";
CREATE POLICY "tenant_isolation_loans" ON "Loans"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Locations
ALTER TABLE "Locations" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_locations" ON "Locations";
CREATE POLICY "tenant_isolation_locations" ON "Locations"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- LoginAudits
ALTER TABLE "LoginAudits" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_loginaudits" ON "LoginAudits";
CREATE POLICY "tenant_isolation_loginaudits" ON "LoginAudits"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- MedicalBenefits
ALTER TABLE "MedicalBenefits" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_medicalbenefits" ON "MedicalBenefits";
CREATE POLICY "tenant_isolation_medicalbenefits" ON "MedicalBenefits"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- MedicalClaims
ALTER TABLE "MedicalClaims" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_medicalclaims" ON "MedicalClaims";
CREATE POLICY "tenant_isolation_medicalclaims" ON "MedicalClaims"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Notifications
ALTER TABLE "Notifications" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_notifications" ON "Notifications";
CREATE POLICY "tenant_isolation_notifications" ON "Notifications"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- OfferLetters
ALTER TABLE "OfferLetters" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_offerletters" ON "OfferLetters";
CREATE POLICY "tenant_isolation_offerletters" ON "OfferLetters"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- OneOnOneMeetings
ALTER TABLE "OneOnOneMeetings" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_oneononemeetings" ON "OneOnOneMeetings";
CREATE POLICY "tenant_isolation_oneononemeetings" ON "OneOnOneMeetings"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- OutOfStationRates
ALTER TABLE "OutOfStationRates" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_outofstationrates" ON "OutOfStationRates";
CREATE POLICY "tenant_isolation_outofstationrates" ON "OutOfStationRates"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- OutOfStationRequests
ALTER TABLE "OutOfStationRequests" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_outofstationrequests" ON "OutOfStationRequests";
CREATE POLICY "tenant_isolation_outofstationrequests" ON "OutOfStationRequests"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- OvertimeRequests
ALTER TABLE "OvertimeRequests" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_overtimerequests" ON "OvertimeRequests";
CREATE POLICY "tenant_isolation_overtimerequests" ON "OvertimeRequests"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- PayrollItemDetails
ALTER TABLE "PayrollItemDetails" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_payrollitemdetails" ON "PayrollItemDetails";
CREATE POLICY "tenant_isolation_payrollitemdetails" ON "PayrollItemDetails"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- PayrollItems
ALTER TABLE "PayrollItems" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_payrollitems" ON "PayrollItems";
CREATE POLICY "tenant_isolation_payrollitems" ON "PayrollItems"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- PayrollRuns
ALTER TABLE "PayrollRuns" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_payrollruns" ON "PayrollRuns";
CREATE POLICY "tenant_isolation_payrollruns" ON "PayrollRuns"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- PolicyAcknowledgements
ALTER TABLE "PolicyAcknowledgements" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_policyacknowledgements" ON "PolicyAcknowledgements";
CREATE POLICY "tenant_isolation_policyacknowledgements" ON "PolicyAcknowledgements"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- PolicyDocuments
ALTER TABLE "PolicyDocuments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_policydocuments" ON "PolicyDocuments";
CREATE POLICY "tenant_isolation_policydocuments" ON "PolicyDocuments"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- ProbationReviews
ALTER TABLE "ProbationReviews" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_probationreviews" ON "ProbationReviews";
CREATE POLICY "tenant_isolation_probationreviews" ON "ProbationReviews"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- ProfileChangeRequests
ALTER TABLE "ProfileChangeRequests" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_profilechangerequests" ON "ProfileChangeRequests";
CREATE POLICY "tenant_isolation_profilechangerequests" ON "ProfileChangeRequests"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Projects
ALTER TABLE "Projects" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_projects" ON "Projects";
CREATE POLICY "tenant_isolation_projects" ON "Projects"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Recognitions
ALTER TABLE "Recognitions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_recognitions" ON "Recognitions";
CREATE POLICY "tenant_isolation_recognitions" ON "Recognitions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- RoleClaims
ALTER TABLE "RoleClaims" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_roleclaims" ON "RoleClaims";
CREATE POLICY "tenant_isolation_roleclaims" ON "RoleClaims"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Roles
ALTER TABLE "Roles" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_roles" ON "Roles";
CREATE POLICY "tenant_isolation_roles" ON "Roles"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- SalaryComponents
ALTER TABLE "SalaryComponents" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_salarycomponents" ON "SalaryComponents";
CREATE POLICY "tenant_isolation_salarycomponents" ON "SalaryComponents"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Separations
ALTER TABLE "Separations" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_separations" ON "Separations";
CREATE POLICY "tenant_isolation_separations" ON "Separations"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- ShiftAssignments
ALTER TABLE "ShiftAssignments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_shiftassignments" ON "ShiftAssignments";
CREATE POLICY "tenant_isolation_shiftassignments" ON "ShiftAssignments"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Shifts
ALTER TABLE "Shifts" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_shifts" ON "Shifts";
CREATE POLICY "tenant_isolation_shifts" ON "Shifts"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Skills
ALTER TABLE "Skills" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_skills" ON "Skills";
CREATE POLICY "tenant_isolation_skills" ON "Skills"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- SuccessionCandidates
ALTER TABLE "SuccessionCandidates" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_successioncandidates" ON "SuccessionCandidates";
CREATE POLICY "tenant_isolation_successioncandidates" ON "SuccessionCandidates"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- SuccessionPlans
ALTER TABLE "SuccessionPlans" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_successionplans" ON "SuccessionPlans";
CREATE POLICY "tenant_isolation_successionplans" ON "SuccessionPlans"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- SurveyAnswers
ALTER TABLE "SurveyAnswers" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_surveyanswers" ON "SurveyAnswers";
CREATE POLICY "tenant_isolation_surveyanswers" ON "SurveyAnswers"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- SurveyQuestions
ALTER TABLE "SurveyQuestions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_surveyquestions" ON "SurveyQuestions";
CREATE POLICY "tenant_isolation_surveyquestions" ON "SurveyQuestions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- SurveyResponses
ALTER TABLE "SurveyResponses" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_surveyresponses" ON "SurveyResponses";
CREATE POLICY "tenant_isolation_surveyresponses" ON "SurveyResponses"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Surveys
ALTER TABLE "Surveys" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_surveys" ON "Surveys";
CREATE POLICY "tenant_isolation_surveys" ON "Surveys"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- TimesheetEntries
ALTER TABLE "TimesheetEntries" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_timesheetentries" ON "TimesheetEntries";
CREATE POLICY "tenant_isolation_timesheetentries" ON "TimesheetEntries"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- TrainingAttendances
ALTER TABLE "TrainingAttendances" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_trainingattendances" ON "TrainingAttendances";
CREATE POLICY "tenant_isolation_trainingattendances" ON "TrainingAttendances"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- TrainingPrograms
ALTER TABLE "TrainingPrograms" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_trainingprograms" ON "TrainingPrograms";
CREATE POLICY "tenant_isolation_trainingprograms" ON "TrainingPrograms"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- TransferPromotions
ALTER TABLE "TransferPromotions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_transferpromotions" ON "TransferPromotions";
CREATE POLICY "tenant_isolation_transferpromotions" ON "TransferPromotions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- TravelRequests
ALTER TABLE "TravelRequests" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_travelrequests" ON "TravelRequests";
CREATE POLICY "tenant_isolation_travelrequests" ON "TravelRequests"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- UserClaims
ALTER TABLE "UserClaims" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_userclaims" ON "UserClaims";
CREATE POLICY "tenant_isolation_userclaims" ON "UserClaims"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- UserLogins
ALTER TABLE "UserLogins" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_userlogins" ON "UserLogins";
CREATE POLICY "tenant_isolation_userlogins" ON "UserLogins"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- UserRoles
ALTER TABLE "UserRoles" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_userroles" ON "UserRoles";
CREATE POLICY "tenant_isolation_userroles" ON "UserRoles"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- UserSystemAccesses
ALTER TABLE "UserSystemAccesses" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_usersystemaccesses" ON "UserSystemAccesses";
CREATE POLICY "tenant_isolation_usersystemaccesses" ON "UserSystemAccesses"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- UserTokens
ALTER TABLE "UserTokens" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_usertokens" ON "UserTokens";
CREATE POLICY "tenant_isolation_usertokens" ON "UserTokens"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Users
ALTER TABLE "Users" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_users" ON "Users";
CREATE POLICY "tenant_isolation_users" ON "Users"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Warnings
ALTER TABLE "Warnings" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_warnings" ON "Warnings";
CREATE POLICY "tenant_isolation_warnings" ON "Warnings"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- WorkflowSteps
ALTER TABLE "WorkflowSteps" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_workflowsteps" ON "WorkflowSteps";
CREATE POLICY "tenant_isolation_workflowsteps" ON "WorkflowSteps"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- Workflows
ALTER TABLE "Workflows" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_workflows" ON "Workflows";
CREATE POLICY "tenant_isolation_workflows" ON "Workflows"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

COMMIT;
