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
            // IMAP settings for inbound reply polling.
            // Credentials default to SMTP values at runtime when null, so a connection that already
            // sends via SMTP needs only ImapHost to start receiving. Added as nullable columns
            // so existing rows (SMTP-only connections) continue working without any data migration.

            migrationBuilder.AddColumn<string>(
                name: "ImapHost",
                table: "EmailConfigurations",
                type: "character varying(255)",
                maxLength: 255,
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
                defaultValue: 2);  // 2 = SmtpSecurityMode.SslOnConnect

            migrationBuilder.AddColumn<string>(
                name: "ImapUsername",
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ImapHost",              table: "EmailConfigurations");
            migrationBuilder.DropColumn(name: "ImapPort",              table: "EmailConfigurations");
            migrationBuilder.DropColumn(name: "ImapSecurity",          table: "EmailConfigurations");
            migrationBuilder.DropColumn(name: "ImapUsername",          table: "EmailConfigurations");
            migrationBuilder.DropColumn(name: "ImapPasswordEncrypted", table: "EmailConfigurations");
        }
    }
}
