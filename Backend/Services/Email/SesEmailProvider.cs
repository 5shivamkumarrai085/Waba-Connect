using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Amazon SES, via the SES v2 API.
///
/// <para>
/// v2 rather than the original API because v2 is the only one that exposes what this feature
/// actually needs: configuration sets for publishing delivery events, the account suppression
/// list, and <c>CreateEmailIdentity</c> with Easy DKIM tokens for domain authentication. The
/// original API can send mail and little else.
/// </para>
/// <para>
/// Sends are raw MIME rather than the structured <c>Simple</c> content shape. Simple content
/// cannot carry attachments or custom headers, and <c>List-Unsubscribe</c> is a custom header —
/// so using it would mean bulk mail with no one-click opt-out. Raw also means SES and SMTP emit
/// byte-identical messages, built by the one shared MIME builder.
/// </para>
/// </summary>
public class SesEmailProvider : IEmailProvider
{
    /// <summary>
    /// SES rejects anything larger, counting encoded attachments. Checked before the call so an
    /// oversized message fails with an explanation rather than an opaque API error.
    /// </summary>
    private const int SesMaxMessageBytes = 40 * 1024 * 1024;

    private readonly IMimeMessageBuilder _mimeBuilder;
    private readonly ILogger<SesEmailProvider> _logger;

    public SesEmailProvider(IMimeMessageBuilder mimeBuilder, ILogger<SesEmailProvider> logger)
    {
        _mimeBuilder = mimeBuilder;
        _logger = logger;
    }

    public string ProviderName => nameof(EmailProviderType.AmazonSes);

    public EmailProviderCapabilities Capabilities => new()
    {
        SupportsEventWebhooks = true,
        SupportsDkimProvisioning = true,
        SupportsSuppressionApi = true,
        SupportsInboundReceiving = true,
        MaxMessageBytes = SesMaxMessageBytes
    };

