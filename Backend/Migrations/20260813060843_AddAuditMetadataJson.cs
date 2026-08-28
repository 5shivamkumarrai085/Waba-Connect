using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <summary>
    /// Adds the structured-payload column, and normalises chat-delete entries onto one shape.
    ///
    /// <para>
    /// Chat.MessagesDeleted was written against EntityType 'ChatConversation' with the
    /// conversation's id, then briefly against 'ChatMessage' with a comma-joined list of message
    /// ids. Two shapes for one event, and the second silently redefined what EntityId meant — as
    /// well as overflowing its 100 characters on any sizeable delete. Both delete events point at
    /// the conversation again; MetadataJson is where the individual messages now live.
    /// </para>
    /// </summary>
    public partial class AddAuditMetadataJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "AuditLogs",
                type: "text",
                nullable: true);

            // Recover the conversation from the first message id still on the row. Message deletes
            // are soft, so the row is normally still there to join against. The regex guard keeps
            // the cast safe if anything non-numeric ever reached the column.
            migrationBuilder.Sql("""
                UPDATE "AuditLogs" a
                SET "EntityType" = 'ChatConversation',
                    "EntityId"   = m."ConversationId"::text
                FROM "ChatMessages" m
                WHERE a."Event" = 'Chat.MessagesDeleted'
                  AND a."EntityType" = 'ChatMessage'
                  AND a."EntityId" ~ '^[0-9]+(,[0-9]+)*$'
                  AND m."Id" = split_part(a."EntityId", ',', 1)::integer;
            """);

            // Anything left points at messages that no longer exist — the conversation was hard
            // deleted afterwards, taking them with it. The id cannot be recovered, and leaving a
            // message-id list under a label that says "conversation" would be worse than leaving
            // it empty. The description on these rows still names what was deleted.
            migrationBuilder.Sql("""
                UPDATE "AuditLogs"
                SET "EntityType" = 'ChatConversation',
                    "EntityId"   = NULL
                WHERE "Event" = 'Chat.MessagesDeleted'
                  AND "EntityType" = 'ChatMessage';
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the column is reversed. The data correction above is not: re-splitting a
            // conversation id back into the message-id list it replaced is not possible, and the
            // shape it would restore is the defective one.
            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "AuditLogs");
        }
    }
}
