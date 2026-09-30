using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <summary>
    /// Brings the model snapshot back in line with <c>20260924154554_AddEmailEventsAndEngagementCounters</c>,
    /// which was written by hand and never recorded EmailEvents or the engagement counters in the
    /// snapshot. Everything that migration created is left alone; this only repairs the three places
    /// where it disagreed with the entity:
    /// <list type="bullet">
    /// <item><c>MetadataJson</c> was never created, so a database built purely from migrations
    /// failed every event insert.</item>
    /// <item><c>Id</c> was <c>integer</c> while the entity is <c>long</c>.</item>
    /// <item><c>DiagnosticCode</c> was 500 characters while the entity allows 2000, so a long SMTP
    /// diagnostic failed the insert instead of being stored.</item>
    /// </list>
    /// Each statement is idempotent because some environments had the column added manually.
    /// </summary>
    public partial class SyncEmailEventsSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"EmailEvents\" ADD COLUMN IF NOT EXISTS \"MetadataJson\" text NULL;");
            migrationBuilder.Sql("ALTER TABLE \"EmailEvents\" ALTER COLUMN \"Id\" TYPE bigint;");
            migrationBuilder.Sql("ALTER TABLE \"EmailEvents\" ALTER COLUMN \"DiagnosticCode\" TYPE character varying(2000);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op: narrowing Id or DiagnosticCode could fail on existing data, and
            // MetadataJson may pre-date this migration in environments that added it by hand.
        }
    }
}
