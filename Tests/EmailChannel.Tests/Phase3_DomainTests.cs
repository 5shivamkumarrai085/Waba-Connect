using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Services.Email;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 3 — the sender gate. Mail is standard SMTP only, so a sender may send when it is active,
/// its connection is active and an SMTP server is configured; the mail server itself decides which
/// From addresses it accepts. The connection page's CanSend must give the same answer as the gate
/// in every state, or the wizard would offer a sender the server refuses (or hide one it accepts).
/// </summary>
public static class Phase3_DomainTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 3 — the sender gate");

        using var scope = harness.CreateScope();
        var connections = scope.ServiceProvider.GetRequiredService<IEmailConnectionService>();
        var gate = scope.ServiceProvider.GetRequiredService<IEmailSenderGate>();
        int? configId = null;

        try
        {
            var created = await connections.CreateAsync(new CreateEmailConnectionRequest
            {
                Name = $"{TestHarness.Prefix}-gate-{harness.Tag}",
                DisplayName = "Gate Test",
                EmailAddress = $"sender@{harness.Tag}.example.test"
            });
            configId = created.Id;
            var senderId = (await connections.GetSendersAsync(created.Id)).Single().Id;

            async Task<bool> ListedCanSend() => (await connections.GetSendersAsync(created.Id)).Single(s => s.Id == senderId).CanSend;

            run.Section("A connection with no SMTP server cannot send");
            var (beforeSetup, beforeReason) = await gate.CanSenderSendAsync(senderId);
            run.Check("the gate refuses a sender whose connection is not set up", !beforeSetup, beforeReason);
            run.Check("and the connection page agrees", !await ListedCanSend());

            run.Section("A configured, active sender may send");
            await connections.SaveProviderAsync(created.Id, new SaveEmailProviderRequest
            {
                Provider = "Smtp", SmtpHost = "smtp.example.invalid", SmtpPort = 587, SmtpSecurity = "StartTls", IsActive = true
            });
            var (allowed, allowedReason) = await gate.CanSenderSendAsync(senderId);
            run.Check("the gate allows it", allowed, allowedReason);
            run.Check("and the connection page agrees", await ListedCanSend());

            run.Section("A deactivated sender is refused");
            var sender = (await connections.GetSendersAsync(created.Id)).Single();
            await connections.UpdateSenderAsync(created.Id, senderId, new SaveEmailSenderIdentityRequest
            {
                DisplayName = sender.DisplayName, EmailAddress = sender.EmailAddress, IsDefault = true, IsActive = false
            });
            var (inactiveAllowed, inactiveReason) = await gate.CanSenderSendAsync(senderId);
            run.Check("the gate refuses it, naming the address", !inactiveAllowed && inactiveReason?.Contains(sender.EmailAddress) == true, inactiveReason);
            run.Check("and the connection page agrees", !await ListedCanSend());

            await connections.UpdateSenderAsync(created.Id, senderId, new SaveEmailSenderIdentityRequest
            {
                DisplayName = sender.DisplayName, EmailAddress = sender.EmailAddress, IsDefault = true, IsActive = true
            });

            run.Section("A disconnected connection is refused");
            await connections.DisconnectAsync(created.Id);
            var (disconnectedAllowed, disconnectedReason) = await gate.CanSenderSendAsync(senderId);
            run.Check("the gate refuses it", !disconnectedAllowed, disconnectedReason);
            run.Check("and the connection page agrees", !await ListedCanSend());

            run.Section("A sender that no longer exists is refused");
            var (missingAllowed, missingReason) = await gate.CanSenderSendAsync(int.MaxValue);
            run.Check("the gate refuses it with a reason", !missingAllowed && missingReason is not null, missingReason);
        }
        catch (Exception ex)
        {
            run.Error("sender gate phase threw", ex);
        }
        finally
        {
            if (configId is { } id)
            {
                try { await connections.DeleteAsync(id); } catch { /* best-effort cleanup */ }
            }
        }
    }
}
