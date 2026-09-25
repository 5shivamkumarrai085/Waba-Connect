using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailEventsAndEngagementCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. EmailEvents table ────────────────────────────────────────────────────────────
            // Append-only, normalized event history for every campaign email event.
            // IdempotencyKey has a unique index — the database-level concurrency guard.
            migrationBuilder.CreateTable(
                name: "EmailEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdempotencyKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EventKind = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CampaignId = table.Column<int>(type: "integer", nullable: true),
                    CampaignContactId = table.Column<int>(type: "integer", nullable: true),
                    MessageId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecipientAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    BounceType = table.Column<string>(type: "text", nullable: true),
                    BounceSubType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DiagnosticCode = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OriginalUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailEvents_CampaignContacts_CampaignContactId",
                        column: x => x.CampaignContactId,
                        principalTable: "CampaignContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmailEvents_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Idempotency guard — unique constraint on IdempotencyKey
            migrationBuilder.CreateIndex(
                name: "ix_email_events_idempotency_key",
                table: "EmailEvents",
                column: "IdempotencyKey",
                unique: true);

            // Campaign-level reporting: filter by campaign + kind + time range
            migrationBuilder.CreateIndex(
                name: "ix_email_events_campaign_kind_occurred",
                table: "EmailEvents",
                columns: new[] { "CampaignId", "EventKind", "OccurredAt" });

            // Recipient-level reporting
            migrationBuilder.CreateIndex(
                name: "ix_email_events_contact_kind",
                table: "EmailEvents",
                columns: new[] { "CampaignContactId", "EventKind" });

            // Message-ID correlation (partial index: non-null only)
            migrationBuilder.Sql(
                "CREATE INDEX ix_email_events_message_id ON \"EmailEvents\" (\"MessageId\") WHERE \"MessageId\" IS NOT NULL;");

            // ── 2. Campaign engagement counter columns ──────────────────────────────────────────
            // These are new integer columns, all defaulting to 0.
            migrationBuilder.AddColumn<int>(
                name: "SentCount",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OpenedCount",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClickedCount",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RepliedCount",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnsubscribedCount",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ComplainedCount",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // ── 3. CampaignContact tracking columns ────────────────────────────────────────────
            // TrackingId: HMAC-verified opaque token used in pixel/click URLs
            migrationBuilder.AddColumn<string>(
                name: "TrackingId",
                table: "CampaignContacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Engagement timestamps for per-recipient detail queries
            migrationBuilder.AddColumn<DateTime>(
                name: "OpenedAt",
                table: "CampaignContacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClickedAt",
                table: "CampaignContacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedAt",
                table: "CampaignContacts",
                type: "timestamp with time zone",
                nullable: true);

            // Unique partial index on TrackingId (used by the tracking pixel lookup)
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ix_campaign_contacts_tracking_id ON \"CampaignContacts\" (\"TrackingId\") WHERE \"TrackingId\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop tracking indexes
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_campaign_contacts_tracking_id;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_email_events_message_id;");

            // Drop EmailEvents table (cascade drops its indexes and FKs)
            migrationBuilder.DropTable(name: "EmailEvents");

            // Drop Campaign engagement columns
            migrationBuilder.DropColumn(name: "SentCount",         table: "Campaigns");
            migrationBuilder.DropColumn(name: "OpenedCount",       table: "Campaigns");
            migrationBuilder.DropColumn(name: "ClickedCount",      table: "Campaigns");
            migrationBuilder.DropColumn(name: "RepliedCount",      table: "Campaigns");
            migrationBuilder.DropColumn(name: "UnsubscribedCount", table: "Campaigns");
            migrationBuilder.DropColumn(name: "ComplainedCount",   table: "Campaigns");

            // Drop CampaignContact tracking columns
            migrationBuilder.DropColumn(name: "TrackingId", table: "CampaignContacts");
            migrationBuilder.DropColumn(name: "OpenedAt",   table: "CampaignContacts");
            migrationBuilder.DropColumn(name: "ClickedAt",  table: "CampaignContacts");
            migrationBuilder.DropColumn(name: "RepliedAt",  table: "CampaignContacts");
        }
    }
}
