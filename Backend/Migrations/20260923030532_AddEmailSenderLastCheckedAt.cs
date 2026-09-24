using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <summary>
    /// Adds EmailSenderIdentities.LastCheckedAt, so the send gate can tell a verification answer
    /// it has just read from SES apart from one stored before the address was verified.
    /// </summary>
    /// <remarks>
    /// The scaffolder also emitted a rename of AppUsers.ExternalUserId to ExternalSubjectId, and
    /// that has been removed by hand. It is an artefact of the temporary EF mapping bridge in
    /// AppDbContext: the host branch already renamed that column in the shared database, and the
    /// bridge exists precisely so this checkout can read it without a schema change. Running the
    /// rename would fail against a database where the column is already ExternalSubjectId, and
    /// would corrupt one where it is not. Remove the bridge with the host merge, not here.
    /// </remarks>
    public partial class AddEmailSenderLastCheckedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastCheckedAt",
                table: "EmailSenderIdentities",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastCheckedAt",
                table: "EmailSenderIdentities");
        }
    }
}
