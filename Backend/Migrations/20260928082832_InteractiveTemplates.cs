using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class InteractiveTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ButtonsJson",
                table: "Templates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComponentsJson",
                table: "Templates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdAttributedAt",
                table: "Contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdClickId",
                table: "Contacts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdHeadline",
                table: "Contacts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdSourceId",
                table: "Contacts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdSourceUrl",
                table: "Contacts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ButtonsJson",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "ComponentsJson",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "AdAttributedAt",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "AdClickId",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "AdHeadline",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "AdSourceId",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "AdSourceUrl",
                table: "Contacts");
        }
    }
}
