using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Templates;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class TemplateService : ITemplateService
{
    private readonly AppDbContext _dbContext;
    private readonly IWhatsAppService _whatsAppService;

    private readonly IAuditService _auditService;
    private readonly ILogger<TemplateService> _logger;

    public TemplateService(
        AppDbContext dbContext,
        IWhatsAppService whatsAppService,
        IAuditService auditService,
        ILogger<TemplateService> logger)
    {
        _dbContext = dbContext;
        _whatsAppService = whatsAppService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<PagedResponse<TemplateResponse>> GetAllAsync(PagedRequest request, string? status = null, string? category = null)
    {
        // No Meta sync here. This used to await SyncFromWhatsAppAsync() on every list request,
        // which made one outbound HTTPS call per connected WABA before returning a single row —
        // the page took 7-15 seconds and wrote to the database on a GET.
        //
        // TemplateSyncBackgroundService now refreshes on a schedule, and the existing
        // "Load Templates" button (POST /api/Templates/sync, Template.LoadTemplate) forces an
        // immediate refresh when someone needs Meta's latest state right now.

        var query = _dbContext.Templates.AsNoTracking().Include(t => t.Variables).AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TemplateStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(t => t.Status == parsedStatus);
        }

        if (!string.IsNullOrEmpty(category) && Enum.TryParse<TemplateCategory>(category, true, out var parsedCategory))
        {
            query = query.Where(t => t.Category == parsedCategory);
        }

        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();

            // Category and Status are enums; see CampaignService for why they are resolved in
            // memory before the query rather than compared as text inside it.
            var matchingCategories = Enum.GetValues<TemplateCategory>()
                .Where(c => c.ToString().ToLower().Contains(search))
                .ToList();

            var matchingStatuses = Enum.GetValues<TemplateStatus>()
                .Where(s => s.ToString().ToLower().Contains(search))
                .ToList();

            query = query.Where(t =>
                t.Name.ToLower().Contains(search) ||
                // The body is the template, and language, category and status are all columns.
                t.BodyText.ToLower().Contains(search) ||
                t.Language.ToLower().Contains(search) ||
                (t.HeaderContent != null && t.HeaderContent.ToLower().Contains(search)) ||
                (t.FooterText != null && t.FooterText.ToLower().Contains(search)) ||
                matchingCategories.Contains(t.Category) ||
                matchingStatuses.Contains(t.Status));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedResponse<TemplateResponse>
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items.Select(MapToResponse).ToList()
        };
    }

    public async Task<TemplateResponse> GetByIdAsync(int id)
    {
        var template = await _dbContext.Templates
            .AsNoTracking()
            .Include(t => t.Variables)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template == null)
            throw new KeyNotFoundException($"Template with ID {id} not found.");

        return MapToResponse(template);
    }

    public async Task<TemplateResponse> CreateAsync(CreateTemplateRequest request)
    {
        // Meta's rules, checked here so a template created in this app can be submitted.
        request.Name = request.Name?.Trim() ?? string.Empty;
        if (!System.Text.RegularExpressions.Regex.IsMatch(request.Name, Catalogs.TemplateAuthoringCatalog.NamePattern))
            throw new ArgumentException("The template name can use only lower-case letters, digits and underscores.");
        const int maxBody = Catalogs.TemplateAuthoringCatalog.MaxBodyLength;
        const int maxHeader = Catalogs.TemplateAuthoringCatalog.MaxHeaderLength;
        const int maxFooter = Catalogs.TemplateAuthoringCatalog.MaxFooterLength;
        if (string.IsNullOrWhiteSpace(request.BodyText) || request.BodyText.Length > maxBody)
            throw new ArgumentException($"The message body is required and can be at most {maxBody} characters.");
        if (request.HeaderContent?.Length > maxHeader && string.Equals(request.HeaderType, "Text", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"A text header can be at most {maxHeader} characters.");
        if (request.FooterText?.Length > maxFooter)
            throw new ArgumentException($"The footer can be at most {maxFooter} characters.");

        var existing = await _dbContext.Templates.AnyAsync(t => t.Name == request.Name);
        if (existing)
            throw new InvalidOperationException($"Template with name '{request.Name}' already exists.");

        var template = new Template
        {
            Name = request.Name,
            Language = request.Language,
            Category = Enum.Parse<TemplateCategory>(request.Category, true),
            TemplateType = Enum.Parse<TemplateType>(request.TemplateType, true),
            BodyText = request.BodyText,
            ButtonsJson = SerializeButtons(request.Buttons),
            HeaderType = Enum.Parse<HeaderType>(request.HeaderType, true),
            HeaderContent = request.HeaderContent,
            FooterText = request.FooterText
        };

        if (request.Variables != null)
        {
            foreach (var v in request.Variables)
            {
                template.Variables.Add(new TemplateVariable
                {
                    Position = v.Position,
                    SampleValue = v.SampleValue,
                    Description = v.Description
                });
            }
        }

        _dbContext.Templates.Add(template);
        await _dbContext.SaveChangesAsync();

        // SyncFromWhatsAppAsync is deliberately not audited: GetAllAsync calls it on every list
        // load, so auditing it would bury real changes under one entry per page view.
        await _auditService.LogAsync(
            "Template.Created", "Data",
            $"Created template \"{template.Name}\" ({template.Language}).",
            "Template", template.Id.ToString());

        return await GetByIdAsync(template.Id);
    }

    public async Task<TemplateResponse> UpdateAsync(int id, UpdateTemplateRequest request)
    {
        var template = await _dbContext.Templates
            .Include(t => t.Variables)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template == null)
            throw new KeyNotFoundException($"Template with ID {id} not found.");

        if (template.Name != request.Name)
        {
            var existing = await _dbContext.Templates.AnyAsync(t => t.Name == request.Name);
            if (existing)
                throw new InvalidOperationException($"Another template with name '{request.Name}' already exists.");
        }

        template.Name = request.Name;
        template.Language = request.Language;
        template.Category = Enum.Parse<TemplateCategory>(request.Category, true);
        template.TemplateType = Enum.Parse<TemplateType>(request.TemplateType, true);
        template.BodyText = request.BodyText;
        if (request.Buttons is not null) template.ButtonsJson = SerializeButtons(request.Buttons);
        template.HeaderType = Enum.Parse<HeaderType>(request.HeaderType, true);
        template.HeaderContent = request.HeaderContent;
        template.FooterText = request.FooterText;

        _dbContext.TemplateVariables.RemoveRange(template.Variables);

        if (request.Variables != null)
        {
            foreach (var v in request.Variables)
            {
                template.Variables.Add(new TemplateVariable
                {
                    TemplateId = template.Id,
                    Position = v.Position,
                    SampleValue = v.SampleValue,
                    Description = v.Description
                });
            }
        }

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Template.Updated", "Data",
            $"Updated template \"{template.Name}\" ({template.Language}).",
            "Template", template.Id.ToString());

        return await GetByIdAsync(template.Id);
    }

    public async Task DeleteAsync(int id)
    {
        var template = await _dbContext.Templates.FindAsync(id);
        if (template == null)
            throw new KeyNotFoundException($"Template with ID {id} not found.");

        // Every reference, including deleted campaigns: a deleted campaign keeps its history, and
        // deleting its template used to cascade the campaign away (or, through a tracked A/B
        // variant, silently blank the variant's template) instead of refusing.
        var campaigns = await _dbContext.Campaigns.IgnoreQueryFilters().CountAsync(c => c.TemplateId == id);
        var variants = await _dbContext.CampaignVariants.IgnoreQueryFilters().CountAsync(v => v.TemplateId == id);
        var bots = await _dbContext.TemplateBots.IgnoreQueryFilters().CountAsync(b => b.TemplateId == id);
        var followUps = await _dbContext.FollowUpRules.IgnoreQueryFilters().CountAsync(f => f.TemplateId == id);
        var uses = new[]
        {
            (campaigns, "campaign"), (variants, "A/B variant"), (bots, "template bot"), (followUps, "follow-up")
        }.Where(u => u.Item1 > 0).Select(u => $"{u.Item1} {u.Item2}{(u.Item1 == 1 ? "" : "s")}").ToList();
        if (uses.Count > 0)
            throw new InvalidOperationException(
                $"This template is still used by {string.Join(", ", uses)} (deleted campaigns included, because their history needs it). Remove it from those first.");

        // Captured before Remove: after SaveChanges the entity is detached and Id reads 0.
        var templateName = template.Name;
        var templateId = template.Id;

        _dbContext.Templates.Remove(template);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Template.Deleted", "Data",
            $"Deleted template \"{templateName}\".",
            "Template", templateId.ToString());
    }

    public async Task<int> SyncFromWhatsAppAsync()
    {
        var allConfigs = await _dbContext.WabaConfigurations.AsNoTracking().Where(c => c.ConnectionId != null && c.Connected).ToListAsync();
        var allWaTemplates = new List<WhatsAppTemplateInfo>();

        // Tracked so the delete pass below can tell "Meta says this template is gone" apart
        // from "we could not reach Meta". Without that distinction a total outage deletes the
        // entire local table.
        var connectionsAttempted = 0;
        var connectionsSucceeded = 0;
        var statusChanges = new List<(string Name, int Id, TemplateStatus From, TemplateStatus To)>();
        var removedNames = new List<string>();

        foreach (var cfg in allConfigs)
        {
            if (cfg.ConnectionId.HasValue)
            {
                connectionsAttempted++;
                try
                {
                    var connTpls = await _whatsAppService.GetTemplatesForConnectionAsync(cfg.ConnectionId.Value);
                    allWaTemplates.AddRange(connTpls);
                    connectionsSucceeded++;
                }
                catch (Exception ex)
                {
                    // Skip connections that fail to fetch templates, but say so — this used to
                    // be swallowed silently, which is how a total outage could look like an
                    // empty template list.
                    _logger.LogWarning(ex,
                        "Template sync could not reach Meta for connection {ConnectionId}.",
                        cfg.ConnectionId.Value);
                }
            }
        }

        var waTemplates = allWaTemplates.GroupBy(t => t.Name).Select(g => g.First()).ToList();
        int newCount = 0;

        // Track which template names came from Meta
        var metaTemplateNames = new HashSet<string>(waTemplates.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        // One query instead of one per Meta template. With 100 templates against a remote
        // database the per-template lookup alone cost tens of seconds.
        var localByName = await _dbContext.Templates
            .ToDictionaryAsync(t => t.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var waTemplate in waTemplates)
        {
            localByName.TryGetValue(waTemplate.Name, out var localTemplate);

            var mappedStatus = waTemplate.Status.ToUpper() switch
            {
                "APPROVED" => TemplateStatus.Approved,
                "REJECTED" => TemplateStatus.Rejected,
                _ => TemplateStatus.Pending
            };

            var mappedCategory = Enum.TryParse<TemplateCategory>(waTemplate.Category, true, out var cat) ? cat : TemplateCategory.Marketing;

            var mappedType = waTemplate.TemplateType?.ToUpper() switch
            {
                "IMAGE" => TemplateType.Image,
                "VIDEO" => TemplateType.Video,
                "DOCUMENT" => TemplateType.Document,
                "CAROUSEL" => TemplateType.Carousel,
                _ => TemplateType.Text
            };

            if (localTemplate == null)
            {
                var newTemplate = new Template
                {
                    Name = waTemplate.Name,
                    WhatsAppTemplateId = waTemplate.Id,
                    Language = waTemplate.Language,
                    Category = mappedCategory,
                    TemplateType = mappedType,
                    Status = mappedStatus,
                    RejectReason = waTemplate.RejectReason,
                    BodyText = waTemplate.BodyText ?? string.Empty,
                    ComponentsJson = waTemplate.ComponentsJson,
                    ButtonsJson = waTemplate.ButtonsJson
                };
                _dbContext.Templates.Add(newTemplate);
                newCount++;
            }
            else
            {
                localTemplate.WhatsAppTemplateId = waTemplate.Id;
                // Meta's review decision is what decides whether a template may be sent: each
                // change of it is audited below.
                if (localTemplate.Status != mappedStatus)
                    statusChanges.Add((localTemplate.Name, localTemplate.Id, localTemplate.Status, mappedStatus));
                localTemplate.Status = mappedStatus;
                localTemplate.TemplateType = mappedType;
                localTemplate.RejectReason = waTemplate.RejectReason;
                localTemplate.ComponentsJson = waTemplate.ComponentsJson ?? localTemplate.ComponentsJson;
                localTemplate.ButtonsJson = waTemplate.ButtonsJson ?? localTemplate.ButtonsJson;
                if (!string.IsNullOrEmpty(waTemplate.BodyText))
                {
                    localTemplate.BodyText = waTemplate.BodyText;
                }
            }
        }

        // Remove local templates Meta no longer lists — but ONLY when every connection answered.
        //
        // This pass treats "absent from the Meta response" as "deleted at Meta". If a call
        // failed, absent instead means "we don't know", and deleting on that basis would wipe
        // the table on any outage. Previously the failure was swallowed and this ran anyway;
        // once the sync moved to an unattended background job that became a matter of time.
        var everyConnectionAnswered = connectionsAttempted > 0 && connectionsSucceeded == connectionsAttempted;

        if (everyConnectionAnswered)
        {
            var templatesToDelete = localByName.Values
                .Where(t => !metaTemplateNames.Contains(t.Name))
                .ToList();

            if (templatesToDelete.Count > 0)
            {
                // One query for every referenced template id rather than one per candidate.
                // IgnoreQueryFilters is load-bearing: a soft-deleted campaign still points at
                // its template, and CampaignService.GetByIdAsync dereferences Template.Name —
                // deleting it would turn that campaign's detail page into a null reference.
                var inUseTemplateIds = await _dbContext.Campaigns
                    .IgnoreQueryFilters()
                    .Select(c => c.TemplateId)
                    .Distinct()
                    .ToHashSetAsync();
                // A/B variants and template bots reference templates too, through Restrict foreign
                // keys: removing one of those failed the whole sync's save.
                inUseTemplateIds.UnionWith(await _dbContext.CampaignVariants.IgnoreQueryFilters()
                    .Where(v => v.TemplateId != null).Select(v => v.TemplateId).Distinct().ToListAsync());
                inUseTemplateIds.UnionWith(await _dbContext.TemplateBots.IgnoreQueryFilters()
                    .Select(b => (int?)b.TemplateId).Distinct().ToListAsync());

                var removable = templatesToDelete.Where(t => !inUseTemplateIds.Contains(t.Id)).ToList();
                removedNames.AddRange(removable.Select(t => t.Name));

                // No explicit TemplateVariables cleanup: the relationship is configured
                // OnDelete(Cascade), so the database removes them.
                _dbContext.Templates.RemoveRange(removable);
            }
        }
        else if (connectionsAttempted > 0)
        {
            _logger.LogWarning(
                "Template sync reached {Succeeded} of {Attempted} connection(s); skipping the removal pass so a transient failure cannot delete local templates.",
                connectionsSucceeded, connectionsAttempted);
        }

        await _dbContext.SaveChangesAsync();

        foreach (var change in statusChanges)
        {
            await _auditService.LogAsync(
                "Template.StatusChanged", "Data",
                $"Meta changed template \"{change.Name}\" from {change.From} to {change.To}.",
                "Template", change.Id.ToString());
        }
        if (connectionsAttempted > 0)
        {
            await _auditService.LogAsync(
                "Template.Synced", "Data",
                $"Synced templates from WhatsApp: {newCount} added, {statusChanges.Count} changed status, {removedNames.Count} removed"
                    + (removedNames.Count > 0 ? $" ({string.Join(", ", removedNames.Take(10))}{(removedNames.Count > 10 ? ", …" : "")})." : "."),
                "Template", null);
        }
        return newCount;
    }

    public async Task<TemplatePreviewResponse> GetPreviewAsync(int id, Dictionary<string, string>? variableValues = null)
    {
        var template = await _dbContext.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (template == null)
            throw new KeyNotFoundException($"Template with ID {id} not found.");

        var preview = template.BodyText;

        if (variableValues != null)
        {
            foreach (var kvp in variableValues)
            {
                preview = preview.Replace("{{" + kvp.Key + "}}", kvp.Value);
            }
        }
        else
        {
            // Use dummy values if none provided
            var variables = await _dbContext.TemplateVariables.AsNoTracking().Where(v => v.TemplateId == id).ToListAsync();
            foreach (var v in variables)
            {
                var val = !string.IsNullOrEmpty(v.SampleValue) ? v.SampleValue : $"[{v.Position}]";
                preview = preview.Replace("{{" + v.Position + "}}", val);
            }
        }

        return new TemplatePreviewResponse
        {
            Id = template.Id,
            Name = template.Name,
            PreviewText = preview
        };
    }

    private static string? SerializeButtons(List<WhatsApp.TemplateButton>? buttons)
    {
        var valid = WhatsApp.TemplateComponents.ValidateButtons(buttons);
        return valid.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(valid, WhatsApp.TemplateComponents.Json);
    }

    /// <summary>Submits a template created here to Meta for review, on the given connection's account.</summary>
    public async Task<TemplateResponse> SubmitToMetaAsync(int id, int connectionId)
    {
        var template = await _dbContext.Templates.Include(t => t.Variables).FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Template not found.");
        if (!string.IsNullOrWhiteSpace(template.WhatsAppTemplateId))
            throw new InvalidOperationException("This template has already been submitted to Meta.");
        if (!Catalogs.TemplateAuthoringCatalog.Categories.Any(c => c.Value == template.Category.ToString()))
            throw new InvalidOperationException($"{template.Category} templates are created in WhatsApp Manager, then synced here.");

        var (success, metaId, status, error) = await _whatsAppService.SubmitTemplateAsync(connectionId, WhatsApp.TemplateComponents.BuildSubmission(template));
        if (!success) throw new InvalidOperationException(error ?? "Meta refused the template.");

        template.WhatsAppTemplateId = metaId;
        template.Status = status?.ToUpperInvariant() switch
        {
            "APPROVED" => TemplateStatus.Approved,
            "REJECTED" => TemplateStatus.Rejected,
            _ => TemplateStatus.Pending
        };
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync("Template.Submitted", "Data",
            $"Submitted template \"{template.Name}\" to Meta for review (status {template.Status}).", "Template", id.ToString());
        return MapToResponse(template);
    }

    private static TemplateResponse MapToResponse(Template t)
    {
        return new TemplateResponse
        {
            Id = t.Id,
            Name = t.Name,
            Language = t.Language,
            Category = t.Category.ToString(),
            TemplateType = t.TemplateType.ToString(),
            Status = t.Status.ToString(),
            BodyText = t.BodyText,
            HeaderType = t.HeaderType.ToString(),
            HeaderContent = t.HeaderContent,
            FooterText = t.FooterText,
            WhatsAppTemplateId = t.WhatsAppTemplateId,
            RejectReason = t.RejectReason,
            Buttons = WhatsApp.TemplateComponents.ParseButtons(t.ButtonsJson),
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            Variables = t.Variables.Select(v => new TemplateVariableRequest
            {
                Position = v.Position,
                SampleValue = v.SampleValue,
                Description = v.Description
            }).ToList()
        };
    }
}
