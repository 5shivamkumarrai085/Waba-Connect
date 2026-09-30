using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Campaigns;

public sealed record FollowUpInfo(
    int Id, string Condition, int DelayHours, string Action, string Channel, string? TemplateName, string? Tag,
    DateTime DueAt, string Status, int? ChildCampaignId, int? MatchedCount, string? Note);

/// <summary>Follow-up rules: validating and storing them with a campaign, and running them when due.</summary>
public interface IFollowUpService
{
    /// <summary>
    /// Validates follow-up rules against the catalog without writing anything, so a campaign is
    /// never saved with rules that would be rejected.
    /// </summary>
    Task<IReadOnlyList<FollowUpRule>> PrepareRulesAsync(MessageChannel campaignChannel, int? campaignConnectionId, IReadOnlyList<FollowUpRequest>? rules, CancellationToken ct = default);

    /// <summary>Stores prepared rules on a saved campaign and schedules them from its start time.</summary>
    Task AttachRulesAsync(Campaign campaign, IReadOnlyList<FollowUpRule> prepared, CancellationToken ct = default);
    Task<IReadOnlyList<FollowUpInfo>> GetForCampaignAsync(int campaignId, CancellationToken ct = default);

    /// <summary>Runs one due rule. Safe to call twice: only the caller that claims it runs it.</summary>
    Task RunAsync(int ruleId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class FollowUpService : IFollowUpService
{
    public static readonly string[] Conditions = Catalogs.CampaignFeatureCatalog.EmailFollowUpConditions
        .Concat(Catalogs.CampaignFeatureCatalog.WhatsAppFollowUpConditions).Select(c => c.Value).Distinct().ToArray();

    private readonly AppDbContext _db;
    private readonly IServiceProvider _services;
    private readonly IAuditService _audit;
    private readonly ILogger<FollowUpService> _logger;

    public FollowUpService(AppDbContext db, IServiceProvider services, IAuditService audit, ILogger<FollowUpService> logger)
    {
        _db = db;
        _services = services;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FollowUpRule>> PrepareRulesAsync(
        MessageChannel campaignChannel, int? campaignConnectionId, IReadOnlyList<FollowUpRequest>? rules, CancellationToken ct = default)
    {
        var prepared = new List<FollowUpRule>();
        if (rules is not { Count: > 0 }) return prepared;
        if (rules.Count > Catalogs.CampaignFeatureCatalog.MaxFollowUps)
            throw new ArgumentException($"A campaign can have at most {Catalogs.CampaignFeatureCatalog.MaxFollowUps} follow-ups.");
        var delayRange = Catalogs.CampaignFeatureCatalog.FollowUpDelayHours;
        var channelConditions = Catalogs.CampaignFeatureCatalog.FollowUpConditions(campaignChannel).Select(c => c.Value).ToHashSet();

        foreach (var (rule, index) in rules.Select((r, i) => (r, i + 1)))
        {
            var condition = Conditions.FirstOrDefault(c => c.Equals(rule.Condition?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Follow-up {index}: \"{rule.Condition}\" is not a condition.");
            if (condition is "NotOpened" or "NotClicked" or "Clicked" && campaignChannel != MessageChannel.Email)
                throw new ArgumentException($"Follow-up {index}: opens and clicks are tracked for email campaigns only.");
            if (condition == "NotRead" && campaignChannel != MessageChannel.WhatsApp)
                throw new ArgumentException($"Follow-up {index}: read receipts exist for WhatsApp campaigns only.");
            if (!channelConditions.Contains(condition))
                throw new ArgumentException($"Follow-up {index}: \"{condition}\" does not apply to {campaignChannel} campaigns.");
            if (rule.DelayHours < delayRange.Min || rule.DelayHours > delayRange.Max)
                throw new ArgumentException($"Follow-up {index}: the delay must be between {delayRange.Min} and {delayRange.Max} hours.");

            var action = Catalogs.CampaignFeatureCatalog.FollowUpActions
                .FirstOrDefault(a => a.Value.Equals(rule.Action?.Trim(), StringComparison.OrdinalIgnoreCase))?.Value ?? "send";
            var channel = Enum.TryParse<MessageChannel>(rule.Channel, true, out var parsed) ? parsed : campaignChannel;

            if (action == "tag")
            {
                if (string.IsNullOrWhiteSpace(rule.Tag) || rule.Tag.Contains(','))
                    throw new ArgumentException($"Follow-up {index}: give a single tag to add.");
            }
            else if (channel == MessageChannel.Email)
            {
                if (rule.EmailTemplateId is not { } et || !await _db.EmailTemplates.AnyAsync(t => t.Id == et && t.IsEnabled, ct))
                    throw new ArgumentException($"Follow-up {index}: choose an enabled email template.");
                if (rule.SenderIdentityId is not { } sid || !await _db.EmailSenderIdentities.AnyAsync(s => s.Id == sid, ct))
                    throw new ArgumentException($"Follow-up {index}: choose the sender email.");
            }
            else
            {
                if (rule.TemplateId is not { } wt || !await _db.Templates.AnyAsync(t => t.Id == wt && t.Status == TemplateStatus.Approved, ct))
                    throw new ArgumentException($"Follow-up {index}: choose an approved WhatsApp template.");
                if ((rule.ConnectionId ?? (campaignChannel == MessageChannel.WhatsApp ? campaignConnectionId : null)) is null)
                    throw new ArgumentException($"Follow-up {index}: choose the WhatsApp connection to send from.");
            }

            prepared.Add(new FollowUpRule
            {
                Condition = condition,
                DelayHours = rule.DelayHours,
                Action = action,
                Channel = channel,
                TemplateId = channel == MessageChannel.WhatsApp ? rule.TemplateId : null,
                EmailTemplateId = channel == MessageChannel.Email ? rule.EmailTemplateId : null,
                SenderIdentityId = channel == MessageChannel.Email ? rule.SenderIdentityId : null,
                ConnectionId = channel == MessageChannel.WhatsApp ? rule.ConnectionId ?? (campaignChannel == MessageChannel.WhatsApp ? campaignConnectionId : null) : null,
                SubjectOverride = string.IsNullOrWhiteSpace(rule.SubjectOverride) ? null : rule.SubjectOverride.Trim(),
                Tag = action == "tag" ? rule.Tag!.Trim() : null
            });
        }

        return prepared;
    }

    public async Task AttachRulesAsync(Campaign campaign, IReadOnlyList<FollowUpRule> prepared, CancellationToken ct = default)
    {
        if (prepared.Count == 0) return;
        var start = campaign.ScheduledAt is { } at && at > DateTime.UtcNow ? at : DateTime.UtcNow;
        foreach (var rule in prepared)
        {
            rule.CampaignId = campaign.Id;
            rule.DueAt = start.AddHours(rule.DelayHours);
            _db.FollowUpRules.Add(rule);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FollowUpInfo>> GetForCampaignAsync(int campaignId, CancellationToken ct = default) =>
        await _db.FollowUpRules.AsNoTracking()
            .Where(r => r.CampaignId == campaignId)
            .OrderBy(r => r.DueAt)
            .Select(r => new FollowUpInfo(
                r.Id, r.Condition, r.DelayHours, r.Action, r.Channel.ToString(),
                r.TemplateId != null ? _db.Templates.Where(t => t.Id == r.TemplateId).Select(t => t.Name).FirstOrDefault()
                    : r.EmailTemplateId != null ? _db.EmailTemplates.Where(t => t.Id == r.EmailTemplateId).Select(t => t.Name).FirstOrDefault() : null,
                r.Tag, r.DueAt, r.Status, r.ChildCampaignId, r.MatchedCount, r.Note))
            .ToListAsync(ct);

    public async Task RunAsync(int ruleId, CancellationToken ct = default)
    {
        // Claim: only one worker (or instance) runs a rule.
        var claimed = await _db.FollowUpRules
            .Where(r => r.Id == ruleId && r.Status == "Pending")
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, "Running"), ct);
        if (claimed == 0) return;

        var rule = await _db.FollowUpRules.Include(r => r.Campaign).FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule is null)
        {
            // The parent campaign was deleted between the claim and now.
            await _db.FollowUpRules.IgnoreQueryFilters().Where(r => r.Id == ruleId)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, "Cancelled"), ct);
            return;
        }
        try
        {
            var matched = await MatchingContactIdsAsync(rule, ct);
            rule.MatchedCount = matched.Count;

            if (matched.Count == 0)
            {
                rule.Note = "No recipients matched.";
            }
            else if (rule.Action == "tag")
            {
                await AddTagAsync(matched, rule.Tag!, ct);
                rule.Note = $"Tagged {matched.Count} contact(s) \"{rule.Tag}\".";
            }
            else
            {
                var campaigns = _services.GetRequiredService<ICampaignService>();
                var parent = rule.Campaign;
                var child = await campaigns.CreateAsync(new CreateCampaignRequest
                {
                    Name = Truncate($"{parent.Name} · follow-up ({Describe(rule.Condition)})", 200),
                    Channel = rule.Channel.ToString(),
                    TemplateId = rule.TemplateId ?? 0,
                    EmailTemplateId = rule.EmailTemplateId,
                    SenderIdentityId = rule.SenderIdentityId,
                    SubjectOverride = rule.SubjectOverride,
                    ConnectionId = rule.ConnectionId,
                    RelationType = parent.RelationType,
                    ScheduleType = nameof(ScheduleType.Immediate),
                    ContactIds = matched,
                    Topic = parent.Topic,
                    IsTransactional = parent.IsTransactional
                });

                await _db.Campaigns.IgnoreQueryFilters().Where(c => c.Id == child.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(c => c.ParentCampaignId, rule.CampaignId), ct);
                rule.ChildCampaignId = child.Id;
                rule.Note = $"Created campaign #{child.Id} for {matched.Count} recipient(s).";
            }

            rule.Status = "Done";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            rule.Status = "Failed";
            rule.Note = Truncate(ex.Message, 1000);
            _logger.LogWarning(ex, "Follow-up rule {RuleId} of campaign {CampaignId} failed.", rule.Id, rule.CampaignId);
        }

        rule.ExecutedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);

        await _audit.LogAsync("Campaign.FollowUpRun", "Data",
            $"Follow-up \"{Describe(rule.Condition)}\" of campaign #{rule.CampaignId}: {rule.Note}",
            "Campaign", rule.CampaignId.ToString());
    }

