using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Ends a customer's automated conversations: open bot flow sessions and AI assistant sessions,
/// and (on opt-in) a Stop Bot suppression. Shared by the Stop Bot keywords and the STOP/START
/// consent keywords so both leave the conversation in the same clean state.
/// </summary>
public interface IBotSessionCleanup
{
    /// <summary>Closes every open flow and assistant session for the phone, on every connection.</summary>
    Task<int> EndAutomationAsync(string phone, CancellationToken ct = default);

    /// <summary>Lifts the phone's active Stop Bot suppressions (kept as history, ended now).</summary>
    Task<int> LiftSuppressionsAsync(string phone, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class BotSessionCleanup : IBotSessionCleanup
{
    private readonly AppDbContext _db;

    public BotSessionCleanup(AppDbContext db) => _db = db;

    /// <summary>Bot state keys phones without the "+".</summary>
    private static string Key(string phone) => phone.Replace("+", string.Empty).Trim();

    public async Task<int> EndAutomationAsync(string phone, CancellationToken ct = default)
    {
        var key = Key(phone);
        var now = DateTime.UtcNow;

        var flows = await _db.ConversationStates
            .Where(c => c.PhoneNumber == key && c.Status == "Active")
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, "Completed").SetProperty(c => c.UpdatedAt, now), ct);

        var assistants = await _db.AiSessions
            .Where(a => a.PhoneNumber == key && a.IsActive)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsActive, false).SetProperty(a => a.UpdatedAt, now), ct);

        return flows + assistants;
    }

    public async Task<int> LiftSuppressionsAsync(string phone, CancellationToken ct = default)
    {
        var key = Key(phone);
        var now = DateTime.UtcNow;
        // A minute in the past rather than "now": every server compares against its own clock,
        // and one running slightly behind would otherwise still see the suppression as active.
        var endedAt = now.AddMinutes(-1);
        return await _db.BotSuppressions
            .Where(b => b.PhoneNumber == key && (b.ResumeAt == null || b.ResumeAt > now))
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.ResumeAt, endedAt), ct);
    }
}
