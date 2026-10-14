using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WhatsAppCampaignApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSendMoneyAndWhatsAppFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FxQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorridorId = table.Column<int>(type: "integer", nullable: false),
                    PayoutMethodId = table.Column<int>(type: "integer", nullable: false),
                    SendCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    SendAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FeeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPayable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FeeLabel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ReceiveCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReceiveAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MidRate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    AppliedRate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MarginBps = table.Column<int>(type: "integer", nullable: false),
                    RateProvider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RateAsOf = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxQuotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OtpChallenges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SubjectKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Destination = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    SendCount = table.Column<int>(type: "integer", nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemittanceBeneficiaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    CorridorId = table.Column<int>(type: "integer", nullable: false),
                    PayoutMethodId = table.Column<int>(type: "integer", nullable: false),
                    InstitutionId = table.Column<int>(type: "integer", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MaskedAccount = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    RelationshipCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittanceBeneficiaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemittanceBeneficiaries_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemittanceCorridors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    SourceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DestinationCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DestinationCountryName = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DestinationCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DestinationDecimals = table.Column<int>(type: "integer", nullable: false),
                    MinSendAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxSendAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FxMarginBps = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittanceCorridors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemittanceLookups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittanceLookups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemittancePaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Badge = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PaymentWindowMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittancePaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemittanceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConnectionId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OtpVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorridorId = table.Column<int>(type: "integer", nullable: true),
                    PayoutMethodId = table.Column<int>(type: "integer", nullable: true),
                    SendAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeneficiaryId = table.Column<int>(type: "integer", nullable: true),
                    SourceOfFundsCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PurposeCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittanceSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemittanceSessions_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransferOrderEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransferOrderId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ProviderEventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOrderEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransferOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    ConnectionId = table.Column<int>(type: "integer", nullable: true),
                    CorridorId = table.Column<int>(type: "integer", nullable: false),
                    PayoutMethodId = table.Column<int>(type: "integer", nullable: false),
                    BeneficiaryId = table.Column<int>(type: "integer", nullable: false),
                    BeneficiaryName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    InstitutionName = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    MaskedAccount = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    PayoutMethodName = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SendCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    SendAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FeeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPayable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceiveCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReceiveAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AppliedRate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    SourceOfFundsCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PurposeCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    PaymentMethodCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PaymentProvider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProviderPaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PaymentExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OrderStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaymentStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayoutProvider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PayoutReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CustomerNotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferOrders_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppFlowEncryptionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConnectionId = table.Column<int>(type: "integer", nullable: false),
                    PublicKeyPem = table.Column<string>(type: "text", nullable: false),
                    PrivateKeyPem = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignatureStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppFlowEncryptionKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppFlowEncryptionKeys_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppFlowRegistrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConnectionId = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MetaFlowId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    JsonVersion = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ValidationErrorsJson = table.Column<string>(type: "text", nullable: true),
                    EndpointKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EndpointUri = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppFlowRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppFlowRegistrations_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemittancePayoutMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CorridorId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DeliveryEta = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PayoutProvider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittancePayoutMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemittancePayoutMethods_RemittanceCorridors_CorridorId",
                        column: x => x.CorridorId,
                        principalTable: "RemittanceCorridors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemittanceBeneficiaryFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PayoutMethodId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InputType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    MinLength = table.Column<int>(type: "integer", nullable: true),
                    MaxLength = table.Column<int>(type: "integer", nullable: true),
                    Pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HelperText = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsAccountIdentifier = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittanceBeneficiaryFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemittanceBeneficiaryFields_RemittancePayoutMethods_PayoutM~",
                        column: x => x.PayoutMethodId,
                        principalTable: "RemittancePayoutMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemittanceFeeRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PayoutMethodId = table.Column<int>(type: "integer", nullable: false),
                    FixedFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PercentFee = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    MinFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    FirstTransferFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PromoLabel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittanceFeeRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemittanceFeeRules_RemittancePayoutMethods_PayoutMethodId",
                        column: x => x.PayoutMethodId,
                        principalTable: "RemittancePayoutMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemittanceInstitutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PayoutMethodId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittanceInstitutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemittanceInstitutions_RemittancePayoutMethods_PayoutMethod~",
                        column: x => x.PayoutMethodId,
                        principalTable: "RemittancePayoutMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FxQuotes_SessionId",
                table: "FxQuotes",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_Purpose_SubjectKey_CreatedAt",
                table: "OtpChallenges",
                columns: new[] { "Purpose", "SubjectKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RemittanceBeneficiaries_ContactId_CorridorId_PayoutMethodId",
                table: "RemittanceBeneficiaries",
                columns: new[] { "ContactId", "CorridorId", "PayoutMethodId" });

            migrationBuilder.CreateIndex(
                name: "UX_RemittanceBeneficiaryFields_Key",
                table: "RemittanceBeneficiaryFields",
                columns: new[] { "PayoutMethodId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RemittanceCorridors_Route",
                table: "RemittanceCorridors",
                columns: new[] { "SourceCountryCode", "DestinationCountryCode", "DestinationCurrency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemittanceFeeRules_PayoutMethodId",
                table: "RemittanceFeeRules",
                column: "PayoutMethodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RemittanceInstitutions_Code",
                table: "RemittanceInstitutions",
                columns: new[] { "PayoutMethodId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RemittanceLookups_Code",
                table: "RemittanceLookups",
                columns: new[] { "Category", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RemittancePaymentMethods_Code",
                table: "RemittancePaymentMethods",
                columns: new[] { "SourceCountryCode", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RemittancePayoutMethods_Code",
                table: "RemittancePayoutMethods",
                columns: new[] { "CorridorId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemittanceSessions_ContactId_CreatedAt",
                table: "RemittanceSessions",
                columns: new[] { "ContactId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_RemittanceSessions_Token",
                table: "RemittanceSessions",
                column: "FlowTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrderEvents_TransferOrderId_OccurredAt",
                table: "TransferOrderEvents",
                columns: new[] { "TransferOrderId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "UX_TransferOrderEvents_ProviderEvent",
                table: "TransferOrderEvents",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true,
                filter: "\"ProviderEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_ContactId_CreatedAt",
                table: "TransferOrders",
                columns: new[] { "ContactId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_OrderStatus_PaymentExpiresAt",
                table: "TransferOrders",
                columns: new[] { "OrderStatus", "PaymentExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOrders_ProviderPayment",
                table: "TransferOrders",
                columns: new[] { "PaymentProvider", "ProviderPaymentId" },
                filter: "\"ProviderPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_TransferOrders_Reference",
                table: "TransferOrders",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TransferOrders_Session",
                table: "TransferOrders",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppFlowEncryptionKeys_ConnectionId",
                table: "WhatsAppFlowEncryptionKeys",
                column: "ConnectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_WhatsAppFlowRegistrations_EndpointKey",
                table: "WhatsAppFlowRegistrations",
                column: "EndpointKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_WhatsAppFlowRegistrations_Purpose",
                table: "WhatsAppFlowRegistrations",
                columns: new[] { "ConnectionId", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FxQuotes");

            migrationBuilder.DropTable(
                name: "OtpChallenges");

            migrationBuilder.DropTable(
                name: "RemittanceBeneficiaries");

            migrationBuilder.DropTable(
                name: "RemittanceBeneficiaryFields");

            migrationBuilder.DropTable(
                name: "RemittanceFeeRules");

            migrationBuilder.DropTable(
                name: "RemittanceInstitutions");

            migrationBuilder.DropTable(
                name: "RemittanceLookups");

            migrationBuilder.DropTable(
                name: "RemittancePaymentMethods");

            migrationBuilder.DropTable(
                name: "RemittanceSessions");

            migrationBuilder.DropTable(
                name: "TransferOrderEvents");

            migrationBuilder.DropTable(
                name: "TransferOrders");

            migrationBuilder.DropTable(
                name: "WhatsAppFlowEncryptionKeys");

            migrationBuilder.DropTable(
                name: "WhatsAppFlowRegistrations");

            migrationBuilder.DropTable(
                name: "RemittancePayoutMethods");

            migrationBuilder.DropTable(
                name: "RemittanceCorridors");
        }
    }
}
