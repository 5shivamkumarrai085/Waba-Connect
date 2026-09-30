using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class FollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentCampaignId",
                table: "Campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FollowUpRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignId = table.Column<int>(type: "integer", nullable: false),
                    Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DelayHours = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: true),
                    EmailTemplateId = table.Column<int>(type: "integer", nullable: true),
                    SenderIdentityId = table.Column<int>(type: "integer", nullable: true),
                    ConnectionId = table.Column<int>(type: "integer", nullable: true),
                    SubjectOverride = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: true),
                    Tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ChildCampaignId = table.Column<int>(type: "integer", nullable: true),
                    MatchedCount = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUpRules_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpRules_CampaignId",
                table: "FollowUpRules",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpRules_Status_DueAt",
                table: "FollowUpRules",
                columns: new[] { "Status", "DueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FollowUpRules");

            migrationBuilder.DropColumn(
                name: "ParentCampaignId",
                table: "Campaigns");
        }
    }
}
