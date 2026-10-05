using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class QueueContractV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Queue contract v3 (see QueueNames.ContractVersion). Jobs still waiting move to the
            // new names so this build sends them; jobs a v2 build has already leased keep their
            // names, and that build finishes them.
            migrationBuilder.Sql("""
                UPDATE "JobQueue" SET "QueueName" = left("QueueName", length("QueueName") - length('.v2')) || '.v3'
                WHERE "QueueName" LIKE '%.v2' AND "Status" = 'Pending';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "JobQueue" SET "QueueName" = left("QueueName", length("QueueName") - length('.v3')) || '.v2'
                WHERE "QueueName" LIKE '%.v3' AND "Status" = 'Pending';
                """);
        }
    }
}