    public async Task<EmailSendResult> SendAsync(
        EmailMessage message,
        EmailProviderContext context,
        CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(context);

            var mime = _mimeBuilder.Build(message);
            using var buffer = new MemoryStream();
            await mime.WriteToAsync(buffer, ct);

            if (buffer.Length > SesMaxMessageBytes)
            {
                // Permanent: the message will never fit, so retrying only wastes attempts.
                return EmailSendResult.Failed(
                    $"Message is {buffer.Length / 1024 / 1024}MB, which exceeds the {SesMaxMessageBytes / 1024 / 1024}MB SES limit.",
                    isTransient: false,
                    errorCode: "MessageTooLarge");
            }

            buffer.Position = 0;

            var request = new SendEmailRequest
            {
                // Stated explicitly even though the MIME carries a From header: SES uses this to
                // resolve which verified identity is sending, and an unverified value here is a
                // clearer error than a generic rejection.
                FromEmailAddress = message.From.Address,

                // Given explicitly so Bcc works without the MIME disclosing it. When Destination
                // is present SES delivers to exactly these addresses and ignores the headers,
                // which is what lets the builder omit Bcc entirely.
                Destination = new Destination
                {
                    ToAddresses = message.To.Select(a => a.Address).ToList(),
                    CcAddresses = message.Cc.Select(a => a.Address).ToList(),
                    BccAddresses = message.Bcc.Select(a => a.Address).ToList()
                },

                Content = new EmailContent
                {
                    Raw = new RawMessage { Data = buffer }
                }
            };

            if (message.ReplyTo is { } replyTo)
            {
                request.ReplyToAddresses = [replyTo.Address];
            }

            // Without a configuration set SES sends but publishes no events, so nothing ever
            // learns whether the message was delivered, bounced or complained about.
            var configurationSet = message.ConfigurationSet ?? context.ConfigurationSet;
            if (!string.IsNullOrWhiteSpace(configurationSet))
            {
                request.ConfigurationSetName = configurationSet;
            }

            if (message.Tags.Count > 0)
            {
                request.EmailTags = message.Tags
                    // SES only accepts alphanumerics, dashes and underscores in tag names and
                    // values, and rejects the whole send otherwise — so they are sanitised here
                    // rather than trusted.
                    .Select(t => new MessageTag { Name = SanitizeTag(t.Key), Value = SanitizeTag(t.Value) })
                    .ToList();
            }

            var response = await client.SendEmailAsync(request, ct);
            return EmailSendResult.Sent(response.MessageId);
        }
        catch (TooManyRequestsException ex)
        {
            // Account or configured send rate exceeded. Emphatically transient — this is the
            // case the rate limiter exists to avoid, and the one where retrying is right.
            return EmailSendResult.Failed(ex.Message, isTransient: true, errorCode: "Throttled",
                retryAfter: TimeSpan.FromSeconds(5));
        }
        catch (SendingPausedException ex)
        {
            // AWS has paused sending for the account, usually over bounce or complaint rates.
            // Transient in principle, but it will not clear on its own within a retry window, so
            // it is surfaced loudly rather than quietly retried into the ground.
            _logger.LogError(ex,
                "SES has paused sending for this account. Delivery will keep failing until the "
              + "underlying reputation problem is resolved in the AWS console.");
            return EmailSendResult.Failed(ex.Message, isTransient: true, errorCode: "SendingPaused",
                retryAfter: TimeSpan.FromMinutes(15));
        }
        catch (MailFromDomainNotVerifiedException ex)
        {
            return EmailSendResult.Failed(
                $"The sending domain is not verified in SES: {ex.Message}",
                isTransient: false, errorCode: "MailFromDomainNotVerified");
        }
        catch (AccountSuspendedException ex)
        {
            return EmailSendResult.Failed(ex.Message, isTransient: false, errorCode: "AccountSuspended");
        }
        catch (MessageRejectedException ex)
        {
            // SES examined the message and refused it. Retrying an identical message gets an
            // identical refusal.
            return EmailSendResult.Failed(ex.Message, isTransient: false, errorCode: "MessageRejected");
        }
        catch (NotFoundException ex)
        {
            // Almost always a configuration set that does not exist in this region.
            return EmailSendResult.Failed(ex.Message, isTransient: false, errorCode: "NotFound");
        }
        catch (BadRequestException ex)
        {
            return EmailSendResult.Failed(ex.Message, isTransient: false, errorCode: "BadRequest");
        }
        catch (AmazonServiceException ex)
        {
            // Everything else from AWS: classified by status code rather than by exception type,
            // because the SDK surfaces a long tail of service errors and the status is the
            // reliable signal. 5xx and 429 are worth another attempt; other 4xx are not.
            var transient = ex.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                         || (int)ex.StatusCode >= 500
                         || ex.StatusCode == 0;

            return EmailSendResult.Failed(ex.Message, transient, ex.ErrorCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A shutdown, not a failure. Transient so the job returns to the queue and is picked
            // up by whichever instance is still running.
            return EmailSendResult.Failed("Send cancelled during shutdown.", isTransient: true);
        }
        catch (Exception ex)
        {
            // Networking and TLS failures land here. Transient: the message itself is fine.
            _logger.LogError(ex, "Unexpected failure sending via SES.");
            return EmailSendResult.Failed(ex.Message, isTransient: true, errorCode: ex.GetType().Name);
        }
    }

