using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Queue;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Sends the queued emails.
///
/// <para>
/// Runs several concurrent loops per instance, and any number of instances. The real send rate is
/// bounded by the cross-instance token bucket rather than by loop count, so adding workers adds
/// throughput up to the provider's configured rate and no further — which is what stops horizontal
/// scaling from becoming an accidental way to get an SES account throttled.
/// </para>
/// </summary>
public class EmailDispatchWorker : BackgroundService
{
    private readonly IJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<EmailDispatchWorker> _logger;

    public EmailDispatchWorker(
        IJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EmailOptions> options,
        ILogger<EmailDispatchWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerCount = Math.Max(1, _options.CurrentValue.Dispatch.WorkerCount);

        _logger.LogInformation("Email dispatch starting {WorkerCount} worker loop(s).", workerCount);

        // Independent loops rather than one loop with internal parallelism: each claims and
        // completes its own leases, so a slow provider call blocks only its own loop.
        var loops = Enumerable.Range(0, workerCount)
            .Select(index => RunLoopAsync(
                $"{Environment.MachineName}-send-{index}-{Guid.NewGuid().ToString("N")[..4]}",
                stoppingToken))
            .ToArray();

        await Task.WhenAll(loops);

        _logger.LogInformation("Email dispatch stopped.");
    }

