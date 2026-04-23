using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NickScanCentralImagingPortal.Infrastructure.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddSplitJobIdToCrossRecordScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 2.15.4 — add splitjobid to crossrecordscans so detection can store
            // the submitted split job's id directly, avoiding a roundtrip to
            // /api/split/search every time an analyst opens the viewer.
            //
            // Auto-generated diff also included pre-existing drift
            // (containerannotations.coordspace*, analysisqueueentries) that
            // was already applied to the DB by hand outside EF — those blocks
            // have been stripped from this migration so it's safe to run on
            // the live schema. The snapshot still carries them so a future
            // `ef migrations add` won't keep regenerating the same drift.
            migrationBuilder.AddColumn<Guid>(
                name: "splitjobid",
                table: "crossrecordscans",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "splitjobid",
                table: "crossrecordscans");
        }
    }
}