    public async Task<EmailProviderTestResult> TestConnectionAsync(
        EmailProviderContext context,
        CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(context);

            // GetAccount is the right probe: it proves the credentials, the region and the
            // ses:GetAccount permission without sending anything or costing quota. It also
            // returns the facts an operator most needs — whether the account is still sandboxed,
            // and what rate it may actually send at.
            var account = await client.GetAccountAsync(new GetAccountRequest(), ct);

            // The SDK models these as nullable, and an absent value is not the same as false —
            // "SES did not tell us whether sending is enabled" must not be reported to an
            // operator as "sending is disabled".
            var sendingEnabled = account.SendingEnabled;
            var productionAccess = account.ProductionAccessEnabled;

            var details = new Dictionary<string, string>
            {
                ["region"] = context.Region ?? "(default)",
                ["sendingEnabled"] = sendingEnabled?.ToString() ?? "unknown",
                ["productionAccess"] = productionAccess?.ToString() ?? "unknown",
                ["maxSendRatePerSecond"] = account.SendQuota?.MaxSendRate?.ToString("0.##") ?? "unknown",
                ["max24HourSend"] = account.SendQuota?.Max24HourSend?.ToString("0") ?? "unknown",
                ["sentLast24Hours"] = account.SendQuota?.SentLast24Hours?.ToString("0") ?? "unknown"
            };

            if (!string.IsNullOrWhiteSpace(context.ConfigurationSet))
            {
                // Checked separately because a missing configuration set does not stop mail from
                // sending — it stops every delivery event from arriving, which looks like
                // "nothing is being delivered" long after the fact.
                try
                {
                    await client.GetConfigurationSetAsync(
                        new GetConfigurationSetRequest { ConfigurationSetName = context.ConfigurationSet }, ct);
                    details["configurationSet"] = $"{context.ConfigurationSet} (found)";
                }
                catch (NotFoundException)
                {
                    return EmailProviderTestResult.Fail(
                        $"Credentials are valid, but configuration set '{context.ConfigurationSet}' does not exist "
                      + $"in {context.Region}. Without it, SES will send mail but report no delivery, bounce or "
                      + "complaint events.");
                }
            }

            // Only a definite false is a failure. An unknown value means the probe worked but SES
            // omitted the flag, which is not grounds for telling the operator their account is
            // disabled.
            if (sendingEnabled == false)
            {
                return EmailProviderTestResult.Fail(
                    "Credentials are valid, but sending is disabled for this SES account. Check the account's "
                  + "reputation status in the AWS console.");
            }

            var message = productionAccess == false
                ? $"Connected to AWS SES ({context.Region}), but the account is still in the sandbox — "
                + "it can only send to verified addresses until production access is granted."
                : $"Connected to AWS SES ({context.Region}). Credentials are valid.";

            return EmailProviderTestResult.Ok(message, details);
        }
        catch (AmazonServiceException ex)
        {
            // Mapped to something an operator can act on. The SDK's own messages are accurate but
            // rarely say which thing to go and change.
            var hint = ex.ErrorCode switch
            {
                "InvalidClientTokenId" or "UnrecognizedClientException" =>
                    "The AWS access key ID is not recognised.",
                "SignatureDoesNotMatch" =>
                    "The AWS secret access key does not match the access key ID.",
                "AccessDenied" or "AccessDeniedException" =>
                    "These credentials exist but lack the required SES permissions (ses:GetAccount and ses:SendEmail).",
                _ => ex.Message
            };

            return EmailProviderTestResult.Fail(hint);
        }
        catch (Exception ex)
        {
            return EmailProviderTestResult.Fail($"Could not reach AWS SES: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds an SES client for one call.
    ///
    /// <para>
    /// A client per call rather than a cached one: credentials live per connection and can be
    /// rotated at any time, and caching would keep sending with a revoked key until a restart.
    /// The clients are cheap, and the underlying HTTP handlers are pooled by the SDK.
    /// </para>
    /// </summary>
    private static AmazonSimpleEmailServiceV2Client CreateClient(EmailProviderContext context)
    {
        var region = string.IsNullOrWhiteSpace(context.Region)
            ? null
            : RegionEndpoint.GetBySystemName(context.Region);

        if (context.AuthMode == EmailAuthMode.IamRole)
        {
            // No stored credentials at all — the SDK resolves them from the instance role, the
            // task role, or the environment. This is the recommended production setup: there is
            // no long-lived secret in the database to leak or rotate.
            return region is null
                ? new AmazonSimpleEmailServiceV2Client()
                : new AmazonSimpleEmailServiceV2Client(region);
        }

        if (string.IsNullOrWhiteSpace(context.AccessKeyId) || string.IsNullOrWhiteSpace(context.SecretAccessKey))
        {
            throw new InvalidOperationException(
                "Access-key authentication is selected but no access key is stored for this email connection.");
        }

        var credentials = new BasicAWSCredentials(context.AccessKeyId, context.SecretAccessKey);
        return region is null
            ? new AmazonSimpleEmailServiceV2Client(credentials)
            : new AmazonSimpleEmailServiceV2Client(credentials, region);
    }

    /// <summary>
    /// SES accepts only ASCII letters, digits, dashes and underscores in tag names and values,
    /// and rejects the entire send otherwise — so a campaign name with a space in it would fail
    /// every message if passed through unchanged.
    /// </summary>
    private static string SanitizeTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";

        var cleaned = new string(value
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')
            .ToArray());

        // SES caps tag names and values at 256 characters.
        return cleaned.Length > 256 ? cleaned[..256] : cleaned;
    }
}
