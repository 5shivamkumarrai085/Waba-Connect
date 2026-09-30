using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class ChatOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_ConnectionId",
                table: "ChatConversations");

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "ChatConversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignedUserId",
                table: "ChatConversations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstRespondedAt",
                table: "ChatConversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstResponseDueAt",
                table: "ChatConversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastInboundAt",
                table: "ChatConversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolveDueAt",
                table: "ChatConversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "ChatConversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SlaBreachedAt",
                table: "ChatConversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ChatConversations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Open");

            // Existing conversations: when the customer last wrote, from the message history.
            migrationBuilder.Sql("""
                UPDATE "ChatConversations" c SET "LastInboundAt" = m.last_inbound
                  FROM (SELECT "ConversationId", max("CreatedAt") AS last_inbound
                          FROM "ChatMessages" WHERE "Direction" = 'Incoming'
                         GROUP BY "ConversationId") m
                 WHERE m."ConversationId" = c."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_AssignedUserId",
                table: "ChatConversations",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ConnectionId_Status_AssignedUserId",
                table: "ChatConversations",
                columns: new[] { "ConnectionId", "Status", "AssignedUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_Status_FirstResponseDueAt",
                table: "ChatConversations",
                columns: new[] { "Status", "FirstResponseDueAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_ChatConversations_AppUsers_AssignedUserId",
                table: "ChatConversations",
                column: "AssignedUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatConversations_AppUsers_AssignedUserId",
                table: "ChatConversations");

            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_AssignedUserId",
                table: "ChatConversations");

            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_ConnectionId_Status_AssignedUserId",
                table: "ChatConversations");

            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_Status_FirstResponseDueAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "FirstRespondedAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "FirstResponseDueAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "LastInboundAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "ResolveDueAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "SlaBreachedAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ChatConversations");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ConnectionId",
                table: "ChatConversations",
                column: "ConnectionId");
        }
    }
}
