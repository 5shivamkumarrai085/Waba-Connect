namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Tells the expansion worker which campaign to fan out.
///
/// <para>
/// Carries an id and nothing else, on purpose. A payload that duplicated the campaign's
/// recipients or template would be a snapshot that goes stale the moment the campaign is edited,
/// and the worker would send the old version. Re-reading from the database means the worker
/// always acts on current state.
/// </para>
/// </summary>
/// <param name="CampaignId">The campaign to expand.</param>
public sealed record CampaignExpansionJob(int CampaignId);

/// <summary>
/// One email to one recipient.
/// </summary>
/// <param name="CampaignId">The campaign this send belongs to.</param>
/// <param name="CampaignContactId">
/// The recipient row. Also the idempotency anchor: the dispatch worker refuses to send when this
/// row is no longer Pending, which is what stops a redelivered job from sending twice.
/// </param>
public sealed record EmailSendJob(int CampaignId, int CampaignContactId);

/// <summary>
/// One inbound email to parse and thread.
/// </summary>
/// <param name="SnsMessageId">Used to discard a redelivered SNS notification.</param>
/// <param name="ProviderMessageId">The provider's id for the received message.</param>
/// <param name="S3Bucket">Where the SES receipt rule wrote the raw MIME.</param>
/// <param name="S3Key">The object key.</param>
/// <param name="ConnectionId">Which connection received it, when it could be determined.</param>
public sealed record InboundEmailJob(
    string SnsMessageId,
    string? ProviderMessageId,
    string? S3Bucket,
    string? S3Key,
    int? ConnectionId);
