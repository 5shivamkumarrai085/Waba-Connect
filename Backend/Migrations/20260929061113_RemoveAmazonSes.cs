using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAmazonSes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Amazon SES is gone; mail is standard SMTP only. A connection that was set up for SES
            // keeps its senders but is switched off until SMTP settings are entered for it.
            migrationBuilder.Sql("""
                UPDATE "EmailConfigurations" SET "Provider" = 'Smtp', "IsActive" = false
                WHERE "Provider" <> 'Smtp';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_EmailSenderIdentities_EmailSendingDomains_SendingDomainId",
                table: "EmailSenderIdentities");

            migrationBuilder.DropTable(
                name: "EmailDeliveryEvents");

            migrationBuilder.DropTable(
                name: "EmailSendingDomains");

            migrationBuilder.DropIndex(
                name: "IX_EmailSenderIdentities_SendingDomainId",
                table: "EmailSenderIdentities");

            migrationBuilder.DropColumn(
                name: "LastCheckedAt",
                table: "EmailSenderIdentities");

            migrationBuilder.DropColumn(
                name: "SendingDomainId",
                table: "EmailSenderIdentities");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "EmailSenderIdentities");

            migrationBuilder.DropColumn(
                name: "AccessKeyId",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "AuthMode",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "ConfigurationSet",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "EmailConfigurations");

            migrationBuilder.DropColumn(
                name: "SecretAccessKeyEncrypted",
                table: "EmailConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastCheckedAt",
                table: "EmailSenderIdentities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SendingDomainId",
                table: "EmailSenderIdentities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "EmailSenderIdentities",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AccessKeyId",
                table: "EmailConfigurations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthMode",
                table: "EmailConfigurations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ConfigurationSet",
                table: "EmailConfigurations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "EmailConfigurations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecretAccessKeyEncrypted",
                table: "EmailConfigurations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailDeliveryEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignContactId = table.Column<int>(type: "integer", nullable: true),
                    ChatMessageId = table.Column<int>(type: "integer", nullable: true),
                    BounceSubType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BounceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DiagnosticCode = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RecipientAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SnsMessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDeliveryEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailDeliveryEvents_CampaignContacts_CampaignContactId",
                        column: x => x.CampaignContactId,
                        principalTable: "CampaignContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmailDeliveryEvents_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmailSendingDomains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmailConfigurationId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DkimStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DkimTokensJson = table.Column<string>(type: "jsonb", nullable: true),
                    DomainName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LastCheckMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MailFromDomain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MailFromStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerificationStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSendingDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSendingDomains_EmailConfigurations_EmailConfigurationId",
                        column: x => x.EmailConfigurationId,
                        principalTable: "EmailConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailSenderIdentities_SendingDomainId",
                table: "EmailSenderIdentities",
                column: "SendingDomainId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_CampaignContactId_EventType",
                table: "EmailDeliveryEvents",
                columns: new[] { "CampaignContactId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_ChatMessageId",
                table: "EmailDeliveryEvents",
                column: "ChatMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_OccurredAt",
                table: "EmailDeliveryEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_ProviderMessageId",
                table: "EmailDeliveryEvents",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_SnsMessageId",
                table: "EmailDeliveryEvents",
                column: "SnsMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailSendingDomains_EmailConfigurationId_DomainName",
                table: "EmailSendingDomains",
                columns: new[] { "EmailConfigurationId", "DomainName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EmailSenderIdentities_EmailSendingDomains_SendingDomainId",
                table: "EmailSenderIdentities",
                column: "SendingDomainId",
                principalTable: "EmailSendingDomains",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
