using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NickERP.Platform.Tenancy.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    BillingPlan = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "internal"),
                    TimeZone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Africa/Accra"),
                    Locale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "en-GH"),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "GHS"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_module_subscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    ModuleName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_module_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_module_subscriptions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_users_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "tenants",
                columns: new[] { "Id", "BillingPlan", "Code", "CreatedAt", "Currency", "IsActive", "Locale", "Name", "TimeZone", "UpdatedAt" },
                values: new object[] { 1L, "internal", "nicktcscan", new DateTime(2026, 4, 6, 0, 0, 0, 0, DateTimeKind.Utc), "GHS", true, "en-GH", "Nick TC-Scan Operations", "Africa/Accra", null });

            migrationBuilder.InsertData(
                table: "tenant_module_subscriptions",
                columns: new[] { "Id", "CreatedAt", "ExpiresAt", "IsEnabled", "ModuleName", "TenantId", "UpdatedAt" },
                values: new object[,]
                {
                    { 1L, new DateTime(2026, 4, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "nscis", 1L, null },
                    { 2L, new DateTime(2026, 4, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "nickhr", 1L, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_module_subscriptions_TenantId_ModuleName",
                table: "tenant_module_subscriptions",
                columns: new[] { "TenantId", "ModuleName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_users_TenantId_UserId",
                table: "tenant_users",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_users_UserId",
                table: "tenant_users",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_Code",
                table: "tenants",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_module_subscriptions");

            migrationBuilder.DropTable(
                name: "tenant_users");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
