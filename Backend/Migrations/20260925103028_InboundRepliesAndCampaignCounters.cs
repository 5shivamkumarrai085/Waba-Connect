using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class InboundRepliesAndCampaignCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contacts_Phone",
                table: "Contacts");

            migrationBuilder.AddColumn<bool>(
                name: "ImapAllowInvalidCertificate",
                table: "EmailConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ImapLastError",
                table: "EmailConfigurations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImapLastPolledAt",
                table: "EmailConfigurations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ImapLastUid",
                table: "EmailConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ImapUidValidity",
                table: "EmailConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BouncedCount",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SuppressedCount",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Phone",
                table: "Contacts",
                column: "Phone",
                unique: true,
                filter: "\"Phone\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignContacts_CampaignId_Status_Id",
                table: "CampaignContacts",
                columns: new[] { "CampaignId", "Status", "Id" });

            // Mailboxes configured before certificate validation was enforced were polled with it
            // switched off. Keep them working; new connections start with validation on.
            migrationBuilder.Sql("""
                UPDATE "EmailConfigurations" SET "ImapAllowInvalidCertificate" = true
                 WHERE "ImapHost" IS NOT NULL AND "ImapHost" <> '';
                """);

            // Re-derive every email campaign's counters from its recipient rows: Bounced and
            // Suppressed are new, and bounces used to be folded into Failed.
            migrationBuilder.Sql("""
                UPDATE "Campaigns" c SET
                    "SentCount"       = s.sent,
                    "FailedCount"     = s.failed,
                    "BouncedCount"    = s.bounced,
                    "SuppressedCount" = s.suppressed,
                    "ComplainedCount" = s.complained,
                    "OpenedCount"     = s.opened,
                    "ClickedCount"    = s.clicked,
                    "RepliedCount"    = s.replied
                FROM (
                    SELECT "CampaignId",
                        count(*) FILTER (WHERE "SentAt" IS NOT NULL
                                           OR "Status" IN ('Sent','Delivered','Read','Bounced','Complained')) AS sent,
                        count(*) FILTER (WHERE "Status" = 'Failed')      AS failed,
                        count(*) FILTER (WHERE "Status" = 'Bounced')     AS bounced,
                        count(*) FILTER (WHERE "Status" = 'Suppressed')  AS suppressed,
                        count(*) FILTER (WHERE "Status" = 'Complained')  AS complained,
                        count(*) FILTER (WHERE "OpenedAt" IS NOT NULL)   AS opened,
                        count(*) FILTER (WHERE "ClickedAt" IS NOT NULL)  AS clicked,
                        count(*) FILTER (WHERE "RepliedAt" IS NOT NULL)  AS replied
                    FROM "CampaignContacts"
                    GROUP BY "CampaignId"
                ) s
                WHERE c."Id" = s."CampaignId" AND c."Channel" = 'Email';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contacts_Phone",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_CampaignContacts_CampaignId_Status_Id",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "ImapAllowInvalidCertificate",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "ImapLastError",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "ImapLastPolledAt",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "ImapLastUid",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "ImapUidValidity",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "BouncedCount",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "SuppressedCount",
                table: "Campaigns");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Phone",
                table: "Contacts",
                column: "Phone",
                unique: true);
        }
    }
}
