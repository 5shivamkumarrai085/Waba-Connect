using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiConnectionSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_ContactId",
                table: "ChatConversations");

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "WabaConfigurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "PhoneNumbers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "ConversationStates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "ChatMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "ChatConversations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "Campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "AiSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Connections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                });

            // Backfill data: create default connection if any WABA records exist
            migrationBuilder.Sql(@"
                INSERT INTO ""Connections"" (""Name"", ""Description"", ""IsActive"", ""CreatedAt"")
                SELECT 'Default Connection', 'Auto-migrated from initial single WABA setup', true, NOW()
                WHERE EXISTS (SELECT 1 FROM ""WabaConfigurations"" LIMIT 1)
                  AND NOT EXISTS (SELECT 1 FROM ""Connections"" WHERE ""Name"" = 'Default Connection');

                UPDATE ""WabaConfigurations"" SET ""ConnectionId"" = (SELECT ""Id"" FROM ""Connections"" WHERE ""Name"" = 'Default Connection' LIMIT 1) WHERE ""ConnectionId"" IS NULL;
                UPDATE ""PhoneNumbers"" SET ""ConnectionId"" = (SELECT ""Id"" FROM ""Connections"" WHERE ""Name"" = 'Default Connection' LIMIT 1) WHERE ""ConnectionId"" IS NULL;
                UPDATE ""ChatConversations"" SET ""ConnectionId"" = (SELECT ""Id"" FROM ""Connections"" WHERE ""Name"" = 'Default Connection' LIMIT 1) WHERE ""ConnectionId"" IS NULL;
                UPDATE ""ChatMessages"" SET ""ConnectionId"" = (SELECT ""Id"" FROM ""Connections"" WHERE ""Name"" = 'Default Connection' LIMIT 1) WHERE ""ConnectionId"" IS NULL;
                UPDATE ""Campaigns"" SET ""ConnectionId"" = (SELECT ""Id"" FROM ""Connections"" WHERE ""Name"" = 'Default Connection' LIMIT 1) WHERE ""ConnectionId"" IS NULL;
                UPDATE ""ConversationStates"" SET ""ConnectionId"" = (SELECT ""Id"" FROM ""Connections"" WHERE ""Name"" = 'Default Connection' LIMIT 1) WHERE ""ConnectionId"" IS NULL;
                UPDATE ""AiSessions"" SET ""ConnectionId"" = (SELECT ""Id"" FROM ""Connections"" WHERE ""Name"" = 'Default Connection' LIMIT 1) WHERE ""ConnectionId"" IS NULL;
            ");

            migrationBuilder.CreateTable(
                name: "DepartmentConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DepartmentId = table.Column<string>(type: "text", nullable: false),
                    ConnectionId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentConnections_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ConnectionId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserConnections_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WabaConfigurations_ConnectionId",
                table: "WabaConfigurations",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneNumbers_ConnectionId",
                table: "PhoneNumbers",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationStates_ConnectionId",
                table: "ConversationStates",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_CreatedAt",
                table: "Contacts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConnectionId",
                table: "ChatMessages",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ConnectionId",
                table: "ChatConversations",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ContactId_ConnectionId",
                table: "ChatConversations",
                columns: new[] { "ContactId", "ConnectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_ConnectionId",
                table: "Campaigns",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_CreatedAt",
                table: "Campaigns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignContacts_SentAt_Status",
                table: "CampaignContacts",
                columns: new[] { "SentAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSessions_ConnectionId",
                table: "AiSessions",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_Name",
                table: "Connections",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentConnections_ConnectionId",
                table: "DepartmentConnections",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentConnections_DepartmentId_ConnectionId",
                table: "DepartmentConnections",
                columns: new[] { "DepartmentId", "ConnectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserConnections_ConnectionId",
                table: "UserConnections",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserConnections_UserId_ConnectionId",
                table: "UserConnections",
                columns: new[] { "UserId", "ConnectionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AiSessions_Connections_ConnectionId",
                table: "AiSessions",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_Connections_ConnectionId",
                table: "Campaigns",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatConversations_Connections_ConnectionId",
                table: "ChatConversations",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Connections_ConnectionId",
                table: "ChatMessages",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationStates_Connections_ConnectionId",
                table: "ConversationStates",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PhoneNumbers_Connections_ConnectionId",
                table: "PhoneNumbers",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WabaConfigurations_Connections_ConnectionId",
                table: "WabaConfigurations",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiSessions_Connections_ConnectionId",
                table: "AiSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_Connections_ConnectionId",
                table: "Campaigns");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatConversations_Connections_ConnectionId",
                table: "ChatConversations");

            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Connections_ConnectionId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationStates_Connections_ConnectionId",
                table: "ConversationStates");

            migrationBuilder.DropForeignKey(
                name: "FK_PhoneNumbers_Connections_ConnectionId",
                table: "PhoneNumbers");

            migrationBuilder.DropForeignKey(
                name: "FK_WabaConfigurations_Connections_ConnectionId",
                table: "WabaConfigurations");

            migrationBuilder.DropTable(
                name: "DepartmentConnections");

            migrationBuilder.DropTable(
                name: "UserConnections");

            migrationBuilder.DropTable(
                name: "Connections");

            migrationBuilder.DropIndex(
                name: "IX_WabaConfigurations_ConnectionId",
                table: "WabaConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_PhoneNumbers_ConnectionId",
                table: "PhoneNumbers");

            migrationBuilder.DropIndex(
                name: "IX_ConversationStates_ConnectionId",
                table: "ConversationStates");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_CreatedAt",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ConnectionId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_ConnectionId",
                table: "ChatConversations");

            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_ContactId_ConnectionId",
                table: "ChatConversations");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_ConnectionId",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_CreatedAt",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_CampaignContacts_SentAt_Status",
                table: "CampaignContacts");

            migrationBuilder.DropIndex(
                name: "IX_AiSessions_ConnectionId",
                table: "AiSessions");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "WabaConfigurations");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "PhoneNumbers");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "ConversationStates");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "AiSessions");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ContactId",
                table: "ChatConversations",
                column: "ContactId",
                unique: true);
        }
    }
}
