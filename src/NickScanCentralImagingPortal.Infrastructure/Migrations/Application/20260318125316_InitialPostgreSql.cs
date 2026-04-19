using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class InitialPostgreSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analysisassignments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    groupid = table.Column<Guid>(type: "uuid", nullable: false),
                    assignedto = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    leaseuntilutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastaccessedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysisassignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analysisgroups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    groupidentifier = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    normalizedgroupidentifier = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    grouptype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    partiallycompleteddate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    totalcontainercount = table.Column<int>(type: "integer", nullable: true),
                    submittedcontainercount = table.Column<int>(type: "integer", nullable: true),
                    pendingcontainercount = table.Column<int>(type: "integer", nullable: true),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysisgroups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analysisrecords",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    groupid = table.Column<Guid>(type: "uuid", nullable: false),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    imageurl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    metadataref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    completenessref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysisrecords", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analysissettings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    assignmentmode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    autoassignstrategy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    autoassign = table.Column<bool>(type: "boolean", nullable: false),
                    leaseminutes = table.Column<int>(type: "integer", nullable: false),
                    maxconcurrentperuser = table.Column<int>(type: "integer", nullable: false),
                    minyearforintake = table.Column<int>(type: "integer", nullable: true),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysissettings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analysissubmissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    groupid = table.Column<Guid>(type: "uuid", nullable: false),
                    payloadpath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    payloadhash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    retries = table.Column<int>(type: "integer", nullable: false),
                    lasterror = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    totalcontainercount = table.Column<int>(type: "integer", nullable: false),
                    submittedcontainercount = table.Column<int>(type: "integer", nullable: false),
                    pendingcontainercount = table.Column<int>(type: "integer", nullable: false),
                    ispartiallycompleted = table.Column<bool>(type: "boolean", nullable: false),
                    partiallycompleteddate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submittedcontainernumbers = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    pendingcontainernumbers = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    submittedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysissubmissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asescans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inspectionid = table.Column<int>(type: "integer", nullable: false),
                    scantime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    inspectionuuid = table.Column<string>(type: "text", nullable: false),
                    containernumber = table.Column<string>(type: "text", nullable: true),
                    truckplate = table.Column<string>(type: "text", nullable: true),
                    scanimage = table.Column<byte[]>(type: "bytea", nullable: true),
                    imagedisplayname = table.Column<string>(type: "text", nullable: true),
                    syncedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asescans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asesynclogs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    lastsyncedinspectionid = table.Column<int>(type: "integer", nullable: false),
                    lastsynctime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recordsprocessed = table.Column<int>(type: "integer", nullable: false),
                    syncstatus = table.Column<string>(type: "text", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asesynclogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "auditlogs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    userid = table.Column<int>(type: "integer", nullable: true),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    eventtype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    entitytype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    entityid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ipaddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    useragent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    oldvalue = table.Column<string>(type: "text", nullable: true),
                    newvalue = table.Column<string>(type: "text", nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    additionaldatajson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditlogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "blreviewrecords",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    masterblnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reviewstartedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewcompletedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reviewedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reviewstatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    finaldecision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    blcomments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    totalcontainers = table.Column<int>(type: "integer", nullable: false),
                    reviewedcontainers = table.Column<int>(type: "integer", nullable: false),
                    normalcontainers = table.Column<int>(type: "integer", nullable: false),
                    abnormalcontainers = table.Column<int>(type: "integer", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blreviewrecords", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "businessrules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    conditionexpression = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    actiontype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    actionmessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    executionorder = table.Column<int>(type: "integer", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updatedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_businessrules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "containerannotations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    x1 = table.Column<double>(type: "double precision", nullable: false),
                    y1 = table.Column<double>(type: "double precision", nullable: false),
                    x2 = table.Column<double>(type: "double precision", nullable: false),
                    y2 = table.Column<double>(type: "double precision", nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "#ff0000"),
                    width = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updatedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    isdeleted = table.Column<bool>(type: "boolean", nullable: false),
                    deletedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deletedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containerannotations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "containerboerelations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannerdataid = table.Column<int>(type: "integer", nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    icumsboeid = table.Column<int>(type: "integer", nullable: false),
                    relationtype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    lastvalidatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containerboerelations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "containercompletenessstatuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    inspectionid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    scandate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    hasicumsdata = table.Column<bool>(type: "boolean", nullable: false),
                    icumsdatadate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    boedocumentid = table.Column<int>(type: "integer", nullable: true),
                    clearancetype = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scannerdatacompleteness = table.Column<int>(type: "integer", nullable: false),
                    icumsdatacompleteness = table.Column<int>(type: "integer", nullable: false),
                    imagedatacompleteness = table.Column<int>(type: "integer", nullable: false),
                    overallcompleteness = table.Column<int>(type: "integer", nullable: false),
                    hasscannerdata = table.Column<bool>(type: "boolean", nullable: false),
                    hasimagedata = table.Column<bool>(type: "boolean", nullable: false),
                    isconsolidated = table.Column<bool>(type: "boolean", nullable: false),
                    totalhousebls = table.Column<int>(type: "integer", nullable: true),
                    completehousebls = table.Column<int>(type: "integer", nullable: true),
                    consolidationdetails = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    groupidentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    retrycount = table.Column<int>(type: "integer", nullable: false),
                    lastcheckedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    workflowstage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containercompletenessstatuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "containers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containerid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scannerid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scandatetime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processingstatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "containerscanqueues",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    inspectionid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    scandate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    retrycount = table.Column<int>(type: "integer", nullable: false),
                    maxretries = table.Column<int>(type: "integer", nullable: false),
                    queuedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    metadata = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containerscanqueues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "crossrecordscans",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    originalscanrecord = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scannerrecordid = table.Column<Guid>(type: "uuid", nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scandatetime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    container1 = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    container1_boe = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    container1_consignee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    container1_crms = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    container1_clearancetype = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    container1_masterbl = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    container1_rotation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    container2 = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    container2_boe = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    container2_consignee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    container2_crms = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    container2_clearancetype = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    container2_masterbl = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    container2_rotation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    crossrecordtype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requiresreview = table.Column<bool>(type: "boolean", nullable: false),
                    samedeclaration = table.Column<bool>(type: "boolean", nullable: false),
                    sameconsignee = table.Column<bool>(type: "boolean", nullable: false),
                    samemasterbl = table.Column<bool>(type: "boolean", nullable: false),
                    samerotation = table.Column<bool>(type: "boolean", nullable: false),
                    samecrms = table.Column<bool>(type: "boolean", nullable: false),
                    sameclearancetype = table.Column<bool>(type: "boolean", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reviewedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reviewnotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reviewstatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crossrecordscans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "endpointusagelog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    statuscode = table.Column<int>(type: "integer", nullable: false),
                    responsetimems = table.Column<double>(type: "double precision", nullable: false),
                    ipaddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    useragent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    isdeprecated = table.Column<bool>(type: "boolean", nullable: false),
                    isphase3route = table.Column<bool>(type: "boolean", nullable: false),
                    correlationid = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_endpointusagelog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "errorinvestigations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    investigationgroupid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    errorpattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    errorcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    serviceid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    operation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    exceptiontype = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    occurrencecount = table.Column<int>(type: "integer", nullable: false),
                    firstseen = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lastseen = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    investigationsummary = table.Column<string>(type: "text", nullable: true),
                    investigationdetails = table.Column<string>(type: "text", nullable: true),
                    relatedlogids = table.Column<string>(type: "text", nullable: true),
                    sampleerrormessage = table.Column<string>(type: "text", nullable: true),
                    samplestacktrace = table.Column<string>(type: "text", nullable: true),
                    hasproposedfix = table.Column<bool>(type: "boolean", nullable: false),
                    approvedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    approvedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approvalnotes = table.Column<string>(type: "text", nullable: true),
                    fixbranchname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fixedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    isverified = table.Column<bool>(type: "boolean", nullable: false),
                    verifiedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    verifiedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_errorinvestigations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fs6000fileprocessings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    filepath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    filename = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    filetype = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    processingstatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fs6000fileprocessings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fs6000scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scantime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    picnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fycopresent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vesselname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    operatorid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    scanresult = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    goodsdescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    shippingcompany = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    consignee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    filepath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    syncstatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hasimage = table.Column<bool>(type: "boolean", nullable: false),
                    imagecount = table.Column<int>(type: "integer", nullable: false),
                    imageingestedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    imagevalidationerror = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fs6000scans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fs6000synclogs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sourcepath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    destinationpath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    syncstatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    retrycount = table.Column<int>(type: "integer", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lastretryat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fs6000synclogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "heimannsmithscannerdata",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containerid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannerid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scandatetime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    rawdata = table.Column<string>(type: "text", nullable: true),
                    imagepath = table.Column<string>(type: "text", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processingstatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_heimannsmithscannerdata", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "icumcontainerdata",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    boedata = table.Column<string>(type: "text", nullable: true),
                    masterblnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    housebl = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rotationnumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consigneename = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shippername = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    countryoforigin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    totaldutypaid = table.Column<decimal>(type: "numeric", nullable: true),
                    crmslevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    clearancetype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    declarationnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    containerweight = table.Column<decimal>(type: "numeric", nullable: true),
                    containerquantity = table.Column<int>(type: "integer", nullable: true),
                    containeriso = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icumcontainerdata", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "icumsdownloadqueues",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    queuedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    firstattemptat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastattemptat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retrycount = table.Column<int>(type: "integer", nullable: false),
                    maxretries = table.Column<int>(type: "integer", nullable: false),
                    lasterrormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    lasterrorcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    requestedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    requestsource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    metadata = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icumsdownloadqueues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "icumssubmissionqueues",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    imagepaths = table.Column<string>(type: "text", nullable: false),
                    reportdata = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    submittedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    icumsresponseid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    retrycount = table.Column<int>(type: "integer", nullable: false),
                    nextretryat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submittedby = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    completedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icumssubmissionqueues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "imageanalysisdecisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comments = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tags = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    suspiciousareas = table.Column<string>(type: "text", nullable: true),
                    reviewedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reviewedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    groupidentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    isconsolidated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imageanalysisdecisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "imagecaches",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    imagedata = table.Column<byte[]>(type: "bytea", nullable: false),
                    mimetype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "image/jpeg"),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    filesizebytes = table.Column<long>(type: "bigint", nullable: false),
                    scantime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cachedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processingpipeline = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quality = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "High")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imagecaches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "manualboerequests",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    requestdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    icumsresponseid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    retrycount = table.Column<int>(type: "integer", nullable: false),
                    completedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    nextretryat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    requestedby = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manualboerequests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    notificationtype = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    targetuser = table.Column<string>(type: "text", nullable: true),
                    targetrole = table.Column<string>(type: "text", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expiresat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    isread = table.Column<bool>(type: "boolean", nullable: false),
                    readat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    additionaldatajson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nuctechscannerdata",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containerid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scannerid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scandatetime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    rawdata = table.Column<string>(type: "text", nullable: true),
                    imagepath = table.Column<string>(type: "text", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processingstatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nuctechscannerdata", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parentorganizationid = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updatedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizations", x => x.id);
                    table.ForeignKey(
                        name: "fk_organizations_organizations_parentorganizationid",
                        column: x => x.parentorganizationid,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    displayname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    displayname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    baserole = table.Column<int>(type: "integer", nullable: true),
                    issystemrole = table.Column<bool>(type: "boolean", nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updatedby = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "systemsettings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    settingkey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    settingvalue = table.Column<string>(type: "text", nullable: false),
                    datatype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    defaultvalue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    isencrypted = table.Column<bool>(type: "boolean", nullable: false),
                    requiresrestart = table.Column<bool>(type: "boolean", nullable: false),
                    allowedroles = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    displayorder = table.Column<int>(type: "integer", nullable: false),
                    validationrules = table.Column<string>(type: "text", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lastmodifiedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_systemsettings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "userpreferences",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<int>(type: "integer", nullable: false),
                    preferencekey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    preferencevalue = table.Column<string>(type: "text", nullable: false),
                    datatype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userpreferences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "userreadiness",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    isready = table.Column<bool>(type: "boolean", nullable: false),
                    lastheartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lastchangedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    changedby = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sessionid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userreadiness", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "containerreviewdecisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    blreviewrecordid = table.Column<int>(type: "integer", nullable: false),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    comments = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    reviewedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reviewedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hasscanner = table.Column<bool>(type: "boolean", nullable: false),
                    hasicums = table.Column<bool>(type: "boolean", nullable: false),
                    hasimages = table.Column<bool>(type: "boolean", nullable: false),
                    scannertype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containerreviewdecisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_containerreviewdecisions_blreviewrecords_blreviewrecordid",
                        column: x => x.blreviewrecordid,
                        principalTable: "blreviewrecords",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "containerimages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containerid = table.Column<int>(type: "integer", nullable: false),
                    imagepath = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    imagetype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    filesizebytes = table.Column<long>(type: "bigint", nullable: false),
                    originalfilename = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processingstatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containerimages", x => x.id);
                    table.ForeignKey(
                        name: "fk_containerimages_containers_containerid",
                        column: x => x.containerid,
                        principalTable: "containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processingresults",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containerid = table.Column<int>(type: "integer", nullable: false),
                    resulttype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resultdata = table.Column<string>(type: "text", nullable: true),
                    errormessage = table.Column<string>(type: "text", nullable: true),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processingresults", x => x.id);
                    table.ForeignKey(
                        name: "fk_processingresults_containers_containerid",
                        column: x => x.containerid,
                        principalTable: "containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fixproposals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    errorinvestigationid = table.Column<long>(type: "bigint", nullable: false),
                    fixtype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: true),
                    impactassessment = table.Column<string>(type: "text", nullable: true),
                    codechanges = table.Column<string>(type: "text", nullable: true),
                    configurationchanges = table.Column<string>(type: "text", nullable: true),
                    affectedfiles = table.Column<string>(type: "text", nullable: true),
                    risklevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    approvedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    approvedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approvalnotes = table.Column<string>(type: "text", nullable: true),
                    implementedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    branchname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    commithash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixproposals", x => x.id);
                    table.ForeignKey(
                        name: "fk_fixproposals_errorinvestigations_errorinvestigationid",
                        column: x => x.errorinvestigationid,
                        principalTable: "errorinvestigations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fs6000images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scanid = table.Column<Guid>(type: "uuid", nullable: false),
                    imagetype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    filename = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    imagedata = table.Column<byte[]>(type: "bytea", nullable: true),
                    filesizebytes = table.Column<int>(type: "integer", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fs6000images", x => x.id);
                    table.ForeignKey(
                        name: "fk_fs6000images_fs6000scans_scanid",
                        column: x => x.scanid,
                        principalTable: "fs6000scans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "icummanifestitems",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    icumcontainerdataid = table.Column<int>(type: "integer", nullable: false),
                    housebl = table.Column<string>(type: "text", nullable: true),
                    hscode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    weight = table.Column<decimal>(type: "numeric", nullable: false),
                    itemfob = table.Column<decimal>(type: "numeric", nullable: false),
                    itemdutypaid = table.Column<decimal>(type: "numeric", nullable: false),
                    fobcurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    countryoforigin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    itemno = table.Column<int>(type: "integer", nullable: false),
                    cpc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icummanifestitems", x => x.id);
                    table.ForeignKey(
                        name: "fk_icummanifestitems_icumcontainerdata_icumcontainerdataid",
                        column: x => x.icumcontainerdataid,
                        principalTable: "icumcontainerdata",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auditdecisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    groupidentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    imageanalysisdecisionid = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    auditnotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    auditedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    auditedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    overallgroupdecision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    iscompleted = table.Column<bool>(type: "boolean", nullable: false),
                    completedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditdecisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_auditdecisions_imageanalysisdecisions_imageanalysisdecision~",
                        column: x => x.imageanalysisdecisionid,
                        principalTable: "imageanalysisdecisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    organizationid = table.Column<Guid>(type: "uuid", nullable: false),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    operationalhours = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sites", x => x.id);
                    table.ForeignKey(
                        name: "fk_sites_organizations_organizationid",
                        column: x => x.organizationid,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rolepermissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    roleid = table.Column<int>(type: "integer", nullable: false),
                    permissionid = table.Column<int>(type: "integer", nullable: false),
                    grantedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    grantedby = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rolepermissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_rolepermissions_permissions_permissionid",
                        column: x => x.permissionid,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rolepermissions_roles_roleid",
                        column: x => x.roleid,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    usernumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    passwordhash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    firstname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    lastname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    phonenumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    roleid = table.Column<int>(type: "integer", nullable: true),
                    legacyrole = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lastloginat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdby = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updatedby = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_roles_roleid",
                        column: x => x.roleid,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "settingshistory",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    systemsettingid = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    settingkey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    oldvalue = table.Column<string>(type: "text", nullable: true),
                    newvalue = table.Column<string>(type: "text", nullable: false),
                    changedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ipaddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    changedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settingshistory", x => x.id);
                    table.ForeignKey(
                        name: "fk_settingshistory_systemsettings_systemsettingid",
                        column: x => x.systemsettingid,
                        principalTable: "systemsettings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fixauditlogs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    errorinvestigationid = table.Column<long>(type: "bigint", nullable: true),
                    fixproposalid = table.Column<long>(type: "bigint", nullable: true),
                    actiontype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    performedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<string>(type: "text", nullable: true),
                    ipaddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    useragent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixauditlogs", x => x.id);
                    table.ForeignKey(
                        name: "fk_fixauditlogs_errorinvestigations_errorinvestigationid",
                        column: x => x.errorinvestigationid,
                        principalTable: "errorinvestigations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_fixauditlogs_fixproposals_fixproposalid",
                        column: x => x.fixproposalid,
                        principalTable: "fixproposals",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employeenumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    firstname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lastname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    othernames = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    dateofbirth = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    nationalid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    organizationid = table.Column<Guid>(type: "uuid", nullable: false),
                    primarysiteid = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hiredate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    terminationdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    employmenttype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updatedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_organizations_organizationid",
                        column: x => x.organizationid,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employees_sites_primarysiteid",
                        column: x => x.primarysiteid,
                        principalTable: "sites",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "orgunits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizationid = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parentorgunitid = table.Column<Guid>(type: "uuid", nullable: true),
                    siteid = table.Column<Guid>(type: "uuid", nullable: true),
                    costcentercode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orgunits", x => x.id);
                    table.ForeignKey(
                        name: "fk_orgunits_organizations_organizationid",
                        column: x => x.organizationid,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_orgunits_orgunits_parentorgunitid",
                        column: x => x.parentorgunitid,
                        principalTable: "orgunits",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_orgunits_sites_siteid",
                        column: x => x.siteid,
                        principalTable: "sites",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "scannerassets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    siteid = table.Column<Guid>(type: "uuid", nullable: false),
                    manufacturer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    serialnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scannertype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    energytype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    commissionedon = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scannerassets", x => x.id);
                    table.ForeignKey(
                        name: "fk_scannerassets_sites_siteid",
                        column: x => x.siteid,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shifttemplates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    starttime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    endtime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    durationhours = table.Column<decimal>(type: "numeric", nullable: false),
                    isnightshift = table.Column<bool>(type: "boolean", nullable: false),
                    breakrules = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    siteid = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updatedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shifttemplates", x => x.id);
                    table.ForeignKey(
                        name: "fk_shifttemplates_sites_siteid",
                        column: x => x.siteid,
                        principalTable: "sites",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "permissionauditlogs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entitytype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entityid = table.Column<int>(type: "integer", nullable: false),
                    permissionid = table.Column<int>(type: "integer", nullable: true),
                    roleid = table.Column<int>(type: "integer", nullable: true),
                    userid = table.Column<int>(type: "integer", nullable: true),
                    result = table.Column<bool>(type: "boolean", nullable: true),
                    ipaddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissionauditlogs", x => x.id);
                    table.ForeignKey(
                        name: "fk_permissionauditlogs_permissions_permissionid",
                        column: x => x.permissionid,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_permissionauditlogs_roles_roleid",
                        column: x => x.roleid,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_permissionauditlogs_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "userpermissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<int>(type: "integer", nullable: false),
                    permissionid = table.Column<int>(type: "integer", nullable: false),
                    isgranted = table.Column<bool>(type: "boolean", nullable: false),
                    grantedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    grantedby = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    expiresat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userpermissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_userpermissions_permissions_permissionid",
                        column: x => x.permissionid,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_userpermissions_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leaverequests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employeeid = table.Column<Guid>(type: "uuid", nullable: false),
                    leavetype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    startdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enddate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requestedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    approvedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    approvedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejectionreason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leaverequests", x => x.id);
                    table.ForeignKey(
                        name: "fk_leaverequests_employees_employeeid",
                        column: x => x.employeeid,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    orgunitid = table.Column<Guid>(type: "uuid", nullable: false),
                    siteid = table.Column<Guid>(type: "uuid", nullable: true),
                    grade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    positiontype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    headcount = table.Column<int>(type: "integer", nullable: false),
                    iscritical = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updatedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                    table.ForeignKey(
                        name: "fk_positions_orgunits_orgunitid",
                        column: x => x.orgunitid,
                        principalTable: "orgunits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_positions_sites_siteid",
                        column: x => x.siteid,
                        principalTable: "sites",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "lanes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    siteid = table.Column<Guid>(type: "uuid", nullable: false),
                    scannerassetid = table.Column<Guid>(type: "uuid", nullable: true),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    maxthroughputperhour = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lanes", x => x.id);
                    table.ForeignKey(
                        name: "fk_lanes_scannerassets_scannerassetid",
                        column: x => x.scannerassetid,
                        principalTable: "scannerassets",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_lanes_sites_siteid",
                        column: x => x.siteid,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employeepositions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employeeid = table.Column<Guid>(type: "uuid", nullable: false),
                    positionid = table.Column<Guid>(type: "uuid", nullable: false),
                    primary = table.Column<bool>(type: "boolean", nullable: false),
                    effectivefrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effectiveto = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updatedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employeepositions", x => x.id);
                    table.ForeignKey(
                        name: "fk_employeepositions_employees_employeeid",
                        column: x => x.employeeid,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employeepositions_positions_positionid",
                        column: x => x.positionid,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shiftassignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employeeid = table.Column<Guid>(type: "uuid", nullable: false),
                    positionid = table.Column<Guid>(type: "uuid", nullable: true),
                    siteid = table.Column<Guid>(type: "uuid", nullable: false),
                    laneid = table.Column<Guid>(type: "uuid", nullable: true),
                    shifttemplateid = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actualstarttime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    actualendtime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    shifttype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updatedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shiftassignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_shiftassignments_employees_employeeid",
                        column: x => x.employeeid,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shiftassignments_lanes_laneid",
                        column: x => x.laneid,
                        principalTable: "lanes",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_shiftassignments_positions_positionid",
                        column: x => x.positionid,
                        principalTable: "positions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_shiftassignments_shifttemplates_shifttemplateid",
                        column: x => x.shifttemplateid,
                        principalTable: "shifttemplates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shiftassignments_sites_siteid",
                        column: x => x.siteid,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shiftcoveragerequirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    siteid = table.Column<Guid>(type: "uuid", nullable: false),
                    laneid = table.Column<Guid>(type: "uuid", nullable: true),
                    shifttemplateid = table.Column<Guid>(type: "uuid", nullable: false),
                    dayofweek = table.Column<int>(type: "integer", nullable: true),
                    requiredrole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    minimumheadcount = table.Column<int>(type: "integer", nullable: false),
                    preferredheadcount = table.Column<int>(type: "integer", nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    effectivefrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effectiveto = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updatedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shiftcoveragerequirements", x => x.id);
                    table.ForeignKey(
                        name: "fk_shiftcoveragerequirements_lanes_laneid",
                        column: x => x.laneid,
                        principalTable: "lanes",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_shiftcoveragerequirements_shifttemplates_shifttemplateid",
                        column: x => x.shifttemplateid,
                        principalTable: "shifttemplates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shiftcoveragerequirements_sites_siteid",
                        column: x => x.siteid,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendancerecords",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shiftassignmentid = table.Column<Guid>(type: "uuid", nullable: true),
                    employeeid = table.Column<Guid>(type: "uuid", nullable: false),
                    siteid = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    checkintime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    checkouttime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    lateminutes = table.Column<int>(type: "integer", nullable: true),
                    earlyleaveminutes = table.Column<int>(type: "integer", nullable: true),
                    overtimeminutes = table.Column<int>(type: "integer", nullable: true),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approvedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendancerecords", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendancerecords_employees_employeeid",
                        column: x => x.employeeid,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attendancerecords_shiftassignments_shiftassignmentid",
                        column: x => x.shiftassignmentid,
                        principalTable: "shiftassignments",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_attendancerecords_sites_siteid",
                        column: x => x.siteid,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shiftswaprequests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requestingemployeeid = table.Column<Guid>(type: "uuid", nullable: false),
                    requestingshiftassignmentid = table.Column<Guid>(type: "uuid", nullable: false),
                    requestedemployeeid = table.Column<Guid>(type: "uuid", nullable: true),
                    requestedshiftassignmentid = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requestedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approvedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    approvedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejectionreason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shiftswaprequests", x => x.id);
                    table.ForeignKey(
                        name: "fk_shiftswaprequests_employees_requestedemployeeid",
                        column: x => x.requestedemployeeid,
                        principalTable: "employees",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_shiftswaprequests_employees_requestingemployeeid",
                        column: x => x.requestingemployeeid,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shiftswaprequests_shiftassignments_requestedshiftassignment~",
                        column: x => x.requestedshiftassignmentid,
                        principalTable: "shiftassignments",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_shiftswaprequests_shiftassignments_requestingshiftassignmen~",
                        column: x => x.requestingshiftassignmentid,
                        principalTable: "shiftassignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analysisassignments_assignedto",
                table: "analysisassignments",
                column: "assignedto");

            migrationBuilder.CreateIndex(
                name: "ix_analysisassignments_groupid_role_state",
                table: "analysisassignments",
                columns: new[] { "groupid", "role", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_analysisgroups_groupidentifier",
                table: "analysisgroups",
                column: "groupidentifier");

            migrationBuilder.CreateIndex(
                name: "ix_analysisgroups_groupidentifier_scannertype",
                table: "analysisgroups",
                columns: new[] { "groupidentifier", "scannertype" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_analysisgroups_normalizedgroupidentifier_scannertype",
                table: "analysisgroups",
                columns: new[] { "normalizedgroupidentifier", "scannertype" });

            migrationBuilder.CreateIndex(
                name: "ix_analysisgroups_status",
                table: "analysisgroups",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_analysisgroups_status_priority",
                table: "analysisgroups",
                columns: new[] { "status", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_analysisrecords_groupid_containernumber",
                table: "analysisrecords",
                columns: new[] { "groupid", "containernumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_analysisrecords_status",
                table: "analysisrecords",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_analysissubmissions_groupid_status",
                table: "analysissubmissions",
                columns: new[] { "groupid", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_attendancerecords_date",
                table: "attendancerecords",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_attendancerecords_employeeid",
                table: "attendancerecords",
                column: "employeeid");

            migrationBuilder.CreateIndex(
                name: "ix_attendancerecords_shiftassignmentid",
                table: "attendancerecords",
                column: "shiftassignmentid");

            migrationBuilder.CreateIndex(
                name: "ix_attendancerecords_siteid",
                table: "attendancerecords",
                column: "siteid");

            migrationBuilder.CreateIndex(
                name: "ix_auditdecisions_imageanalysisdecisionid",
                table: "auditdecisions",
                column: "imageanalysisdecisionid");

            migrationBuilder.CreateIndex(
                name: "ix_blreviewrecords_finaldecision",
                table: "blreviewrecords",
                column: "finaldecision");

            migrationBuilder.CreateIndex(
                name: "ix_blreviewrecords_masterblnumber",
                table: "blreviewrecords",
                column: "masterblnumber");

            migrationBuilder.CreateIndex(
                name: "ix_blreviewrecords_reviewcompletedat",
                table: "blreviewrecords",
                column: "reviewcompletedat");

            migrationBuilder.CreateIndex(
                name: "ix_blreviewrecords_reviewedby",
                table: "blreviewrecords",
                column: "reviewedby");

            migrationBuilder.CreateIndex(
                name: "ix_blreviewrecords_reviewstartedat",
                table: "blreviewrecords",
                column: "reviewstartedat");

            migrationBuilder.CreateIndex(
                name: "ix_blreviewrecords_reviewstatus",
                table: "blreviewrecords",
                column: "reviewstatus");

            migrationBuilder.CreateIndex(
                name: "ix_businessrules_category",
                table: "businessrules",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_businessrules_executionorder",
                table: "businessrules",
                column: "executionorder");

            migrationBuilder.CreateIndex(
                name: "ix_businessrules_isactive",
                table: "businessrules",
                column: "isactive");

            migrationBuilder.CreateIndex(
                name: "ix_businessrules_isactive_executionorder",
                table: "businessrules",
                columns: new[] { "isactive", "executionorder" });

            migrationBuilder.CreateIndex(
                name: "ix_businessrules_priority",
                table: "businessrules",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_containerannotations_containernumber",
                table: "containerannotations",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_containerannotations_createdat",
                table: "containerannotations",
                column: "createdat");

            migrationBuilder.CreateIndex(
                name: "ix_containerannotations_createdby",
                table: "containerannotations",
                column: "createdby");

            migrationBuilder.CreateIndex(
                name: "ix_containerannotations_isdeleted",
                table: "containerannotations",
                column: "isdeleted");

            migrationBuilder.CreateIndex(
                name: "ix_containerannotations_type",
                table: "containerannotations",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "ix_containerboerelations_containernumber",
                table: "containerboerelations",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_containerboerelations_icumsboeid",
                table: "containerboerelations",
                column: "icumsboeid");

            migrationBuilder.CreateIndex(
                name: "ix_containerboerelations_isactive",
                table: "containerboerelations",
                column: "isactive");

            migrationBuilder.CreateIndex(
                name: "ix_containerboerelations_scannertype",
                table: "containerboerelations",
                column: "scannertype");

            migrationBuilder.CreateIndex(
                name: "ix_containercompletenessstatuses_containernumber",
                table: "containercompletenessstatuses",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_containercompletenessstatuses_containernumber_scannertype",
                table: "containercompletenessstatuses",
                columns: new[] { "containernumber", "scannertype" });

            migrationBuilder.CreateIndex(
                name: "ix_containercompletenessstatuses_hasicumsdata",
                table: "containercompletenessstatuses",
                column: "hasicumsdata");

            migrationBuilder.CreateIndex(
                name: "ix_containercompletenessstatuses_scannertype",
                table: "containercompletenessstatuses",
                column: "scannertype");

            migrationBuilder.CreateIndex(
                name: "ix_containercompletenessstatuses_status",
                table: "containercompletenessstatuses",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_containercompletenessstatuses_status_workflowstage",
                table: "containercompletenessstatuses",
                columns: new[] { "status", "workflowstage" });

            migrationBuilder.CreateIndex(
                name: "ix_containerimages_containerid",
                table: "containerimages",
                column: "containerid");

            migrationBuilder.CreateIndex(
                name: "ix_containerreviewdecisions_blreviewrecordid",
                table: "containerreviewdecisions",
                column: "blreviewrecordid");

            migrationBuilder.CreateIndex(
                name: "ix_containerreviewdecisions_containernumber",
                table: "containerreviewdecisions",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_containerreviewdecisions_decision",
                table: "containerreviewdecisions",
                column: "decision");

            migrationBuilder.CreateIndex(
                name: "ix_containerreviewdecisions_reviewedat",
                table: "containerreviewdecisions",
                column: "reviewedat");

            migrationBuilder.CreateIndex(
                name: "ix_employeepositions_effectivefrom",
                table: "employeepositions",
                column: "effectivefrom");

            migrationBuilder.CreateIndex(
                name: "ix_employeepositions_effectiveto",
                table: "employeepositions",
                column: "effectiveto");

            migrationBuilder.CreateIndex(
                name: "ix_employeepositions_employeeid_positionid_status",
                table: "employeepositions",
                columns: new[] { "employeeid", "positionid", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_employeepositions_positionid",
                table: "employeepositions",
                column: "positionid");

            migrationBuilder.CreateIndex(
                name: "ix_employees_employeenumber",
                table: "employees",
                column: "employeenumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_organizationid",
                table: "employees",
                column: "organizationid");

            migrationBuilder.CreateIndex(
                name: "ix_employees_primarysiteid",
                table: "employees",
                column: "primarysiteid");

            migrationBuilder.CreateIndex(
                name: "ix_employees_status",
                table: "employees",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_endpointusagelog_endpoint_timestamp",
                table: "endpointusagelog",
                columns: new[] { "endpoint", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_endpointusagelog_isdeprecated_timestamp",
                table: "endpointusagelog",
                columns: new[] { "isdeprecated", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_endpointusagelog_isphase3route_timestamp",
                table: "endpointusagelog",
                columns: new[] { "isphase3route", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_endpointusagelog_timestamp",
                table: "endpointusagelog",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_fixauditlogs_errorinvestigationid",
                table: "fixauditlogs",
                column: "errorinvestigationid");

            migrationBuilder.CreateIndex(
                name: "ix_fixauditlogs_fixproposalid",
                table: "fixauditlogs",
                column: "fixproposalid");

            migrationBuilder.CreateIndex(
                name: "ix_fixproposals_errorinvestigationid",
                table: "fixproposals",
                column: "errorinvestigationid");

            migrationBuilder.CreateIndex(
                name: "ix_fs6000images_scanid",
                table: "fs6000images",
                column: "scanid");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_containernumber",
                table: "icumcontainerdata",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_createdat",
                table: "icumcontainerdata",
                column: "createdat");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_housebl",
                table: "icumcontainerdata",
                column: "housebl");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_masterblnumber",
                table: "icumcontainerdata",
                column: "masterblnumber");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_rotationnumber",
                table: "icumcontainerdata",
                column: "rotationnumber");

            migrationBuilder.CreateIndex(
                name: "ix_icumcontainerdata_status",
                table: "icumcontainerdata",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_icummanifestitems_hscode",
                table: "icummanifestitems",
                column: "hscode");

            migrationBuilder.CreateIndex(
                name: "ix_icummanifestitems_icumcontainerdataid",
                table: "icummanifestitems",
                column: "icumcontainerdataid");

            migrationBuilder.CreateIndex(
                name: "ix_icummanifestitems_itemno",
                table: "icummanifestitems",
                column: "itemno");

            migrationBuilder.CreateIndex(
                name: "ix_icumsdownloadqueues_containernumber",
                table: "icumsdownloadqueues",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_icumsdownloadqueues_containernumber_status",
                table: "icumsdownloadqueues",
                columns: new[] { "containernumber", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_icumsdownloadqueues_priority",
                table: "icumsdownloadqueues",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_icumsdownloadqueues_queuedat",
                table: "icumsdownloadqueues",
                column: "queuedat");

            migrationBuilder.CreateIndex(
                name: "ix_icumsdownloadqueues_status",
                table: "icumsdownloadqueues",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_icumssubmissionqueues_containernumber",
                table: "icumssubmissionqueues",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_icumssubmissionqueues_nextretryat",
                table: "icumssubmissionqueues",
                column: "nextretryat");

            migrationBuilder.CreateIndex(
                name: "ix_icumssubmissionqueues_priority",
                table: "icumssubmissionqueues",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_icumssubmissionqueues_status",
                table: "icumssubmissionqueues",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_imagecaches_containernumber",
                table: "imagecaches",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_imagecaches_containernumber_scannertype",
                table: "imagecaches",
                columns: new[] { "containernumber", "scannertype" });

            migrationBuilder.CreateIndex(
                name: "ix_imagecaches_scannertype",
                table: "imagecaches",
                column: "scannertype");

            migrationBuilder.CreateIndex(
                name: "ix_lanes_scannerassetid",
                table: "lanes",
                column: "scannerassetid");

            migrationBuilder.CreateIndex(
                name: "ix_lanes_siteid_code",
                table: "lanes",
                columns: new[] { "siteid", "code" });

            migrationBuilder.CreateIndex(
                name: "ix_lanes_status",
                table: "lanes",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_leaverequests_employeeid",
                table: "leaverequests",
                column: "employeeid");

            migrationBuilder.CreateIndex(
                name: "ix_leaverequests_startdate_enddate",
                table: "leaverequests",
                columns: new[] { "startdate", "enddate" });

            migrationBuilder.CreateIndex(
                name: "ix_leaverequests_status",
                table: "leaverequests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_manualboerequests_containernumber",
                table: "manualboerequests",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_manualboerequests_nextretryat",
                table: "manualboerequests",
                column: "nextretryat");

            migrationBuilder.CreateIndex(
                name: "ix_manualboerequests_requestdate",
                table: "manualboerequests",
                column: "requestdate");

            migrationBuilder.CreateIndex(
                name: "ix_manualboerequests_status",
                table: "manualboerequests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_isread_targetuser_targetrole",
                table: "notifications",
                columns: new[] { "isread", "targetuser", "targetrole" });

            migrationBuilder.CreateIndex(
                name: "ix_organizations_code",
                table: "organizations",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organizations_parentorganizationid",
                table: "organizations",
                column: "parentorganizationid");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_status",
                table: "organizations",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_orgunits_organizationid_code",
                table: "orgunits",
                columns: new[] { "organizationid", "code" });

            migrationBuilder.CreateIndex(
                name: "ix_orgunits_parentorgunitid",
                table: "orgunits",
                column: "parentorgunitid");

            migrationBuilder.CreateIndex(
                name: "ix_orgunits_siteid",
                table: "orgunits",
                column: "siteid");

            migrationBuilder.CreateIndex(
                name: "ix_orgunits_status",
                table: "orgunits",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_permissionauditlogs_action",
                table: "permissionauditlogs",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_permissionauditlogs_entityid",
                table: "permissionauditlogs",
                column: "entityid");

            migrationBuilder.CreateIndex(
                name: "ix_permissionauditlogs_entitytype",
                table: "permissionauditlogs",
                column: "entitytype");

            migrationBuilder.CreateIndex(
                name: "ix_permissionauditlogs_permissionid",
                table: "permissionauditlogs",
                column: "permissionid");

            migrationBuilder.CreateIndex(
                name: "ix_permissionauditlogs_result",
                table: "permissionauditlogs",
                column: "result");

            migrationBuilder.CreateIndex(
                name: "ix_permissionauditlogs_roleid",
                table: "permissionauditlogs",
                column: "roleid");

            migrationBuilder.CreateIndex(
                name: "ix_permissionauditlogs_timestamp",
                table: "permissionauditlogs",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_permissionauditlogs_userid",
                table: "permissionauditlogs",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_category",
                table: "permissions",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_isactive",
                table: "permissions",
                column: "isactive");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_name",
                table: "permissions",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_code",
                table: "positions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_orgunitid",
                table: "positions",
                column: "orgunitid");

            migrationBuilder.CreateIndex(
                name: "ix_positions_siteid",
                table: "positions",
                column: "siteid");

            migrationBuilder.CreateIndex(
                name: "ix_positions_status",
                table: "positions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_processingresults_containerid",
                table: "processingresults",
                column: "containerid");

            migrationBuilder.CreateIndex(
                name: "ix_rolepermissions_permissionid",
                table: "rolepermissions",
                column: "permissionid");

            migrationBuilder.CreateIndex(
                name: "ix_rolepermissions_roleid",
                table: "rolepermissions",
                column: "roleid");

            migrationBuilder.CreateIndex(
                name: "ix_rolepermissions_roleid_permissionid",
                table: "rolepermissions",
                columns: new[] { "roleid", "permissionid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_isactive",
                table: "roles",
                column: "isactive");

            migrationBuilder.CreateIndex(
                name: "ix_roles_issystemrole",
                table: "roles",
                column: "issystemrole");

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scannerassets_code",
                table: "scannerassets",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scannerassets_siteid",
                table: "scannerassets",
                column: "siteid");

            migrationBuilder.CreateIndex(
                name: "ix_scannerassets_status",
                table: "scannerassets",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_settingshistory_category",
                table: "settingshistory",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_settingshistory_changedat",
                table: "settingshistory",
                column: "changedat");

            migrationBuilder.CreateIndex(
                name: "ix_settingshistory_changedby",
                table: "settingshistory",
                column: "changedby");

            migrationBuilder.CreateIndex(
                name: "ix_settingshistory_systemsettingid",
                table: "settingshistory",
                column: "systemsettingid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftassignments_date",
                table: "shiftassignments",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_shiftassignments_employeeid",
                table: "shiftassignments",
                column: "employeeid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftassignments_employeeid_siteid_date_shifttemplateid",
                table: "shiftassignments",
                columns: new[] { "employeeid", "siteid", "date", "shifttemplateid" });

            migrationBuilder.CreateIndex(
                name: "ix_shiftassignments_laneid",
                table: "shiftassignments",
                column: "laneid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftassignments_positionid",
                table: "shiftassignments",
                column: "positionid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftassignments_shifttemplateid",
                table: "shiftassignments",
                column: "shifttemplateid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftassignments_siteid",
                table: "shiftassignments",
                column: "siteid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftassignments_status",
                table: "shiftassignments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_shiftcoveragerequirements_isactive_effectivefrom_effectiveto",
                table: "shiftcoveragerequirements",
                columns: new[] { "isactive", "effectivefrom", "effectiveto" });

            migrationBuilder.CreateIndex(
                name: "ix_shiftcoveragerequirements_laneid",
                table: "shiftcoveragerequirements",
                column: "laneid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftcoveragerequirements_shifttemplateid",
                table: "shiftcoveragerequirements",
                column: "shifttemplateid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftcoveragerequirements_siteid",
                table: "shiftcoveragerequirements",
                column: "siteid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftswaprequests_requestedemployeeid",
                table: "shiftswaprequests",
                column: "requestedemployeeid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftswaprequests_requestedshiftassignmentid",
                table: "shiftswaprequests",
                column: "requestedshiftassignmentid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftswaprequests_requestingemployeeid",
                table: "shiftswaprequests",
                column: "requestingemployeeid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftswaprequests_requestingshiftassignmentid",
                table: "shiftswaprequests",
                column: "requestingshiftassignmentid");

            migrationBuilder.CreateIndex(
                name: "ix_shiftswaprequests_status",
                table: "shiftswaprequests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_shifttemplates_code",
                table: "shifttemplates",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shifttemplates_siteid",
                table: "shifttemplates",
                column: "siteid");

            migrationBuilder.CreateIndex(
                name: "ix_shifttemplates_status",
                table: "shifttemplates",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_sites_code",
                table: "sites",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sites_organizationid",
                table: "sites",
                column: "organizationid");

            migrationBuilder.CreateIndex(
                name: "ix_sites_status",
                table: "sites",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_systemsettings_category",
                table: "systemsettings",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_systemsettings_category_settingkey",
                table: "systemsettings",
                columns: new[] { "category", "settingkey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_systemsettings_isactive",
                table: "systemsettings",
                column: "isactive");

            migrationBuilder.CreateIndex(
                name: "ix_systemsettings_lastmodifiedat",
                table: "systemsettings",
                column: "lastmodifiedat");

            migrationBuilder.CreateIndex(
                name: "ix_userpermissions_expiresat",
                table: "userpermissions",
                column: "expiresat");

            migrationBuilder.CreateIndex(
                name: "ix_userpermissions_isgranted",
                table: "userpermissions",
                column: "isgranted");

            migrationBuilder.CreateIndex(
                name: "ix_userpermissions_permissionid",
                table: "userpermissions",
                column: "permissionid");

            migrationBuilder.CreateIndex(
                name: "ix_userpermissions_userid",
                table: "userpermissions",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_userpermissions_userid_permissionid",
                table: "userpermissions",
                columns: new[] { "userid", "permissionid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_userpreferences_userid",
                table: "userpreferences",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_userpreferences_userid_preferencekey",
                table: "userpreferences",
                columns: new[] { "userid", "preferencekey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_userreadiness_lastheartbeat",
                table: "userreadiness",
                column: "lastheartbeat");

            migrationBuilder.CreateIndex(
                name: "ix_userreadiness_role_ready_heartbeat",
                table: "userreadiness",
                columns: new[] { "role", "isready", "lastheartbeat" });

            migrationBuilder.CreateIndex(
                name: "ix_userreadiness_username",
                table: "userreadiness",
                column: "username");

            migrationBuilder.CreateIndex(
                name: "ix_userreadiness_username_role",
                table: "userreadiness",
                columns: new[] { "username", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_isactive",
                table: "users",
                column: "isactive");

            migrationBuilder.CreateIndex(
                name: "ix_users_roleid",
                table: "users",
                column: "roleid");

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_usernumber",
                table: "users",
                column: "usernumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analysisassignments");

            migrationBuilder.DropTable(
                name: "analysisgroups");

            migrationBuilder.DropTable(
                name: "analysisrecords");

            migrationBuilder.DropTable(
                name: "analysissettings");

            migrationBuilder.DropTable(
                name: "analysissubmissions");

            migrationBuilder.DropTable(
                name: "asescans");

            migrationBuilder.DropTable(
                name: "asesynclogs");

            migrationBuilder.DropTable(
                name: "attendancerecords");

            migrationBuilder.DropTable(
                name: "auditdecisions");

            migrationBuilder.DropTable(
                name: "auditlogs");

            migrationBuilder.DropTable(
                name: "businessrules");

            migrationBuilder.DropTable(
                name: "containerannotations");

            migrationBuilder.DropTable(
                name: "containerboerelations");

            migrationBuilder.DropTable(
                name: "containercompletenessstatuses");

            migrationBuilder.DropTable(
                name: "containerimages");

            migrationBuilder.DropTable(
                name: "containerreviewdecisions");

            migrationBuilder.DropTable(
                name: "containerscanqueues");

            migrationBuilder.DropTable(
                name: "crossrecordscans");

            migrationBuilder.DropTable(
                name: "employeepositions");

            migrationBuilder.DropTable(
                name: "endpointusagelog");

            migrationBuilder.DropTable(
                name: "fixauditlogs");

            migrationBuilder.DropTable(
                name: "fs6000fileprocessings");

            migrationBuilder.DropTable(
                name: "fs6000images");

            migrationBuilder.DropTable(
                name: "fs6000synclogs");

            migrationBuilder.DropTable(
                name: "heimannsmithscannerdata");

            migrationBuilder.DropTable(
                name: "icummanifestitems");

            migrationBuilder.DropTable(
                name: "icumsdownloadqueues");

            migrationBuilder.DropTable(
                name: "icumssubmissionqueues");

            migrationBuilder.DropTable(
                name: "imagecaches");

            migrationBuilder.DropTable(
                name: "leaverequests");

            migrationBuilder.DropTable(
                name: "manualboerequests");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "nuctechscannerdata");

            migrationBuilder.DropTable(
                name: "permissionauditlogs");

            migrationBuilder.DropTable(
                name: "processingresults");

            migrationBuilder.DropTable(
                name: "rolepermissions");

            migrationBuilder.DropTable(
                name: "settingshistory");

            migrationBuilder.DropTable(
                name: "shiftcoveragerequirements");

            migrationBuilder.DropTable(
                name: "shiftswaprequests");

            migrationBuilder.DropTable(
                name: "userpermissions");

            migrationBuilder.DropTable(
                name: "userpreferences");

            migrationBuilder.DropTable(
                name: "userreadiness");

            migrationBuilder.DropTable(
                name: "imageanalysisdecisions");

            migrationBuilder.DropTable(
                name: "blreviewrecords");

            migrationBuilder.DropTable(
                name: "fixproposals");

            migrationBuilder.DropTable(
                name: "fs6000scans");

            migrationBuilder.DropTable(
                name: "icumcontainerdata");

            migrationBuilder.DropTable(
                name: "containers");

            migrationBuilder.DropTable(
                name: "systemsettings");

            migrationBuilder.DropTable(
                name: "shiftassignments");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "errorinvestigations");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "lanes");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropTable(
                name: "shifttemplates");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "scannerassets");

            migrationBuilder.DropTable(
                name: "orgunits");

            migrationBuilder.DropTable(
                name: "sites");

            migrationBuilder.DropTable(
                name: "organizations");
        }
    }
}
