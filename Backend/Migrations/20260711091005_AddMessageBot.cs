using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageBot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageBots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RelationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReplyText = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ReplyType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TriggerKeyword = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Header = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Footer = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OptionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Button1 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Button1Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Button2 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Button2Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Button3 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Button3Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CtaButtonName = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CtaButtonLink = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    FileType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FileUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AssistantName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageBots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageBots_Name",
                table: "MessageBots",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_MessageBots_TriggerKeyword",
                table: "MessageBots",
                column: "TriggerKeyword");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageBots");
        }
    }
}
