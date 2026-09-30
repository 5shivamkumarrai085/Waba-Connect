using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AbTesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AbDecideAt",
                table: "Campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AbDecidedAt",
                table: "Campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AbTestPercent",
                table: "Campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AbWinnerMetric",
                table: "Campaigns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AbWinnerVariantId",
                table: "Campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HeldForWinner",
                table: "CampaignContacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "CampaignContacts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignId = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: true),
                    EmailTemplateId = table.Column<int>(type: "integer", nullable: true),
                    SubjectOverride = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignVariants_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignVariants_EmailTemplates_EmailTemplateId",
                        column: x => x.EmailTemplateId,
                        principalTable: "EmailTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignVariants_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignVariants_CampaignId_Label",
                table: "CampaignVariants",
                columns: new[] { "CampaignId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignVariants_EmailTemplateId",
                table: "CampaignVariants",
                column: "EmailTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignVariants_TemplateId",
                table: "CampaignVariants",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignVariants");

            migrationBuilder.DropColumn(
                name: "AbDecideAt",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "AbDecidedAt",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "AbTestPercent",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "AbWinnerMetric",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "AbWinnerVariantId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "HeldForWinner",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "CampaignContacts");
        }
    }
}
