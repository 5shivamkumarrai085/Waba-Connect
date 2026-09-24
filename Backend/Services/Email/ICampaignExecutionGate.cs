namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Decides whether a campaign is authorised to run.
///
/// <para>
/// This application does not implement maker-checker, and deliberately so — the host application
/// owns that. This interface is the whole integration surface: a host registers its own
/// implementation, and campaign execution starts consulting it with no change to the campaign
/// service, the queue, the workers or the providers.
/// </para>
/// <para>
/// Consulted twice: once when the campaign is submitted, and again by the expansion worker before
/// it fans out recipients. The second check matters because a decision can change while a
/// campaign sits waiting, and because a worker picking up a parked campaign after a restart must
/// not assume the earlier answer still holds.
/// </para>
/// </summary>
public interface ICampaignExecutionGate
{
    /// <summary>
    /// Identifies this implementation, matching <c>Email:ExecutionGate:Provider</c>. "none" is
    /// the built-in pass-through.
    /// </summary>
    string GateName { get; }

    Task<CampaignExecutionDecision> EvaluateAsync(CampaignExecutionRequest request, CancellationToken ct = default);
}

/// <param name="CampaignId">The campaign being submitted.</param>
/// <param name="CampaignName">Included so a host can show something meaningful to an approver.</param>
/// <param name="Channel">"WhatsApp" or "Email".</param>
/// <param name="RecipientCount">
/// Usually the deciding factor — approval thresholds are almost always about blast radius.
/// </param>
/// <param name="TemplateName">What the campaign will send.</param>
/// <param name="RequestedByUserId">The maker, when the request came from a user rather than a worker.</param>
/// <param name="ExistingExternalReferenceId">
/// Set on re-evaluation, so a host can look up the decision it already recorded instead of
/// creating a second approval request for the same campaign.
/// </param>
public sealed record CampaignExecutionRequest(
    int CampaignId,
    string CampaignName,
    string Channel,
    int RecipientCount,
    string? TemplateName,
    int? RequestedByUserId,
    string? ExistingExternalReferenceId);

public enum CampaignExecutionOutcome
{
    /// <summary>Proceed. What the built-in gate always returns.</summary>
    Approved,

    /// <summary>
    /// Park the campaign in <c>CampaignStatus.AwaitingApproval</c> and wait. Nothing is sent, and
    /// no recipient is expanded, until a later evaluation or an inbound decision approves it.
    /// </summary>
    AwaitingExternalApproval,

    /// <summary>Refused. The campaign is cancelled with the supplied reason.</summary>
    Rejected
}

/// <param name="Outcome">The decision.</param>
/// <param name="Reason">Shown to the operator. Required for a rejection to be actionable.</param>
/// <param name="ExternalReferenceId">
/// The host's own identifier for the approval request, stored so a later decision can be matched
/// back to this campaign.
/// </param>
public sealed record CampaignExecutionDecision(
    CampaignExecutionOutcome Outcome,
    string? Reason = null,
    string? ExternalReferenceId = null)
{
    public static CampaignExecutionDecision Approve() => new(CampaignExecutionOutcome.Approved);

    public static CampaignExecutionDecision AwaitApproval(string externalReferenceId, string? reason = null) =>
        new(CampaignExecutionOutcome.AwaitingExternalApproval, reason, externalReferenceId);

    public static CampaignExecutionDecision Reject(string reason) =>
        new(CampaignExecutionOutcome.Rejected, reason);
}

/// <summary>
/// The default gate: everything is approved.
///
/// <para>
/// Registered whenever <c>Email:ExecutionGate:Provider</c> is "none", which is this deployment's
/// configuration. It exists so the campaign pipeline has exactly one code path whether or not a
/// host is attached — the alternative, null-checking an optional gate at every call site, is how
/// an approval requirement ends up silently skipped on one of them.
/// </para>
/// </summary>
public class AutoApproveCampaignExecutionGate : ICampaignExecutionGate
{
    public string GateName => "none";

    public Task<CampaignExecutionDecision> EvaluateAsync(
        CampaignExecutionRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(CampaignExecutionDecision.Approve());
}
