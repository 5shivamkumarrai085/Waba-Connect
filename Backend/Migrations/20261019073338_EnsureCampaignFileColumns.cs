using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <summary>
    /// AddMediaFieldsToChatMessage dropped Campaigns.FileName/FileType/FileUrl, and the properties
    /// came back to the model and snapshot without a migration. Every campaign insert then failed
    /// with "column does not exist". Idempotent, so databases that kept the columns are untouched.
    /// </summary>
    public partial class EnsureCampaignFileColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""Campaigns"" ADD COLUMN IF NOT EXISTS ""FileName"" text;
                ALTER TABLE ""Campaigns"" ADD COLUMN IF NOT EXISTS ""FileType"" text;
                ALTER TABLE ""Campaigns"" ADD COLUMN IF NOT EXISTS ""FileUrl"" text;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the model already had these columns before this migration, so
            // dropping them here would recreate the original failure.
        }
    }
}
