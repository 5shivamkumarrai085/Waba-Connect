using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class ConnectionScoping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AppUserId",
                table: "UserConnections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RoleId",
                table: "DepartmentConnections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Link existing rows to real accounts and roles. Rows that match nothing were sample
            // data ("usr-1", "dept-1") that never granted anything, and are removed.
            migrationBuilder.Sql("""
                UPDATE "UserConnections" uc SET "AppUserId" = u."Id"
                  FROM "AppUsers" u
                 WHERE NOT u."IsDeleted" AND lower(u."Email") = lower(uc."UserEmail");

                UPDATE "UserConnections" uc SET "AppUserId" = u."Id"
                  FROM "AppUsers" u
                 WHERE uc."AppUserId" = 0 AND uc."UserId" = u."Id"::text;

                DELETE FROM "UserConnections" WHERE "AppUserId" = 0;

                DELETE FROM "UserConnections" a USING "UserConnections" b
                 WHERE a."AppUserId" = b."AppUserId" AND a."ConnectionId" = b."ConnectionId" AND a."Id" > b."Id";

                UPDATE "UserConnections" SET "UserId" = "AppUserId"::text;

                UPDATE "DepartmentConnections" dc SET "RoleId" = r."Id"
                  FROM "Roles" r
                 WHERE lower(r."Name") = lower(dc."DepartmentName");

                DELETE FROM "DepartmentConnections" WHERE "RoleId" = 0;

                DELETE FROM "DepartmentConnections" a USING "DepartmentConnections" b
                 WHERE a."RoleId" = b."RoleId" AND a."ConnectionId" = b."ConnectionId" AND a."Id" > b."Id";

                UPDATE "DepartmentConnections" SET "DepartmentId" = "RoleId"::text;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UserConnections_AppUserId_ConnectionId",
                table: "UserConnections",
                columns: new[] { "AppUserId", "ConnectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentConnections_RoleId_ConnectionId",
                table: "DepartmentConnections",
                columns: new[] { "RoleId", "ConnectionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DepartmentConnections_Roles_RoleId",
                table: "DepartmentConnections",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserConnections_AppUsers_AppUserId",
                table: "UserConnections",
                column: "AppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DepartmentConnections_Roles_RoleId",
                table: "DepartmentConnections");

            migrationBuilder.DropForeignKey(
                name: "FK_UserConnections_AppUsers_AppUserId",
                table: "UserConnections");

            migrationBuilder.DropIndex(
                name: "IX_UserConnections_AppUserId_ConnectionId",
                table: "UserConnections");

            migrationBuilder.DropIndex(
                name: "IX_DepartmentConnections_RoleId_ConnectionId",
                table: "DepartmentConnections");

            migrationBuilder.DropColumn(
                name: "AppUserId",
                table: "UserConnections");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "DepartmentConnections");
        }
    }
}