    private async Task RunLoopAsync(string workerId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;

            try
            {
                claimed = await ClaimAndSendAsync(workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email dispatch loop {WorkerId} failed a cycle.", workerId);
            }

            try
            {
                var delay = claimed > 0
                    ? _options.CurrentValue.Queue.PollIntervalMs
                    : _options.CurrentValue.Queue.IdleBackoffMs;

                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<int> ClaimAndSendAsync(string workerId, CancellationToken ct)
    {
        var batchSize = Math.Max(1, _options.CurrentValue.Dispatch.BatchSize);

        var leases = await _queue.ClaimAsync(new QueueClaimRequest
        {
            QueueName = QueueNames.EmailSend,
            WorkerId = workerId,
            BatchSize = batchSize
        }, ct);

        if (leases.Count == 0) return 0;

        foreach (var lease in leases)
        {
            // Not passing ct into the send itself: a shutdown mid-send would otherwise abandon a
            // message whose fate is unknown. Each send is allowed to finish, and the loop stops
            // afterwards.
            await ProcessOneAsync(lease, ct);
        }

        return leases.Count;
    }

    private async Task ProcessOneAsync(QueueLease lease, CancellationToken ct)
    {
        EmailSendJob? job;

        try
        {
            job = JsonSerializer.Deserialize<EmailSendJob>(lease.Payload);
        }
        catch (JsonException ex)
        {
            await _queue.FailAsync(lease, new QueueFailure($"Malformed payload: {ex.Message}", IsTransient: false), ct);
            return;
        }

        if (job is null || job.CampaignContactId <= 0)
        {
            await _queue.FailAsync(lease, new QueueFailure("Payload has no recipient id.", IsTransient: false), ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<AppDbContext>();
        var rateLimiter = services.GetRequiredService<IEmailRateLimiter>();
        var suppression = services.GetRequiredService<IEmailSuppressionService>();
        var providerFactory = services.GetRequiredService<IEmailProviderFactory>();
        var mimeBuilder = services.GetRequiredService<IMimeMessageBuilder>();
        var unsubscribe = services.GetRequiredService<IUnsubscribeTokenService>();
        var recorder = services.GetRequiredService<IEmailSendRecorder>();
        var trackingService = services.GetRequiredService<IEmailTrackingService>();

        // Declared out here so the catch blocks below can report against the right recipient and
        // give back a token they may have reserved. Inside the try they were invisible to the
        // handlers, which is how a failure came to leave no trace anywhere an operator looks.
        CampaignContact? failingRecipient = null;
        int? reservedForConnection = null;

        try
        {
            var context = await LoadSendContextAsync(dbContext, job, ct);

            if (context is null)
            {
                // The campaign or recipient is gone, or already handled. Nothing wrong, so this
                // completes rather than dead-lettering.
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var (campaign, recipient, detail, template) = context.Value;
            failingRecipient = recipient;

            // Ensure recipient has a tracking ID for open/click tracking
            if (string.IsNullOrEmpty(recipient.TrackingId))
            {
                recipient.TrackingId = trackingService.GenerateTrackingId();
                await dbContext.SaveChangesAsync(ct);
            }

            // The idempotency guard. A redelivered job — after a lease lapsed, or a crash between
            // sending and committing — must not send again. Anything other than Pending means
            // this recipient has already been dealt with.
            if (recipient.Status != MessageStatus.Pending)
            {
                _logger.LogDebug(
                    "Recipient {RecipientId} is already {Status}; skipping a duplicate send.",
                    recipient.Id, recipient.Status);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // A send attempt stamped with no provider id means a previous attempt may have
            // reached the provider before the process died. Sending again could duplicate real
            // mail, so it is escalated rather than retried: a possible duplicate is worse than a
            // message an operator has to re-send deliberately.
            if (recipient.SendAttemptedAt is not null && string.IsNullOrEmpty(recipient.ProviderMessageId))
            {
                _logger.LogWarning(
                    "Recipient {RecipientId} has an in-flight send attempt from {AttemptedAt} with no provider id. "
                  + "Not re-sending, to avoid a duplicate.",
                    recipient.Id, recipient.SendAttemptedAt);

                recipient.Status = MessageStatus.Failed;
                recipient.ErrorMessage =
                    "A previous send attempt did not complete cleanly. Not retried automatically to avoid "
                  + "sending twice — re-send this recipient manually if nothing arrived.";
                await dbContext.SaveChangesAsync(ct);

                await _queue.CompleteAsync(lease, ct);
                return;
            }

            if (campaign.Status is CampaignStatus.Cancelled or CampaignStatus.Paused)
            {
                // Mid-flight cancellation. The job is completed and the recipient left Pending,
                // so resuming a paused campaign can pick it up again.
                _logger.LogDebug("Campaign {CampaignId} is {Status}; not sending.", campaign.Id, campaign.Status);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            var emailAddress = recipient.Contact.Email;
            if (string.IsNullOrWhiteSpace(emailAddress))
            {
                await FailRecipientAsync(dbContext, recipient, "The contact has no email address.", ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // Second suppression check. The first was at expansion, and a complaint or a bounce
            // can arrive in the minutes between — which for a large campaign is routine, not
            // theoretical.
            if (await suppression.IsSuppressedAsync(emailAddress, campaign.ConnectionId, ct))
            {
                recipient.Status = MessageStatus.Suppressed;
                recipient.ErrorMessage = "The address was suppressed before this message was sent.";
                await dbContext.SaveChangesAsync(ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            if (campaign.ConnectionId is not { } connectionId)
            {
                await FailRecipientAsync(dbContext, recipient, "The campaign has no email connection.", ct);
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // Rate limiting. Returning the job to the queue rather than sleeping keeps the worker
            // free for another campaign's traffic, and lets a differently-limited connection make
            // progress meanwhile.
            var granted = await rateLimiter.TryReserveAsync(connectionId, 1, ct);
            if (granted > 0) reservedForConnection = connectionId;

            if (granted == 0)
            {
                await _queue.FailAsync(lease, new QueueFailure(
                    "Rate limited; waiting for send capacity.",
                    IsTransient: true,
                    RetryAfter: TimeSpan.FromSeconds(1)), ct);
                return;
            }

            var (provider, providerContext) = await providerFactory.ResolveAsync(connectionId, ct);

            var message = BuildMessage(
                campaign, recipient, detail, template, emailAddress!,
                providerContext, mimeBuilder, unsubscribe, trackingService, out var buildFailure);

            if (message is null)
            {
                await rateLimiter.ReleaseAsync(connectionId, 1, ct);
                reservedForConnection = null;

                var reason = buildFailure ?? "The message could not be rendered.";
                await FailRecipientAsync(dbContext, recipient, reason, ct);
                await recorder.RecordJobFailureAsync(recipient, reason, ct);
                await recorder.TryFinalizeCampaignAsync(campaign.Id, ct);

                // Completed, not failed: retrying cannot help — the template needs a value it
                // does not have, and only an operator can supply it.
                await _queue.CompleteAsync(lease, ct);
                return;
            }

            // Stamped before the provider call and committed immediately, so a crash in the next
            // few milliseconds is detectable as "possibly sent" by the guard above. SES offers no
            // idempotent send, so this marker is the only protection there is.
            recipient.SendAttemptedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            var result = await provider.SendAsync(message, providerContext, CancellationToken.None);

            if (result.Success)
            {
                // CancellationToken.None from here on, deliberately. The message is already on
                // the wire: cancelling the write that records it — because the host happens to be
                // shutting down — would lose all evidence the send happened, and the job would
                // then be redelivered and send the same mail again. Recording an outcome that has
                // already occurred is not a cancellable operation.
                await recorder.RecordSentAsync(
                    campaign, recipient, message, result, provider.ProviderName, CancellationToken.None);

                // Cheap and idempotent, so it runs after every recipient rather than needing a
                // sweeper: whichever worker happens to finish last is the one that lands it.
                await recorder.TryFinalizeCampaignAsync(campaign.Id, CancellationToken.None);

                reservedForConnection = null;
                await _queue.CompleteAsync(lease, CancellationToken.None);
                return;
            }

            // The provider decided whether this is worth retrying. A transient failure returns
            // the token — the send never happened, so it should not count against the rate.
            await rateLimiter.ReleaseAsync(connectionId, 1, CancellationToken.None);
            reservedForConnection = null;

            if (result.IsTransient && !lease.IsFinalAttempt)
            {
                // Left Pending so a retry can send it. The attempt marker is cleared, because the
                // provider told us it did not accept the message — so there is no in-flight send
                // to be cautious about. Also uncancellable: leaving the marker set would make the
                // next attempt treat this as a possible duplicate and refuse to send at all.
                recipient.SendAttemptedAt = null;
                await dbContext.SaveChangesAsync(CancellationToken.None);

                await _queue.FailAsync(lease, new QueueFailure(
                    result.ErrorMessage ?? "Transient send failure.",
                    IsTransient: true,
                    result.RetryAfter), CancellationToken.None);
                return;
            }

            await recorder.RecordFailedAsync(
                campaign, recipient, message, result, provider.ProviderName, CancellationToken.None);

            await recorder.TryFinalizeCampaignAsync(campaign.Id, CancellationToken.None);

            await _queue.FailAsync(lease, new QueueFailure(
                result.ErrorMessage ?? "Send failed.", result.IsTransient), CancellationToken.None);
        }
        catch (KeyNotFoundException ex)
        {
            // A missing connection or configuration. Permanent — the operator has to fix it.
            _logger.LogError(ex, "Email send job {JobId} has an unusable configuration.", lease.JobId);
            await AbandonAsync(dbContext, rateLimiter, recorder, lease, failingRecipient,
                reservedForConnection, ex.Message, isTransient: false);
        }
        catch (InvalidOperationException ex)
        {
            // A disconnected connection, or an undecryptable credential.
            _logger.LogError(ex, "Email send job {JobId} cannot proceed.", lease.JobId);
            await AbandonAsync(dbContext, rateLimiter, recorder, lease, failingRecipient,
                reservedForConnection, ex.Message, isTransient: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure on email send job {JobId}.", lease.JobId);
            await AbandonAsync(dbContext, rateLimiter, recorder, lease, failingRecipient,
                reservedForConnection, ex.Message, isTransient: true);
        }
    }

    /// <summary>
    /// Ends a job that threw, leaving the same trail a provider rejection would.
    ///
    /// <para>
    /// Previously these paths only dead-lettered the queue row. The recipient stayed
    /// <see cref="MessageStatus.Pending"/> with no error, nothing reached the activity log, and
    /// the reserved rate-limit token was never given back — so the campaign looked like it had
    /// simply not got round to that recipient yet, and the connection quietly lost send capacity
    /// with every failure.
    /// </para>
    /// <para>
    /// A transient failure leaves the recipient Pending on purpose: the retry has to be able to
    /// pick it up. Only a permanent one marks it Failed.
    /// </para>
    /// </summary>
    private async Task AbandonAsync(
        AppDbContext dbContext,
        IEmailRateLimiter rateLimiter,
        IEmailSendRecorder recorder,
        QueueLease lease,
        CampaignContact? recipient,
        int? reservedForConnection,
        string reason,
        bool isTransient)
    {
        // CancellationToken.None throughout: this runs while the host may already be shutting
        // down, and an unrecorded failure is exactly the state being fixed here.
        if (reservedForConnection is { } connectionId)
        {
            try
            {
                await rateLimiter.ReleaseAsync(connectionId, 1, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Never let the cleanup replace the real error with a less useful one.
                _logger.LogWarning(ex, "Could not return the rate-limit token for connection {ConnectionId}.",
                    connectionId);
            }
        }

        if (recipient is not null && !isTransient)
        {
            try
            {
                await FailRecipientAsync(dbContext, recipient, reason, CancellationToken.None);
                await recorder.RecordJobFailureAsync(recipient, reason, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not record the failure for recipient {RecipientId}.", recipient.Id);
            }
        }

        await _queue.FailAsync(lease, new QueueFailure(reason, isTransient), CancellationToken.None);
    }

    private static async Task<(Campaign Campaign, CampaignContact Recipient, EmailCampaignDetail Detail, EmailTemplate Template)?>
        LoadSendContextAsync(AppDbContext dbContext, EmailSendJob job, CancellationToken ct)
    {
        var recipient = await dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .Include(cc => cc.Contact)
            .FirstOrDefaultAsync(cc => cc.Id == job.CampaignContactId, ct);

        if (recipient?.Contact is null) return null;

        var campaign = await dbContext.Campaigns
            .IgnoreQueryFilters()
            .Include(c => c.EmailTemplate)
            .Include(c => c.EmailDetail)!
                .ThenInclude(d => d!.SenderIdentity)
            .Include(c => c.Variables)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == job.CampaignId, ct);

        if (campaign is null || campaign.IsDeleted) return null;
        if (campaign.EmailDetail is null || campaign.EmailTemplate is null) return null;

        return (campaign, recipient, campaign.EmailDetail, campaign.EmailTemplate);
    }

    /// <summary>
    /// Renders the template for one recipient.
    /// </summary>
    /// <returns>
    /// The message, or null with <paramref name="failureReason"/> set when this recipient cannot
    /// be sent to. The reason is returned rather than logged so the caller can put it on the
    /// recipient row, where an operator will actually see it.
    /// </returns>
    private EmailMessage? BuildMessage(
        Campaign campaign,
        CampaignContact recipient,
        EmailCampaignDetail detail,
        EmailTemplate template,
        string emailAddress,
        EmailProviderContext providerContext,
        IMimeMessageBuilder mimeBuilder,
        IUnsubscribeTokenService unsubscribe,
        IEmailTrackingService trackingService,
        out string? failureReason)
    {
        failureReason = null;

        var sender = detail.SenderIdentity;
        if (sender is null)
        {
            failureReason = "The message could not be rendered — the sender identity is missing.";
            return null;
        }

        var contact = recipient.Contact;

        // Contact fields first, then the campaign's own variables — so a campaign variable can
        // deliberately override a contact field, which is how "use this company name for everyone
        // in this send" is expressed.
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = contact.Name,
            ["user_name"] = contact.Name,
            ["contact_name"] = contact.Name,
            ["first_name"] = contact.Name?.Split(' ').FirstOrDefault(),
            ["email"] = contact.Email,
            ["phone"] = contact.Phone,
            ["contact_phone"] = contact.Phone,
            ["company"] = contact.Company,
            ["company_name"] = contact.Company,
            ["city"] = contact.City,
            ["country"] = contact.Country,
            ["site_name"] = sender.DisplayName
        };

        foreach (var variable in campaign.Variables)
        {
            // The same merge-field convention the WhatsApp path uses, so an operator's mental
            // model carries across channels.
            var value = variable.MergeField switch
            {
                "@name" => contact.Name,
                "@phone" => contact.Phone,
                "@email" => contact.Email,
                _ => variable.VariableValue
            };

            values[variable.VariableName] = value;
        }

        var subject = MergeFieldRenderer.Render(
            string.IsNullOrWhiteSpace(detail.SubjectOverride) ? template.Subject : detail.SubjectOverride,
            values);

        var body = MergeFieldRenderer.Render(template.BodyHtml, values);
        var text = MergeFieldRenderer.Render(template.TextBody, values);

        // Refuse rather than deliver a literal placeholder. The seeded templates put their link
        // variables inside href attributes, so an unfilled one produces <a href="{reset_link}">
        // — a broken link that mailbox providers score as obfuscation, which is how a
        // legitimate message ends up in the spam folder.
        //
        // The subject matters as much as the body: "Welcome to {site_name}" in a subject line is
        // both embarrassing and a strong spam signal.
        var unresolved = MergeFieldRenderer.Extract(subject + " " + body)
            .Concat(MergeFieldRenderer.Extract(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unresolved.Count > 0)
        {
            failureReason =
                $"Not sent: {string.Join(", ", unresolved.Select(f => "{" + f + "}"))} "
              + (unresolved.Count == 1 ? "has no value." : "have no value.")
              + " Fill it in on the campaign's Variables step, or use a template that does not need it.";
            return null;
        }

        // A message with nothing in it is not worth sending, and one carrying List-Unsubscribe
        // headers and no content looks exactly like the spam those headers exist to prevent.
        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(text))
        {
            failureReason = "Not sent: the template rendered to an empty message.";
            return null;
        }

        var fromDomain = sender.EmailAddress.Contains('@') ? sender.EmailAddress.Split('@')[1] : "localhost";

        var headers = new Dictionary<string, string>
        {
            // RFC 8058 one-click unsubscribe. Both headers are required together: without the
            // Post header, Gmail and Outlook will not show the unsubscribe affordance at all,
            // and recipients use the spam button instead — which costs far more reputation.
            ["List-Unsubscribe"] = $"<{unsubscribe.BuildUnsubscribeUrl(emailAddress, campaign.Id, recipient.ContactId)}>",
            ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",

            // Our own correlation id, echoed on replies, so an inbound message can be tied back
            // to the exact campaign and recipient even when the provider id is unavailable.
            ["X-Omni-Campaign-Id"] = campaign.Id.ToString(),
            ["X-Omni-Recipient-Id"] = recipient.Id.ToString()
        };

        if (!string.IsNullOrWhiteSpace(template.PreheaderText))
        {
            // Prepended to the body as a hidden element, which is the only mechanism mail clients
            // actually honour — they take the preheader from the first text in the body. It was
            // previously written to an X-Omni-Preheader header, which nothing reads, so the
            // feature the template entity documents never worked.
            //
            // The preheader is rendered through the same values, so it cannot smuggle an
            // unresolved placeholder past the check above.
            var preheader = MergeFieldRenderer.Render(template.PreheaderText, values);

            body = $"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">"
                 + $"{System.Net.WebUtility.HtmlEncode(preheader)}</div>{body}";
        }

        // Inject click-tracking redirect URLs if enabled
        if (detail.TrackClicks && !string.IsNullOrEmpty(recipient.TrackingId) && !string.IsNullOrWhiteSpace(body))
        {
            body = RewriteLinks(body, recipient.TrackingId, trackingService);
        }

        // Inject open-tracking 1x1 transparent pixel if enabled
        if (detail.TrackOpens && !string.IsNullOrEmpty(recipient.TrackingId))
        {
            var openUrl = trackingService.BuildOpenUrl(recipient.TrackingId);
            var pixelTag = $"<img src=\"{openUrl}\" width=\"1\" height=\"1\" alt=\"\" style=\"display:none;max-height:0;overflow:hidden;mso-hide:all;\" />";
            if (!string.IsNullOrWhiteSpace(body))
            {
                body = body.Contains("</body>", StringComparison.OrdinalIgnoreCase)
                    ? body.Replace("</body>", $"{pixelTag}</body>", StringComparison.OrdinalIgnoreCase)
                    : $"{body}{pixelTag}";
            }
        }

        // Resolve email attachments
        var emailAttachments = new List<EmailAttachment>();
        if (!string.IsNullOrWhiteSpace(detail.AttachmentsJson))
        {
            try
            {
                var attachmentRequests = System.Text.Json.JsonSerializer.Deserialize<List<WhatsAppCampaignApi.Models.DTOs.Campaigns.CampaignAttachmentRequest>>(detail.AttachmentsJson);
                if (attachmentRequests != null)
                {
                    foreach (var att in attachmentRequests)
                    {
                        var bytes = LoadAttachmentBytes(att.Url);
                        if (bytes != null && bytes.Length > 0)
                        {
                            var ct = !string.IsNullOrWhiteSpace(att.ContentType) ? att.ContentType : GetMimeType(att.FileName);
                            emailAttachments.Add(new EmailAttachment(att.FileName, ct, bytes));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load campaign attachments for campaign {CampaignId}", campaign.Id);
            }
        }
        else if (!string.IsNullOrWhiteSpace(campaign.FileUrl))
        {
            try
            {
                var bytes = LoadAttachmentBytes(campaign.FileUrl);
                if (bytes != null && bytes.Length > 0)
                {
                    var fileName = !string.IsNullOrWhiteSpace(campaign.FileName) ? campaign.FileName : Path.GetFileName(campaign.FileUrl);
                    var ct = GetMimeType(fileName);
                    emailAttachments.Add(new EmailAttachment(fileName, ct, bytes));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load campaign file attachment for campaign {CampaignId}", campaign.Id);
            }
        }

        return new EmailMessage
        {
            From = new EmailAddress(sender.EmailAddress, sender.DisplayName),
            ReplyTo = ResolveReplyTo(detail, sender, providerContext),
            To = [new EmailAddress(emailAddress, contact.Name)],
            Subject = subject,
            HtmlBody = body,
            TextBody = string.IsNullOrWhiteSpace(text) ? null : text,
            Attachments = emailAttachments,
            Headers = headers,
            MessageId = mimeBuilder.NewMessageId(fromDomain),
            ConfigurationSet = providerContext.ConfigurationSet,
            Tags = new Dictionary<string, string>
            {
                ["campaign_id"] = campaign.Id.ToString(),
                ["channel"] = "email"
            }
        };
    }

    private static byte[]? LoadAttachmentBytes(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            // 1. If it's a relative or absolute URL containing /uploads/
            if (url.Contains("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = url.Substring(url.IndexOf("/uploads/", StringComparison.OrdinalIgnoreCase)).TrimStart('/');
                var localPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(localPath))
                {
                    return File.ReadAllBytes(localPath);
                }
            }

            // 2. Direct file path
            if (File.Exists(url))
            {
                return File.ReadAllBytes(url);
            }

            // 3. Remote URL fallback
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                return http.GetByteArrayAsync(uri).GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Handled by caller
        }

        return null;
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    private static string RewriteLinks(string html, string trackingId, IEmailTrackingService tracking)
    {
        var linkIndex = 0;
        return System.Text.RegularExpressions.Regex.Replace(
            html,
            @"(<a\b[^>]*?\bhref\s*=\s*[""'])(https?://[^""'>\s]+)([""'][^>]*>)",
            match =>
            {
                var prefix = match.Groups[1].Value;
                var originalUrl = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;

                // Skip unsubscribe links or existing tracking URLs
                if (originalUrl.Contains("/api/unsubscribe", StringComparison.OrdinalIgnoreCase) ||
                    originalUrl.Contains("/api/t/", StringComparison.OrdinalIgnoreCase))
                {
                    return match.Value;
                }

                var clickUrl = tracking.BuildClickUrl(trackingId, originalUrl, linkIndex++);
                return $"{prefix}{clickUrl}{suffix}";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static EmailAddress? ResolveReplyTo(
        EmailCampaignDetail detail,
        EmailSenderIdentity sender,
        EmailProviderContext providerContext)
    {
        // Most specific wins: the campaign's override, then the sender's own, then the
        // connection default.
        var address = detail.ReplyToOverride
                   ?? sender.ReplyTo
                   ?? providerContext.DefaultReplyTo;

        return string.IsNullOrWhiteSpace(address) ? null : new EmailAddress(address);
    }

    private static async Task FailRecipientAsync(
        AppDbContext dbContext,
        CampaignContact recipient,
        string reason,
        CancellationToken ct)
    {
        recipient.Status = MessageStatus.Failed;
        recipient.ErrorMessage = reason.Length > 500 ? reason[..500] : reason;
        recipient.SendAttemptedAt = null;
        await dbContext.SaveChangesAsync(ct);
    }
}
