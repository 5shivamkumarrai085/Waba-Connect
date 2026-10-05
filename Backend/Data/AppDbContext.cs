using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Contact> Contacts { get; set; } = null!;
    public DbSet<ConnectionDailySendCounter> ConnectionDailySendCounters { get; set; } = null!;
    public DbSet<ContactGroup> ContactGroups { get; set; } = null!;
    public DbSet<ContactGroupMember> ContactGroupMembers { get; set; } = null!;
    public DbSet<Template> Templates { get; set; } = null!;
    public DbSet<TemplateVariable> TemplateVariables { get; set; } = null!;
    public DbSet<Campaign> Campaigns { get; set; } = null!;
    public DbSet<CampaignContact> CampaignContacts { get; set; } = null!;
    public DbSet<CampaignVariable> CampaignVariables { get; set; } = null!;
    public DbSet<ChatConversation> ChatConversations { get; set; } = null!;
    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;
    public DbSet<ContactNote> ContactNotes { get; set; } = null!;

    // Multi-WABA Connections & Permissions
    public DbSet<Connection> Connections { get; set; } = null!;
    public DbSet<DepartmentConnection> DepartmentConnections { get; set; } = null!;
    public DbSet<UserConnection> UserConnections { get; set; } = null!;
    public DbSet<CampaignRetryRun> CampaignRetryRuns { get; set; } = null!;
    public DbSet<CampaignVariant> CampaignVariants { get; set; } = null!;
    public DbSet<FollowUpRule> FollowUpRules { get; set; } = null!;
    public DbSet<WebhookSubscription> WebhookSubscriptions { get; set; } = null!;
    public DbSet<WebhookDelivery> WebhookDeliveries { get; set; } = null!;
    public DbSet<ReportSchedule> ReportSchedules { get; set; } = null!;
    public DbSet<ContactConsent> ContactConsents { get; set; } = null!;
    public DbSet<Segment> Segments { get; set; } = null!;
    public DbSet<CampaignSegment> CampaignSegments { get; set; } = null!;
    public DbSet<ConsentEvent> ConsentEvents { get; set; } = null!;

    // WABA Configuration
    public DbSet<WabaConfiguration> WabaConfigurations { get; set; } = null!;
    public DbSet<WabaPhoneNumber> WabaPhoneNumbers { get; set; } = null!;
    public DbSet<Business> Businesses { get; set; } = null!;
    public DbSet<HealthLog> HealthLogs { get; set; } = null!;
    public DbSet<MessageBot> MessageBots { get; set; } = null!;
    public DbSet<TemplateBot> TemplateBots { get; set; } = null!;
    public DbSet<TemplateBotVariable> TemplateBotVariables { get; set; } = null!;
    public DbSet<BotFlow> BotFlows { get; set; } = null!;
    public DbSet<FlowNode> FlowNodes { get; set; } = null!;
    public DbSet<FlowEdge> FlowEdges { get; set; } = null!;
    public DbSet<ConversationState> ConversationStates { get; set; } = null!;
    public DbSet<BotMessage> Messages { get; set; } = null!;
    public DbSet<ClientAiSetting> ClientAiSettings { get; set; } = null!;
    public DbSet<AiSession> AiSessions { get; set; } = null!;

    /// <summary>Customers who have told the bots to stop. See <see cref="BotSuppression"/>.</summary>
    public DbSet<BotSuppression> BotSuppressions { get; set; } = null!;

    // Authentication & RBAC
    public DbSet<AppUser> AppUsers { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<Permission> Permissions { get; set; } = null!;
    public DbSet<RolePermission> RolePermissions { get; set; } = null!;
    public DbSet<UserPermission> UserPermissions { get; set; } = null!;

    // Audit trail
    public DbSet<LoginAttempt> LoginAttempts { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    // Reporting
    public DbSet<ReportDefinition> ReportDefinitions { get; set; } = null!;

    /// <summary>Key/value store behind the OmniConnect settings screen. See AppSetting.</summary>
    public DbSet<AppSetting> AppSettings { get; set; } = null!;

    // Setup lookups
    public DbSet<ContactStatusLookup> ContactStatuses { get; set; } = null!;
    public DbSet<ContactSourceLookup> ContactSources { get; set; } = null!;
    public DbSet<ContactTypeLookup> ContactTypes { get; set; } = null!;
    public DbSet<Language> Languages { get; set; } = null!;
    public DbSet<Translation> Translations { get; set; } = null!;
    public DbSet<AiPrompt> AiPrompts { get; set; } = null!;
    public DbSet<CannedReply> CannedReplies { get; set; } = null!;
    public DbSet<EmailTemplate> EmailTemplates { get; set; } = null!;

    // Email channel
    public DbSet<EmailConfiguration> EmailConfigurations { get; set; } = null!;
    public DbSet<EmailSenderIdentity> EmailSenderIdentities { get; set; } = null!;
    public DbSet<EmailCampaignDetail> EmailCampaignDetails { get; set; } = null!;
    public DbSet<EmailMessageDetail> EmailMessageDetails { get; set; } = null!;
    public DbSet<EmailSuppression> EmailSuppressions { get; set; } = null!;
    public DbSet<EmailSendQuota> EmailSendQuotas { get; set; } = null!;

    /// <summary>
    /// Normalized, channel-agnostic email event history. Append-only by design.
    /// See <see cref="Models.Entities.EmailEvent"/> for the full schema rationale.
    /// </summary>
    public DbSet<EmailEvent> EmailEvents { get; set; } = null!;

    /// <summary>
    /// The background job queue. Exposed for migrations and the monitoring endpoint only — the
    /// claim path is raw SQL in PostgresJobQueue, because FOR UPDATE SKIP LOCKED cannot be
    /// expressed through the change tracker without racing between instances.
    /// </summary>
    public DbSet<JobQueueEntry> JobQueue { get; set; } = null!;

    /// <summary>Written only when an external host owns campaign approval. See CampaignApprovalState.</summary>
    public DbSet<CampaignApprovalState> CampaignApprovalStates { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- Authentication & RBAC ------------------------------------------------
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("AppUsers");
            entity.HasIndex(e => e.Email).IsUnique();

            // ───────────────────────────────────────────────────────────────────────────────
            // TEMPORARY BRIDGE — REMOVE WHEN THE HOST BRANCH IS MERGED
            //
            // The shared database is ahead of this checkout. The host application's
            // "LinkOmniCXIdentity" migration renamed AppUsers.ExternalUserId to
            // ExternalSubjectId, and this branch does not have that migration, so every query
            // that materialises an AppUser — including login — asks for a column that no longer
            // exists and fails with "42703: column a.ExternalUserId does not exist".
            //
            // This maps the existing property onto the column the database actually has. It is
            // a mapping only: no schema is created, altered or dropped, and the shared database
            // is left exactly as the host left it.
            //
            // It is the sole mismatch on this table — the other 20 columns line up exactly.
            //
            // To remove: pull the host branch (which renames the property properly), delete
            // this HasColumnName call, and restore `entity.HasIndex(e => e.ExternalUserId)`
            // below to whatever the merged model names it.
            // ───────────────────────────────────────────────────────────────────────────────
            entity.Property(e => e.ExternalUserId).HasColumnName("ExternalSubjectId");
            entity.HasIndex(e => e.ExternalUserId);

            // SetNull rather than Cascade: deleting a role must not delete the people holding
            // it. They fall back to no permissions until reassigned, which is safe.
            entity.HasOne(e => e.Role)
                  .WithMany(r => r.Users)
                  .HasForeignKey(e => e.RoleId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("Permissions");
            entity.HasIndex(e => e.Key).IsUnique();
            entity.HasIndex(e => e.Feature);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("RolePermissions");
            entity.HasIndex(e => new { e.RoleId, e.PermissionId }).IsUnique();

            entity.HasOne(e => e.Role)
                  .WithMany(r => r.RolePermissions)
                  .HasForeignKey(e => e.RoleId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Permission)
                  .WithMany()
                  .HasForeignKey(e => e.PermissionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.ToTable("UserPermissions");
            entity.HasIndex(e => new { e.UserId, e.PermissionId }).IsUnique();

            entity.HasOne(e => e.User)
                  .WithMany(u => u.UserPermissions)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Permission)
                  .WithMany()
                  .HasForeignKey(e => e.PermissionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Audit trail ----------------------------------------------------------
        // No FK to AppUser on either table: the trail must outlive the accounts it describes,
        // and login attempts routinely reference an email that matches no account at all.
        modelBuilder.Entity<LoginAttempt>(entity =>
        {
            entity.ToTable("LoginAttempts");
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Success);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Category);

            // The activity log filters and pages on these in SQL, so each needs an index — the
            // table is append-only and grows without bound.
            entity.HasIndex(e => e.Module);
            entity.HasIndex(e => e.Action);
            entity.HasIndex(e => e.UserId);
            // Recent Activity (latest rows of some modules) and the audit page's module filter.
            entity.HasIndex(e => new { e.Module, e.CreatedAt });
            // "History of this record": every event for one entity.
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.Status);

            // The user-facing event number: its own sequence rather than the primary key, so the
            // API never exposes a surrogate id that could be enumerated. Starts well above the
            // current row count so it is visibly not a row id.
            entity.Property(e => e.EventNumber)
                .HasDefaultValueSql("nextval('\"AuditLogEventNumberSeq\"')");
            entity.HasIndex(e => e.EventNumber).IsUnique();
        });

        modelBuilder.HasSequence<long>("AuditLogEventNumberSeq").StartsAt(10000).IncrementsBy(1);

        // ---- Saved reports --------------------------------------------------------
        modelBuilder.Entity<ReportDefinition>(entity =>
        {
            entity.ToTable("ReportDefinitions");
            // The list query is "mine, or shared", so both sides of that OR need an index.
            entity.HasIndex(e => e.OwnerUserId);
            entity.HasIndex(e => e.IsShared);

            // SetNull, not Cascade: deleting a user must not take a report the rest of the team
            // is using down with them. The denormalised OwnerName keeps the row readable.
            entity.HasOne(e => e.OwnerUser)
                .WithMany()
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ---- Setup lookups --------------------------------------------------------
        // Deliberately no foreign key from Contact to these tables. Contact.Status/Source keep
        // storing the lookup's immutable Value string, which means this migration adds tables
        // without touching a single existing contact row.
        modelBuilder.Entity<AppSetting>(entity =>
        {
            // Unique rather than merely indexed: two rows for one key would make "the current
            // value" ambiguous, and which one won would depend on row order.
            entity.HasIndex(e => e.Key).IsUnique();
        });

        modelBuilder.Entity<ContactStatusLookup>(entity =>
        {
            entity.ToTable("ContactStatuses");
            entity.HasIndex(e => e.Value).IsUnique();
        });

        modelBuilder.Entity<ContactSourceLookup>(entity =>
        {
            entity.ToTable("ContactSources");
            entity.HasIndex(e => e.Value).IsUnique();
        });

        modelBuilder.Entity<ContactTypeLookup>(entity =>
        {
            entity.ToTable("ContactTypes");
            entity.HasIndex(e => e.Value).IsUnique();
        });

        modelBuilder.Entity<Language>(entity =>
        {
            entity.ToTable("Languages");
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<Translation>(entity =>
        {
            entity.ToTable("Translations");
            entity.HasIndex(e => new { e.LanguageId, e.Key }).IsUnique();

            entity.HasOne(e => e.Language)
                  .WithMany(l => l.Translations)
                  .HasForeignKey(e => e.LanguageId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiPrompt>(entity =>
        {
            entity.ToTable("AiPrompts");
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<CannedReply>(entity =>
        {
            entity.ToTable("CannedReplies");
            entity.HasIndex(e => e.IsPublic);
        });

        modelBuilder.Entity<EmailTemplate>(entity =>
        {
            entity.ToTable("EmailTemplates");
            entity.HasIndex(e => e.Key).IsUnique();
        });

        // Connection entity configuration
        modelBuilder.Entity<Connection>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();

            // Nickname carries [Required] for request validation but the column is, and stays,
            // nullable: connections created before nicknames existed have NULL, and the agreed
            // approach was to enforce it on new input only rather than rewrite that history.
            // Without this explicit mapping EF treats [Required] as NOT NULL and scaffolds a
            // destructive AlterColumn into every subsequent migration.
            entity.Property(e => e.Nickname).IsRequired(false);
        });

        // Connection access by role (the "department" assignments). Read by IAccessScope.
        modelBuilder.Entity<DepartmentConnection>(entity =>
        {
            entity.HasIndex(e => new { e.DepartmentId, e.ConnectionId }).IsUnique();
            entity.HasIndex(e => new { e.RoleId, e.ConnectionId }).IsUnique();
            entity.HasOne(e => e.Connection).WithMany().HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Role).WithMany().HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Segment>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<CampaignSegment>(entity =>
        {
            entity.HasKey(e => new { e.CampaignId, e.SegmentId });
            entity.HasOne(e => e.Campaign).WithMany(c => c.Segments).HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Segment).WithMany().HasForeignKey(e => e.SegmentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.SegmentId);
            // Matches Campaign's soft-delete filter, so a deleted campaign hides its children too.
            entity.HasQueryFilter(e => !e.Campaign.IsDeleted);
        });

        modelBuilder.Entity<ContactConsent>(entity =>
        {
            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.ContactId, e.Channel, e.Topic }).IsUnique();
            entity.HasOne(e => e.Contact).WithMany().HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Cascade);
            // Matches Contact's soft-delete filter. The append-only ConsentEvents history is unaffected.
            entity.HasQueryFilter(e => !e.Contact.IsDeleted);
        });

        modelBuilder.Entity<ConsentEvent>(entity =>
        {
            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.ContactId, e.OccurredAt });
        });

        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).HasDefaultValue(ConversationStatus.Open).HasSentinel((ConversationStatus)(-1));
            entity.HasOne(e => e.AssignedUser).WithMany().HasForeignKey(e => e.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.ConnectionId, e.Status, e.AssignedUserId });
            entity.HasIndex(e => new { e.Status, e.FirstResponseDueAt });
        });

        modelBuilder.Entity<FollowUpRule>(entity =>
        {
            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.Status, e.DueAt });
            entity.HasIndex(e => e.CampaignId);
            entity.HasOne(e => e.Campaign).WithMany().HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade);
            // Matches Campaign's soft-delete filter, so a deleted campaign hides its children too.
            entity.HasQueryFilter(e => !e.Campaign.IsDeleted);
        });

        modelBuilder.Entity<WebhookSubscription>(entity =>
        {
            // The signing secret is encrypted at rest, like every other stored credential.
            entity.Property(e => e.Secret).HasConversion(
                v => WhatsAppCampaignApi.Services.SecretCipher.EncryptForStorage(v),
                v => WhatsAppCampaignApi.Services.SecretCipher.DecryptOrPassThrough(v));
        });

        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.HasOne(e => e.Subscription).WithMany().HasForeignKey(e => e.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.SubscriptionId, e.Id });
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<ReportSchedule>(entity =>
        {
            entity.HasOne(e => e.ReportDefinition).WithMany().HasForeignKey(e => e.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.IsActive, e.NextRunAt });
            entity.HasIndex(e => e.OwnerUserId);
        });

        modelBuilder.Entity<CampaignVariant>(entity =>
        {
            entity.HasIndex(e => new { e.CampaignId, e.Label }).IsUnique();
            entity.HasOne(e => e.Campaign).WithMany(c => c.Variants).HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Template).WithMany().HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.EmailTemplate).WithMany().HasForeignKey(e => e.EmailTemplateId).OnDelete(DeleteBehavior.Restrict);
            // Matches Campaign's soft-delete filter, so a deleted campaign hides its children too.
            entity.HasQueryFilter(e => !e.Campaign.IsDeleted);
        });

        modelBuilder.Entity<CampaignRetryRun>(entity =>
        {
            entity.HasIndex(e => e.CampaignId);
            entity.HasOne(e => e.Campaign).WithMany().HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade);
            // Matches Campaign's soft-delete filter, so a deleted campaign hides its children too.
            entity.HasQueryFilter(e => !e.Campaign.IsDeleted);
        });

        // Connection access per user. Read by IAccessScope.
        modelBuilder.Entity<UserConnection>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.ConnectionId }).IsUnique();
            entity.HasIndex(e => new { e.AppUserId, e.ConnectionId }).IsUnique();
            entity.HasOne(e => e.Connection).WithMany().HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AppUser).WithMany().HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.Cascade);
        });

        // WabaConfiguration Connection FK
        modelBuilder.Entity<WabaConfiguration>(entity =>
        {
            entity.HasOne(e => e.Connection).WithMany(c => c.WabaConfigurations).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.SetNull);
        });

        // WabaPhoneNumber Connection FK
        modelBuilder.Entity<WabaPhoneNumber>(entity =>
        {
            entity.HasOne(e => e.Connection).WithMany(c => c.WabaPhoneNumbers).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.SetNull);

            // Resolved on every inbound webhook message.
            entity.HasIndex(e => e.PhoneNumberId);
        });

        // WABA credentials are encrypted at rest, transparently: every existing reader keeps
        // reading a plain string. Rows written before this still hold plaintext, which the
        // converter passes through until the startup re-encryption (or the next save) upgrades it.
        modelBuilder.Entity<WabaConfiguration>(entity =>
        {
            entity.Property(e => e.AccessToken).HasConversion(
                v => WhatsAppCampaignApi.Services.SecretCipher.EncryptForStorage(v),
                v => WhatsAppCampaignApi.Services.SecretCipher.DecryptOrPassThrough(v));
            entity.Property(e => e.FacebookAppSecret).HasConversion(
                v => WhatsAppCampaignApi.Services.SecretCipher.EncryptForStorage(v),
                v => WhatsAppCampaignApi.Services.SecretCipher.DecryptOrPassThrough(v));
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.RevokedAt });
            entity.HasIndex(e => e.FamilyId);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConnectionDailySendCounter>(entity =>
        {
            entity.ToTable("ConnectionDailySendCounters");
            entity.HasKey(e => new { e.ConnectionId, e.Day });
        });

        // Contact configurations
        modelBuilder.Entity<Contact>(entity =>
        {
            // Unique only among contacts that have a phone. Email-only contacts (created from an
            // inbound reply, or imported with an address and no number) store an empty string, and
            // an unfiltered index let only the first of them exist.
            entity.HasIndex(e => e.Phone).IsUnique().HasFilter("\"Phone\" <> ''");
            entity.HasIndex(e => e.CreatedAt);
            // Age segments compare the date of birth, never a stored age.
            entity.HasIndex(e => e.DateOfBirth);
            // The campaign wizard's audience: counts and pages of active contacts by type.
            entity.HasIndex(e => new { e.Type, e.IsActive });
            entity.HasQueryFilter(e => !e.IsDeleted);
            // No HasConversion: Contact.Type is a plain string now, matching Status and Source.
            entity.Property(e => e.Type).HasMaxLength(50);
            // Status and Source are now plain strings holding a lookup's Value. The column
            // definition is identical to what HasConversion<string>() produced, so this is a
            // CLR-side change only — verified by the generated migration being empty.
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.Source).HasMaxLength(50);
        });

        // ContactGroupMember
        modelBuilder.Entity<ContactGroupMember>(entity =>
        {
            entity.HasIndex(e => new { e.ContactId, e.GroupId }).IsUnique();
            entity.HasOne(e => e.Contact).WithMany(c => c.GroupMemberships).HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Group).WithMany(g => g.Members).HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        // Template configurations
        modelBuilder.Entity<Template>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Category).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.TemplateType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.HeaderType).HasConversion<string>().HasMaxLength(50);
            entity.HasMany(e => e.Variables).WithOne(v => v.Template).HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        // Campaign configurations
        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);

            // The scheduler's "due now" scan.
            entity.HasIndex(e => new { e.Status, e.ScheduledAt });
            entity.HasQueryFilter(e => !e.IsDeleted);
            // RelationType is a comma-joined list of contact type values. Types are an
            // administrator-managed lookup now, so a campaign can name several custom types;
            // 50 characters held barely three.
            entity.Property(e => e.RelationType).HasMaxLength(500);
            entity.Property(e => e.ScheduleType).HasConversion<string>().HasMaxLength(50);
            // A wall-clock time ("09:30 on 3 October" in each recipient's zone), not an instant.
            entity.Property(e => e.LocalSendAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasMany(e => e.Variables).WithOne(v => v.Campaign).HasForeignKey(v => v.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Connection).WithMany(c => c.Campaigns).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.SetNull);

            // Channel: stored as a string like the other enums here, defaulted in the database so
            // the column is backfilled to WhatsApp for every pre-existing row without a data
            // migration step.
            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(50).HasDefaultValue(MessageChannel.WhatsApp);

            // Campaign listings and the dashboard both slice by channel; without this they scan.
            entity.HasIndex(e => e.Channel);

            // Restrict, not SetNull: silently detaching a campaign from its template would leave
            // a sent campaign that can no longer explain what it sent. EmailTemplate deletion is
            // blocked while a campaign references it, and the service reports that as a conflict.
            entity.HasOne(e => e.EmailTemplate).WithMany().HasForeignKey(e => e.EmailTemplateId).OnDelete(DeleteBehavior.Restrict);

            // Stated explicitly to hold the pre-existing behaviour. TemplateId became nullable
            // for the email channel, and EF's convention for an optional relationship is
            // NoAction rather than the Cascade a required one gets — so leaving this to
            // convention would quietly change what happens when a template is deleted.
            //
            // TemplateService.DeleteAsync already refuses to delete a template that any campaign
            // uses, so this cascade is unreachable from the app for live campaigns. It still
            // fires for soft-deleted ones, which that guard does not see (the Campaigns DbSet is
            // filtered on !IsDeleted). Preserving Cascade keeps that path behaving exactly as it
            // did. Worth revisiting: a template delete hard-deleting soft-deleted campaign rows
            // destroys history that the soft delete was meant to keep.
            // Restrict: deleting a template must never take a campaign's history with it (it used
            // to cascade, silently removing soft-deleted campaigns). TemplateService refuses first.
            entity.HasOne(e => e.Template).WithMany(t => t.Campaigns).HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
        });

        // Dependents of the two soft-deleted entities carry matching filters — see the note on
        // CampaignContact below for why.
        modelBuilder.Entity<CampaignVariable>()
            .HasQueryFilter(e => !e.Campaign.IsDeleted);

        modelBuilder.Entity<ContactNote>()
            .HasQueryFilter(e => !e.Contact.IsDeleted);

        modelBuilder.Entity<ContactGroupMember>()
            .HasQueryFilter(e => !e.Contact.IsDeleted);

        modelBuilder.Entity<ChatConversation>()
            .HasQueryFilter(e => !e.Contact.IsDeleted);

        // Filtering ChatConversation makes it a filtered principal in turn, so its own required
        // dependent needs the matching filter or the same advisory just moves down a level.
        //
        // Deliberately mirrors the conversation only. ChatMessage.IsDeleted (per-message soft
        // delete) stays an explicit .Where in ChatService, because DeleteMessagesAsync has to be
        // able to see already-deleted rows to report "already deleted" rather than "not found".
        modelBuilder.Entity<ChatMessage>()
            .HasQueryFilter(e => !e.Conversation.Contact.IsDeleted);

        // CampaignContact
        modelBuilder.Entity<CampaignContact>(entity =>
        {
            // Unique: Meta's message id identifies exactly one send. Status webhooks resolve through
            // it, and uniqueness is what makes a duplicate write fail loudly instead of splitting
            // one message's statuses across two rows.
            entity.HasIndex(e => e.WhatsAppMessageId).IsUnique().HasFilter("\"WhatsAppMessageId\" IS NOT NULL");

            // The channel-neutral equivalent, and what a bounce report (DSN) read over IMAP is
            // resolved back to a recipient with.
            entity.HasIndex(e => e.ProviderMessageId);

            entity.HasIndex(e => new { e.CampaignId, e.ContactId }).IsUnique();
            entity.HasIndex(e => new { e.SentAt, e.Status });

            // Frequency cap: "how many messages did these contacts get in the last N days".
            entity.HasIndex(e => new { e.ContactId, e.SentAt });

            // "Is anything in this campaign still pending?", keyset paging of a campaign's
            // recipients by status, and the per-status counts — all per campaign, all hot.
            entity.HasIndex(e => new { e.CampaignId, e.Status, e.Id });
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasOne(e => e.Campaign).WithMany(c => c.CampaignContacts).HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Contact).WithMany(c => c.CampaignContacts).HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Restrict);

            // Mirrors the filters on both required principals. Without these EF emits a
            // PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning on every
            // model build — 90 of the 90 warnings in the log file were this one advisory —
            // and, more importantly, it was a real defect: reporting and dashboard queries read
            // CampaignContacts directly, so recipients of deleted campaigns were still being
            // counted in delivery totals.
            entity.HasQueryFilter(e => !e.Campaign.IsDeleted && !e.Contact.IsDeleted);
        });

        // Chat conversations: UNIQUE ON {ContactId, ConnectionId, Channel} so one contact can have
        // conversations across multiple connections — and, since the email channel, across
        // channels too. Channel is part of the key because the previous two-column index would
        // have merged a contact's WhatsApp thread and their email thread into one row whenever
        // both arrived through the same connection.
        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(50).HasDefaultValue(MessageChannel.WhatsApp);
            entity.HasIndex(e => new { e.ContactId, e.ConnectionId, e.Channel }).IsUnique();
            entity.HasIndex(e => e.LastMessageAt);
            entity.HasOne(e => e.Contact).WithMany().HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.WabaPhoneNumber).WithMany().HasForeignKey(e => e.WabaPhoneNumberId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Connection).WithMany(c => c.ChatConversations).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.SetNull);
        });

        // Chat messages
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            // Unique: the webhook's "already stored?" check is only race-free with this behind it —
            // Meta can deliver the same inbound message to two requests at once.
            entity.HasIndex(e => e.WhatsAppMessageId).IsUnique().HasFilter("\"WhatsAppMessageId\" IS NOT NULL");
            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt });

            // Keyset paging of one thread (newest page, older pages, "since id").
            entity.HasIndex(e => new { e.ConversationId, e.Id });

            // The daily-limit seed and per-connection reporting.
            entity.HasIndex(e => new { e.ConnectionId, e.Direction, e.CreatedAt });
            entity.HasIndex(e => e.CampaignContactId);

            // Reporting reads this table across every conversation at once, which the composite
            // above cannot serve — its leading column is the conversation. A date-filtered report
            // without this scans the whole table.
            entity.HasIndex(e => e.CreatedAt);

            // Campaign-scoped reporting, and the recipient lookups that resolve a message back to
            // the campaign that sent it.
            entity.HasIndex(e => e.CampaignId);

            // "The next incoming message in this conversation after time T" — the derivation
            // behind the report's Response and Response Time columns. Direction sits in the
            // middle so it is an equality seek before the range scan on CreatedAt.
            entity.HasIndex(e => new { e.ConversationId, e.Direction, e.CreatedAt });

            // The email channel's equivalent of WhatsAppMessageId, and the key inbound replies and
            // bounce reports correlate on.
            entity.HasIndex(e => e.ProviderMessageId);

            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(50).HasDefaultValue(MessageChannel.WhatsApp);
            entity.Property(e => e.Direction).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasOne(e => e.Conversation).WithMany(c => c.Messages).HasForeignKey(e => e.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Contact).WithMany().HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Campaign).WithMany().HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.CampaignContact).WithMany().HasForeignKey(e => e.CampaignContactId).OnDelete(DeleteBehavior.SetNull);
        });

        // ---- Email channel --------------------------------------------------------------------

        modelBuilder.Entity<EmailConfiguration>(entity =>
        {
            entity.Property(e => e.Provider).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.SmtpSecurity).HasConversion<string>().HasMaxLength(50);

            // One email configuration per connection. A connection is either a WhatsApp sender or
            // an email sender; allowing two email configs on one connection would make "which
            // credential did this campaign send with" unanswerable after the fact.
            entity.HasIndex(e => e.ConnectionId).IsUnique();

            // SetNull mirrors WabaConfiguration: releasing a connection must not silently destroy
            // the credential history, it just detaches it.
            entity.HasOne(e => e.Connection).WithMany().HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EmailSenderIdentity>(entity =>
        {
            // The same address may legitimately be configured on two different connections, so
            // uniqueness is scoped to the configuration rather than global.
            entity.HasIndex(e => new { e.EmailConfigurationId, e.EmailAddress }).IsUnique();

            entity.HasOne(e => e.EmailConfiguration).WithMany(c => c.SenderIdentities)
                  .HasForeignKey(e => e.EmailConfigurationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailCampaignDetail>(entity =>
        {
            entity.HasKey(e => e.CampaignId);
            entity.Property(e => e.AttachmentsJson).HasColumnType("jsonb");

            entity.HasOne(e => e.Campaign).WithOne(c => c.EmailDetail)
                  .HasForeignKey<EmailCampaignDetail>(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade);

            // Restrict: a sender identity a campaign has already sent from stays resolvable.
            entity.HasOne(e => e.SenderIdentity).WithMany()
                  .HasForeignKey(e => e.SenderIdentityId).OnDelete(DeleteBehavior.Restrict);

            // Campaign carries a soft-delete filter, so its required dependent needs the matching
            // one — same reasoning as CampaignVariable and CampaignContact above.
            entity.HasQueryFilter(e => !e.Campaign.IsDeleted);
        });

        modelBuilder.Entity<EmailMessageDetail>(entity =>
        {
            entity.HasKey(e => e.ChatMessageId);

            // The inbound path resolves a reply by matching its In-Reply-To against this column.
            entity.HasIndex(e => e.MessageIdHeader);

            entity.HasOne(e => e.ChatMessage).WithOne(m => m.EmailDetail)
                  .HasForeignKey<EmailMessageDetail>(e => e.ChatMessageId).OnDelete(DeleteBehavior.Cascade);

            // Matches the filter on ChatMessage's principal chain.
            entity.HasQueryFilter(e => !e.ChatMessage.Conversation.Contact.IsDeleted);
        });

        modelBuilder.Entity<EmailSuppression>(entity =>
        {
            entity.Property(e => e.Scope).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Reason).HasConversion<string>().HasMaxLength(50);

            // Scoped uniqueness, as two partial indexes rather than one composite. A single
            // unique index over {address, scope, connection} would not de-duplicate global rows:
            // their ConnectionId is NULL, and Postgres treats distinct NULLs as unequal, so the
            // same address could be suppressed globally any number of times.
            entity.HasIndex(e => e.EmailAddressNormalized)
                  .IsUnique()
                  .HasDatabaseName("UX_EmailSuppression_Global")
                  .HasFilter("\"ConnectionId\" IS NULL");

            entity.HasIndex(e => new { e.EmailAddressNormalized, e.ConnectionId })
                  .IsUnique()
                  .HasDatabaseName("UX_EmailSuppression_Connection")
                  .HasFilter("\"ConnectionId\" IS NOT NULL");

            // The hot path: "is this address suppressed", asked once per recipient at expansion
            // and again per message at dispatch.
            entity.HasIndex(e => new { e.EmailAddressNormalized, e.ExpiresAt });

            entity.HasOne(e => e.Connection).WithMany()
                  .HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailSendQuota>(entity =>
        {
            entity.HasKey(e => e.ConnectionId);
            entity.HasOne(e => e.Connection).WithMany()
                  .HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Background job queue -------------------------------------------------------------

        modelBuilder.Entity<JobQueueEntry>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Payload).HasColumnType("jsonb");

            // The claim index. Filtered to the two statuses the claim query can actually return,
            // which keeps it small even once millions of completed rows have accumulated between
            // maintenance sweeps. Column order matches the query's ORDER BY.
            entity.HasIndex(e => new { e.QueueName, e.Priority, e.AvailableAt, e.Id })
                  .HasDatabaseName("IX_JobQueue_Claim")
                  .HasFilter("\"Status\" IN ('Pending', 'Leased')");

            // Serves the fairness window in the claim CTE.
            entity.HasIndex(e => new { e.QueueName, e.PartitionKey, e.Status })
                  .HasDatabaseName("IX_JobQueue_Partition");

            // Finds leases orphaned by a killed instance.
            entity.HasIndex(e => new { e.Status, e.LeaseExpiresAt })
                  .HasDatabaseName("IX_JobQueue_Lease");

            // De-duplication. Partial, because most queues do not supply a key and Postgres would
            // otherwise reject the second null-keyed row on some index configurations.
            entity.HasIndex(e => new { e.QueueName, e.IdempotencyKey })
                  .IsUnique()
                  .HasDatabaseName("UX_JobQueue_Idempotency")
                  .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        });

        modelBuilder.Entity<CampaignApprovalState>(entity =>
        {
            entity.HasKey(e => e.CampaignId);
            entity.HasIndex(e => e.ExternalReferenceId);
            entity.HasOne(e => e.Campaign).WithOne()
                  .HasForeignKey<CampaignApprovalState>(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(e => !e.Campaign.IsDeleted);
        });

        // MessageBot configurations
        modelBuilder.Entity<MessageBot>(entity =>
        {
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.TriggerKeyword);
            entity.HasOne(e => e.Connection).WithMany(c => c.MessageBots).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.SetNull);
        });

        // TemplateBot configurations
        modelBuilder.Entity<TemplateBot>(entity =>
        {
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.TriggerKeyword);
            entity.HasOne(e => e.Template).WithMany().HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Variables).WithOne(v => v.TemplateBot).HasForeignKey(v => v.TemplateBotId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Connection).WithMany(c => c.TemplateBots).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.SetNull);
        });

        // BotFlow configurations
        modelBuilder.Entity<BotFlow>(entity =>
        {
            entity.HasIndex(e => e.Name);
            entity.HasOne(e => e.Connection).WithMany(c => c.BotFlows).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<FlowNode>(entity =>
        {
            entity.ToTable("FlowNodes");
            entity.HasOne(e => e.Flow).WithMany().HasForeignKey(e => e.FlowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FlowEdge>(entity =>
        {
            entity.ToTable("FlowEdges");
            entity.HasOne(e => e.Flow).WithMany().HasForeignKey(e => e.FlowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConversationState>(entity =>
        {
            entity.ToTable("ConversationStates");
            entity.HasOne(e => e.Flow).WithMany().HasForeignKey(e => e.FlowId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.PhoneNumber);
        });

        modelBuilder.Entity<BotMessage>(entity =>
        {
            entity.ToTable("Messages");
            entity.HasOne(e => e.Flow).WithMany().HasForeignKey(e => e.FlowId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.PhoneNumber);
        });

        modelBuilder.Entity<ClientAiSetting>(entity =>
        {
            entity.ToTable("ClientAiSettings");
            entity.HasIndex(e => e.ClientId).IsUnique();
        });

        modelBuilder.Entity<AiSession>(entity =>
        {
            entity.ToTable("AiSessions");
            entity.HasIndex(e => e.PhoneNumber);
            entity.HasOne(e => e.MessageBot).WithMany().HasForeignKey(e => e.MessageBotId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BotSuppression>(entity =>
        {
            entity.ToTable("BotSuppressions");
            entity.Property(e => e.PhoneNumber).HasMaxLength(32).IsRequired();
            entity.Property(e => e.MatchedKeyword).HasMaxLength(100);
            entity.HasIndex(e => new { e.PhoneNumber, e.ResumeAt });
            entity.HasOne(e => e.Connection).WithMany().HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Normalized email event history -------------------------------------------

        modelBuilder.Entity<EmailEvent>(entity =>
        {
            entity.ToTable("EmailEvents");

            // Idempotency: the unique index on IdempotencyKey is the authoritative guard.
            // All INSERT attempts for the same event will hit this and fail with 23505,
            // which the event store catches and converts to a "duplicate, skip" result.
            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName("ix_email_events_idempotency_key");

            // Campaign-level reporting queries — filter to all events for a campaign
            entity.HasIndex(e => new { e.CampaignId, e.EventKind, e.OccurredAt })
                .HasDatabaseName("ix_email_events_campaign_kind_occurred");

            // Recipient-level reporting — all events for a specific recipient
            // The reconcile sweep finds campaigns with fresh events (an open arriving days later).
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.CampaignContactId, e.EventKind })
                .HasDatabaseName("ix_email_events_contact_kind");

            // MessageId correlation — find events by RFC 5322 Message-ID
            entity.HasIndex(e => e.MessageId)
                .HasDatabaseName("ix_email_events_message_id")
                .HasFilter("\"MessageId\" IS NOT NULL");

            entity.HasOne(e => e.Campaign)
                .WithMany()
                .HasForeignKey(e => e.CampaignId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CampaignContact)
                .WithMany()
                .HasForeignKey(e => e.CampaignContactId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.EventKind).HasConversion<string>();
            entity.Property(e => e.BounceType).HasConversion<string>();
        });

        // TrackingId index on CampaignContacts — used by the tracking pixel and click redirect
        modelBuilder.Entity<CampaignContact>(entity =>
        {
            entity.HasIndex(e => e.TrackingId)
                .HasDatabaseName("ix_campaign_contacts_tracking_id")
                .HasFilter("\"TrackingId\" IS NOT NULL")
                .IsUnique();
        });
    }

    // PendingModelChangesWarning is deliberately NOT suppressed any more. Suppressing it is how a
    // hand-written migration shipped without its snapshot update and nobody noticed: a model that
    // disagrees with the migrations must fail loudly at `dotnet ef` time.

    /// <summary>
    /// Receives field-level changes observed during a save, for the audit trail.
    ///
    /// A property rather than a constructor dependency because this context is built by
    /// <c>AddDbContextFactory</c>, which supplies only <c>DbContextOptions</c>. The scoped
    /// registration in Program.cs attaches the buffer; short-lived contexts created straight from
    /// the factory leave it null, which is correct — those exist to run concurrent reads.
    /// </summary>
    public Services.Interfaces.IAuditChangeBuffer? AuditChangeSink { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);
        var now = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Properties.Any(p => p.Metadata.Name == "CreatedAt"))
                {
                    entry.Property("CreatedAt").CurrentValue = now;
                }
            }

            if (entry.Properties.Any(p => p.Metadata.Name == "UpdatedAt"))
            {
                entry.Property("UpdatedAt").CurrentValue = now;
            }
        }

        // Snapshot before the save: this is the only point where OriginalValues still holds the
        // "before" state and deleted rows are still tracked. Publishing waits until after, so
        // inserted rows carry the key the database assigned rather than 0.
        //
        // Every part of this is best-effort. Auditing must never be the reason a customer
        // operation fails, which is the same position AuditService takes when it writes.
        List<(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry, Services.Interfaces.AuditEntityChange Change)>? pendingAudit = null;
        if (AuditChangeSink is not null)
        {
            try
            {
                pendingAudit = AuditChangeCapture.Capture(ChangeTracker);
            }
            catch
            {
                pendingAudit = null;
            }
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        if (pendingAudit is not null && AuditChangeSink is not null)
        {
            try
            {
                AuditChangeCapture.Publish(pendingAudit, AuditChangeSink);
            }
            catch
            {
                // The write already succeeded; losing its diff must not undo that.
            }
        }

        return result;
    }
}
