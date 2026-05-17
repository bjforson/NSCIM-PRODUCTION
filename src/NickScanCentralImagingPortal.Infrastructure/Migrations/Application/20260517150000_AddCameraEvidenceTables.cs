using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    public partial class AddCameraEvidenceTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
CREATE TABLE IF NOT EXISTS cameraevidencesites (
    id uuid PRIMARY KEY,
    sitekey character varying(80) NOT NULL,
    displayname character varying(200) NOT NULL,
    locationname character varying(200) NULL,
    baseurl character varying(1000) NOT NULL,
    apikeysecretname character varying(200) NULL,
    webhooksecretname character varying(200) NULL,
    allowedwebhooksourcecidrsjson jsonb NULL,
    verifyssl boolean NOT NULL,
    requesttimeoutseconds integer NOT NULL,
    isenabled boolean NOT NULL,
    createdatutc timestamp with time zone NOT NULL,
    updatedatutc timestamp with time zone NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_cameraevidencesites_sitekey ON cameraevidencesites (sitekey);
CREATE INDEX IF NOT EXISTS ix_cameraevidencesites_isenabled ON cameraevidencesites (isenabled);

CREATE TABLE IF NOT EXISTS cameraevidencesources (
    id uuid PRIMARY KEY,
    siteid uuid NOT NULL,
    provider character varying(50) NOT NULL,
    protectcameraid character varying(200) NULL,
    protectdevicekey character varying(200) NULL,
    macaddress character varying(100) NULL,
    displayname character varying(200) NOT NULL,
    locationname character varying(200) NULL,
    operationalzone character varying(200) NULL,
    expectedtexttype character varying(50) NOT NULL,
    capturemode character varying(50) NOT NULL,
    ocrprofile character varying(80) NOT NULL,
    isenabled boolean NOT NULL,
    createdatutc timestamp with time zone NOT NULL,
    updatedatutc timestamp with time zone NOT NULL,
    CONSTRAINT fk_cameraevidencesources_cameraevidencesites_siteid
        FOREIGN KEY (siteid) REFERENCES cameraevidencesites (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_cameraevidencesources_siteid ON cameraevidencesources (siteid);
CREATE INDEX IF NOT EXISTS ix_cameraevidencesources_siteid_protectcameraid ON cameraevidencesources (siteid, protectcameraid);
CREATE INDEX IF NOT EXISTS ix_cameraevidencesources_siteid_protectdevicekey ON cameraevidencesources (siteid, protectdevicekey);
CREATE INDEX IF NOT EXISTS ix_cameraevidencesources_isenabled ON cameraevidencesources (isenabled);

CREATE TABLE IF NOT EXISTS cameraevidenceevents (
    id uuid PRIMARY KEY,
    siteid uuid NOT NULL,
    sourceid uuid NULL,
    providereventid character varying(200) NULL,
    idempotencykey character varying(256) NOT NULL,
    alarmname character varying(200) NULL,
    triggerkey character varying(200) NULL,
    triggertype character varying(100) NULL,
    protectdevicekey character varying(200) NULL,
    eventtimestamputc timestamp with time zone NULL,
    receivedatutc timestamp with time zone NOT NULL,
    rawpayloadjson jsonb NOT NULL,
    processingstatus character varying(40) NOT NULL,
    processingerror character varying(2000) NULL,
    CONSTRAINT fk_cameraevidenceevents_cameraevidencesites_siteid
        FOREIGN KEY (siteid) REFERENCES cameraevidencesites (id) ON DELETE CASCADE,
    CONSTRAINT fk_cameraevidenceevents_cameraevidencesources_sourceid
        FOREIGN KEY (sourceid) REFERENCES cameraevidencesources (id) ON DELETE SET NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_cameraevidenceevents_siteid_idempotencykey ON cameraevidenceevents (siteid, idempotencykey);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceevents_siteid_eventtimestamputc ON cameraevidenceevents (siteid, eventtimestamputc);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceevents_sourceid ON cameraevidenceevents (sourceid);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceevents_receivedatutc ON cameraevidenceevents (receivedatutc);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceevents_processingstatus ON cameraevidenceevents (processingstatus);

CREATE TABLE IF NOT EXISTS cameraevidenceframes (
    id uuid PRIMARY KEY,
    eventid uuid NOT NULL,
    siteid uuid NOT NULL,
    sourceid uuid NOT NULL,
    capturemode character varying(50) NOT NULL,
    frametimestamputc timestamp with time zone NOT NULL,
    relativeoffsetms integer NULL,
    storagepath character varying(1200) NOT NULL,
    contenttype character varying(100) NOT NULL,
    sha256 character varying(128) NOT NULL,
    width integer NULL,
    height integer NULL,
    ishighquality boolean NOT NULL,
    protectsnapshotparametersjson jsonb NULL,
    createdatutc timestamp with time zone NOT NULL,
    CONSTRAINT fk_cameraevidenceframes_cameraevidenceevents_eventid
        FOREIGN KEY (eventid) REFERENCES cameraevidenceevents (id) ON DELETE CASCADE,
    CONSTRAINT fk_cameraevidenceframes_cameraevidencesites_siteid
        FOREIGN KEY (siteid) REFERENCES cameraevidencesites (id) ON DELETE CASCADE,
    CONSTRAINT fk_cameraevidenceframes_cameraevidencesources_sourceid
        FOREIGN KEY (sourceid) REFERENCES cameraevidencesources (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_cameraevidenceframes_eventid ON cameraevidenceframes (eventid);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceframes_siteid ON cameraevidenceframes (siteid);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceframes_sourceid ON cameraevidenceframes (sourceid);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceframes_frametimestamputc ON cameraevidenceframes (frametimestamputc);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceframes_sha256 ON cameraevidenceframes (sha256);

CREATE TABLE IF NOT EXISTS cameraevidenceocrresults (
    id uuid PRIMARY KEY,
    frameid uuid NOT NULL,
    siteid uuid NOT NULL,
    sourceid uuid NOT NULL,
    engine character varying(80) NOT NULL,
    engineversion character varying(80) NULL,
    rawtext text NOT NULL,
    normalizedtext character varying(500) NULL,
    candidatetype character varying(50) NOT NULL,
    confidence double precision NOT NULL,
    validationstatus character varying(50) NOT NULL,
    validationreasonsjson jsonb NULL,
    boundingboxesjson jsonb NULL,
    reviewstatus character varying(40) NOT NULL,
    createdatutc timestamp with time zone NOT NULL,
    CONSTRAINT fk_cameraevidenceocrresults_cameraevidenceframes_frameid
        FOREIGN KEY (frameid) REFERENCES cameraevidenceframes (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_cameraevidenceocrresults_frameid ON cameraevidenceocrresults (frameid);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceocrresults_siteid ON cameraevidenceocrresults (siteid);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceocrresults_sourceid ON cameraevidenceocrresults (sourceid);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceocrresults_normalizedtext ON cameraevidenceocrresults (normalizedtext);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceocrresults_candidatetype ON cameraevidenceocrresults (candidatetype);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceocrresults_reviewstatus ON cameraevidenceocrresults (reviewstatus);

CREATE TABLE IF NOT EXISTS cameraevidencereviewdecisions (
    id uuid PRIMARY KEY,
    ocrresultid uuid NOT NULL,
    revieweruserid character varying(100) NOT NULL,
    decision character varying(40) NOT NULL,
    correctedtext character varying(500) NULL,
    correctedcandidatetype character varying(50) NULL,
    notes character varying(2000) NULL,
    createdatutc timestamp with time zone NOT NULL,
    CONSTRAINT fk_cameraevidencereviewdecisions_cameraevidenceocrresults_ocrresultid
        FOREIGN KEY (ocrresultid) REFERENCES cameraevidenceocrresults (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_cameraevidencereviewdecisions_ocrresultid ON cameraevidencereviewdecisions (ocrresultid);
CREATE INDEX IF NOT EXISTS ix_cameraevidencereviewdecisions_decision ON cameraevidencereviewdecisions (decision);
CREATE INDEX IF NOT EXISTS ix_cameraevidencereviewdecisions_createdatutc ON cameraevidencereviewdecisions (createdatutc);

CREATE TABLE IF NOT EXISTS cameraevidencecorelinkcandidates (
    id uuid PRIMARY KEY,
    eventid uuid NOT NULL,
    ocrresultid uuid NULL,
    candidatevalue character varying(500) NOT NULL,
    candidatetype character varying(50) NOT NULL,
    coreentitytype character varying(80) NULL,
    coreentitykey character varying(200) NULL,
    matchconfidence double precision NOT NULL,
    matchreason character varying(1000) NULL,
    promotionstate character varying(60) NOT NULL,
    createdatutc timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_cameraevidencecorelinkcandidates_eventid ON cameraevidencecorelinkcandidates (eventid);
CREATE INDEX IF NOT EXISTS ix_cameraevidencecorelinkcandidates_ocrresultid ON cameraevidencecorelinkcandidates (ocrresultid);
CREATE INDEX IF NOT EXISTS ix_cameraevidencecorelinkcandidates_candidatevalue ON cameraevidencecorelinkcandidates (candidatevalue);
CREATE INDEX IF NOT EXISTS ix_cameraevidencecorelinkcandidates_promotionstate ON cameraevidencecorelinkcandidates (promotionstate);

CREATE TABLE IF NOT EXISTS cameraevidencepromotionrequests (
    id uuid PRIMARY KEY,
    requestedbyuserid character varying(100) NOT NULL,
    requestedatutc timestamp with time zone NOT NULL,
    datafield character varying(200) NOT NULL,
    coreconsumer character varying(200) NOT NULL,
    proposeduse text NOT NULL,
    riskassessment text NOT NULL,
    accuracyevidence text NOT NULL,
    rollbackplan text NOT NULL,
    featureflag character varying(200) NOT NULL,
    status character varying(40) NOT NULL,
    approvedbyuserid character varying(100) NULL,
    approvedatutc timestamp with time zone NULL
);

CREATE INDEX IF NOT EXISTS ix_cameraevidencepromotionrequests_status ON cameraevidencepromotionrequests (status);
CREATE INDEX IF NOT EXISTS ix_cameraevidencepromotionrequests_requestedatutc ON cameraevidencepromotionrequests (requestedatutc);

CREATE TABLE IF NOT EXISTS cameraevidenceauditlogs (
    id uuid PRIMARY KEY,
    siteid uuid NULL,
    eventid uuid NULL,
    entityid uuid NULL,
    entitytype character varying(80) NOT NULL,
    action character varying(80) NOT NULL,
    actoruserid character varying(100) NULL,
    detailsjson jsonb NULL,
    createdatutc timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_cameraevidenceauditlogs_siteid ON cameraevidenceauditlogs (siteid);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceauditlogs_eventid ON cameraevidenceauditlogs (eventid);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceauditlogs_entityid ON cameraevidenceauditlogs (entityid);
CREATE INDEX IF NOT EXISTS ix_cameraevidenceauditlogs_createdatutc ON cameraevidenceauditlogs (createdatutc);

CREATE TABLE IF NOT EXISTS cameraevidencequeueitems (
    id uuid PRIMARY KEY,
    siteid uuid NOT NULL,
    eventid uuid NOT NULL,
    frameid uuid NULL,
    worktype character varying(50) NOT NULL,
    status character varying(40) NOT NULL,
    attemptcount integer NOT NULL,
    nextattemptatutc timestamp with time zone NULL,
    lockeduntilutc timestamp with time zone NULL,
    lasterror character varying(2000) NULL,
    createdatutc timestamp with time zone NOT NULL,
    updatedatutc timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_cameraevidencequeueitems_siteid ON cameraevidencequeueitems (siteid);
CREATE INDEX IF NOT EXISTS ix_cameraevidencequeueitems_eventid ON cameraevidencequeueitems (eventid);
CREATE INDEX IF NOT EXISTS ix_cameraevidencequeueitems_frameid ON cameraevidencequeueitems (frameid);
CREATE INDEX IF NOT EXISTS ix_cameraevidencequeueitems_status_nextattemptatutc_createdatutc ON cameraevidencequeueitems (status, nextattemptatutc, createdatutc);
CREATE INDEX IF NOT EXISTS ix_cameraevidencequeueitems_worktype_status ON cameraevidencequeueitems (worktype, status);
""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP TABLE IF EXISTS cameraevidencequeueitems;
DROP TABLE IF EXISTS cameraevidenceauditlogs;
DROP TABLE IF EXISTS cameraevidencepromotionrequests;
DROP TABLE IF EXISTS cameraevidencecorelinkcandidates;
DROP TABLE IF EXISTS cameraevidencereviewdecisions;
DROP TABLE IF EXISTS cameraevidenceocrresults;
DROP TABLE IF EXISTS cameraevidenceframes;
DROP TABLE IF EXISTS cameraevidenceevents;
DROP TABLE IF EXISTS cameraevidencesources;
DROP TABLE IF EXISTS cameraevidencesites;
""");
        }
    }
}