    private async Task<List<int>> MatchingContactIdsAsync(FollowUpRule rule, CancellationToken ct)
    {
        var q = _db.CampaignContacts.IgnoreQueryFilters().AsNoTracking().Where(cc => cc.CampaignId == rule.CampaignId);
        q = rule.Condition switch
        {
            "NotOpened" => q.Where(cc => cc.SentAt != null && cc.OpenedAt == null && cc.Status != MessageStatus.Bounced),
            "NotClicked" => q.Where(cc => cc.SentAt != null && cc.ClickedAt == null && cc.Status != MessageStatus.Bounced),
            "Clicked" => q.Where(cc => cc.ClickedAt != null),
            "Replied" => q.Where(cc => cc.RepliedAt != null),
            "NotReplied" => q.Where(cc => cc.SentAt != null && cc.RepliedAt == null && cc.Status != MessageStatus.Bounced),
            "NotRead" => q.Where(cc => cc.SentAt != null && cc.ReadAt == null && cc.Status != MessageStatus.Read),
            "Failed" => q.Where(cc => cc.Status == MessageStatus.Failed),
            _ => q.Where(_ => false)
        };
        return await q.Select(cc => cc.ContactId).Distinct().ToListAsync(ct);
    }

    private async Task AddTagAsync(List<int> contactIds, string tag, CancellationToken ct)
    {
        foreach (var chunk in contactIds.Chunk(500))
        {
            var contacts = await _db.Contacts.Where(c => chunk.Contains(c.Id)).ToListAsync(ct);
            foreach (var contact in contacts)
            {
                var tags = (contact.Tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) tags.Add(tag);
                contact.Tags = string.Join(", ", tags);
            }
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }
    }

