using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageActivityPayloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestPayload",
                table: "MessageActivityLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsePayload",
                table: "MessageActivityLogs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestPayload",
                table: "MessageActivityLogs");

            migrationBuilder.DropColumn(
                name: "ResponsePayload",
                table: "MessageActivityLogs");
        }
    }
}
