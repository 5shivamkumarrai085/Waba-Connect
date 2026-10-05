using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMessageActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The message activity log is gone (its rows were backed up first, see
            // Backend/scripts/restore-round5-2026-10-01.sql). Its Delete and Clear permissions
            // applied to nothing else: the audit log keeps View only.
            migrationBuilder.Sql("""
                DELETE FROM "RolePermissions" WHERE "PermissionId" IN (SELECT "Id" FROM "Permissions" WHERE "Key" IN ('ActivityLog.Delete', 'ActivityLog.Clear'));
                DELETE FROM "UserPermissions" WHERE "PermissionId" IN (SELECT "Id" FROM "Permissions" WHERE "Key" IN ('ActivityLog.Delete', 'ActivityLog.Clear'));
                DELETE FROM "Permissions" WHERE "Key" IN ('ActivityLog.Delete', 'ActivityLog.Clear');
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_Templates_TemplateId",
                table: "Campaigns");

            migrationBuilder.DropTable(
                name: "MessageActivityLogs");

            migrationBuilder.CreateIndex(
                name: "IX_EmailEvents_CreatedAt",
                table: "EmailEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Module_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "Module", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Status",
                table: "AuditLogs",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_Templates_TemplateId",
                table: "Campaigns",
                column: "TemplateId",
                principalTable: "Templates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_Templates_TemplateId",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_EmailEvents_CreatedAt",
                table: "EmailEvents");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Module_CreatedAt",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Status",
                table: "AuditLogs");

            migrationBuilder.CreateTable(
                name: "MessageActivityLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConnectionId = table.Column<int>(type: "integer", nullable: true),
                    ContactId = table.Column<int>(type: "integer", nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PerformedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RelationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RequestPayload = table.Column<string>(type: "text", nullable: true),
                    ResponseCode = table.Column<int>(type: "integer", nullable: true),
                    ResponsePayload = table.Column<string>(type: "text", nullable: true),
                    TemplateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TriggeredBy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WhatsAppMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageActivityLogs_Category",
                table: "MessageActivityLogs",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_MessageActivityLogs_ContactId",
                table: "MessageActivityLogs",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageActivityLogs_CreatedAt",
                table: "MessageActivityLogs",
                column: "CreatedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_Templates_TemplateId",
                table: "Campaigns",
                column: "TemplateId",
                principalTable: "Templates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
