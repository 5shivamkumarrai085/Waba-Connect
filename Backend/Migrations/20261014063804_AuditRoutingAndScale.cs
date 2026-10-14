using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <summary>
    /// Audit fixes: ChatMessages.AutomationHandledAt (a retried webhook finishes an unfinished bot
    /// reply instead of skipping it), indexes for the ordered inbound claim, the bot's node lookup,
    /// the Send Money session sweep and per-number OTP limits, and four-decimal receive amounts.
    /// Additive only: a nullable column, new indexes, and wider numeric columns on the Send Money
    /// tables. Nothing is dropped or rewritten in tables other builds use.
    /// </summary>
    public partial class AuditRoutingAndScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF would drop IX_FlowNodes_FlowId here because the new (FlowId, NodeId) index covers
            // it. It is kept: the database is shared with other builds, and this migration only adds.

            migrationBuilder.AlterColumn<decimal>(
                name: "ReceiveAmount",
                table: "TransferOrders",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "ReceiveAmount",
                table: "FxQuotes",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutomationHandledAt",
                table: "ChatMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemittanceSessions_OpenExpiry",
                table: "RemittanceSessions",
                column: "ExpiresAt",
                filter: "\"Status\" IN ('Started', 'Verified')");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_Destination",
                table: "OtpChallenges",
                columns: new[] { "Purpose", "Destination", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobQueue_OrderedHead",
                table: "JobQueue",
                columns: new[] { "QueueName", "PartitionKey", "Id" },
                filter: "\"Status\" IN ('Pending', 'Leased')");

            migrationBuilder.CreateIndex(
                name: "IX_FlowNodes_Flow_Node",
                table: "FlowNodes",
                columns: new[] { "FlowId", "NodeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RemittanceSessions_OpenExpiry",
                table: "RemittanceSessions");

            migrationBuilder.DropIndex(
                name: "IX_OtpChallenges_Destination",
                table: "OtpChallenges");

            migrationBuilder.DropIndex(
                name: "IX_JobQueue_OrderedHead",
                table: "JobQueue");

            migrationBuilder.DropIndex(
                name: "IX_FlowNodes_Flow_Node",
                table: "FlowNodes");

            migrationBuilder.DropColumn(
                name: "AutomationHandledAt",
                table: "ChatMessages");

            migrationBuilder.AlterColumn<decimal>(
                name: "ReceiveAmount",
                table: "TransferOrders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "ReceiveAmount",
                table: "FxQuotes",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

        }
    }
}
