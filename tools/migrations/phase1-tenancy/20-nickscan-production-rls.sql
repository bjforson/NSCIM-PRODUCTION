-- =============================================================================
-- NICKSCAN ERP SOLUTION - Phase 1 - Enable Row-Level Security on nickscan_production
-- 
-- Each table gets a policy that filters by current_setting('app.tenant_id').
-- The TenantOwnedEntityInterceptor sets this session variable on every
-- connection. Bypass for the postgres superuser via the BYPASSRLS attribute
-- (already on by default for postgres role).
-- =============================================================================
BEGIN;
SET LOCAL search_path = public;

-- aidatasetsnapshots
ALTER TABLE "aidatasetsnapshots" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_aidatasetsnapshots" ON "aidatasetsnapshots";
CREATE POLICY "tenant_isolation_aidatasetsnapshots" ON "aidatasetsnapshots"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- aiimageanalysissuggestions
ALTER TABLE "aiimageanalysissuggestions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_aiimageanalysissuggestions" ON "aiimageanalysissuggestions";
CREATE POLICY "tenant_isolation_aiimageanalysissuggestions" ON "aiimageanalysissuggestions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- analysisassignments
ALTER TABLE "analysisassignments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_analysisassignments" ON "analysisassignments";
CREATE POLICY "tenant_isolation_analysisassignments" ON "analysisassignments"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- analysisgroups
ALTER TABLE "analysisgroups" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_analysisgroups" ON "analysisgroups";
CREATE POLICY "tenant_isolation_analysisgroups" ON "analysisgroups"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- analysisparentgroups
ALTER TABLE "analysisparentgroups" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_analysisparentgroups" ON "analysisparentgroups";
CREATE POLICY "tenant_isolation_analysisparentgroups" ON "analysisparentgroups"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- analysisrecords
ALTER TABLE "analysisrecords" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_analysisrecords" ON "analysisrecords";
CREATE POLICY "tenant_isolation_analysisrecords" ON "analysisrecords"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- analysissettings
ALTER TABLE "analysissettings" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_analysissettings" ON "analysissettings";
CREATE POLICY "tenant_isolation_analysissettings" ON "analysissettings"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- analysissubmissions
ALTER TABLE "analysissubmissions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_analysissubmissions" ON "analysissubmissions";
CREATE POLICY "tenant_isolation_analysissubmissions" ON "analysissubmissions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- applicationlogs
ALTER TABLE "applicationlogs" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_applicationlogs" ON "applicationlogs";
CREATE POLICY "tenant_isolation_applicationlogs" ON "applicationlogs"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- asescans
ALTER TABLE "asescans" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_asescans" ON "asescans";
CREATE POLICY "tenant_isolation_asescans" ON "asescans"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- asesynclogs
ALTER TABLE "asesynclogs" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_asesynclogs" ON "asesynclogs";
CREATE POLICY "tenant_isolation_asesynclogs" ON "asesynclogs"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- attendancerecords
ALTER TABLE "attendancerecords" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_attendancerecords" ON "attendancerecords";
CREATE POLICY "tenant_isolation_attendancerecords" ON "attendancerecords"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- auditdecisions
ALTER TABLE "auditdecisions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_auditdecisions" ON "auditdecisions";
CREATE POLICY "tenant_isolation_auditdecisions" ON "auditdecisions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- auditlogs
ALTER TABLE "auditlogs" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_auditlogs" ON "auditlogs";
CREATE POLICY "tenant_isolation_auditlogs" ON "auditlogs"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- blreviewrecords
ALTER TABLE "blreviewrecords" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_blreviewrecords" ON "blreviewrecords";
CREATE POLICY "tenant_isolation_blreviewrecords" ON "blreviewrecords"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- businessrules
ALTER TABLE "businessrules" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_businessrules" ON "businessrules";
CREATE POLICY "tenant_isolation_businessrules" ON "businessrules"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- containerannotations
ALTER TABLE "containerannotations" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_containerannotations" ON "containerannotations";
CREATE POLICY "tenant_isolation_containerannotations" ON "containerannotations"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- containerboerelations
ALTER TABLE "containerboerelations" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_containerboerelations" ON "containerboerelations";
CREATE POLICY "tenant_isolation_containerboerelations" ON "containerboerelations"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- containercompletenessstatuses
ALTER TABLE "containercompletenessstatuses" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_containercompletenessstatuses" ON "containercompletenessstatuses";
CREATE POLICY "tenant_isolation_containercompletenessstatuses" ON "containercompletenessstatuses"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- containerimages
ALTER TABLE "containerimages" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_containerimages" ON "containerimages";
CREATE POLICY "tenant_isolation_containerimages" ON "containerimages"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- containerreviewdecisions
ALTER TABLE "containerreviewdecisions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_containerreviewdecisions" ON "containerreviewdecisions";
CREATE POLICY "tenant_isolation_containerreviewdecisions" ON "containerreviewdecisions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- containers
ALTER TABLE "containers" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_containers" ON "containers";
CREATE POLICY "tenant_isolation_containers" ON "containers"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- containerscanqueues
ALTER TABLE "containerscanqueues" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_containerscanqueues" ON "containerscanqueues";
CREATE POLICY "tenant_isolation_containerscanqueues" ON "containerscanqueues"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- crossrecordscans
ALTER TABLE "crossrecordscans" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_crossrecordscans" ON "crossrecordscans";
CREATE POLICY "tenant_isolation_crossrecordscans" ON "crossrecordscans"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- decisionagentauditlogs
ALTER TABLE "decisionagentauditlogs" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_decisionagentauditlogs" ON "decisionagentauditlogs";
CREATE POLICY "tenant_isolation_decisionagentauditlogs" ON "decisionagentauditlogs"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- decisionagentconditions
ALTER TABLE "decisionagentconditions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_decisionagentconditions" ON "decisionagentconditions";
CREATE POLICY "tenant_isolation_decisionagentconditions" ON "decisionagentconditions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- decisionagentsettings
ALTER TABLE "decisionagentsettings" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_decisionagentsettings" ON "decisionagentsettings";
CREATE POLICY "tenant_isolation_decisionagentsettings" ON "decisionagentsettings"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- employeepositions
ALTER TABLE "employeepositions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_employeepositions" ON "employeepositions";
CREATE POLICY "tenant_isolation_employeepositions" ON "employeepositions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- employees
ALTER TABLE "employees" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_employees" ON "employees";
CREATE POLICY "tenant_isolation_employees" ON "employees"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- endpointusagelog
ALTER TABLE "endpointusagelog" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_endpointusagelog" ON "endpointusagelog";
CREATE POLICY "tenant_isolation_endpointusagelog" ON "endpointusagelog"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- errorinvestigations
ALTER TABLE "errorinvestigations" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_errorinvestigations" ON "errorinvestigations";
CREATE POLICY "tenant_isolation_errorinvestigations" ON "errorinvestigations"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- fixauditlogs
ALTER TABLE "fixauditlogs" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_fixauditlogs" ON "fixauditlogs";
CREATE POLICY "tenant_isolation_fixauditlogs" ON "fixauditlogs"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- fixproposals
ALTER TABLE "fixproposals" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_fixproposals" ON "fixproposals";
CREATE POLICY "tenant_isolation_fixproposals" ON "fixproposals"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- fs6000fileprocessings
ALTER TABLE "fs6000fileprocessings" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_fs6000fileprocessings" ON "fs6000fileprocessings";
CREATE POLICY "tenant_isolation_fs6000fileprocessings" ON "fs6000fileprocessings"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- fs6000images
ALTER TABLE "fs6000images" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_fs6000images" ON "fs6000images";
CREATE POLICY "tenant_isolation_fs6000images" ON "fs6000images"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- fs6000scans
ALTER TABLE "fs6000scans" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_fs6000scans" ON "fs6000scans";
CREATE POLICY "tenant_isolation_fs6000scans" ON "fs6000scans"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- fs6000synclogs
ALTER TABLE "fs6000synclogs" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_fs6000synclogs" ON "fs6000synclogs";
CREATE POLICY "tenant_isolation_fs6000synclogs" ON "fs6000synclogs"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- heimannsmithscannerdata
ALTER TABLE "heimannsmithscannerdata" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_heimannsmithscannerdata" ON "heimannsmithscannerdata";
CREATE POLICY "tenant_isolation_heimannsmithscannerdata" ON "heimannsmithscannerdata"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- icumcontainerdata
ALTER TABLE "icumcontainerdata" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_icumcontainerdata" ON "icumcontainerdata";
CREATE POLICY "tenant_isolation_icumcontainerdata" ON "icumcontainerdata"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- icummanifestitems
ALTER TABLE "icummanifestitems" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_icummanifestitems" ON "icummanifestitems";
CREATE POLICY "tenant_isolation_icummanifestitems" ON "icummanifestitems"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- icumsdownloadqueues
ALTER TABLE "icumsdownloadqueues" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_icumsdownloadqueues" ON "icumsdownloadqueues";
CREATE POLICY "tenant_isolation_icumsdownloadqueues" ON "icumsdownloadqueues"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- icumssubmissionqueues
ALTER TABLE "icumssubmissionqueues" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_icumssubmissionqueues" ON "icumssubmissionqueues";
CREATE POLICY "tenant_isolation_icumssubmissionqueues" ON "icumssubmissionqueues"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- image_split_assignments
ALTER TABLE "image_split_assignments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_image_split_assignments" ON "image_split_assignments";
CREATE POLICY "tenant_isolation_image_split_assignments" ON "image_split_assignments"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- image_split_jobs
ALTER TABLE "image_split_jobs" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_image_split_jobs" ON "image_split_jobs";
CREATE POLICY "tenant_isolation_image_split_jobs" ON "image_split_jobs"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- image_split_results
ALTER TABLE "image_split_results" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_image_split_results" ON "image_split_results";
CREATE POLICY "tenant_isolation_image_split_results" ON "image_split_results"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- imageanalysisdecisions
ALTER TABLE "imageanalysisdecisions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_imageanalysisdecisions" ON "imageanalysisdecisions";
CREATE POLICY "tenant_isolation_imageanalysisdecisions" ON "imageanalysisdecisions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- imagecaches
ALTER TABLE "imagecaches" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_imagecaches" ON "imagecaches";
CREATE POLICY "tenant_isolation_imagecaches" ON "imagecaches"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- lanes
ALTER TABLE "lanes" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_lanes" ON "lanes";
CREATE POLICY "tenant_isolation_lanes" ON "lanes"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- leaverequests
ALTER TABLE "leaverequests" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_leaverequests" ON "leaverequests";
CREATE POLICY "tenant_isolation_leaverequests" ON "leaverequests"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- manualboerequests
ALTER TABLE "manualboerequests" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_manualboerequests" ON "manualboerequests";
CREATE POLICY "tenant_isolation_manualboerequests" ON "manualboerequests"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- notifications
ALTER TABLE "notifications" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_notifications" ON "notifications";
CREATE POLICY "tenant_isolation_notifications" ON "notifications"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- nuctechscannerdata
ALTER TABLE "nuctechscannerdata" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_nuctechscannerdata" ON "nuctechscannerdata";
CREATE POLICY "tenant_isolation_nuctechscannerdata" ON "nuctechscannerdata"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- organizations
ALTER TABLE "organizations" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_organizations" ON "organizations";
CREATE POLICY "tenant_isolation_organizations" ON "organizations"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- orgunits
ALTER TABLE "orgunits" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_orgunits" ON "orgunits";
CREATE POLICY "tenant_isolation_orgunits" ON "orgunits"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- originalscanrecords
ALTER TABLE "originalscanrecords" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_originalscanrecords" ON "originalscanrecords";
CREATE POLICY "tenant_isolation_originalscanrecords" ON "originalscanrecords"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- permissionauditlogs
ALTER TABLE "permissionauditlogs" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_permissionauditlogs" ON "permissionauditlogs";
CREATE POLICY "tenant_isolation_permissionauditlogs" ON "permissionauditlogs"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- permissions
ALTER TABLE "permissions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_permissions" ON "permissions";
CREATE POLICY "tenant_isolation_permissions" ON "permissions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- positions
ALTER TABLE "positions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_positions" ON "positions";
CREATE POLICY "tenant_isolation_positions" ON "positions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- processingresults
ALTER TABLE "processingresults" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_processingresults" ON "processingresults";
CREATE POLICY "tenant_isolation_processingresults" ON "processingresults"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- rolepermissions
ALTER TABLE "rolepermissions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_rolepermissions" ON "rolepermissions";
CREATE POLICY "tenant_isolation_rolepermissions" ON "rolepermissions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- roles
ALTER TABLE "roles" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_roles" ON "roles";
CREATE POLICY "tenant_isolation_roles" ON "roles"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- scannerassets
ALTER TABLE "scannerassets" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_scannerassets" ON "scannerassets";
CREATE POLICY "tenant_isolation_scannerassets" ON "scannerassets"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- settingshistory
ALTER TABLE "settingshistory" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_settingshistory" ON "settingshistory";
CREATE POLICY "tenant_isolation_settingshistory" ON "settingshistory"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- shiftassignments
ALTER TABLE "shiftassignments" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_shiftassignments" ON "shiftassignments";
CREATE POLICY "tenant_isolation_shiftassignments" ON "shiftassignments"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- shiftcoveragerequirements
ALTER TABLE "shiftcoveragerequirements" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_shiftcoveragerequirements" ON "shiftcoveragerequirements";
CREATE POLICY "tenant_isolation_shiftcoveragerequirements" ON "shiftcoveragerequirements"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- shiftswaprequests
ALTER TABLE "shiftswaprequests" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_shiftswaprequests" ON "shiftswaprequests";
CREATE POLICY "tenant_isolation_shiftswaprequests" ON "shiftswaprequests"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- shifttemplates
ALTER TABLE "shifttemplates" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_shifttemplates" ON "shifttemplates";
CREATE POLICY "tenant_isolation_shifttemplates" ON "shifttemplates"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- sites
ALTER TABLE "sites" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_sites" ON "sites";
CREATE POLICY "tenant_isolation_sites" ON "sites"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- systemsettings
ALTER TABLE "systemsettings" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_systemsettings" ON "systemsettings";
CREATE POLICY "tenant_isolation_systemsettings" ON "systemsettings"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- userpermissions
ALTER TABLE "userpermissions" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_userpermissions" ON "userpermissions";
CREATE POLICY "tenant_isolation_userpermissions" ON "userpermissions"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- userpreferences
ALTER TABLE "userpreferences" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_userpreferences" ON "userpreferences";
CREATE POLICY "tenant_isolation_userpreferences" ON "userpreferences"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- userreadiness
ALTER TABLE "userreadiness" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_userreadiness" ON "userreadiness";
CREATE POLICY "tenant_isolation_userreadiness" ON "userreadiness"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- users
ALTER TABLE "users" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_users" ON "users";
CREATE POLICY "tenant_isolation_users" ON "users"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

-- wavependingcontainers
ALTER TABLE "wavependingcontainers" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS "tenant_isolation_wavependingcontainers" ON "wavependingcontainers";
CREATE POLICY "tenant_isolation_wavependingcontainers" ON "wavependingcontainers"
    USING (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint)
    WITH CHECK (tenant_id = COALESCE(NULLIF(current_setting('app.tenant_id', true), ''), '1')::bigint);

COMMIT;
