using System.Text.RegularExpressions;
using DnsClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>One pre-flight finding. Level is "pass", "warn" or "fail"; only "fail" stops a send.</summary>
public sealed record PrecheckItem(string Key, string Level, string Title, string Detail);

public sealed class PrecheckRequest
{
    public string Channel { get; set; } = "Email";
    public int? EmailTemplateId { get; set; }
    public int? SenderIdentityId { get; set; }
    public string? SubjectOverride { get; set; }
    public int? TemplateId { get; set; }
    public bool IsTransactional { get; set; }

    /// <summary>Campaign variable names the operator filled in, so their fields are not flagged.</summary>
    public List<string>? VariableNames { get; set; }
}

public sealed class ProofSendRequest
{
    public int EmailTemplateId { get; set; }
    public int SenderIdentityId { get; set; }
    public string? SubjectOverride { get; set; }
    public List<string> ToAddresses { get; set; } = [];
    public Dictionary<string, string?>? Values { get; set; }
}

/// <summary>
/// Pre-flight checks before an email campaign sends: the sender domain's DNS (SPF, DKIM, DMARC,
/// MX) and the content itself. Everything runs locally — the message is never sent to a
/// third-party scoring service.
/// </summary>
public interface IDeliverabilityService
{
    Task<IReadOnlyList<PrecheckItem>> PrecheckAsync(PrecheckRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PrecheckItem>> CheckDomainAsync(string domain, CancellationToken ct = default);
    IReadOnlyList<PrecheckItem> LintContent(string? subject, string? html, bool isTransactional, IEnumerable<string>? extraKnownFields = null);

    /// <summary>Sends the rendered campaign (sample values) to up to five proof addresses.</summary>
    Task<int> SendProofAsync(ProofSendRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class DeliverabilityService : IDeliverabilityService
{
    public const int GmailClipBytes = 102 * 1024;

    private static readonly string[] DefaultSpamPhrases =
    [
        "100% free", "act now", "apply now", "be your own boss", "buy now", "cash bonus", "click here",
        "congratulations", "double your", "earn extra cash", "free gift", "guaranteed", "limited time",
        "lowest price", "no credit check", "no obligation", "once in a lifetime", "risk-free", "urgent",
        "winner", "you have been selected", "100% guaranteed", "claim your", "exclusive deal"
    ];

    private static readonly string[] Shorteners = ["bit.ly", "tinyurl.com", "goo.gl", "t.co", "ow.ly", "is.gd", "buff.ly", "rebrand.ly", "cutt.ly", "shorturl.at"];

    private static readonly string[] DefaultDkimSelectors = ["default", "selector1", "selector2", "google", "k1", "mail", "dkim", "s1", "s2", "smtp"];

    /// <summary>Merge fields the dispatcher fills from the contact and sender (see EmailDispatchWorker).</summary>
    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "user_name", "contact_name", "first_name", "email", "phone", "contact_phone",
        "company", "company_name", "city", "country", "site_name", "unsubscribe_url", "preferences_url"
    };

    private readonly AppDbContext _db;
    private readonly ILookupClient _dns;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly IEmailTemplateService _templates;
    private readonly IEmailProviderFactory _providers;
    private readonly IMimeMessageBuilder _mime;
    private readonly IAuditService _audit;
    private readonly IPublicEndpointProbe _publicEndpoint;

    public DeliverabilityService(
        AppDbContext db, ILookupClient dns, IMemoryCache cache, IConfiguration configuration,
        IEmailTemplateService templates, IEmailProviderFactory providers, IMimeMessageBuilder mime, IAuditService audit,
        IPublicEndpointProbe publicEndpoint)
    {
        _publicEndpoint = publicEndpoint;
        _db = db;
        _dns = dns;
        _cache = cache;
        _configuration = configuration;
        _templates = templates;
        _providers = providers;
        _mime = mime;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PrecheckItem>> PrecheckAsync(PrecheckRequest request, CancellationToken ct = default)
    {
        var items = new List<PrecheckItem>();

        if (!request.Channel.Equals("Email", StringComparison.OrdinalIgnoreCase))
        {
            var template = request.TemplateId is { } tid
                ? await _db.Templates.AsNoTracking().Where(t => t.Id == tid).Select(t => new { t.Name, t.Status }).FirstOrDefaultAsync(ct)
                : null;
            items.Add(template is null
                ? new("wa-template", "fail", "WhatsApp template", "Choose an approved template.")
                : template.Status == TemplateStatus.Approved
                    ? new("wa-template", "pass", "WhatsApp template", $"\"{template.Name}\" is approved by Meta.")
                    : new("wa-template", "fail", "WhatsApp template", $"\"{template.Name}\" is {template.Status}; only approved templates can be sent."));
            return items;
        }

        var emailTemplate = request.EmailTemplateId is { } eid
            ? await _db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == eid, ct)
            : null;
        if (emailTemplate is null)
        {
            items.Add(new("template", "fail", "Email template", "Choose an email template."));
            return items;
        }

        var subject = string.IsNullOrWhiteSpace(request.SubjectOverride) ? emailTemplate.Subject : request.SubjectOverride;
        items.AddRange(LintContent(subject, emailTemplate.BodyHtml, request.IsTransactional, request.VariableNames));

        // Opens, clicks and unsubscribes only count when recipients' mail apps can reach this
        // server. A localhost or dead-tunnel address loses every one of them without an error.
        var endpoint = await _publicEndpoint.CheckAsync(ct);
        items.Add(endpoint.Reachable
            ? new("tracking", "pass", "Tracking address", $"Opens, clicks and unsubscribes reach this server at {endpoint.BaseUrl}.")
            : new("tracking", "warn", "Tracking address",
                $"Opens, clicks and unsubscribes won't be counted: {(string.IsNullOrEmpty(endpoint.BaseUrl) ? "the public address" : endpoint.BaseUrl)} — {endpoint.Description}."));

        var links = EmailDispatchWorker.CountTrackableLinks(emailTemplate.BodyHtml);
        items.Add(new("click-tracking", "pass", "Click tracking", links == 0
            ? "This message has no links, so the Links report will stay empty."
            : $"{links} link{(links == 1 ? "" : "s")} will be tracked."));

        var sender = request.SenderIdentityId is { } sid
            ? await _db.EmailSenderIdentities.AsNoTracking()
                .Where(s => s.Id == sid)
                .Select(s => new { s.EmailAddress, Provider = s.EmailConfiguration.Provider })
                .FirstOrDefaultAsync(ct)
            : null;
        if (sender is null)
        {
            items.Add(new("sender", "fail", "Sender", "Choose the sender email for this campaign."));
            return items;
        }

        var domain = sender.EmailAddress.Contains('@') ? sender.EmailAddress[(sender.EmailAddress.IndexOf('@') + 1)..].Trim().ToLowerInvariant() : null;
        if (string.IsNullOrWhiteSpace(domain))
            items.Add(new("sender", "fail", "Sender", $"\"{sender.EmailAddress}\" is not a valid address."));
        else
            items.AddRange(await CheckDomainAsync(domain, ct));

        return items;
    }

    public async Task<IReadOnlyList<PrecheckItem>> CheckDomainAsync(string domain, CancellationToken ct = default)
    {
        var key = $"deliverability:dns:{domain}";
        if (_cache.TryGetValue(key, out IReadOnlyList<PrecheckItem>? cached) && cached is not null) return cached;

        var items = new List<PrecheckItem>();

        var txt = await TxtAsync(domain, ct);
        if (txt is null)
        {
            items.Add(new("dns", "warn", "DNS", $"The DNS for {domain} could not be read. The checks below were skipped; try again shortly."));
            return items;
        }

        // SPF
        var spf = txt.Where(r => r.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase)).ToList();
        if (spf.Count == 0)
            items.Add(new("spf", "warn", "SPF", $"{domain} has no SPF record. Receivers cannot tell which servers may send its mail, so it is more likely to be filtered."));
        else if (spf.Count > 1)
            items.Add(new("spf", "warn", "SPF", $"{domain} has {spf.Count} SPF records. Only one is allowed; receivers treat this as an SPF error."));
        else
            items.Add(new("spf", "pass", "SPF", spf[0]));

        // DMARC
        var dmarc = (await TxtAsync($"_dmarc.{domain}", ct) ?? []).FirstOrDefault(r => r.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase));
        if (dmarc is null)
            items.Add(new("dmarc", "warn", "DMARC", $"{domain} has no DMARC policy. Gmail and Yahoo require one for bulk senders."));
        else if (Regex.IsMatch(dmarc, @"\bp\s*=\s*none\b", RegexOptions.IgnoreCase))
            items.Add(new("dmarc", "warn", "DMARC", "The DMARC policy is p=none (monitoring only); spoofed mail using this domain is not rejected."));
        else
            items.Add(new("dmarc", "pass", "DMARC", dmarc));

        // DKIM — the mail server signs; we look for its public key under the configured selectors.
        {
            var selectors = _configuration.GetSection("Email:Deliverability:DkimSelectors").Get<string[]>() is { Length: > 0 } configured
                ? configured
                : DefaultDkimSelectors;
            string? found = null;
            foreach (var selector in selectors)
            {
                var records = await TxtAsync($"{selector}._domainkey.{domain}", ct);
                if (records?.Any(r => r.Contains("p=", StringComparison.OrdinalIgnoreCase)) == true)
                {
                    found = selector;
                    break;
                }
            }
            items.Add(found is not null
                ? new("dkim", "pass", "DKIM", $"A DKIM key is published under the \"{found}\" selector.")
                : new("dkim", "warn", "DKIM",
                    $"No DKIM key was found under the usual selectors ({string.Join(", ", selectors)}). If your mail server signs with another selector, add it to Email:Deliverability:DkimSelectors."));
        }

        // MX — bounces and replies come back here.
        var mx = await MxAsync(domain, ct);
        items.Add(mx
            ? new("mx", "pass", "MX", $"{domain} can receive replies and bounces.")
            : new("mx", "warn", "MX", $"{domain} has no MX record, so replies and bounce reports cannot come back to it."));

        _cache.Set(key, (IReadOnlyList<PrecheckItem>)items, TimeSpan.FromMinutes(10));
        return items;
    }

