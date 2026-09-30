using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Email;

namespace WhatsAppCampaignApi.Services.Campaigns;

/// <summary>
/// Maker-checker: every campaign waits for a second user to approve it before anything is sent.
/// </summary>
/// <remarks>
/// <para>
/// Selected by <c>Campaigns:Approval:Required = true</c>. The decision itself is taken by a person
/// through <c>POST /api/Campaigns/{id}/approve</c> or <c>/reject</c>, which record it in
/// <c>CampaignApprovalStates</c>; this gate only reads that record. So the dispatchers and the
/// expansion worker, which re-check the gate before sending, see the same answer the approver
/// gave — and a campaign nobody has approved never reaches the queue.
/// </para>
/// <para>
/// Editing an approved campaign clears its approval (see <c>CampaignService.UpdateAsync</c>), so
/// what is sent is always what was approved.
/// </para>
/// </remarks>
public sealed class InternalApprovalGate : ICampaignExecutionGate
{
    public const string Name = "internal";

    private readonly AppDbContext _db;

    public InternalApprovalGate(AppDbContext db) => _db = db;

    public string GateName => Name;

    public async Task<CampaignExecutionDecision> EvaluateAsync(CampaignExecutionRequest request, CancellationToken ct = default)
    {
        var approval = await _db.CampaignApprovalStates.AsNoTracking()
            .Where(a => a.CampaignId == request.CampaignId)
            .Select(a => new { a.State, a.Reason })
            .FirstOrDefaultAsync(ct);

        return approval?.State switch
        {
            "Approved" => CampaignExecutionDecision.Approve(),
            "Rejected" => CampaignExecutionDecision.Reject(approval.Reason ?? "Rejected by an approver."),
            _ => CampaignExecutionDecision.AwaitApproval($"internal:{request.CampaignId}", "Waiting for a second user to approve.")
        };
    }
}
