using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailChannelAndJobQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_ContactId_ConnectionId",
                table: "ChatConversations");

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "ChatMessages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "WhatsApp");

            migrationBuilder.AddColumn<string>(
                name: "ProviderMessageId",
                table: "ChatMessages",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "ChatConversations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "WhatsApp");

            migrationBuilder.AlterColumn<int>(
                name: "TemplateId",
                table: "Campaigns",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "Campaigns",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "WhatsApp");

            migrationBuilder.AddColumn<int>(
                name: "EmailTemplateId",
                table: "Campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderMessageId",
                table: "CampaignContacts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SendAttemptedAt",
                table: "CampaignContacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignApprovalStates",
                columns: table => new
                {
                    CampaignId = table.Column<int>(type: "integer", nullable: false),
                    ExternalReferenceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignApprovalStates", x => x.CampaignId);
                    table.ForeignKey(
                        name: "FK_CampaignApprovalStates_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConnectionId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Region = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AuthMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AccessKeyId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SecretAccessKeyEncrypted = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ConfigurationSet = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SmtpHost = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SmtpPort = table.Column<int>(type: "integer", nullable: true),
                    SmtpSecurity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SmtpUsername = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SmtpPasswordEncrypted = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MaxSendRatePerSecond = table.Column<decimal>(type: "numeric", nullable: true),
                    DefaultFromName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DefaultFromEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DefaultReplyTo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LastTestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "boolean", nullable: true),
                    LastTestMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailConfigurations_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmailDeliveryEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignContactId = table.Column<int>(type: "integer", nullable: true),
                    ChatMessageId = table.Column<int>(type: "integer", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SnsMessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    BounceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BounceSubType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DiagnosticCode = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecipientAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LinkUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDeliveryEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailDeliveryEvents_CampaignContacts_CampaignContactId",
                        column: x => x.CampaignContactId,
                        principalTable: "CampaignContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmailDeliveryEvents_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmailMessageDetails",
                columns: table => new
                {
                    ChatMessageId = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FromAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FromName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ToAddresses = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CcAddresses = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BccAddresses = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReplyTo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    HtmlBody = table.Column<string>(type: "text", nullable: true),
                    MessageIdHeader = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InReplyTo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReferencesHeader = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    HasAttachments = table.Column<bool>(type: "boolean", nullable: false),
                    AttachmentsJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessageDetails", x => x.ChatMessageId);
                    table.ForeignKey(
                        name: "FK_EmailMessageDetails_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailSendQuotas",
                columns: table => new
                {
                    ConnectionId = table.Column<int>(type: "integer", nullable: false),
                    Capacity = table.Column<double>(type: "double precision", nullable: false),
                    RefillPerSecond = table.Column<double>(type: "double precision", nullable: false),
                    Tokens = table.Column<double>(type: "double precision", nullable: false),
                    LastRefillAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSendQuotas", x => x.ConnectionId);
                    table.ForeignKey(
                        name: "FK_EmailSendQuotas_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailSuppressions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmailAddressNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Scope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConnectionId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuppressedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSuppressions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSuppressions_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobQueue",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QueueName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PartitionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeasedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobQueue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailSendingDomains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmailConfigurationId = table.Column<int>(type: "integer", nullable: false),
                    DomainName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    VerificationStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DkimStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DkimTokensJson = table.Column<string>(type: "jsonb", nullable: true),
                    MailFromDomain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MailFromStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSendingDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSendingDomains_EmailConfigurations_EmailConfigurationId",
                        column: x => x.EmailConfigurationId,
                        principalTable: "EmailConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailSenderIdentities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmailConfigurationId = table.Column<int>(type: "integer", nullable: false),
                    SendingDomainId = table.Column<int>(type: "integer", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EmailAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ReplyTo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    VerificationStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSenderIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSenderIdentities_EmailConfigurations_EmailConfiguratio~",
                        column: x => x.EmailConfigurationId,
                        principalTable: "EmailConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailSenderIdentities_EmailSendingDomains_SendingDomainId",
                        column: x => x.SendingDomainId,
                        principalTable: "EmailSendingDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailCampaignDetails",
                columns: table => new
                {
                    CampaignId = table.Column<int>(type: "integer", nullable: false),
                    SenderIdentityId = table.Column<int>(type: "integer", nullable: false),
                    ReplyToOverride = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SubjectOverride = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AttachmentsJson = table.Column<string>(type: "jsonb", nullable: true),
                    TrackOpens = table.Column<bool>(type: "boolean", nullable: false),
                    TrackClicks = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCampaignDetails", x => x.CampaignId);
                    table.ForeignKey(
                        name: "FK_EmailCampaignDetails_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailCampaignDetails_EmailSenderIdentities_SenderIdentityId",
                        column: x => x.SenderIdentityId,
                        principalTable: "EmailSenderIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ProviderMessageId",
                table: "ChatMessages",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ContactId_ConnectionId_Channel",
                table: "ChatConversations",
                columns: new[] { "ContactId", "ConnectionId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Channel",
                table: "Campaigns",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_EmailTemplateId",
                table: "Campaigns",
                column: "EmailTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignContacts_ProviderMessageId",
                table: "CampaignContacts",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignApprovalStates_ExternalReferenceId",
                table: "CampaignApprovalStates",
                column: "ExternalReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailCampaignDetails_SenderIdentityId",
                table: "EmailCampaignDetails",
                column: "SenderIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailConfigurations_ConnectionId",
                table: "EmailConfigurations",
                column: "ConnectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_CampaignContactId_EventType",
                table: "EmailDeliveryEvents",
                columns: new[] { "CampaignContactId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_ChatMessageId",
                table: "EmailDeliveryEvents",
                column: "ChatMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_OccurredAt",
                table: "EmailDeliveryEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_ProviderMessageId",
                table: "EmailDeliveryEvents",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_SnsMessageId",
                table: "EmailDeliveryEvents",
                column: "SnsMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessageDetails_MessageIdHeader",
                table: "EmailMessageDetails",
                column: "MessageIdHeader");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSenderIdentities_EmailConfigurationId_EmailAddress",
                table: "EmailSenderIdentities",
                columns: new[] { "EmailConfigurationId", "EmailAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailSenderIdentities_SendingDomainId",
                table: "EmailSenderIdentities",
                column: "SendingDomainId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSendingDomains_EmailConfigurationId_DomainName",
                table: "EmailSendingDomains",
                columns: new[] { "EmailConfigurationId", "DomainName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailSuppressions_ConnectionId",
                table: "EmailSuppressions",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSuppressions_EmailAddressNormalized_ExpiresAt",
                table: "EmailSuppressions",
                columns: new[] { "EmailAddressNormalized", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "UX_EmailSuppression_Connection",
                table: "EmailSuppressions",
                columns: new[] { "EmailAddressNormalized", "ConnectionId" },
                unique: true,
                filter: "\"ConnectionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_EmailSuppression_Global",
                table: "EmailSuppressions",
                column: "EmailAddressNormalized",
                unique: true,
                filter: "\"ConnectionId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JobQueue_Claim",
                table: "JobQueue",
                columns: new[] { "QueueName", "Priority", "AvailableAt", "Id" },
                filter: "\"Status\" IN ('Pending', 'Leased')");

            migrationBuilder.CreateIndex(
                name: "IX_JobQueue_Lease",
                table: "JobQueue",
                columns: new[] { "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobQueue_Partition",
                table: "JobQueue",
                columns: new[] { "QueueName", "PartitionKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_JobQueue_Idempotency",
                table: "JobQueue",
                columns: new[] { "QueueName", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_EmailTemplates_EmailTemplateId",
                table: "Campaigns",
                column: "EmailTemplateId",
                principalTable: "EmailTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_EmailTemplates_EmailTemplateId",
                table: "Campaigns");

            migrationBuilder.DropTable(
                name: "CampaignApprovalStates");

            migrationBuilder.DropTable(
                name: "EmailCampaignDetails");

            migrationBuilder.DropTable(
                name: "EmailDeliveryEvents");

            migrationBuilder.DropTable(
                name: "EmailMessageDetails");

            migrationBuilder.DropTable(
                name: "EmailSendQuotas");

            migrationBuilder.DropTable(
                name: "EmailSuppressions");

            migrationBuilder.DropTable(
                name: "JobQueue");

            migrationBuilder.DropTable(
                name: "EmailSenderIdentities");

            migrationBuilder.DropTable(
                name: "EmailSendingDomains");

            migrationBuilder.DropTable(
                name: "EmailConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ProviderMessageId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_ContactId_ConnectionId_Channel",
                table: "ChatConversations");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_Channel",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_EmailTemplateId",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_CampaignContacts_ProviderMessageId",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ProviderMessageId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "EmailTemplateId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ProviderMessageId",
                table: "CampaignContacts");

            migrationBuilder.DropColumn(
                name: "SendAttemptedAt",
                table: "CampaignContacts");

            migrationBuilder.AlterColumn<int>(
                name: "TemplateId",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ContactId_ConnectionId",
                table: "ChatConversations",
                columns: new[] { "ContactId", "ConnectionId" },
                unique: true);
        }
    }
}
