using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateAndBotFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BotFlows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FlowData = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotFlows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TemplateBots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RelationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    ReplyType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TriggerKeyword = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateBots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateBots_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplateBotVariables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemplateBotId = table.Column<int>(type: "integer", nullable: false),
                    VariableName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VariableValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MergeField = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateBotVariables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateBotVariables_TemplateBots_TemplateBotId",
                        column: x => x.TemplateBotId,
                        principalTable: "TemplateBots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BotFlows_Name",
                table: "BotFlows",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateBots_Name",
                table: "TemplateBots",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateBots_TemplateId",
                table: "TemplateBots",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateBots_TriggerKeyword",
                table: "TemplateBots",
                column: "TriggerKeyword");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateBotVariables_TemplateBotId",
                table: "TemplateBotVariables",
                column: "TemplateBotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BotFlows");

            migrationBuilder.DropTable(
                name: "TemplateBotVariables");

            migrationBuilder.DropTable(
                name: "TemplateBots");
        }
    }
}
