using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Remittance.Providers;

namespace WhatsAppCampaignApi.Services.Remittance;

/// <param name="DevCode">Only in Development with the mock provider and ExposeCodeInFlow on; otherwise null.</param>
public sealed record OtpIssueResult(bool Issued, DateTime? ExpiresAt, string? Error, int? RetryAfterSeconds, string? DevCode);

public sealed record OtpVerifyResult(bool Success, string? Error, int AttemptsRemaining, bool Locked);

public interface IOtpService
{
    /// <summary>Issues (or re-issues) a code for <paramref name="subjectKey"/> and hands it to the configured provider.</summary>
    Task<OtpIssueResult> IssueAsync(string purpose, string subjectKey, string destination, int? connectionId, CancellationToken ct);

    /// <summary>Checks a code server-side: expiry, attempt limit and a constant-time comparison of keyed hashes.</summary>
    Task<OtpVerifyResult> VerifyAsync(string purpose, string subjectKey, string code, CancellationToken ct);
}

/// <summary>
/// Provider-independent one-time passwords. Generation (CSPRNG), storage (HMAC-SHA256 keyed from
/// Encryption:Key — the code itself is never stored or logged), expiry, attempt limits and resend
/// throttling all live here, so they behave identically whichever provider delivers the code.
/// </summary>
public sealed class OtpService : IOtpService
{
    private readonly AppDbContext _db;
    private readonly IRemittanceProviderRegistry _providers;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly IWebHostEnvironment _environment;
    private readonly byte[] _hashKey;

