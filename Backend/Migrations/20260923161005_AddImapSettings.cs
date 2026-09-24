using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddImapSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImapHost",
                table: "EmailConfigurations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImapPasswordEncrypted",
                table: "EmailConfigurations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImapPort",
                table: "EmailConfigurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImapSecurity",
                table: "EmailConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ImapUsername",
                table: "EmailConfigurations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImapHost",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "ImapPasswordEncrypted",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "ImapPort",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "ImapSecurity",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "ImapUsername",
                table: "EmailConfigurations");
        }
    }
}
