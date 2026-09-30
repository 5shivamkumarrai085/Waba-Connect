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
/// <param name="RunId">Which run of the campaign this is; null for the original submission.</param>
public sealed record CampaignExpansionJob(int CampaignId, string? RunId = null);

/// <summary>
/// One email to one recipient.
/// </summary>
/// <param name="CampaignId">The campaign this send belongs to.</param>
/// <param name="CampaignContactId">
/// The recipient row. Also the idempotency anchor: the dispatch worker refuses to send when this
/// row is no longer Pending, which is what stops a redelivered job from sending twice.
/// </param>
public sealed record EmailSendJob(int CampaignId, int CampaignContactId);

