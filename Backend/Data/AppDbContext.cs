using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Contact> Contacts { get; set; } = null!;
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

    // Authentication & RBAC
    public DbSet<AppUser> AppUsers { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<Permission> Permissions { get; set; } = null!;
    public DbSet<RolePermission> RolePermissions { get; set; } = null!;
    public DbSet<UserPermission> UserPermissions { get; set; } = null!;

    // Audit trail
    public DbSet<LoginAttempt> LoginAttempts { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    // Setup lookups
    public DbSet<ContactStatusLookup> ContactStatuses { get; set; } = null!;
    public DbSet<ContactSourceLookup> ContactSources { get; set; } = null!;
    public DbSet<ContactTypeLookup> ContactTypes { get; set; } = null!;
    public DbSet<Language> Languages { get; set; } = null!;
    public DbSet<Translation> Translations { get; set; } = null!;
    public DbSet<AiPrompt> AiPrompts { get; set; } = null!;
    public DbSet<CannedReply> CannedReplies { get; set; } = null!;
    public DbSet<EmailTemplate> EmailTemplates { get; set; } = null!;
    public DbSet<MessageActivityLog> MessageActivityLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- Authentication & RBAC ------------------------------------------------
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("AppUsers");
            entity.HasIndex(e => e.Email).IsUnique();
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
        });

        // ---- Setup lookups --------------------------------------------------------
        // Deliberately no foreign key from Contact to these tables. Contact.Status/Source keep
        // storing the lookup's immutable Value string, which means this migration adds tables
        // without touching a single existing contact row.
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

        modelBuilder.Entity<MessageActivityLog>(entity =>
        {
            entity.ToTable("MessageActivityLogs");
            // The list is always newest-first and usually filtered by category.
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.ContactId);
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

        // DepartmentConnection configuration (future permissions)
        modelBuilder.Entity<DepartmentConnection>(entity =>
        {
            entity.HasIndex(e => new { e.DepartmentId, e.ConnectionId }).IsUnique();
            entity.HasOne(e => e.Connection).WithMany().HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        // UserConnection configuration (future permissions)
        modelBuilder.Entity<UserConnection>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.ConnectionId }).IsUnique();
            entity.HasOne(e => e.Connection).WithMany().HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.Cascade);
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
        });

        // Contact configurations
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasIndex(e => e.Phone).IsUnique();
            entity.HasIndex(e => e.CreatedAt);
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
            entity.HasQueryFilter(e => !e.IsDeleted);
            // RelationType is now a plain string (comma-joined ContactType names), not an
            // enum, so no HasConversion<string>() is needed here anymore — the column
            // stays VARCHAR(50), unchanged.
            entity.Property(e => e.RelationType).HasMaxLength(50);
            entity.Property(e => e.ScheduleType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasMany(e => e.Variables).WithOne(v => v.Campaign).HasForeignKey(v => v.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Connection).WithMany(c => c.Campaigns).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.SetNull);
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
            entity.HasIndex(e => e.WhatsAppMessageId);
            entity.HasIndex(e => new { e.CampaignId, e.ContactId }).IsUnique();
            entity.HasIndex(e => new { e.SentAt, e.Status });
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

        // Chat conversations: UNIQUE ON {ContactId, ConnectionId} so one contact can have conversations across multiple connections!
        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.HasIndex(e => new { e.ContactId, e.ConnectionId }).IsUnique();
            entity.HasIndex(e => e.LastMessageAt);
            entity.HasOne(e => e.Contact).WithMany().HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.WabaPhoneNumber).WithMany().HasForeignKey(e => e.WabaPhoneNumberId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Connection).WithMany(c => c.ChatConversations).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.SetNull);
        });

        // Chat messages
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasIndex(e => e.WhatsAppMessageId);
            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt });
            entity.HasIndex(e => e.CampaignContactId);
            entity.Property(e => e.Direction).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasOne(e => e.Conversation).WithMany(c => c.Messages).HasForeignKey(e => e.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Contact).WithMany().HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Campaign).WithMany().HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.CampaignContact).WithMany().HasForeignKey(e => e.CampaignContactId).OnDelete(DeleteBehavior.SetNull);
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
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
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

        return base.SaveChangesAsync(cancellationToken);
    }
}
