using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBotFlowRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FlowId = table.Column<int>(type: "integer", nullable: false),
                    CurrentNodeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VariablesJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationStates_BotFlows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "BotFlows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlowEdges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FlowId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Target = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceHandle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowEdges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlowEdges_BotFlows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "BotFlows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlowNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FlowId = table.Column<int>(type: "integer", nullable: false),
                    NodeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NodeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PositionX = table.Column<double>(type: "double precision", nullable: false),
                    PositionY = table.Column<double>(type: "double precision", nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlowNodes_BotFlows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "BotFlows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FlowId = table.Column<int>(type: "integer", nullable: true),
                    NodeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_BotFlows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "BotFlows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationStates_FlowId",
                table: "ConversationStates",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationStates_PhoneNumber",
                table: "ConversationStates",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_FlowEdges_FlowId",
                table: "FlowEdges",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowNodes_FlowId",
                table: "FlowNodes",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_FlowId",
                table: "Messages",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_PhoneNumber",
                table: "Messages",
                column: "PhoneNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationStates");

            migrationBuilder.DropTable(
                name: "FlowEdges");

            migrationBuilder.DropTable(
                name: "FlowNodes");

            migrationBuilder.DropTable(
                name: "Messages");

        }
    }
}
