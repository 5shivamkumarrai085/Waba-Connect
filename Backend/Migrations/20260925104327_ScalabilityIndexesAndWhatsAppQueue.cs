using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class ScalabilityIndexesAndWhatsAppQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The WhatsApp message ids become unique. Any existing duplicate (a double-processed
            // webhook) keeps its id on the oldest row only, so the unique index can be built.
            migrationBuilder.Sql("""
                UPDATE "ChatMessages" m SET "WhatsAppMessageId" = NULL
                 WHERE "WhatsAppMessageId" IS NOT NULL
                   AND EXISTS (SELECT 1 FROM "ChatMessages" o
                                WHERE o."WhatsAppMessageId" = m."WhatsAppMessageId" AND o."Id" < m."Id");
                UPDATE "CampaignContacts" c SET "WhatsAppMessageId" = NULL
                 WHERE "WhatsAppMessageId" IS NOT NULL
                   AND EXISTS (SELECT 1 FROM "CampaignContacts" o
                                WHERE o."WhatsAppMessageId" = c."WhatsAppMessageId" AND o."Id" < c."Id");
                """);

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ConnectionId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_WhatsAppMessageId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_CampaignContacts_WhatsAppMessageId",
                table: "CampaignContacts");

            migrationBuilder.CreateTable(
                name: "ConnectionDailySendCounters",
                columns: table => new
                {
                    ConnectionId = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionDailySendCounters", x => new { x.ConnectionId, x.Day });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneNumbers_PhoneNumberId",
                table: "PhoneNumbers",
                column: "PhoneNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConnectionId_Direction_CreatedAt",
                table: "ChatMessages",
                columns: new[] { "ConnectionId", "Direction", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConversationId_Id",
                table: "ChatMessages",
                columns: new[] { "ConversationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_WhatsAppMessageId",
                table: "ChatMessages",
                column: "WhatsAppMessageId",
                unique: true,
                filter: "\"WhatsAppMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Status_ScheduledAt",
                table: "Campaigns",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignContacts_WhatsAppMessageId",
                table: "CampaignContacts",
                column: "WhatsAppMessageId",
                unique: true,
                filter: "\"WhatsAppMessageId\" IS NOT NULL");

            // Inbox ordering: keyset pages over last activity, overall and per connection.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_chat_conversations_activity
                    ON "ChatConversations" ((COALESCE("LastMessageAt", "UpdatedAt")) DESC, "Id" DESC);
                CREATE INDEX IF NOT EXISTS ix_chat_conversations_connection_activity
                    ON "ChatConversations" ("ConnectionId", (COALESCE("LastMessageAt", "UpdatedAt")) DESC, "Id" DESC);
                """);

            // Case-insensitive email lookups (inbound reply threading, suppression, search).
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_contacts_email_lower ON "Contacts" (lower("Email")) WHERE "Email" IS NOT NULL;
                """);

            // The job-queue claim filters on COALESCE(PartitionKey, '') and the ready predicate,
            // which the plain composite index could not serve.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_job_queue_claim
                    ON "JobQueue" ("QueueName", (COALESCE("PartitionKey", '')), "AvailableAt", "Id")
                    WHERE "Status" IN ('Pending', 'Leased');
                """);

            // Substring search ("contains") on names, phones and emails uses trigram indexes; a
            // B-tree cannot serve LIKE '%x%', so without these search is a full scan at scale.
            // Skipped, not failed, where the role may not create extensions.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    BEGIN
                        CREATE EXTENSION IF NOT EXISTS pg_trgm;
                    EXCEPTION WHEN insufficient_privilege THEN
                        RAISE NOTICE 'pg_trgm not available; contact search stays unindexed.';
                    END;

                    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') THEN
                        CREATE INDEX IF NOT EXISTS ix_contacts_name_trgm  ON "Contacts" USING gin (lower("Name") gin_trgm_ops);
                        CREATE INDEX IF NOT EXISTS ix_contacts_phone_trgm ON "Contacts" USING gin ("Phone" gin_trgm_ops);
                        CREATE INDEX IF NOT EXISTS ix_contacts_email_trgm ON "Contacts" USING gin (lower("Email") gin_trgm_ops);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_chat_conversations_activity;
                DROP INDEX IF EXISTS ix_chat_conversations_connection_activity;
                DROP INDEX IF EXISTS ix_contacts_email_lower;
                DROP INDEX IF EXISTS ix_job_queue_claim;
                DROP INDEX IF EXISTS ix_contacts_name_trgm;
                DROP INDEX IF EXISTS ix_contacts_phone_trgm;
                DROP INDEX IF EXISTS ix_contacts_email_trgm;
                """);

            migrationBuilder.DropTable(
                name: "ConnectionDailySendCounters");

            migrationBuilder.DropIndex(
                name: "IX_PhoneNumbers_PhoneNumberId",
                table: "PhoneNumbers");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ConnectionId_Direction_CreatedAt",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ConversationId_Id",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_WhatsAppMessageId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_Status_ScheduledAt",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_CampaignContacts_WhatsAppMessageId",
                table: "CampaignContacts");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConnectionId",
                table: "ChatMessages",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_WhatsAppMessageId",
                table: "ChatMessages",
                column: "WhatsAppMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignContacts_WhatsAppMessageId",
                table: "CampaignContacts",
                column: "WhatsAppMessageId");
        }
    }
}
