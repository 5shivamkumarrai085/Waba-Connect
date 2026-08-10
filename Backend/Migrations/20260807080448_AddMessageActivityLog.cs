using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageActivityLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TemplateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResponseCode = table.Column<int>(type: "integer", nullable: true),
                    RelationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ContactId = table.Column<int>(type: "integer", nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ConnectionId = table.Column<int>(type: "integer", nullable: true),
                    WhatsAppMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PerformedByUserId = table.Column<int>(type: "integer", nullable: true),
                    TriggeredBy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageActivityLogs_Category",
                table: "MessageActivityLogs",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_MessageActivityLogs_ContactId",
                table: "MessageActivityLogs",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageActivityLogs_CreatedAt",
                table: "MessageActivityLogs",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageActivityLogs");
        }
    }
}
