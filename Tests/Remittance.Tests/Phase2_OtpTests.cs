using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Services.Remittance;

namespace Remittance.Tests;

/// <summary>The development OTP service: server-side verification, hashing, expiry, attempts, resend throttling.</summary>
public static class Phase2_OtpTests
{
    private const string Purpose = "zz-otp-test";

    public static async Task RunAsync(RemittanceHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 2 — OTP (development mock, real verification)");

        try
        {
            await using var scope = harness.CreateScope();
            var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();

            run.Section("Issue and verify");
            var subject = $"zzrt-{Guid.NewGuid():N}";
            var issued = await otp.IssueAsync(Purpose, subject, "60990000001", null, CancellationToken.None);
            run.Check("a code is issued", issued.Issued, issued.Error);
            run.Check("the mock exposes the code in Development (it delivers nothing)", issued.DevCode is { Length: 6 }, issued.DevCode);

            var stored = (string?)await harness.ScalarAsync("SELECT \"CodeHash\" FROM \"OtpChallenges\" WHERE \"SubjectKey\" = @p0", subject);
            run.Check("only a 64-hex keyed hash is stored, never the code", stored is { Length: 64 } && stored != issued.DevCode && !stored.Contains(issued.DevCode!), stored);

            var wrong = await otp.VerifyAsync(Purpose, subject, Wrong(issued.DevCode!), CancellationToken.None);
            run.Check("a wrong code fails with attempts left", !wrong.Success && wrong.AttemptsRemaining == 4 && !wrong.Locked, wrong.Error);

            var right = await otp.VerifyAsync(Purpose, subject, issued.DevCode!, CancellationToken.None);
            run.Check("the right code verifies", right.Success, right.Error);

            run.Section("Throttling and expiry");
            var other = $"zzrt-{Guid.NewGuid():N}";
            var first = await otp.IssueAsync(Purpose, other, "60990000002", null, CancellationToken.None);
            var tooSoon = await otp.IssueAsync(Purpose, other, "60990000002", null, CancellationToken.None);
            run.Check("an immediate resend is refused (cooldown)", first.Issued && !tooSoon.Issued && tooSoon.RetryAfterSeconds > 0, tooSoon.Error);
            await otp.VerifyAsync(Purpose, other, Wrong(first.DevCode!), CancellationToken.None); // one guess used

            await harness.ExecAsync("UPDATE \"OtpChallenges\" SET \"ExpiresAt\" = now() - interval '1 minute' WHERE \"SubjectKey\" = @p0", other);
            var expired = await otp.VerifyAsync(Purpose, other, first.DevCode!, CancellationToken.None);
            run.Check("an expired code is refused even when correct", !expired.Success && expired.Error!.Contains("expired"), expired.Error);

            await harness.ExecAsync("UPDATE \"OtpChallenges\" SET \"LastSentAt\" = now() - interval '2 minutes' WHERE \"SubjectKey\" = @p0", other);
            var resent = await otp.IssueAsync(Purpose, other, "60990000002", null, CancellationToken.None);
            run.Check("after the cooldown a new code can be requested", resent.Issued && resent.DevCode != null, resent.Error);
            if (first.DevCode == resent.DevCode)
            {
                run.Skip("the superseded code no longer works", "the new code happened to equal the old one");
            }
            else
            {
                var oldCode = await otp.VerifyAsync(Purpose, other, first.DevCode!, CancellationToken.None);
                run.Check("the superseded code no longer works", !oldCode.Success);
                run.Check("attempts carry over a resend (no extra guesses)", oldCode.AttemptsRemaining == 3, oldCode.AttemptsRemaining.ToString());
            }

            run.Section("Lockout");
            var locked = $"zzrt-{Guid.NewGuid():N}";
            var lockCode = await otp.IssueAsync(Purpose, locked, "60990000003", null, CancellationToken.None);
            OtpVerifyResult last = new(false, null, 0, false);
            for (var i = 0; i < 5; i++) last = await otp.VerifyAsync(Purpose, locked, Wrong(lockCode.DevCode!), CancellationToken.None);
            run.Check("five wrong codes lock the challenge", last.Locked, last.Error);
            var afterLock = await otp.VerifyAsync(Purpose, locked, lockCode.DevCode!, CancellationToken.None);
            run.Check("once locked, even the right code is refused", !afterLock.Success && afterLock.Locked);
            var reissue = await otp.IssueAsync(Purpose, locked, "60990000003", null, CancellationToken.None);
            run.Check("a locked subject cannot request a new code", !reissue.Issued, reissue.Error);
        }
        catch (Exception ex)
        {
            run.Error("Phase 2 aborted", ex);
        }
    }

    /// <summary>A different code of the same length.</summary>
    private static string Wrong(string code) => new(code.Select(c => c == '9' ? '0' : (char)(c + 1)).ToArray());
}
