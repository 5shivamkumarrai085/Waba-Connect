using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBotConnectionScoping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "TemplateBots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "MessageBots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "BotFlows",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemplateBots_ConnectionId",
                table: "TemplateBots",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageBots_ConnectionId",
                table: "MessageBots",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_BotFlows_ConnectionId",
                table: "BotFlows",
                column: "ConnectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_BotFlows_Connections_ConnectionId",
                table: "BotFlows",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MessageBots_Connections_ConnectionId",
                table: "MessageBots",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TemplateBots_Connections_ConnectionId",
                table: "TemplateBots",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BotFlows_Connections_ConnectionId",
                table: "BotFlows");

            migrationBuilder.DropForeignKey(
                name: "FK_MessageBots_Connections_ConnectionId",
                table: "MessageBots");

            migrationBuilder.DropForeignKey(
                name: "FK_TemplateBots_Connections_ConnectionId",
                table: "TemplateBots");

            migrationBuilder.DropIndex(
                name: "IX_TemplateBots_ConnectionId",
                table: "TemplateBots");

            migrationBuilder.DropIndex(
                name: "IX_MessageBots_ConnectionId",
                table: "MessageBots");

            migrationBuilder.DropIndex(
                name: "IX_BotFlows_ConnectionId",
                table: "BotFlows");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "TemplateBots");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "MessageBots");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "BotFlows");
        }
    }
}
