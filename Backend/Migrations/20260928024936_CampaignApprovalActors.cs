using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class CampaignApprovalActors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DecidedByUserId",
                table: "CampaignApprovalStates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedByUserId",
                table: "CampaignApprovalStates",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecidedByUserId",
                table: "CampaignApprovalStates");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "CampaignApprovalStates");
        }
    }
}
