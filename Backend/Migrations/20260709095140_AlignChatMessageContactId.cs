using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AlignChatMessageContactId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "ChatMessages"
                ADD COLUMN IF NOT EXISTS "ContactId" integer;

                UPDATE "ChatMessages" m
                SET "ContactId" = c."ContactId"
                FROM "ChatConversations" c
                WHERE m."ContactId" IS NULL
                    AND m."ConversationId" = c."Id";

                CREATE INDEX IF NOT EXISTS "IX_ChatMessages_ContactId"
                    ON "ChatMessages" ("ContactId");

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'FK_ChatMessages_Contacts_ContactId'
                    ) AND NOT EXISTS (
                        SELECT 1
                        FROM "ChatMessages" m
                        LEFT JOIN "Contacts" c ON c."Id" = m."ContactId"
                        WHERE m."ContactId" IS NOT NULL AND c."Id" IS NULL
                    ) THEN
                        ALTER TABLE "ChatMessages"
                        ADD CONSTRAINT "FK_ChatMessages_Contacts_ContactId"
                        FOREIGN KEY ("ContactId") REFERENCES "Contacts" ("Id")
                        ON DELETE CASCADE;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "ChatMessages"
                DROP CONSTRAINT IF EXISTS "FK_ChatMessages_Contacts_ContactId";

                DROP INDEX IF EXISTS "IX_ChatMessages_ContactId";

                ALTER TABLE "ChatMessages"
                DROP COLUMN IF EXISTS "ContactId";
                """);
        }
    }
}