    public IReadOnlyList<PrecheckItem> LintContent(string? subject, string? html, bool isTransactional, IEnumerable<string>? extraKnownFields = null)
    {
        var items = new List<PrecheckItem>();
        subject ??= string.Empty;
        html ??= string.Empty;

        if (string.IsNullOrWhiteSpace(subject)) items.Add(new("subject", "fail", "Subject", "The subject line is empty."));
        else if (subject.Length > 150) items.Add(new("subject", "warn", "Subject", $"The subject is {subject.Length} characters; most inboxes show about 60."));
        else if (subject.Count(char.IsLetter) >= 8 && subject.Where(char.IsLetter).All(char.IsUpper)) items.Add(new("subject", "warn", "Subject", "The subject is in capitals, which spam filters penalise."));
        else if (subject.Count(c => c == '!') >= 3) items.Add(new("subject", "warn", "Subject", "The subject has several exclamation marks, which spam filters penalise."));
        else items.Add(new("subject", "pass", "Subject", subject));

        var text = Regex.Replace(html, "<[^>]+>", " ");
        if (string.IsNullOrWhiteSpace(text) && !html.Contains("<img", StringComparison.OrdinalIgnoreCase))
            items.Add(new("body", "fail", "Body", "The message body is empty."));

        var phrases = (_configuration.GetSection("Email:Deliverability:SpamPhrases").Get<string[]>() is { Length: > 0 } configuredPhrases ? configuredPhrases : DefaultSpamPhrases)
            .Where(p => (subject + " " + text).Contains(p, StringComparison.OrdinalIgnoreCase))
            .ToList();
        items.Add(phrases.Count > 0
            ? new("phrases", "warn", "Wording", $"Contains phrases spam filters look for: {string.Join(", ", phrases.Select(p => $"\"{p}\""))}.")
            : new("phrases", "pass", "Wording", "No common spam phrases."));

        var links = Regex.Matches(html, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value).ToList();
        var shortened = links.Where(l => Shorteners.Any(s => Regex.IsMatch(l, $@"^https?://(www\.)?{Regex.Escape(s)}/", RegexOptions.IgnoreCase))).Distinct().ToList();
        if (shortened.Count > 0) items.Add(new("links", "warn", "Links", $"Uses link shorteners ({string.Join(", ", shortened.Take(3))}), which hide the destination and are widely filtered."));
        else if (links.Count > 25) items.Add(new("links", "warn", "Links", $"{links.Count} links; a message with this many is often filtered."));
        else items.Add(new("links", "pass", "Links", $"{links.Count} link(s)."));

        var images = Regex.Matches(html, "<img\\b", RegexOptions.IgnoreCase).Count;
        if (images > 0 && text.Trim().Length < 300)
            items.Add(new("images", "warn", "Images", "Mostly images with little text; image-only mail is commonly filtered and unreadable when images are blocked."));

        var bytes = System.Text.Encoding.UTF8.GetByteCount(html);
        if (bytes > GmailClipBytes)
            items.Add(new("size", "warn", "Size", $"The HTML is {bytes / 1024} KB; Gmail cuts messages over 102 KB and hides the rest (including the unsubscribe link)."));

        if (!isTransactional)
        {
            var hasOptOut = Regex.IsMatch(html, @"\{\{\s*(unsubscribe_url|preferences_url)\s*\}\}", RegexOptions.IgnoreCase);
            items.Add(hasOptOut
                ? new("unsubscribe", "pass", "Unsubscribe link", "The message has a visible unsubscribe link.")
                : new("unsubscribe", "warn", "Unsubscribe link", "No visible unsubscribe link. Add {{unsubscribe_url}} or {{preferences_url}} to the template; the unsubscribe header alone is not shown by every mail client."));
        }

        var known = new HashSet<string>(KnownFields, StringComparer.OrdinalIgnoreCase);
        foreach (var name in extraKnownFields ?? []) known.Add(name);
        var unknown = MergeFieldRenderer.Extract(subject + " " + html).Where(f => !known.Contains(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
            items.Add(new("fields", "warn", "Merge fields", $"Needs a value on the Variables step: {string.Join(", ", unknown.Select(f => "{{" + f + "}}"))}. Recipients without one are not sent."));

        return items;
    }

    public async Task<int> SendProofAsync(ProofSendRequest request, CancellationToken ct = default)
    {
        var addresses = request.ToAddresses.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (addresses.Count == 0) throw new ArgumentException("Add at least one address to send the proof to.");
        if (addresses.Count > Catalogs.CampaignFeatureCatalog.MaxProofAddresses)
            throw new ArgumentException($"Send a proof to at most {Catalogs.CampaignFeatureCatalog.MaxProofAddresses} addresses.");
        if (addresses.Any(a => !System.Net.Mail.MailAddress.TryCreate(a, out _))) throw new ArgumentException("One of the proof addresses is not valid.");

        var sender = await _db.EmailSenderIdentities.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SenderIdentityId, ct)
            ?? throw new KeyNotFoundException("Sender not found.");

        var preview = await _templates.PreviewAsync(request.EmailTemplateId, request.Values, useSampleData: true, ct);
        var subject = string.IsNullOrWhiteSpace(request.SubjectOverride) ? preview.Subject : request.SubjectOverride;

        var (provider, context) = await _providers.ResolveByConfigurationAsync(sender.EmailConfigurationId, ct);
        var domain = sender.EmailAddress.Contains('@') ? sender.EmailAddress.Split('@')[1] : "localhost";

        var sent = 0;
        foreach (var address in addresses)
        {
            var result = await provider.SendAsync(new EmailMessage
            {
                From = new EmailAddress(sender.EmailAddress, sender.DisplayName),
                ReplyTo = string.IsNullOrWhiteSpace(sender.ReplyTo) ? null : new EmailAddress(sender.ReplyTo),
                To = [new EmailAddress(address)],
                Subject = $"[Proof] {subject}",
                HtmlBody = preview.BodyHtml,
                TextBody = preview.TextBody,
                MessageId = _mime.NewMessageId(domain),
                Tags = new Dictionary<string, string> { ["purpose"] = "proof" },
            }, context, ct);
            if (result.Success) sent++;
        }

        await _audit.LogAsync("Campaign.ProofSent", "Data",
            $"Proof of email template #{request.EmailTemplateId} sent to {sent} of {addresses.Count} address(es).",
            "EmailTemplate", request.EmailTemplateId.ToString());
        return sent;
    }

    // ── DNS ────────────────────────────────────────────────────────────────────────────────

    private async Task<List<string>?> TxtAsync(string name, CancellationToken ct)
    {
        try
        {
            var result = await _dns.QueryAsync(name, QueryType.TXT, cancellationToken: ct);
            if (result.HasError && result.Header.ResponseCode != DnsHeaderResponseCode.NotExistentDomain) return null;
            return result.Answers.TxtRecords().Select(r => string.Concat(r.Text)).ToList();
        }
        catch (DnsResponseException)
        {
            return null;
        }
    }

    private async Task<bool> MxAsync(string domain, CancellationToken ct)
    {
        try
        {
            var result = await _dns.QueryAsync(domain, QueryType.MX, cancellationToken: ct);
            return result.Answers.MxRecords().Any();
        }
        catch (DnsResponseException)
        {
            return false;
        }
    }
}
