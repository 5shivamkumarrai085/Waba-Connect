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

    // WABA Configuration
    public DbSet<WabaConfiguration> WabaConfigurations { get; set; } = null!;
    public DbSet<WabaPhoneNumber> WabaPhoneNumbers { get; set; } = null!;
    public DbSet<Business> Businesses { get; set; } = null!;
    public DbSet<HealthLog> HealthLogs { get; set; } = null!;
    public DbSet<MessageBot> MessageBots { get; set; } = null!;
    public DbSet<TemplateBot> TemplateBots { get; set; } = null!;
    public DbSet<TemplateBotVariable> TemplateBotVariables { get; set; } = null!;
    public DbSet<BotFlow> BotFlows { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Contact configurations
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasIndex(e => e.Phone).IsUnique();
            entity.HasQueryFilter(e => e.IsActive);
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Source).HasConversion<string>().HasMaxLength(50);
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
            entity.Property(e => e.RelationType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ScheduleType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasMany(e => e.Variables).WithOne(v => v.Campaign).HasForeignKey(v => v.CampaignId).OnDelete(DeleteBehavior.Cascade);
        });

        // CampaignContact
        modelBuilder.Entity<CampaignContact>(entity =>
        {
            entity.HasIndex(e => e.WhatsAppMessageId);
            entity.HasIndex(e => new { e.CampaignId, e.ContactId }).IsUnique();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasOne(e => e.Campaign).WithMany(c => c.CampaignContacts).HasForeignKey(e => e.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Contact).WithMany(c => c.CampaignContacts).HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Restrict);
        });

        // Chat conversations
        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.HasIndex(e => e.ContactId).IsUnique();
            entity.HasIndex(e => e.LastMessageAt);
            entity.HasOne(e => e.Contact).WithMany().HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.WabaPhoneNumber).WithMany().HasForeignKey(e => e.WabaPhoneNumberId).OnDelete(DeleteBehavior.SetNull);
        });

        // Chat messages
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasIndex(e => e.WhatsAppMessageId);
            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt });
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
        });

        // TemplateBot configurations
        modelBuilder.Entity<TemplateBot>(entity =>
        {
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.TriggerKeyword);
            entity.HasOne(e => e.Template).WithMany().HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Variables).WithOne(v => v.TemplateBot).HasForeignKey(v => v.TemplateBotId).OnDelete(DeleteBehavior.Cascade);
        });

        // BotFlow configurations
        modelBuilder.Entity<BotFlow>(entity =>
        {
            entity.HasIndex(e => e.Name);
        });
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
