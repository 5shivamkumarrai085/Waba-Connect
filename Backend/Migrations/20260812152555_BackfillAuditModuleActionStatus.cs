using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <summary>
    /// Backfills Module / Action / Status on audit rows written before AddAuditChangeTracking.
    ///
    /// <para>
    /// Those columns are populated going forward by AuditService, which splits the "Entity.Verb"
    /// event name. Existing rows kept their Event but had all three NULL, which surfaced three
    /// ways in the UI: the Module filter could only offer the handful of modules seen since the
    /// migration, every historical row rendered its Action as a placeholder badge, and a failed
    /// sign-in displayed as "Success" because a NULL status falls back to it.
    /// </para>
    /// <para>
    /// Data-only and idempotent — the WHERE clause means re-running it touches nothing, and it
    /// derives every value from data already in the row rather than inventing anything.
    /// </para>
    /// </summary>
    public partial class BackfillAuditModuleActionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AuditLogs"
                SET "Module" = split_part("Event", '.', 1),
                    "Action" = split_part("Event", '.', 2),
                    "Status" = CASE WHEN "Event" LIKE '%Failed' THEN 'Failed' ELSE 'Success' END
                WHERE "Module" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately not reversible. Down would have to null the three columns, but it
            // cannot distinguish a row this migration filled from one AuditService wrote
            // afterwards — so it would destroy live data to undo a backfill. Rolling back the
            // schema migration that added the columns removes them outright, which is the real
            // undo.
        }
    }
}
