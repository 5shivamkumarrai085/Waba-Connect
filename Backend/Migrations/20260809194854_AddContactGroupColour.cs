using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddContactGroupColour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "ContactGroups",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "ContactGroups");
        }
    }
}
