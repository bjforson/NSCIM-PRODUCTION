using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.IcumDownloadsDb
{
    /// <inheritdoc />
    public partial class InitialPostgreSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archivedfiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    downloadedfileid = table.Column<int>(type: "integer", nullable: false),
                    originalfilename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    originalfilepath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    archivefilename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    archivefilepath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    archivedirectory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    originalsizebytes = table.Column<long>(type: "bigint", nullable: false),
                    archivedsizebytes = table.Column<long>(type: "bigint", nullable: false),
                    compressionratio = table.Column<double>(type: "double precision", nullable: false),
                    compressiontype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    processeddate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    archiveddate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    containernumbers = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    documentcount = table.Column<int>(type: "integer", nullable: false),
                    filetype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    isrestored = table.Column<bool>(type: "boolean", nullable: false),
                    restoreddate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archivedfiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cmrredownloadqueue",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    queuedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    retrycount = table.Column<int>(type: "integer", nullable: false),
                    maxretries = table.Column<int>(type: "integer", nullable: false),
                    processedby = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    originaldeclarationnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    originalclearancetype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cmrredownloadqueue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cmrvalidationmetrics",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    recordedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    totalcmrrecords = table.Column<int>(type: "integer", nullable: false),
                    validcmrrecords = table.Column<int>(type: "integer", nullable: false),
                    invalidcmrrecords = table.Column<int>(type: "integer", nullable: false),
                    validationsuccessrate = table.Column<double>(type: "double precision", nullable: false),
                    missingblnumber = table.Column<int>(type: "integer", nullable: false),
                    missingrotationnumber = table.Column<int>(type: "integer", nullable: false),
                    missingbothfields = table.Column<int>(type: "integer", nullable: false),
                    newrecordstoday = table.Column<int>(type: "integer", nullable: false),
                    fixedrecordstoday = table.Column<int>(type: "integer", nullable: false),
                    newissuesdetectedtoday = table.Column<int>(type: "integer", nullable: false),
                    queuependingcount = table.Column<int>(type: "integer", nullable: false),
                    queueprocessingcount = table.Column<int>(type: "integer", nullable: false),
                    queuecompletedcount = table.Column<int>(type: "integer", nullable: false),
                    queuefailedcount = table.Column<int>(type: "integer", nullable: false),
                    averageredownloadtimeminutes = table.Column<double>(type: "double precision", nullable: false),
                    queuesuccessrate = table.Column<double>(type: "double precision", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cmrvalidationmetrics", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "containerdownloadhistory",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    downloadedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    downloadsource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    hasvaliddata = table.Column<bool>(type: "boolean", nullable: false),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    downloadedfileid = table.Column<int>(type: "integer", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_containerdownloadhistory", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "downloadedfiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    filename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    filepath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    filesize = table.Column<long>(type: "bigint", nullable: false),
                    filehash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    downloaddate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processeddate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processingstatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    errormessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recordcount = table.Column<int>(type: "integer", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_downloadedfiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "failedprocessingqueue",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    downloadedfileid = table.Column<int>(type: "integer", nullable: false),
                    filename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    filepath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    failurereason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    errordetails = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    failurestage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    retrycount = table.Column<int>(type: "integer", nullable: false),
                    maxretries = table.Column<int>(type: "integer", nullable: false),
                    nextretryat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    failedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolvedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_failedprocessingqueue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "icumsdownloadqueue",
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
                    table.PrimaryKey("pk_icumsdownloadqueue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "boedocuments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    downloadedfileid = table.Column<int>(type: "integer", nullable: false),
                    documentindex = table.Column<int>(type: "integer", nullable: false),
                    containernumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    containerdescription = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    containeriso = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    containerquantity = table.Column<int>(type: "integer", nullable: true),
                    containerweight = table.Column<decimal>(type: "numeric", nullable: true),
                    impname = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    totaldutypaid = table.Column<decimal>(type: "numeric", nullable: true),
                    crmslevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    expaddress = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    declarationnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    regimecode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    noofcontainers = table.Column<int>(type: "integer", nullable: true),
                    compoffremarks = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    declarantname = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expname = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    impaddress = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    impexpname = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ccvrintelremarks = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    declarationversion = table.Column<int>(type: "integer", nullable: true),
                    impexpaddress = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    declarationdate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    clearancetype = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    declarantaddress = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    rotationnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    consigneename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    countryoforigin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    marksnumbers = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    shippername = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    shipperaddress = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    blnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    deliveryplace = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    housebl = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    consigneeaddress = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    goodsdescription = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    isconsolidated = table.Column<bool>(type: "boolean", nullable: false),
                    unmappedfield1label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield1value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield2label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield2value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield3label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield3value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield4label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield4value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield5label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield5value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield6label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield6value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield7label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield7value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield8label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield8value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield9label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield9value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield10label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield10value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield11label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield11value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield12label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield12value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield13label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield13value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield14label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield14value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield15label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield15value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield16label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield16value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield17label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield17value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield18label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield18value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield19label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield19value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield20label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield20value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    rawjsondata = table.Column<string>(type: "text", nullable: true),
                    unmappedfieldscount = table.Column<int>(type: "integer", nullable: true),
                    unmappedfieldsoverflow = table.Column<bool>(type: "boolean", nullable: false),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processingstatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    errormessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boedocuments", x => x.id);
                    table.ForeignKey(
                        name: "fk_boedocuments_downloadedfiles_downloadedfileid",
                        column: x => x.downloadedfileid,
                        principalTable: "downloadedfiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ingestionlogs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    downloadedfileid = table.Column<int>(type: "integer", nullable: false),
                    processtype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    starttime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endtime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    recordsprocessed = table.Column<int>(type: "integer", nullable: true),
                    errormessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingestionlogs", x => x.id);
                    table.ForeignKey(
                        name: "fk_ingestionlogs_downloadedfiles_downloadedfileid",
                        column: x => x.downloadedfileid,
                        principalTable: "downloadedfiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manifestitems",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    boedocumentid = table.Column<int>(type: "integer", nullable: false),
                    itemindex = table.Column<int>(type: "integer", nullable: false),
                    hscode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    weight = table.Column<decimal>(type: "numeric", nullable: true),
                    itemfob = table.Column<decimal>(type: "numeric", nullable: true),
                    itemdutypaid = table.Column<decimal>(type: "numeric", nullable: true),
                    fobcurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    countryoforigin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    itemno = table.Column<int>(type: "integer", nullable: true),
                    cpc = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unmappedfield1label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield1value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield2label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield2value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield3label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield3value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield4label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield4value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield5label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield5value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield6label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield6value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield7label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield7value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield8label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield8value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield9label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield9value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield10label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield10value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield11label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield11value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield12label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield12value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield13label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield13value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield14label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield14value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield15label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield15value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield16label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield16value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield17label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield17value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield18label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield18value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield19label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield19value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    unmappedfield20label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    unmappedfield20value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    rawjsondata = table.Column<string>(type: "text", nullable: true),
                    unmappedfieldscount = table.Column<int>(type: "integer", nullable: true),
                    unmappedfieldsoverflow = table.Column<bool>(type: "boolean", nullable: false),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processingstatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    errormessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manifestitems", x => x.id);
                    table.ForeignKey(
                        name: "fk_manifestitems_boedocuments_boedocumentid",
                        column: x => x.boedocumentid,
                        principalTable: "boedocuments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicleimports",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vin = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    boedocumentid = table.Column<int>(type: "integer", nullable: false),
                    declarationnumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    chassisnumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    vehicletype = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    make = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vehicleyear = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    enginecapacity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    weight = table.Column<decimal>(type: "numeric", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    hscode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    countryoforigin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    fobvalue = table.Column<decimal>(type: "numeric", nullable: true),
                    fobcurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    dutypaid = table.Column<decimal>(type: "numeric", nullable: true),
                    importername = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    shippername = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    consigneename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    blnumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    housebl = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rotationnumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    clearancetype = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    crmslevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    processingstatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    errormessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    importtype = table.Column<int>(type: "integer", nullable: false),
                    containernumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicleimports", x => x.id);
                    table.ForeignKey(
                        name: "fk_vehicleimports_boedocuments_boedocumentid",
                        column: x => x.boedocumentid,
                        principalTable: "boedocuments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archivedfile_archiveddate_filetype",
                table: "archivedfiles",
                columns: new[] { "archiveddate", "filetype" });

            migrationBuilder.CreateIndex(
                name: "ix_archivedfiles_archiveddate",
                table: "archivedfiles",
                column: "archiveddate");

            migrationBuilder.CreateIndex(
                name: "ix_archivedfiles_downloadedfileid",
                table: "archivedfiles",
                column: "downloadedfileid");

            migrationBuilder.CreateIndex(
                name: "ix_archivedfiles_filetype",
                table: "archivedfiles",
                column: "filetype");

            migrationBuilder.CreateIndex(
                name: "ix_archivedfiles_isrestored",
                table: "archivedfiles",
                column: "isrestored");

            migrationBuilder.CreateIndex(
                name: "ix_archivedfiles_processeddate",
                table: "archivedfiles",
                column: "processeddate");

            migrationBuilder.CreateIndex(
                name: "ix_boedocument_container_unique_when_declaration_null",
                table: "boedocuments",
                column: "containernumber",
                unique: true,
                filter: "declarationnumber IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_boedocument_containernumber_declarationnumber_unique",
                table: "boedocuments",
                columns: new[] { "containernumber", "declarationnumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_boedocuments_declarationnumber",
                table: "boedocuments",
                column: "declarationnumber");

            migrationBuilder.CreateIndex(
                name: "ix_boedocuments_downloadedfileid",
                table: "boedocuments",
                column: "downloadedfileid");

            migrationBuilder.CreateIndex(
                name: "ix_boedocuments_processingstatus",
                table: "boedocuments",
                column: "processingstatus");

            migrationBuilder.CreateIndex(
                name: "ix_cmrredownloadqueue_containernumber",
                table: "cmrredownloadqueue",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_cmrredownloadqueue_processedat",
                table: "cmrredownloadqueue",
                column: "processedat");

            migrationBuilder.CreateIndex(
                name: "ix_cmrredownloadqueue_queuedat",
                table: "cmrredownloadqueue",
                column: "queuedat");

            migrationBuilder.CreateIndex(
                name: "ix_cmrredownloadqueue_status",
                table: "cmrredownloadqueue",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_cmrredownloadqueue_statuspriorityqueued",
                table: "cmrredownloadqueue",
                columns: new[] { "status", "priority", "queuedat" });

            migrationBuilder.CreateIndex(
                name: "ix_cmrvalidationmetrics_createdat",
                table: "cmrvalidationmetrics",
                column: "createdat");

            migrationBuilder.CreateIndex(
                name: "ix_cmrvalidationmetrics_recordedat",
                table: "cmrvalidationmetrics",
                column: "recordedat");

            migrationBuilder.CreateIndex(
                name: "ix_containerdownloadhistory_containernumber",
                table: "containerdownloadhistory",
                column: "containernumber");

            migrationBuilder.CreateIndex(
                name: "ix_containerdownloadhistory_containernumber_downloadedat",
                table: "containerdownloadhistory",
                columns: new[] { "containernumber", "downloadedat" });

            migrationBuilder.CreateIndex(
                name: "ix_containerdownloadhistory_downloadedat",
                table: "containerdownloadhistory",
                column: "downloadedat");

            migrationBuilder.CreateIndex(
                name: "ix_containerdownloadhistory_hasvaliddata",
                table: "containerdownloadhistory",
                column: "hasvaliddata");

            migrationBuilder.CreateIndex(
                name: "ix_downloadedfiles_downloaddate",
                table: "downloadedfiles",
                column: "downloaddate");

            migrationBuilder.CreateIndex(
                name: "ix_downloadedfiles_filehash",
                table: "downloadedfiles",
                column: "filehash");

            migrationBuilder.CreateIndex(
                name: "ix_downloadedfiles_filename",
                table: "downloadedfiles",
                column: "filename");

            migrationBuilder.CreateIndex(
                name: "ix_downloadedfiles_processingstatus",
                table: "downloadedfiles",
                column: "processingstatus");

            migrationBuilder.CreateIndex(
                name: "ix_failedprocessingqueue_downloadedfileid",
                table: "failedprocessingqueue",
                column: "downloadedfileid");

            migrationBuilder.CreateIndex(
                name: "ix_failedprocessingqueue_failedat",
                table: "failedprocessingqueue",
                column: "failedat");

            migrationBuilder.CreateIndex(
                name: "ix_failedprocessingqueue_nextretryat",
                table: "failedprocessingqueue",
                column: "nextretryat");

            migrationBuilder.CreateIndex(
                name: "ix_failedprocessingqueue_status",
                table: "failedprocessingqueue",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_failedprocessingqueue_status_nextretryat",
                table: "failedprocessingqueue",
                columns: new[] { "status", "nextretryat" });

            migrationBuilder.CreateIndex(
                name: "ix_icumsdownloadqueue_completedat",
                table: "icumsdownloadqueue",
                column: "completedat");

            migrationBuilder.CreateIndex(
                name: "ix_icumsdownloadqueue_containernumber",
                table: "icumsdownloadqueue",
                column: "containernumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_icumsdownloadqueue_queuedat",
                table: "icumsdownloadqueue",
                column: "queuedat");

            migrationBuilder.CreateIndex(
                name: "ix_icumsdownloadqueue_status",
                table: "icumsdownloadqueue",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_icumsdownloadqueue_statuspriorityqueued",
                table: "icumsdownloadqueue",
                columns: new[] { "status", "priority", "queuedat" });

            migrationBuilder.CreateIndex(
                name: "ix_ingestionlogs_downloadedfileid",
                table: "ingestionlogs",
                column: "downloadedfileid");

            migrationBuilder.CreateIndex(
                name: "ix_ingestionlogs_processtype",
                table: "ingestionlogs",
                column: "processtype");

            migrationBuilder.CreateIndex(
                name: "ix_ingestionlogs_status",
                table: "ingestionlogs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_manifestitems_boedocumentid",
                table: "manifestitems",
                column: "boedocumentid");

            migrationBuilder.CreateIndex(
                name: "ix_manifestitems_hscode",
                table: "manifestitems",
                column: "hscode");

            migrationBuilder.CreateIndex(
                name: "ix_manifestitems_processingstatus",
                table: "manifestitems",
                column: "processingstatus");

            migrationBuilder.CreateIndex(
                name: "ix_vehicleimports_boedocumentid",
                table: "vehicleimports",
                column: "boedocumentid");

            migrationBuilder.CreateIndex(
                name: "ix_vehicleimports_chassisnumber",
                table: "vehicleimports",
                column: "chassisnumber");

            migrationBuilder.CreateIndex(
                name: "ix_vehicleimports_createdat",
                table: "vehicleimports",
                column: "createdat");

            migrationBuilder.CreateIndex(
                name: "ix_vehicleimports_declarationnumber",
                table: "vehicleimports",
                column: "declarationnumber");

            migrationBuilder.CreateIndex(
                name: "ix_vehicleimports_importtype",
                table: "vehicleimports",
                column: "importtype");

            migrationBuilder.CreateIndex(
                name: "ix_vehicleimports_processingstatus",
                table: "vehicleimports",
                column: "processingstatus");

            migrationBuilder.CreateIndex(
                name: "ix_vehicleimports_vin",
                table: "vehicleimports",
                column: "vin",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archivedfiles");

            migrationBuilder.DropTable(
                name: "cmrredownloadqueue");

            migrationBuilder.DropTable(
                name: "cmrvalidationmetrics");

            migrationBuilder.DropTable(
                name: "containerdownloadhistory");

            migrationBuilder.DropTable(
                name: "failedprocessingqueue");

            migrationBuilder.DropTable(
                name: "icumsdownloadqueue");

            migrationBuilder.DropTable(
                name: "ingestionlogs");

            migrationBuilder.DropTable(
                name: "manifestitems");

            migrationBuilder.DropTable(
                name: "vehicleimports");

            migrationBuilder.DropTable(
                name: "boedocuments");

            migrationBuilder.DropTable(
                name: "downloadedfiles");
        }
    }
}
