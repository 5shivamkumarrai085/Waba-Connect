using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Data.Seed;

public interface ISetupSeeder
{
    Task SeedAsync();
}

/// <summary>
/// Brings the permission catalogue, default roles and the first super admin into existence.
///
/// <para>
/// Runs once at startup rather than lazily on first request (the pre-existing
/// PermissionManagementService pattern), because the lazy version races when several requests
/// arrive concurrently on a cold start and can double-insert.
/// </para>
/// <para>
/// Additive by design: it inserts what is missing and never deletes or overwrites what an
/// administrator has since edited. That means adding a permission to the catalogue and
/// restarting is enough to publish it.
/// </para>
/// </summary>
public class SetupSeeder : ISetupSeeder
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SetupSeeder> _logger;

    /// <summary>Kept in sync with appsettings.json. Matching it triggers the must-change flow.</summary>
    private const string DocumentedDefaultPassword = "Admin@123";

    public SetupSeeder(AppDbContext dbContext, IConfiguration configuration, ILogger<SetupSeeder> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        try
        {
            await SeedPermissionsAsync();
            await SeedRolesAsync();
            await SeedSuperAdminAsync();
            await SeedLookupsAsync();
            await SeedAiPromptAsync();
            await SeedCannedReplyAsync();
            await SeedEmailTemplatesAsync();
        }
        catch (Exception ex)
        {
            // Most likely cause: migrations haven't been applied yet, so the tables don't exist.
            // The pending-migration guard in Program.cs already reports that loudly; failing
            // startup here as well would just make the fix harder to reach.
            _logger.LogError(ex, "Setup seeding skipped. If tables are missing, run: dotnet ef database update");
        }
    }

    private async Task SeedPermissionsAsync()
    {
        var catalog = PermissionCatalog.All();
        var existingKeys = await _dbContext.Permissions.Select(p => p.Key).ToHashSetAsync();

        var missing = catalog.Where(p => !existingKeys.Contains(p.Key)).ToList();
        if (missing.Count > 0)
        {
            _dbContext.Permissions.AddRange(missing);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} new permission(s).", missing.Count);
        }

        // Refresh display metadata on existing rows so renaming a label in the catalogue takes
        // effect. The Key is the identity and is never rewritten.
        var byKey = await _dbContext.Permissions.ToDictionaryAsync(p => p.Key);
        var changed = false;

        foreach (var definition in catalog)
        {
            if (!byKey.TryGetValue(definition.Key, out var row)) continue;

            if (row.FeatureDisplayName != definition.FeatureDisplayName ||
                row.CapabilityDisplayName != definition.CapabilityDisplayName ||
                row.GroupName != definition.GroupName ||
                row.SortOrder != definition.SortOrder)
            {
                row.FeatureDisplayName = definition.FeatureDisplayName;
                row.CapabilityDisplayName = definition.CapabilityDisplayName;
                row.GroupName = definition.GroupName;
                row.SortOrder = definition.SortOrder;
                changed = true;
            }
        }

        if (changed) await _dbContext.SaveChangesAsync();

        // Prune permissions the catalogue no longer defines.
        //
        // Seeding is otherwise purely additive, but without this a retired key lingers in the
        // database forever — and because the permission matrix is served from the database
        // rather than from this file, the UI would keep offering a checkbox that grants
        // nothing. RolePermission and UserPermission cascade on delete, so the grants go with
        // it. Retiring a key is always a deliberate edit to PermissionCatalog, never accidental.
        var catalogKeys = catalog.Select(p => p.Key).ToHashSet();
        var retired = await _dbContext.Permissions
            .Where(p => !catalogKeys.Contains(p.Key))
            .ToListAsync();

        if (retired.Count > 0)
        {
            _dbContext.Permissions.RemoveRange(retired);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation(
                "Removed {Count} retired permission(s): {Keys}",
                retired.Count, string.Join(", ", retired.Select(p => p.Key)));
        }
    }

    private async Task SeedRolesAsync()
    {
        var permissionIdByKey = await _dbContext.Permissions.ToDictionaryAsync(p => p.Key, p => p.Id);

        foreach (var definition in RoleSeed.All())
        {
            var role = await _dbContext.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Name == definition.Name);

            if (role is null)
            {
                role = new Role
                {
                    Name = definition.Name,
                    Description = definition.Description,
                    IsSystem = true,
                    IsAdministrator = definition.IsAdministrator
                };

                _dbContext.Roles.Add(role);
                await _dbContext.SaveChangesAsync();

                foreach (var key in definition.PermissionKeys.Distinct())
                {
                    if (permissionIdByKey.TryGetValue(key, out var permissionId))
                    {
                        _dbContext.RolePermissions.Add(new RolePermission
                        {
                            RoleId = role.Id,
                            PermissionId = permissionId
                        });
                    }
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Seeded role '{Role}'.", definition.Name);
            }
            else
            {
                if (!role.IsSystem)
                {
                    // A role with a seeded name existed but wasn't marked system. Mark it so it
                    // gains delete protection.
                    role.IsSystem = true;
                    await _dbContext.SaveChangesAsync();
                }

                await GrantNewCatalogueKeysAsync(role, definition, permissionIdByKey);
            }
        }
    }

    /// <summary>
    /// Grants a built-in role any permission its seed definition calls for but which it doesn't
    /// yet hold.
    ///
    /// <para>
    /// Additive only — nothing is ever revoked. That distinction matters: when a new capability
    /// is added to the catalogue (say Chat.Delete), the built-in roles that should have it need
    /// to pick it up on the next start, or Admin silently lacks a permission it was designed to
    /// have. But an administrator may also have tuned a built-in role, and a full re-sync would
    /// wipe that without warning. Adding what's missing satisfies the first case without the
    /// second.
    /// </para>
    /// <para>
    /// To genuinely restrict a built-in role, clone it and assign the clone.
    /// </para>
    /// </summary>
    private async Task GrantNewCatalogueKeysAsync(
        Role role,
        RoleSeed.RoleDef definition,
        Dictionary<string, int> permissionIdByKey)
    {
        // Administrator roles short-circuit every check, so materialising rows for them would
        // only go stale as the catalogue grows.
        if (role.IsAdministrator) return;

        var heldPermissionIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();

        var toGrant = definition.PermissionKeys
            .Distinct()
            .Where(key => permissionIdByKey.TryGetValue(key, out var id) && !heldPermissionIds.Contains(id))
            .Select(key => permissionIdByKey[key])
            .ToList();

        if (toGrant.Count == 0) return;

        foreach (var permissionId in toGrant)
            _dbContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation(
            "Granted {Count} newly catalogued permission(s) to built-in role '{Role}'.",
            toGrant.Count, role.Name);
    }

    /// <summary>
    /// Inserts any lookup values that are missing. Additive only — an administrator may have
    /// renamed "In Progress" or recoloured a status, and re-seeding must never revert that.
    /// </summary>
    private async Task SeedLookupsAsync()
    {
        var existingStatuses = await _dbContext.ContactStatuses.Select(s => s.Value).ToHashSetAsync();
        var newStatuses = LookupSeed.Statuses()
            .Where(s => !existingStatuses.Contains(s.Value))
            .Select(s => new ContactStatusLookup
            {
                Value = s.Value,
                Name = s.Name,
                Color = s.Color,
                IsActive = s.IsActive,
                IsSystem = true,
                SortOrder = s.SortOrder
            })
            .ToList();

        if (newStatuses.Count > 0)
        {
            _dbContext.ContactStatuses.AddRange(newStatuses);
            _logger.LogInformation("Seeded {Count} contact status value(s).", newStatuses.Count);
        }

        var existingSources = await _dbContext.ContactSources.Select(s => s.Value).ToHashSetAsync();
        var newSources = LookupSeed.Sources()
            .Where(s => !existingSources.Contains(s.Value))
            .Select(s => new ContactSourceLookup
            {
                Value = s.Value,
                Name = s.Name,
                Color = s.Color,
                IsActive = s.IsActive,
                IsSystem = true,
                SortOrder = s.SortOrder
            })
            .ToList();

        if (newSources.Count > 0)
        {
            _dbContext.ContactSources.AddRange(newSources);
            _logger.LogInformation("Seeded {Count} contact source value(s).", newSources.Count);
        }

        var existingTypes = await _dbContext.ContactTypes.Select(t => t.Value).ToHashSetAsync();
        var newTypes = LookupSeed.Types()
            .Where(t => !existingTypes.Contains(t.Value))
            .Select(t => new ContactTypeLookup
            {
                Value = t.Value,
                Name = t.Name,
                Color = t.Color,
                IsActive = t.IsActive,
                IsSystem = true,
                SortOrder = t.SortOrder
            })
            .ToList();

        if (newTypes.Count > 0)
        {
            _dbContext.ContactTypes.AddRange(newTypes);
            _logger.LogInformation("Seeded {Count} contact type value(s).", newTypes.Count);
        }

        var existingLanguages = await _dbContext.Languages.Select(l => l.Code).ToHashSetAsync();
        var newLanguages = LookupSeed.Languages()
            .Where(l => !existingLanguages.Contains(l.Code))
            .Select(l => new Language
            {
                Code = l.Code,
                Name = l.Name,
                Color = l.Color,
                IsActive = true,
                IsDefault = l.IsDefault,
                SortOrder = l.SortOrder
            })
            .ToList();

        if (newLanguages.Count > 0)
        {
            _dbContext.Languages.AddRange(newLanguages);
            _logger.LogInformation("Seeded {Count} language(s).", newLanguages.Count);
        }

        await _dbContext.SaveChangesAsync();

        // Colour was added to sources and languages after they were first seeded, so the rows
        // already in the database carry null. Backfill only where it is still null — an admin
        // who has chosen their own colour must not have it overwritten on every boot.
        await BackfillLookupColoursAsync();

        // Safety net: something has to be the fallback language, and a picker with no default
        // is worse than an arbitrary one.
        if (!await _dbContext.Languages.AnyAsync(l => l.IsDefault))
        {
            var first = await _dbContext.Languages.OrderBy(l => l.SortOrder).FirstOrDefaultAsync();
            if (first is not null)
            {
                first.IsDefault = true;
                await _dbContext.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// Seeds the prompt that was previously hardcoded in appsettings.json as the default.
    ///
    /// This is what makes the move to database-backed prompts behaviour-preserving: the bot
    /// router now reads the default prompt from here, and because the seeded text is byte-for-byte
    /// what configuration supplied, existing conversations reply exactly as they did before.
    /// </summary>
    /// <summary>
    /// Fills in colours on lookup rows seeded before the Color column existed.
    ///
    /// <para>
    /// Matches on <c>Color == null</c> rather than on IsSystem, so it never overwrites a colour
    /// an admin has picked — including one they deliberately set on a system row.
    /// </para>
    /// </summary>
    private async Task BackfillLookupColoursAsync()
    {
        var updated = 0;

        var sourceColours = LookupSeed.Sources().ToDictionary(s => s.Value, s => s.Color);
        foreach (var source in await _dbContext.ContactSources.Where(s => s.Color == null).ToListAsync())
        {
            if (sourceColours.TryGetValue(source.Value, out var colour))
            {
                source.Color = colour;
                updated++;
            }
        }

        var languageColours = LookupSeed.Languages().ToDictionary(l => l.Code, l => l.Color);
        foreach (var language in await _dbContext.Languages.Where(l => l.Color == null).ToListAsync())
        {
            if (languageColours.TryGetValue(language.Code, out var colour))
            {
                language.Color = colour;
                updated++;
            }
        }

        if (updated > 0)
        {
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Backfilled colour on {Count} lookup row(s).", updated);
        }
    }

    private async Task SeedAiPromptAsync()
    {
        if (await _dbContext.AiPrompts.AnyAsync()) return;

        var configuredPrompt = _configuration["PersonalAssistants:OmniBot:Prompt"];
        var promptText = string.IsNullOrWhiteSpace(configuredPrompt)
            ? "You are OmniBot, a highly capable customer assistant for OmniConnect platform. Respond to the customer query in a polite, helpful, and concise manner."
            : configuredPrompt;

        _dbContext.AiPrompts.Add(new AiPrompt
        {
            Name = "OmniBot",
            PromptText = promptText,
            Description = "Default assistant personality, carried over from configuration.",
            IsActive = true,
            IsDefault = true
        });

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Seeded the default AI prompt from configuration.");
    }

    /// <summary>
    /// Seeds the four templates from the reference UI. Content only — nothing sends them.
    /// </summary>
    private async Task SeedEmailTemplatesAsync()
    {
        var existing = await _dbContext.EmailTemplates.Select(t => t.Key).ToHashSetAsync();

        var defaults = new (string Key, string Name, string Subject, string Body, string Variables)[]
        {
            ("EmailConfirmation", "Email Confirmation", "Email Confirmation",
             "<p>Hello {user_name},</p><p>Please confirm your email address to finish setting up your {site_name} account.</p><p><a href=\"{confirmation_link}\">Confirm my email</a></p>",
             "site_name,user_name,confirmation_link"),

            ("WelcomeEmail", "Welcome Email", "Welcome to {site_name}!",
             "<p>Hi {user_name},</p><p>Welcome to {site_name}. Your account is ready to use.</p><p>If you have any questions, just reply to this email.</p>",
             "site_name,user_name"),

            ("PasswordReset", "Password Reset", "Password Reset Request",
             "<p>Hi {user_name},</p><p>We received a request to reset your {site_name} password.</p><p><a href=\"{reset_link}\">Choose a new password</a></p><p>If you didn't ask for this, you can ignore this email.</p>",
             "site_name,user_name,reset_link"),

            ("NewContactAssigned", "New Contact Assigned", "New Contact Assigned to You",
             "<p>Hi {user_name},</p><p>{contact_name} ({contact_phone}) has been assigned to you.</p><p><a href=\"{contact_link}\">Open the contact</a></p>",
             "user_name,contact_name,contact_phone,contact_link")
        };

        var missing = defaults
            .Where(t => !existing.Contains(t.Key))
            .Select(t => new EmailTemplate
            {
                Key = t.Key,
                Name = t.Name,
                Subject = t.Subject,
                BodyHtml = t.Body,
                AvailableVariables = t.Variables,
                IsEnabled = true,
                IsSystem = true
            })
            .ToList();

        if (missing.Count > 0)
        {
            _dbContext.EmailTemplates.AddRange(missing);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} email template(s).", missing.Count);
        }
    }

    private async Task SeedCannedReplyAsync()
    {
        if (await _dbContext.CannedReplies.AnyAsync()) return;

        _dbContext.CannedReplies.Add(new CannedReply
        {
            Title = "Welcome to our support team",
            Description = "Welcome to our support team! How can we help you today?",
            IsPublic = true,
            IsActive = true
        });

        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedSuperAdminAsync()
    {
        if (await _dbContext.AppUsers.AnyAsync()) return;

        var email = _configuration["Auth:SuperAdmin:Email"]?.Trim().ToLowerInvariant();
        var password = _configuration["Auth:SuperAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Auth:SuperAdmin:Email is not configured — no initial account was created.");
            return;
        }

        var usingDefaultPassword = string.IsNullOrWhiteSpace(password) || password == DocumentedDefaultPassword;
        if (string.IsNullOrWhiteSpace(password)) password = DocumentedDefaultPassword;

        var superAdminRole = await _dbContext.Roles.FirstOrDefaultAsync(r => r.IsAdministrator);

        _dbContext.AppUsers.Add(new AppUser
        {
            FirstName = "Super",
            LastName = "Admin",
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsActive = true,
            IsVerified = true,
            IsAdministrator = true,
            RoleId = superAdminRole?.Id,
            DefaultLanguageCode = "en",
            // Force a reset while the documented default is in play. If an operator supplied a
            // real password via configuration, don't nag them.
            MustChangePassword = usingDefaultPassword
        });

        await _dbContext.SaveChangesAsync();

        if (usingDefaultPassword)
        {
            _logger.LogWarning(
                "==================================================================\n" +
                " INITIAL SUPER ADMIN CREATED WITH THE DEFAULT PASSWORD\n" +
                "   Email:    {Email}\n" +
                "   Password: {Password}\n" +
                " This account must change its password at first login. Set\n" +
                " Auth:SuperAdmin:Password before deploying anywhere real.\n" +
                "==================================================================",
                email, DocumentedDefaultPassword);
        }
        else
        {
            _logger.LogInformation("Initial super admin created for {Email}.", email);
        }
    }
}
