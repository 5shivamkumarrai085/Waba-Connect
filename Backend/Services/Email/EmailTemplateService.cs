using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Email;

/// <inheritdoc />
public class EmailTemplateService : IEmailTemplateService
{
    /// <summary>
    /// Stand-in values for the preview pane, keyed by the placeholder names the seeded templates
    /// and the campaign UI actually use.
    ///
    /// <para>
    /// Only a preview aid — nothing in the send path reads this. Anything unrecognised falls back
    /// to a bracketed echo of the field name, so an unknown placeholder renders as
    /// <c>[order_total]</c> rather than silently disappearing.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> SampleValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "Shivam",
        ["user_name"] = "Shivam",
        ["first_name"] = "Shivam",
        ["last_name"] = "Kumar Rai",
        ["contact_name"] = "Shivam Kumar Rai",
        ["email"] = "shivam@example.com",
        ["contact_phone"] = "+91 93415 95395",
        ["phone"] = "+91 93415 95395",
        ["company"] = "OmniConnect",
        ["company_name"] = "OmniConnect",
        ["site_name"] = "OmniConnect",
        ["country"] = "India",
        ["city"] = "Bengaluru",
        ["assigned_by"] = "Super Admin",
        ["confirmation_link"] = "https://example.com/confirm/sample-token",
        ["reset_link"] = "https://example.com/reset/sample-token",
        ["contact_link"] = "https://example.com/contacts/1",
        ["date"] = "22 Sep 2026"
    };

    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public EmailTemplateService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public async Task<List<EmailTemplateResponse>> GetAllAsync(bool enabledOnly = false, CancellationToken ct = default)
    {
        var query = _dbContext.EmailTemplates.AsNoTracking();
        if (enabledOnly) query = query.Where(t => t.IsEnabled);

        var templates = await query.OrderBy(t => t.Id).ToListAsync(ct);
        return templates.Select(MapToResponse).ToList();
    }

    public async Task<EmailTemplateResponse> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var template = await _dbContext.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("Email template not found.");

        return MapToResponse(template);
    }

    public async Task<EmailTemplateResponse?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        var template = await _dbContext.EmailTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Key.ToLower() == key.ToLower(), ct);

        return template is null ? null : MapToResponse(template);
    }

    public async Task<EmailTemplateResponse> CreateAsync(
        SaveEmailTemplateRequest request,
        CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(request.Key)
            ? DeriveKey(request.Name)
            : NormalizeKey(request.Key);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException(
                "A template key could not be derived from the name. Provide one explicitly using "
              + "lowercase letters, numbers and underscores.");
        }

        // Key is uniquely indexed. Checked here so the conflict is reported as a conflict rather
        // than surfacing as a database constraint violation.
        if (await _dbContext.EmailTemplates.AnyAsync(t => t.Key.ToLower() == key.ToLower(), ct))
        {
            throw new InvalidOperationException($"An email template with the key \"{key}\" already exists.");
        }

        var template = new EmailTemplate
        {
            Key = key,
            Name = request.Name.Trim(),
            Subject = request.Subject.Trim(),
            BodyHtml = SanitizeBody(request.BodyHtml),
            TextBody = Trim(request.TextBody),
            PreheaderText = Trim(request.PreheaderText),
            Language = Trim(request.Language),
            Description = Trim(request.Description),
            AvailableVariables = Trim(request.AvailableVariables),
            IsEnabled = request.IsEnabled,

            // Only the seeder creates system templates. Anything authored through the API is an
            // ordinary template, and therefore deletable.
            IsSystem = false
        };

        _dbContext.EmailTemplates.Add(template);
        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "EmailTemplate.Created", "Settings",
            $"Created email template '{template.Name}' ({template.Key}).",
            nameof(EmailTemplate), template.Id.ToString());

        return MapToResponse(template);
    }

    public async Task<EmailTemplateResponse> UpdateAsync(
        int id,
        SaveEmailTemplateRequest request,
        CancellationToken ct = default)
    {
        var template = await _dbContext.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("Email template not found.");

        // Key is deliberately not updatable. System notifications resolve templates by key, and
        // existing campaigns were authored against it; changing it would break those silently
        // rather than visibly.
        template.Name = request.Name.Trim();
        template.Subject = request.Subject.Trim();
        template.BodyHtml = SanitizeBody(request.BodyHtml);
        template.TextBody = Trim(request.TextBody);
        template.PreheaderText = Trim(request.PreheaderText);
        template.Language = Trim(request.Language);
        template.Description = Trim(request.Description);
        template.AvailableVariables = Trim(request.AvailableVariables);
        template.IsEnabled = request.IsEnabled;
        template.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "EmailTemplate.Updated", "Settings",
            $"Updated email template '{template.Name}'.",
            nameof(EmailTemplate), id.ToString());

        return MapToResponse(template);
    }

    public async Task<EmailTemplateResponse> ToggleAsync(int id, CancellationToken ct = default)
    {
        var template = await _dbContext.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("Email template not found.");

        // Disabling a template a scheduled campaign is about to render would make that campaign
        // fail at dispatch. Refused up front, where it can be explained.
        if (template.IsEnabled)
        {
            var pendingCampaigns = await _dbContext.Campaigns
                .Where(c => c.EmailTemplateId == id
                         && (c.Status == Models.Enums.CampaignStatus.Scheduled
                          || c.Status == Models.Enums.CampaignStatus.Sending))
                .CountAsync(ct);

            if (pendingCampaigns > 0)
            {
                throw new InvalidOperationException(
                    $"This template is used by {pendingCampaigns} scheduled or sending campaign(s) and cannot be "
                  + "disabled yet. Cancel or let those campaigns finish first.");
            }
        }

        template.IsEnabled = !template.IsEnabled;
        template.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            template.IsEnabled ? "EmailTemplate.Enabled" : "EmailTemplate.Disabled", "Settings",
            $"{(template.IsEnabled ? "Enabled" : "Disabled")} email template '{template.Name}'.",
            nameof(EmailTemplate), id.ToString());

        return MapToResponse(template);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var template = await _dbContext.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("Email template not found.");

        if (template.IsSystem)
        {
            throw new InvalidOperationException(
                $"'{template.Name}' is a system template that other features resolve by key, and cannot be "
              + "deleted. Disable it instead.");
        }

        // Also enforced by the Restrict foreign key, but checked here so the answer is an
        // explanation rather than a constraint violation. IgnoreQueryFilters because a
        // soft-deleted campaign still needs to be able to explain what it sent.
        var campaignCount = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .CountAsync(c => c.EmailTemplateId == id, ct);

        if (campaignCount > 0)
        {
            throw new InvalidOperationException(
                $"This template has been used by {campaignCount} campaign(s) and cannot be deleted — those "
              + "campaigns would no longer be able to show what they sent. Disable it instead.");
        }

        var name = template.Name;
        _dbContext.EmailTemplates.Remove(template);
        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "EmailTemplate.Deleted", "Settings",
            $"Deleted email template '{name}'.",
            nameof(EmailTemplate), id.ToString());
    }

    public async Task<EmailTemplatePreviewResponse> PreviewAsync(
        int id,
        IReadOnlyDictionary<string, string?>? values = null,
        bool useSampleData = false,
        CancellationToken ct = default)
    {
        var template = await _dbContext.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("Email template not found.");

        // Every field the template references gets something: the caller's value, then —
        // only when sample data was asked for — a plausible stand-in, then a bracketed echo of
        // the name. The echo is deliberately not mistakable for real content.
        var detected = MergeFieldRenderer.Extract(template.Subject + " " + template.BodyHtml);

        var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Tracked as they are found rather than recomputed from the rendered output: once a
        // placeholder has been replaced by "[name]" it is no longer a placeholder, so re-extracting
        // could never have found it, and UnresolvedVariables was always coming back empty.
        var unfilled = new List<string>();

        foreach (var field in detected)
        {
            if (values is not null && TryGetIgnoreCase(values, field, out var supplied)
                && !string.IsNullOrWhiteSpace(supplied))
            {
                resolved[field] = supplied;
                continue;
            }

            unfilled.Add(field);

            resolved[field] = useSampleData && SampleValues.TryGetValue(field, out var sample)
                ? sample
                : $"[{field}]";
        }

        var subject = MergeFieldRenderer.Render(template.Subject, resolved);
        var body = MergeFieldRenderer.Render(template.BodyHtml, resolved);
        var text = MergeFieldRenderer.Render(template.TextBody, resolved);

        return new EmailTemplatePreviewResponse
        {
            Subject = subject,
            BodyHtml = body,
            TextBody = string.IsNullOrWhiteSpace(text) ? null : text,

            // What the caller did not supply. This is what lets the campaign wizard warn before
            // sending, instead of the operator discovering it in the delivered mail.
            UnresolvedVariables = unfilled
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static EmailTemplateResponse MapToResponse(EmailTemplate template) => new()
    {
        Id = template.Id,
        Key = template.Key,
        Name = template.Name,
        Subject = template.Subject,
        BodyHtml = template.BodyHtml,
        TextBody = template.TextBody,
        PreheaderText = template.PreheaderText,
        Language = template.Language,
        Description = template.Description,
        IsEnabled = template.IsEnabled,
        IsSystem = template.IsSystem,
        AvailableVariables = template.AvailableVariables,

        // Read from the content rather than the declared list, so the campaign wizard's variable
        // inputs stay correct after somebody edits a body.
        DetectedVariables = MergeFieldRenderer
            .Extract(template.Subject + " " + template.BodyHtml)
            .ToList(),

        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt
    };

    /// <summary>
    /// Allowlist sanitization, not the global stripper.
    ///
    /// <para>
    /// The global SanitizeInputFilter removes every tag, which would reduce a formatted template
    /// to plain text — which is why the DTO carries [SkipSanitization] and this runs instead.
    /// Skipping sanitization altogether is not an option: template bodies are operator input that
    /// is later rendered in the preview pane and the inbox.
    /// </para>
    /// </summary>
    private static string SanitizeBody(string bodyHtml) =>
        SanitizationHelper.SanitizeHtml(bodyHtml) ?? string.Empty;

    /// <summary>
    /// Builds a key from a display name: lowercase, underscores, no punctuation — matching the
    /// convention the editor states ("lowercase letters, numbers and underscores only").
    /// </summary>
    private static string DeriveKey(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')
            .ToArray();

        var key = new string(chars);

        // Collapse runs of underscores that punctuation and spaces leave behind.
        while (key.Contains("__")) key = key.Replace("__", "_");

        key = key.Trim('_');
        return key.Length > 100 ? key[..100] : key;
    }

    private static string NormalizeKey(string key) => DeriveKey(key);

    private static bool TryGetIgnoreCase(
        IReadOnlyDictionary<string, string?> values,
        string key,
        out string? value)
    {
        if (values.TryGetValue(key, out value)) return true;

        foreach (var (candidateKey, candidateValue) in values)
        {
            if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
            {
                value = candidateValue;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