    public OtpService(
        AppDbContext db,
        IRemittanceProviderRegistry providers,
        IOptionsMonitor<RemittanceOptions> options,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _db = db;
        _providers = providers;
        _options = options;
        _environment = environment;

        var root = configuration["Encryption:Key"];
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Encryption:Key is required to protect one-time codes.");
        _hashKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(root), 32, info: Encoding.UTF8.GetBytes("WabaConnect.Otp.v1"));
    }

    public async Task<OtpIssueResult> IssueAsync(string purpose, string subjectKey, string destination, int? connectionId, CancellationToken ct)
    {
        var settings = _options.CurrentValue.Otp;
        var now = DateTime.UtcNow;

        var previous = await LatestAsync(purpose, subjectKey, ct);
        if (previous is not null)
        {
            if (previous.LockedAt is not null)
                return new(false, null, "Too many wrong codes. Please start again from the chat.", null, null);

            var nextAllowed = previous.LastSentAt.AddSeconds(settings.ResendCooldownSeconds);
            if (previous.VerifiedAt is null && now < nextAllowed)
            {
                var wait = (int)Math.Ceiling((nextAllowed - now).TotalSeconds);
                return new(false, previous.ExpiresAt, $"Please wait {wait}s before requesting another code.", wait, null);
            }

            if (previous.SendCount > settings.MaxResends)
                return new(false, null, "Too many codes requested. Please start again from the chat.", null, null);
        }

        // Per number, across sessions: starting the form again must not reset the allowance.
        var hourAgo = now.AddHours(-1);
        var dayAgo = now.AddDays(-1);
        var sentToNumber = await _db.OtpChallenges.AsNoTracking()
            .Where(c => c.Purpose == purpose && c.Destination == destination && c.CreatedAt > dayAgo)
            .GroupBy(_ => 1)
            .Select(g => new { Day = g.Count(), Hour = g.Count(c => c.CreatedAt > hourAgo) })
            .FirstOrDefaultAsync(ct);
        if (sentToNumber is not null
            && (sentToNumber.Hour >= settings.MaxPerDestinationPerHour || sentToNumber.Day >= settings.MaxPerDestinationPerDay))
        {
            return new(false, null, "Too many codes were requested for this number. Please try again later.", null, null);
        }

        var provider = _providers.Otp();
        var code = NewCode(settings.Length);
        var challenge = new OtpChallenge
        {
            Purpose = purpose,
            SubjectKey = subjectKey,
            Destination = destination,
            CodeHash = Hash(purpose, subjectKey, code),
            Provider = provider.ProviderName,
            ExpiresAt = now.AddSeconds(settings.TtlSeconds),
            // Attempts carry over a resend, so asking for a new code never buys more guesses.
            Attempts = previous?.Attempts ?? 0,
            MaxAttempts = settings.MaxAttempts,
            SendCount = (previous?.SendCount ?? 0) + 1,
            LastSentAt = now
        };

        // Saved before it is sent: a code that reached the customer must always be verifiable.
        _db.OtpChallenges.Add(challenge);
        await _db.SaveChangesAsync(ct);

        // Two requests at once (WhatsApp retrying the opening request, or the form reopened) both
        // passed the cooldown check above, against the same previous challenge. Any challenge
        // inserted after that one and before ours is such a twin: the older wins and this one
        // stands down, so the customer gets one code rather than two where only the last works.
        var previousId = previous?.Id ?? 0;
        var racedBy = await _db.OtpChallenges.AsNoTracking()
            .Where(c => c.Purpose == purpose && c.SubjectKey == subjectKey && c.Id > previousId && c.Id < challenge.Id)
            .OrderBy(c => c.Id)
            .Select(c => (DateTime?)c.ExpiresAt)
            .FirstOrDefaultAsync(ct);
        if (racedBy is { } otherExpiry)
        {
            await Retire(challenge);
            return new(true, otherExpiry, null, null, null);
        }

        var delivery = await provider.SendAsync(new OtpDeliveryRequest(destination, code, purpose, challenge.ExpiresAt, connectionId), ct);
        if (!delivery.Sent)
        {
            await Retire(challenge);
            return new(false, null, "We couldn't send the verification code. Please try again shortly.", null, null);
        }

        var exposeForDev = !provider.DeliversCode && _environment.IsDevelopment() && settings.Mock.ExposeCodeInFlow;
        return new(true, challenge.ExpiresAt, null, null, exposeForDev ? code : null);
    }

    public async Task<OtpVerifyResult> VerifyAsync(string purpose, string subjectKey, string code, CancellationToken ct)
    {
        var challenge = await LatestAsync(purpose, subjectKey, ct);
        if (challenge is null) return new(false, "Request a verification code first.", 0, false);
        if (challenge.VerifiedAt is not null) return new(true, null, challenge.MaxAttempts - challenge.Attempts, false);
        if (challenge.LockedAt is not null) return new(false, "Too many wrong codes. Please start again from the chat.", 0, true);
        if (DateTime.UtcNow > challenge.ExpiresAt) return new(false, "This code has expired. Tap Resend code.", challenge.MaxAttempts - challenge.Attempts, false);

        // Count the attempt in the database before comparing, conditionally, so two concurrent
        // guesses cannot both slip under the limit.
        var counted = await _db.OtpChallenges
            .Where(c => c.Id == challenge.Id && c.Attempts < c.MaxAttempts && c.VerifiedAt == null && c.LockedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.Attempts, c => c.Attempts + 1), ct);
        if (counted == 0)
        {
            await LockAsync(challenge.Id, ct);
            return new(false, "Too many wrong codes. Please start again from the chat.", 0, true);
        }

        var attempts = challenge.Attempts + 1;
        var supplied = Encoding.ASCII.GetBytes(Hash(purpose, subjectKey, (code ?? string.Empty).Trim()));
        var expected = Encoding.ASCII.GetBytes(challenge.CodeHash);

        if (CryptographicOperations.FixedTimeEquals(supplied, expected))
        {
            await _db.OtpChallenges.Where(c => c.Id == challenge.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.VerifiedAt, DateTime.UtcNow), ct);
            return new(true, null, challenge.MaxAttempts - attempts, false);
        }

        var remaining = Math.Max(0, challenge.MaxAttempts - attempts);
        if (remaining == 0)
        {
            await LockAsync(challenge.Id, ct);
            return new(false, "Too many wrong codes. Please start again from the chat.", 0, true);
        }

        return new(false, remaining == 1 ? "Incorrect code. 1 attempt left." : $"Incorrect code. {remaining} attempts left.", remaining, false);
    }

    private Task<OtpChallenge?> LatestAsync(string purpose, string subjectKey, CancellationToken ct) =>
        _db.OtpChallenges.AsNoTracking()
            .Where(c => c.Purpose == purpose && c.SubjectKey == subjectKey)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Removes a challenge whose code was never delivered, so the latest challenge is always the
    /// one the customer actually received (verification and resends read the latest).
    /// </summary>
    private async Task Retire(OtpChallenge challenge)
    {
        var id = challenge.Id;
        _db.Entry(challenge).State = EntityState.Detached;
        await _db.OtpChallenges.Where(c => c.Id == id).ExecuteDeleteAsync();
    }

    private Task LockAsync(long id, CancellationToken ct) =>
        _db.OtpChallenges.Where(c => c.Id == id && c.LockedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.LockedAt, DateTime.UtcNow), ct);

    private string Hash(string purpose, string subjectKey, string code)
    {
        var mac = HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes($"{purpose}\n{subjectKey}\n{code}"));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    private static string NewCode(int length)
    {
        var max = (int)Math.Pow(10, length);
        return RandomNumberGenerator.GetInt32(0, max).ToString(new string('0', length));
    }
}