    private static string Describe(string condition) => condition switch
    {
        "NotOpened" => "did not open",
        "NotClicked" => "did not click",
        "Clicked" => "clicked",
        "Replied" => "replied",
        "NotReplied" => "did not reply",
        "NotRead" => "did not read",
        "Failed" => "failed",
        _ => condition
    };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Runs follow-up rules when they come due.</summary>
public sealed class FollowUpWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FollowUpWorker> _logger;

    public FollowUpWorker(IServiceScopeFactory scopeFactory, ILogger<FollowUpWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<int> due;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTime.UtcNow;
                    // The parent must have gone out: a follow-up to a cancelled or unsent campaign
                    // would target nobody meaningful.
                    due = await db.FollowUpRules.AsNoTracking()
                        .Where(r => r.Status == "Pending" && r.DueAt <= now
                                 && (r.Campaign.Status == CampaignStatus.Sent || r.Campaign.Status == CampaignStatus.PartiallyFailed
                                     || r.Campaign.Status == CampaignStatus.Sending))
                        .OrderBy(r => r.DueAt)
                        .Select(r => r.Id)
                        .Take(20)
                        .ToListAsync(stoppingToken);
                }

                foreach (var id in due)
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IFollowUpService>().RunAsync(id, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Follow-up sweep failed; retrying.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
