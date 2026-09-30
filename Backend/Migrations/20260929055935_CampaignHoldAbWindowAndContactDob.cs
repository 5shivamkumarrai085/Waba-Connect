using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;
using WhatsAppCampaignApi.Services.Catalogs;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class CampaignHoldAbWindowAndContactDob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "Contacts",
                type: "date",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RelationType",
                table: "Campaigns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<int>(
                name: "AbDecideAfterHours",
                table: "Campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PausedReason",
                table: "Campaigns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_DateOfBirth",
                table: "Contacts",
                column: "DateOfBirth");

            // Queue names now carry the job contract version (v2 at this point), so builds
            // that predate it stop taking these jobs. Existing rows move with them; only rows an
            // older build is still writing keep the old names, and that build keeps handling them.
            migrationBuilder.Sql($$"""
                UPDATE "JobQueue" SET "QueueName" = "QueueName" || '.v2'
                WHERE "QueueName" IN ('campaign-expansion', 'email-send', 'wa-campaign-expansion', 'wa-send', 'wa-webhook', 'webhook-out');
                """);

            // The required-contact-fields setting starts at the catalog's defaults, so Settings ›
            // Contacts shows what is actually enforced instead of an empty list.
            var defaults = System.Text.Json.JsonSerializer.Serialize(
                ContactFieldCatalog.Configurable.Where(f => f.RequiredByDefault).Select(f => f.Key));
            migrationBuilder.Sql($$"""
                INSERT INTO "AppSettings" ("Key", "Value", "UpdatedByName", "CreatedAt", "UpdatedAt")
                SELECT '{{ContactFieldCatalog.RequiredFieldsSettingKey}}', '{{defaults}}', 'System', now(), now()
                WHERE NOT EXISTS (SELECT 1 FROM "AppSettings" WHERE "Key" = '{{ContactFieldCatalog.RequiredFieldsSettingKey}}');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE "JobQueue" SET "QueueName" = left("QueueName", length("QueueName") - length('.v2'))
                WHERE "QueueName" LIKE '%.v2';
                """);

            migrationBuilder.DropIndex(
                name: "IX_Contacts_DateOfBirth",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "AbDecideAfterHours",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "PausedReason",
                table: "Campaigns");

            migrationBuilder.AlterColumn<string>(
                name: "RelationType",
                table: "Campaigns",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);
        }
    }
}
