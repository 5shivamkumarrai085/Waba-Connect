using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionsAndTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DepartmentName",
                table: "UserConnections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserEmail",
                table: "UserConnections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "UserConnections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DepartmentName",
                table: "DepartmentConnections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "DepartmentConnections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MemberCount",
                table: "DepartmentConnections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Contacts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepartmentName",
                table: "UserConnections");

            migrationBuilder.DropColumn(
                name: "UserEmail",
                table: "UserConnections");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "UserConnections");

            migrationBuilder.DropColumn(
                name: "DepartmentName",
                table: "DepartmentConnections");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "DepartmentConnections");

            migrationBuilder.DropColumn(
                name: "MemberCount",
                table: "DepartmentConnections");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Contacts");
        }
    }
}
