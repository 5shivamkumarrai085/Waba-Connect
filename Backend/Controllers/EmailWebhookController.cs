using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Services.Email;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Receives Amazon SES delivery events, published through SNS.
///
/// <para>
/// Anonymous by necessity — AWS has no credential to present — but, unlike the WhatsApp webhook
/// alongside it, every request is cryptographically verified before it is believed. That
/// difference is deliberate and not optional here: these notifications suppress customers'
/// addresses and mark campaigns as failed, so an unauthenticated endpoint that trusted its input
/// would let anyone silently stop mail reaching a real recipient.
/// </para>
/// </summary>
[ApiController]
[Route("api/webhook/email")]
public class EmailWebhookController : ControllerBase
{
    private readonly ISnsMessageValidator _validator;
    private readonly IEmailEventProcessor _eventProcessor;
    private readonly ILogger<EmailWebhookController> _logger;

    public EmailWebhookController(
        ISnsMessageValidator validator,
        IEmailEventProcessor eventProcessor,
        ILogger<EmailWebhookController> logger)
    {
        _validator = validator;
        _eventProcessor = eventProcessor;
        _logger = logger;
    }

    /// <summary>
    /// The SES event endpoint.
    ///
    /// <para>
    /// Read from the raw body rather than a bound model, because the SNS signature is computed
    /// over the exact field values as sent. Model binding would parse and re-materialise them —
    /// a <c>Timestamp</c> round-tripped through <c>DateTime</c> no longer matches the bytes AWS
    /// signed, and verification would fail for legitimate messages.
    /// </para>
    /// <para>
    /// SNS retries any non-2xx response. Anything that will not succeed on a retry — a duplicate,
    /// an unparseable payload, an event that cannot be attributed — therefore returns 200 with an
    /// explanatory body, so SNS stops rather than redelivering it indefinitely. A rejected
    /// signature is the exception: that is 403, because it must be visibly refused.
    /// </para>
    /// </summary>
    [HttpPost("ses")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveSesEvent(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(body))
        {
            return BadRequest(new { message = "Empty request body." });
        }

        SnsEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SnsEnvelope>(body);
        }
        catch (JsonException ex)
        {
            // An anonymous endpoint receives malformed input from scanners routinely, so this is
            // not logged as an error.
            _logger.LogDebug(ex, "Discarded a malformed SNS payload.");
            return BadRequest(new { message = "Malformed SNS payload." });
        }

        if (envelope?.Type is null)
        {
            return BadRequest(new { message = "The payload is not an SNS message." });
        }

        if (!await _validator.IsValidAsync(envelope, ct))
        {
            // Deliberately terse. A detailed explanation of which check failed would help
            // somebody probing the endpoint work out how to pass it.
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Signature verification failed." });
        }

        switch (envelope.Type)
        {
            case "SubscriptionConfirmation":
                // Confirmed here rather than by hand in the AWS console, so re-pointing the topic
                // at a new environment does not need a manual step that is easy to forget.
                var confirmed = await _validator.ConfirmSubscriptionAsync(envelope, ct);
                return confirmed
                    ? Ok(new { message = "Subscription confirmed." })
                    : StatusCode(StatusCodes.Status502BadGateway,
                        new { message = "Could not confirm the subscription." });

            case "UnsubscribeConfirmation":
                // Worth a warning: something has detached our event feed, and delivery reporting
                // will now go quiet without any other symptom.
                _logger.LogWarning(
                    "The SNS subscription for topic {TopicArn} was removed. SES delivery events will no "
                  + "longer arrive until it is re-subscribed.", envelope.TopicArn);
                return Ok(new { message = "Acknowledged." });

            case "Notification":
                if (string.IsNullOrWhiteSpace(envelope.Message))
                {
                    return Ok(new { message = "Notification carried no message; ignored." });
                }

                var processed = await _eventProcessor.ProcessAsync(
                    envelope.MessageId ?? Guid.NewGuid().ToString("N"),
                    envelope.Message,
                    ct);

                // 200 either way. A duplicate or unattributable event will not become processable
                // on redelivery, and a non-2xx would have SNS retry it for hours.
                return Ok(new { message = processed ? "Processed." : "Ignored (duplicate or unattributable)." });

            default:
                _logger.LogInformation("Ignoring an unknown SNS message type '{Type}'.", envelope.Type);
                return Ok(new { message = "Unknown message type; ignored." });
        }
    }
}
