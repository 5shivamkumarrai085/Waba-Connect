using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Segments;

/// <summary>A group of rules: all must match, or any may.</summary>
public sealed class SegmentRuleGroup
{
    /// <summary>"all" (AND) or "any" (OR).</summary>
    public string Match { get; set; } = "all";
    public List<SegmentRule> Rules { get; set; } = [];
    public List<SegmentRuleGroup> Groups { get; set; } = [];
}

/// <summary>One condition. Which of Value / Values / Days is used depends on the field and operator.</summary>
public sealed class SegmentRule
{
    public string Field { get; set; } = string.Empty;
    public string Op { get; set; } = "eq";
    public string? Value { get; set; }
    public List<string>? Values { get; set; }
    public int? Days { get; set; }
}

/// <summary>
/// Turns segment rules into a database query over contacts.
/// </summary>
/// <remarks>
/// Only whitelisted fields and operators are accepted, and every rule becomes a parameterised EF
/// expression — no user text is ever concatenated into SQL. Unknown fields or operators are a
/// validation error, not ignored, so a saved segment never silently means something broader than
/// it says.
/// </remarks>
public static class SegmentQueryBuilder
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const int MaxRules = 50;
    private const int MaxDepth = 3;

    /// <summary>Contact text fields that can be compared, and how to read each.</summary>
    private static readonly Dictionary<string, Expression<Func<Contact, string?>>> TextFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["type"] = c => c.Type,
        ["status"] = c => c.Status,
        ["source"] = c => c.Source,
        ["assignedTo"] = c => c.AssignedTo,
        ["country"] = c => c.Country,
        ["state"] = c => c.State,
        ["city"] = c => c.City,
        ["company"] = c => c.Company,
        ["timeZone"] = c => c.TimeZone,
        ["email"] = c => c.Email,
        ["phone"] = c => c.Phone,
        ["adSourceId"] = c => c.AdSourceId,
        ["adHeadline"] = c => c.AdHeadline,
    };

    public static IReadOnlyCollection<string> Fields =>
        [.. TextFields.Keys, "tag", "group", "createdAt", "consent", "opened", "clicked", "replied", "received"];

    public static SegmentRuleGroup Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SegmentRuleGroup();
        try
        {
            return JsonSerializer.Deserialize<SegmentRuleGroup>(json, Json) ?? new SegmentRuleGroup();
        }
        catch (JsonException)
        {
            throw new ArgumentException("The segment rules are not valid.");
        }
    }

    /// <summary>Checks the rules without running them. Throws ArgumentException with a readable message.</summary>
    public static void Validate(SegmentRuleGroup root)
    {
        var count = 0;
        void Walk(SegmentRuleGroup group, int depth)
        {
            if (depth > MaxDepth) throw new ArgumentException($"Rule groups can be nested at most {MaxDepth} deep.");
            if (group.Match is not ("all" or "any")) throw new ArgumentException("A rule group must match \"all\" or \"any\".");
            foreach (var rule in group.Rules)
            {
                if (++count > MaxRules) throw new ArgumentException($"A segment can have at most {MaxRules} rules.");
                _ = BuildRule(null!, rule, DateTime.UtcNow, validateOnly: true);
            }
            foreach (var child in group.Groups) Walk(child, depth + 1);
        }
        Walk(root, 1);
        if (count == 0) throw new ArgumentException("Add at least one rule.");
    }

    /// <summary>Active, non-deleted contacts matching the rules.</summary>
    public static IQueryable<Contact> Apply(AppDbContext db, IQueryable<Contact> contacts, SegmentRuleGroup root, DateTime nowUtc)
    {
        Validate(root);
        var predicate = BuildGroup(db, root, nowUtc);
        return contacts.Where(c => c.IsActive && !c.IsDeleted).Where(predicate);
    }

    private static Expression<Func<Contact, bool>> BuildGroup(AppDbContext db, SegmentRuleGroup group, DateTime nowUtc)
    {
        var parts = group.Rules.Select(r => BuildRule(db, r, nowUtc, validateOnly: false)!)
            .Concat(group.Groups.Select(g => BuildGroup(db, g, nowUtc)))
            .ToList();

        if (parts.Count == 0) return c => true;
        return group.Match == "any" ? parts.Aggregate(Or) : parts.Aggregate(And);
    }

    private static Expression<Func<Contact, bool>>? BuildRule(AppDbContext db, SegmentRule rule, DateTime nowUtc, bool validateOnly)
    {
        var field = rule.Field?.Trim() ?? string.Empty;
        var op = rule.Op?.Trim().ToLowerInvariant() ?? "eq";
        var value = rule.Value?.Trim();
        var values = (rule.Values ?? []).Select(v => v.Trim().ToLower()).Where(v => v.Length > 0).Distinct().ToList();

        string Require(string? v) => string.IsNullOrWhiteSpace(v) ? throw new ArgumentException($"The \"{field}\" rule needs a value.") : v;
        var dayRange = Catalogs.SegmentFieldCatalog.Days;
        int Days() => rule.Days is { } d && d >= dayRange.Min && d <= dayRange.Max
            ? d
            : throw new ArgumentException($"The \"{field}\" rule needs a number of days between {dayRange.Min} and {dayRange.Max}.");

        if (TextFields.TryGetValue(field, out var selector))
        {
            switch (op)
            {
                case "eq": { var v = Require(value).ToLower(); return validateOnly ? null : Text(selector, s => s != null && s.ToLower() == v); }
                case "neq": { var v = Require(value).ToLower(); return validateOnly ? null : Text(selector, s => s == null || s.ToLower() != v); }
                case "contains": { var v = Require(value).ToLower(); return validateOnly ? null : Text(selector, s => s != null && s.ToLower().Contains(v)); }
                case "in":
                    if (values.Count == 0) throw new ArgumentException($"The \"{field}\" rule needs at least one value.");
                    return validateOnly ? null : Text(selector, s => s != null && values.Contains(s.ToLower()));
                case "empty": return validateOnly ? null : Text(selector, s => s == null || s == "");
                case "notempty": return validateOnly ? null : Text(selector, s => s != null && s != "");
                default: throw new ArgumentException($"\"{rule.Op}\" is not an operator for \"{field}\".");
            }
        }

        switch (field.ToLowerInvariant())
        {
            case "tag":
            {
                var needle = "," + Require(value).ToLower() + ",";
                Expression<Func<Contact, bool>> has = c => c.Tags != null && ("," + c.Tags.ToLower().Replace(", ", ",") + ",").Contains(needle);
                return op switch
                {
                    "has" => validateOnly ? null : has,
                    "nothas" => validateOnly ? null : Not(has),
                    _ => throw new ArgumentException($"\"{rule.Op}\" is not an operator for tags (use has / notHas).")
                };
            }
            case "group":
            {
                if (!int.TryParse(Require(value), out var groupId)) throw new ArgumentException("Choose a group.");
                Expression<Func<Contact, bool>> member = c => c.GroupMemberships.Any(m => m.GroupId == groupId);
                return op switch
                {
                    "in" or "eq" => validateOnly ? null : member,
                    "notin" or "neq" => validateOnly ? null : Not(member),
                    _ => throw new ArgumentException($"\"{rule.Op}\" is not an operator for groups (use in / notIn).")
                };
            }
            case "createdat":
            {
                switch (op)
                {
                    case "withindays": { var since = nowUtc.AddDays(-Days()); return validateOnly ? null : c => c.CreatedAt >= since; }
                    case "olderthandays": { var before = nowUtc.AddDays(-Days()); return validateOnly ? null : c => c.CreatedAt < before; }
                    default: throw new ArgumentException($"\"{rule.Op}\" is not an operator for the created date (use withinDays / olderThanDays).");
                }
            }
            case "age":
            {
                // Age is never stored: it is compared through the date of birth, so a segment
                // stays right as birthdays pass and the comparison can use an index on the date.
                if (op == "empty") return validateOnly ? null : c => c.DateOfBirth == null;
                var ages = Catalogs.ContactFieldCatalog.AgeYears;
                if (!int.TryParse(Require(value), out var years) || years < ages.Min || years > ages.Max)
                    throw new ArgumentException($"An age rule needs a whole number of years between {ages.Min} and {ages.Max}.");
                var today = DateOnly.FromDateTime(nowUtc);
                switch (op)
                {
                    case "gte": { var bornOnOrBefore = today.AddYears(-years); return validateOnly ? null : c => c.DateOfBirth != null && c.DateOfBirth <= bornOnOrBefore; }
                    case "lte": { var bornAfter = today.AddYears(-(years + 1)); return validateOnly ? null : c => c.DateOfBirth != null && c.DateOfBirth > bornAfter; }
                    default: throw new ArgumentException($"\"{rule.Op}\" is not an operator for age (use gte / lte / empty).");
                }
            }
            case "consent":
            {
                // Value: "Email:marketing:OptedOut" — channel, topic, status.
                var parts = Require(value).Split(':');
                if (parts.Length != 3
                    || !Enum.TryParse<MessageChannel>(parts[0], true, out var channel)
                    || !Enum.TryParse<ConsentStatus>(parts[2], true, out var status))
                    throw new ArgumentException("A consent rule's value is Channel:topic:Status, e.g. WhatsApp:marketing:OptedIn.");
                var topic = ConsentTopics.Normalize(parts[1]);
                if (validateOnly) return null;
                Expression<Func<Contact, bool>> has = c => db.ContactConsents.Any(k => k.ContactId == c.Id && k.Channel == channel && k.Topic == topic && k.Status == status);
                return op is "neq" or "not" ? Not(has) : has;
            }
            case "opened":
            case "clicked":
            case "replied":
            case "received":
            {
                if (op is not ("withindays" or "notwithindays"))
                    throw new ArgumentException($"\"{rule.Op}\" is not an operator for {field} (use withinDays / notWithinDays).");
                var since = nowUtc.AddDays(-Days());
                if (validateOnly) return null;

                Expression<Func<Contact, bool>> did = field.ToLowerInvariant() switch
                {
                    "opened" => c => db.CampaignContacts.Any(cc => cc.ContactId == c.Id && cc.OpenedAt >= since),
                    "clicked" => c => db.CampaignContacts.Any(cc => cc.ContactId == c.Id && cc.ClickedAt >= since),
                    "replied" => c => db.CampaignContacts.Any(cc => cc.ContactId == c.Id && cc.RepliedAt >= since),
                    _ => c => db.CampaignContacts.Any(cc => cc.ContactId == c.Id && cc.SentAt >= since)
                };
                return op == "withindays" ? did : Not(did);
            }
        }

        throw new ArgumentException($"\"{rule.Field}\" is not a field a segment can use.");
    }

    // ── Expression plumbing ────────────────────────────────────────────────────────────────

    private static Expression<Func<Contact, bool>> Text(Expression<Func<Contact, string?>> selector, Expression<Func<string?, bool>> test)
    {
        var body = new Replace(test.Parameters[0], selector.Body).Visit(test.Body)!;
        return Expression.Lambda<Func<Contact, bool>>(body, selector.Parameters[0]);
    }

    private static Expression<Func<Contact, bool>> And(Expression<Func<Contact, bool>> a, Expression<Func<Contact, bool>> b) => Combine(a, b, Expression.AndAlso);
    private static Expression<Func<Contact, bool>> Or(Expression<Func<Contact, bool>> a, Expression<Func<Contact, bool>> b) => Combine(a, b, Expression.OrElse);

    private static Expression<Func<Contact, bool>> Not(Expression<Func<Contact, bool>> a) =>
        Expression.Lambda<Func<Contact, bool>>(Expression.Not(a.Body), a.Parameters[0]);

    private static Expression<Func<Contact, bool>> Combine(
        Expression<Func<Contact, bool>> a, Expression<Func<Contact, bool>> b, Func<Expression, Expression, BinaryExpression> join)
    {
        var body = new Replace(b.Parameters[0], a.Parameters[0]).Visit(b.Body)!;
        return Expression.Lambda<Func<Contact, bool>>(join(a.Body, body), a.Parameters[0]);
    }

    private sealed class Replace(Expression from, Expression to) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node) => node == from ? to : base.Visit(node);
    }
}
