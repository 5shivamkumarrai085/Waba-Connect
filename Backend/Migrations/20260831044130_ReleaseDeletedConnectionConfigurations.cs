using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <summary>
    /// Releases the WhatsApp configurations still held by connections that were deleted.
    ///
    /// <para>
    /// Deleting a connection used to set <c>IsActive = false</c> and nothing more, leaving its
    /// <c>WabaConfiguration</c> row marked Connected with a live Facebook App ID, WABA ID and
    /// access token. The duplicate-App-ID guard on the connect wizard counted those rows, so a
    /// deleted connection reserved its App ID permanently — and the resulting error asked the
    /// operator to "disconnect it first", naming a connection that no longer appears anywhere in
    /// the product. There was no way out from the UI.
    /// </para>
    /// <para>
    /// The code paths are fixed (ConnectionService.SoftDeleteAsync now releases the configuration;
    /// WabaController only counts configurations whose connection is still active). This clears the
    /// rows already in that state, so the fix applies to existing data rather than only to
    /// connections deleted from here on.
    /// </para>
    /// <para>
    /// Only configurations belonging to an inactive connection are touched. Live connections are
    /// not affected, and the configuration rows themselves are kept — they carry identity the audit
    /// trail refers to, and reconnecting reuses them.
    /// </para>
    /// </summary>
    public partial class ReleaseDeletedConnectionConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clear the credentials and drop the Connected flag on every configuration whose
            // connection has been deleted. Written as one statement so a connection cannot be
            // half-released if this is interrupted.
            migrationBuilder.Sql(
                """
                UPDATE "WabaConfigurations" AS w
                SET "Connected" = FALSE,
                    "FacebookAppId" = '',
                    "FacebookAppSecret" = '',
                    "AccessToken" = '',
                    "WabaId" = '',
                    "VerifyToken" = '',
                    "WebhookUrl" = '',
                    "UpdatedAt" = NOW() AT TIME ZONE 'UTC'
                FROM "Connections" AS c
                WHERE w."ConnectionId" = c."Id"
                  AND c."IsActive" = FALSE;
                """);

            // A deleted connection keeps no sender numbers either — the same rule disconnecting
            // has always followed. Left behind, these are numbers the product would still offer
            // as senders for an account it can no longer authenticate.
            //
            // The table is "PhoneNumbers". The entity is WabaPhoneNumber and the DbSet is
            // WabaPhoneNumbers, but the mapping renames it (AppDbContext -> ToTable("PhoneNumbers")),
            // so the obvious guess is wrong. This has bitten the codebase before — see the note in
            // ChatConversationReconcilerService about a raw-SQL statement that failed on every run
            // for exactly this reason. Names here were read from the model snapshot, not assumed.
            migrationBuilder.Sql(
                """
                DELETE FROM "PhoneNumbers" AS p
                USING "Connections" AS c
                WHERE p."ConnectionId" = c."Id"
                  AND c."IsActive" = FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately not reversed. The credentials this cleared were secrets that should not
            // have survived the delete, and they are not recoverable from anything left in the
            // database — restoring the rows would mean inventing values. A connection that needs to
            // come back is reconnected through the wizard, which is the only honest way to get a
            // working token anyway.
        }
    }
}
