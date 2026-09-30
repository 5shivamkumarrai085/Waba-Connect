import React, { useCallback, useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  AlertTriangle,
  ArrowLeft,
  Ban,
  RotateCcw,
  ShieldCheck,
  CheckCircle,
  ChevronLeft,
  ChevronRight,
  Eye,
  FileSpreadsheet,
  MousePointerClick,
  Pause,
  Play,
  Reply,
  Send,
  ShieldX,
  Users,
  Wifi,
  Loader2,
  MailX,
  PauseCircle,
  Repeat,
} from 'lucide-react'
import toast from 'react-hot-toast'
import { useCampaignStore } from '../../store/campaignStore'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { StatCard } from '../../components/StatCard'
import { useCampaignEvents } from '../../hooks/useCampaignEvents'
import { apiClient } from '../../services/apiClient'
import { getErrorMessage } from '../../utils/errorHelper'
import type { CampaignRecipient } from '../../types/campaigns'
import Can from '../../components/Can/Can'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { CampaignLinkReport } from './CampaignLinkReport'
import { CampaignAbResults } from './CampaignAbResults'
import { campaignService } from '../../services/campaigns/campaignService'
import useReference from '../../hooks/useReference'
import { referenceService, labelOf } from '../../services/referenceService'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'
import './CampaignDetails.css'

type Tab = 'queue' | 'executed'

/** Badge colour per follow-up state. */
const FOLLOW_UP_BADGE: Record<string, string> = { Pending: 'pending', Running: 'info', Done: 'success', Failed: 'error', Cancelled: 'closed' }

const PAGE_SIZES = [10, 25, 50]

export const CampaignDetails: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const campaignId = id ? parseInt(id, 10) : null

  const selectedCampaign = useCampaignStore((s) => s.selectedCampaign)
  const selectedStats = useCampaignStore((s) => s.selectedStats)
  const isLoading = useCampaignStore((s) => s.isLoading)
  const loadCampaignDetails = useCampaignStore((s) => s.loadCampaignDetails)
  const refreshCampaignDetails = useCampaignStore((s) => s.refreshCampaignDetails)
  const toggleCampaignPause = useCampaignStore((s) => s.toggleCampaignPause)
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [confirmRetry, setConfirmRetry] = useState(false)
  const [retrying, setRetrying] = useState(false)
  const [decisionComment, setDecisionComment] = useState('')
  const [deciding, setDeciding] = useState(false)
  // Follow-up condition labels, as the server's catalogue words them, for this campaign's channel.
  const optionsChannel = selectedCampaign?.channel?.toLowerCase() === 'email' ? 'Email' : 'WhatsApp'
  const campaignOptions = useReference(() => referenceService.getCampaignOptions(optionsChannel), `campaign-options:${optionsChannel}`)

  // Recipients are paged, filtered and searched on the server.
  const [tab, setTab] = useState<Tab>('queue')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZES[0])
  const [recipients, setRecipients] = useState<CampaignRecipient[]>([])
  const [recipientTotal, setRecipientTotal] = useState(0)
  const [counts, setCounts] = useState({ queued: 0, executed: 0 })
  const [recipientsLoading, setRecipientsLoading] = useState(false)
  const [recipientsError, setRecipientsError] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)

  useEffect(() => {
    if (campaignId) void loadCampaignDetails(campaignId)
  }, [campaignId, loadCampaignDetails])

  const loadCounts = useCallback(async () => {
    if (!campaignId) return
    try {
      const response = await apiClient.get(`/Campaigns/${campaignId}/recipients/counts`)
      const data = response.data?.data
      if (data) setCounts({ queued: data.queued ?? 0, executed: data.executed ?? 0 })
    } catch {
      // Tab counters are informative; the table itself reports its own errors.
    }
  }, [campaignId])

  const loadRecipients = useCallback(async () => {
    if (!campaignId) return
    setRecipientsLoading(true)
    setRecipientsError(null)
    try {
      const response = await apiClient.get(`/Campaigns/${campaignId}/recipients`, {
        params: { page, pageSize, state: tab, search: search.trim() || undefined },
      })
      const pageResult = response.data?.data
      setRecipients(
        (pageResult?.items ?? []).map((r: any) => ({
          id: r.id,
          contactId: r.contactId,
          phone: r.phone || 'Unknown',
          email: r.email || '',
          name: r.contactName || 'Unknown',
          message: r.message || '',
          sentStatus: r.status,
          deliveredAt: r.deliveredAt || '-',
          readAt: r.readAt || '-',
          openedAt: r.openedAt || '-',
          failedReason: r.errorMessage || null,
        }))
      )
      setRecipientTotal(pageResult?.totalCount ?? 0)
    } catch (error) {
      setRecipientsError(getErrorMessage(error, 'Could not load recipients.'))
    } finally {
      setRecipientsLoading(false)
    }
  }, [campaignId, page, pageSize, tab, search])

  useEffect(() => {
    const timer = setTimeout(() => void loadRecipients(), search ? 300 : 0)
    return () => clearTimeout(timer)
  }, [loadRecipients, search])

  useEffect(() => {
    void loadCounts()
  }, [loadCounts])

  // Once nothing is queued, open on what was executed.
  useEffect(() => {
    if (counts.queued === 0 && counts.executed > 0 && tab === 'queue') setTab('executed')
  }, [counts, tab])

  // Live figures; a full re-read after a reconnect, and a slow poll while offline.
  const resync = useCallback(() => {
    if (!campaignId) return
    void refreshCampaignDetails(campaignId)
    void loadCounts()
    void loadRecipients()
  }, [campaignId, refreshCampaignDetails, loadCounts, loadRecipients])
  useCampaignEvents(campaignId, resync)

  if (isLoading && !selectedCampaign) {
    return (
      <div className="page-loader">
        <p className="page-loader-text">Loading campaign…</p>
      </div>
    )
  }

  if (!selectedCampaign) {
    return (
      <div className="data-table-empty">
        <p>Campaign not found.</p>
        <button type="button" className="btn-cancel" onClick={() => navigate('/campaigns/campaign')}>
          Back to Campaigns
        </button>
      </div>
    )
  }

  const c = selectedCampaign
  const isEmail = c.channel?.toLowerCase() === 'email'
  const total = c.total || 0
  const pct = (n: number) => (total > 0 ? `${((n / total) * 100).toFixed(1)}% of recipients` : '—')
  const rate = (n: number, of: number) => (of > 0 ? `${((n / of) * 100).toFixed(1)}%` : '0.0%')
  const isLive = c.status === 'Sending'

  const sent = c.sentCount ?? c.emailStats?.sent ?? 0
  const opened = c.openedCount ?? c.emailStats?.opened ?? 0
  const clicked = c.clickedCount ?? c.emailStats?.clicked ?? 0
  const replied = c.repliedCount ?? c.emailStats?.replied ?? 0
  const bounced = c.emailStats?.bounced ?? 0
  const unsubscribed = c.unsubscribedCount ?? c.emailStats?.unsubscribed ?? 0
  const failed = c.failedCount ?? 0
  const skipped = c.skippedCount ?? 0
  const delivered = c.reportsDelivery === false ? Math.max(0, sent - bounced) : c.emailStats?.delivered ?? sent

  const totalPages = Math.max(1, Math.ceil(recipientTotal / pageSize))
  const startIndex = recipientTotal > 0 ? (page - 1) * pageSize + 1 : 0
  const endIndex = Math.min(recipientTotal, (page - 1) * pageSize + recipients.length)

  const canCancel = !c.isDeleted && ['Sending', 'Scheduled', 'Paused', 'AwaitingApproval'].includes(c.status)
  const approval = c.approval
  const awaitingApproval = c.status === 'AwaitingApproval'

  const handleCancel = async () => {
    setConfirmCancel(false)
    try {
      await campaignService.cancelCampaign(c.id)
      toast.success('Campaign cancelled.')
      await refreshCampaignDetails(c.id)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Could not cancel the campaign.'))
    }
  }

  const retryable = c.retryableCount ?? 0
  const retriesLeft = Math.max(0, (c.maxRetryRuns ?? 0) - (c.retryRuns ?? 0))
  const canRetry = !c.isDeleted && retryable > 0 && retriesLeft > 0
    && ['Sent', 'PartiallyFailed', 'Failed'].includes(c.status)

  const handleRetry = async () => {
    setConfirmRetry(false)
    setRetrying(true)
    try {
      const queued = await campaignService.retryFailed(c.id)
      toast.success(`Retrying ${queued} failed recipient${queued === 1 ? '' : 's'}.`)
      await refreshCampaignDetails(c.id)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Could not retry the failed recipients.'))
    } finally {
      setRetrying(false)
    }
  }

  const handleDecision = async (decision: 'approve' | 'reject') => {
    const comment = decisionComment.trim()
    if (decision === 'reject' && !comment) {
      toast.error('Give a reason for rejecting the campaign.')
      return
    }
    setDeciding(true)
    try {
      if (decision === 'approve') await campaignService.approveCampaign(c.id, comment || undefined)
      else await campaignService.rejectCampaign(c.id, comment)
      toast.success(decision === 'approve' ? 'Campaign approved and released for sending.' : 'Campaign rejected.')
      setDecisionComment('')
      await refreshCampaignDetails(c.id)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Could not record the decision.'))
    } finally {
      setDeciding(false)
    }
  }

  const handlePauseToggle = async () => {
    try {
      await toggleCampaignPause(c.id)
      toast.success(c.status === 'Paused' ? 'Campaign resumed.' : 'Campaign paused.')
    } catch (error) {
      toast.error(getErrorMessage(error, 'Unable to update campaign status.'))
    }
  }

  const handleExport = async () => {
    setExporting(true)
    try {
      // Streamed by the server in batches; the browser saves the file as it arrives.
      const response = await apiClient.get(`/Campaigns/${c.id}/recipients/export`, { responseType: 'blob' })
      const disposition = String(response.headers['content-disposition'] ?? '')
      const fileName = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? `campaign-${c.id}-recipients.csv`
      const url = URL.createObjectURL(response.data as Blob)
      const link = document.createElement('a')
      link.href = url
      link.download = fileName
      link.click()
      URL.revokeObjectURL(url)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Export failed.'))
    } finally {
      setExporting(false)
    }
  }

  const canPause = !c.isDeleted && ['Sending', 'Scheduled', 'Paused'].includes(c.status)

  return (
    <motion.div {...pageTransitionProps}>
      <div className="omni-page-hero campaign-details-hero">
        <div>
          <h1>{c.name}</h1>
          <p>
            {isEmail ? 'Email' : 'WhatsApp'} campaign · {c.templateName || 'No template'}
            {c.scheduledAt ? ` · Scheduled ${new Date(c.scheduledAt).toLocaleString()}` : ''}
          </p>
        </div>
        <div className="campaign-details-hero-actions">
          <StatusBadge type={getCampaignStatusBadgeType(c.status)} text={formatCampaignStatus(c.status)} />
          {isLive && (
            <span className="live-badge" title="Figures update in real time">
              <Wifi size={12} /> Live
            </span>
          )}
          <button type="button" className="btn-toolbar" onClick={() => navigate('/campaigns/campaign')}>
            <ArrowLeft size={16} /> Back
          </button>
          {canPause && (
            <Can permission="Campaign.Send">
              <button type="button" className="btn-toolbar" onClick={handlePauseToggle}>
                {c.status === 'Paused' ? <Play size={16} /> : <Pause size={16} />}
                {c.status === 'Paused' ? 'Resume' : 'Pause'}
              </button>
            </Can>
          )}
          {canRetry && (
            <Can permission="Campaign.Retry">
              <button type="button" className="btn-toolbar" disabled={retrying} onClick={() => setConfirmRetry(true)}>
                <RotateCcw size={16} /> Retry failed ({retryable})
              </button>
            </Can>
          )}
          {canCancel && (
            <Can permission="Campaign.Send">
              <button type="button" className="btn-toolbar" onClick={() => setConfirmCancel(true)}>
                <Ban size={16} /> Cancel campaign
              </button>
            </Can>
          )}
        </div>
      </div>

      {approval && (
        <section className={`campaign-approval-panel is-${String(approval.state).toLowerCase()}`} aria-live="polite" aria-labelledby="approval-title">
          <div className="campaign-approval-head">
            <ShieldCheck size={20} aria-hidden="true" />
            <div>
              <h2 id="approval-title">
                {approval.state === 'Pending' && 'Waiting for Approval'}
                {approval.state === 'Approved' && 'Approved'}
                {approval.state === 'Rejected' && 'Rejected'}
              </h2>
              <p>
                Submitted{approval.requestedBy ? ` by ${approval.requestedBy}` : ''} on <time dateTime={approval.requestedAt}>{formatAbsoluteDateTime(approval.requestedAt)}</time>
                {approval.decidedAt && (
                  <> · {approval.state === 'Rejected' ? 'Rejected' : 'Approved'}{approval.decidedBy ? ` by ${approval.decidedBy}` : ''} on <time dateTime={approval.decidedAt}>{formatAbsoluteDateTime(approval.decidedAt)}</time></>
                )}
              </p>
              {approval.reason && approval.state !== 'Pending' && <p className="campaign-approval-reason">“{approval.reason}”</p>}
            </div>
          </div>

          {awaitingApproval && approval.canDecide && (
            <div className="campaign-approval-actions">
              <label className="form-label" htmlFor="approval-comment">Comment <span className="campaign-optional">(required to reject)</span></label>
              <textarea
                id="approval-comment"
                name="approval-comment"
                className="campaign-approval-comment"
                placeholder="Why you are approving or rejecting it…"
                value={decisionComment}
                maxLength={1000}
                onChange={(e) => setDecisionComment(e.target.value)}
                rows={2}
              />
              <div className="campaign-approval-buttons">
                <button type="button" className="oc-dialog-btn oc-dialog-btn-secondary" disabled={deciding || !decisionComment.trim()}
                  title={decisionComment.trim() ? undefined : 'Write a reason first'} onClick={() => handleDecision('reject')}>
                  Reject
                </button>
                <button type="button" className="oc-dialog-btn oc-dialog-btn-primary" disabled={deciding} onClick={() => handleDecision('approve')}>
                  {deciding && <Loader2 size={15} className="campaign-spin" aria-hidden="true" />}
                  {deciding ? 'Saving…' : 'Approve and Send'}
                </button>
              </div>
            </div>
          )}
          {awaitingApproval && !approval.canDecide && (
            <p className="campaign-approval-note">
              Another user with the Approve permission must approve this campaign. The person who submitted it cannot approve it.
            </p>
          )}
        </section>
      )}

      {c.status === 'Paused' && c.pausedReason && (
        <div className="campaign-hold" role="alert">
          <PauseCircle size={20} aria-hidden="true" />
          <div>
            <strong>Sending is on hold</strong>
            <p>{c.pausedReason.replace(/^On hold:\s*/, '')}</p>
            <p className="campaign-hold-next">
              No recipient has been marked as failed. Fix the connection, then choose <strong>Resume</strong> to send to everyone still waiting.
            </p>
          </div>
          {c.connectionId && (
            <button type="button" className="btn btn-secondary" onClick={() => navigate('/connections')}>
              Open Connections
            </button>
          )}
        </div>
      )}

      <ConfirmationModal
        isOpen={confirmRetry}
        title={`Retry ${retryable} failed recipient${retryable === 1 ? '' : 's'}?`}
        message={`They will be sent the same message again. Bounced, complained and unsubscribed recipients are never retried. ${retriesLeft} retr${retriesLeft === 1 ? 'y' : 'ies'} left for this campaign.`}
        confirmText="Retry now"
        onConfirm={handleRetry}
        onCancel={() => setConfirmRetry(false)}
      />

      <ConfirmationModal
        isOpen={confirmCancel}
        title="Cancel this campaign?"
        message="Messages that have not been sent yet will not be sent. Messages already sent are not affected. This cannot be undone."
        confirmText="Cancel campaign"
        cancelText="Keep it"
        isDestructive
        showWarningIcon
        onConfirm={handleCancel}
        onCancel={() => setConfirmCancel(false)}
      />

      {isEmail ? (
        <div className="stat-cards-grid">
          <StatCard icon={<Users size={22} />} label="Recipients" value={total} colorClass="blue"
            footnote={`${counts.queued} still queued`} />
          <StatCard icon={<Send size={22} />} label={c.reportsDelivery === false ? 'Accepted' : 'Delivered'}
            value={delivered} colorClass="green"
            footnote={c.reportsDelivery === false
              ? `Accepted by the mail server · ${pct(delivered)}`
              : pct(delivered)} />
          <StatCard icon={<Eye size={22} />} label="Unique Opens" value={opened} colorClass="purple"
            footnote={`Open rate ${rate(opened, delivered)}`} />
          <StatCard icon={<MousePointerClick size={22} />} label="Unique Clicks" value={clicked} colorClass="orange"
            footnote={`Click rate ${rate(clicked, delivered)}`} />
          <StatCard icon={<Reply size={22} />} label="Replies" value={replied} colorClass="blue"
            footnote={`Reply rate ${rate(replied, delivered)}`} />
          {/* Two different things: a bounce is the recipient's mail server refusing the message
              after it was sent; a failure is a message that was never accepted for sending. */}
          <StatCard icon={<MailX size={22} />} label="Bounced" value={bounced} colorClass="orange"
            footnote={`Rejected by the recipient's server · ${rate(bounced, sent)}`} />
          <StatCard icon={<AlertTriangle size={22} />} label="Failed" value={failed} colorClass="red"
            footnote={skipped ? `Never sent · ${skipped} skipped (consent or limits)` : 'Never sent; reasons in the list below'} />
          <StatCard icon={<ShieldX size={22} />} label="Unsubscribed" value={unsubscribed} colorClass="orange"
            footnote={`Unsubscribe rate ${rate(unsubscribed, delivered)}`} />
        </div>
      ) : (
        selectedStats && (
          <div className="stat-cards-grid">
            <StatCard icon={<Users size={22} />} label="Recipients" value={selectedStats.totalLeads} colorClass="blue"
              footnote={`${counts.queued} still queued`} />
            <StatCard icon={<CheckCircle size={22} />} label="Delivered" value={selectedStats.deliveredCount}
              colorClass="green" footnote={selectedStats.deliveredPercent} />
            <StatCard icon={<Eye size={22} />} label="Read" value={selectedStats.readCount} colorClass="purple"
              footnote={selectedStats.readPercent} />
            <StatCard icon={<AlertTriangle size={22} />} label="Failed" value={selectedStats.failedCount} colorClass="red"
              footnote={skipped ? `${selectedStats.failedPercent} · ${skipped} skipped (consent or limits)` : selectedStats.failedPercent} />
          </div>
        )
      )}

      {c.abTest && (
        <CampaignAbResults campaignId={c.id} channel={isEmail ? 'email' : 'whatsapp'} status={c.status} abTest={c.abTest} onChanged={() => refreshCampaignDetails(c.id)} />
      )}

      {(c.followUps?.length ?? 0) > 0 && (
        <section className="campaign-section-card" aria-labelledby="followups-title">
          <header className="campaign-section-head"><h2 id="followups-title"><Repeat size={16} aria-hidden="true" /> Follow-ups</h2></header>
          <ul className="campaign-followups">
            {c.followUps!.map(f => (
              <li key={f.id}>
                <StatusBadge type={FOLLOW_UP_BADGE[f.status] ?? 'closed'} text={f.status} />
                <span>
                  {f.delayHours} hours after sending, to recipients who {labelOf(campaignOptions.data?.followUpConditions, f.condition)} →{' '}
                  {f.action === 'tag' ? `tag “${f.tag}”` : `${f.channel} “${f.templateName ?? 'template'}”`}
                </span>
                <small>
                  {f.status === 'Pending' ? <>Due <time dateTime={f.dueAt}>{formatAbsoluteDateTime(f.dueAt)}</time></> : f.note}
                  {f.childCampaignId ? <> · <Link to={`/campaigns/campaign/details/${f.childCampaignId}`}>Open campaign #{f.childCampaignId}</Link></> : null}
                </small>
              </li>
            ))}
          </ul>
        </section>
      )}

      {c.parentCampaignId && (
        <p className="campaign-parent-link">
          Follow-up of <Link to={`/campaigns/campaign/details/${c.parentCampaignId}`}>campaign #{c.parentCampaignId}</Link>.
        </p>
      )}

      {isEmail && <CampaignLinkReport campaignId={c.id} clickedCount={clicked} />}

      <div className="contacts-card campaign-recipients-card">
        <div className="tabs-container">
          {(['queue', 'executed'] as Tab[]).map((t) => (
            <button
              key={t}
              type="button"
              className={`tab-btn ${tab === t ? 'active' : ''}`}
              onClick={() => {
                setTab(t)
                setPage(1)
              }}
            >
              {t === 'queue' ? `Queue (${counts.queued})` : `Executed (${counts.executed})`}
            </button>
          ))}
        </div>

        <div className="contacts-controls-row">
          <div className="contacts-controls-left">
            <button
              type="button"
              className="btn-control-icon"
              onClick={handleExport}
              disabled={exporting}
              title="Export execution log (CSV)"
              aria-label="Export execution log as CSV"
            >
              <FileSpreadsheet size={16} />
            </button>
          </div>
          <div className="contacts-controls-right">
            <SearchBar
              value={search}
              onChange={(value) => {
                setSearch(value)
                setPage(1)
              }}
              placeholder="Search name, phone or email…"
            />
          </div>
        </div>

        <div className="data-table-wrapper">
          {recipientsError ? (
            <div className="data-table-empty">
              <p>{recipientsError}</p>
              <button type="button" className="btn-cancel" onClick={() => void loadRecipients()}>Retry</button>
            </div>
          ) : recipients.length === 0 ? (
            <div className="data-table-empty">
              <p>{recipientsLoading ? 'Loading…' : 'No records found'}</p>
            </div>
          ) : (
            <table className="data-table" aria-busy={recipientsLoading}>
              <thead>
                <tr>
                  <th>ID</th>
                  <th>Name</th>
                  <th>{isEmail ? 'Email / Phone' : 'Phone'}</th>
                  <th>Message</th>
                  <th>Status</th>
                  <th>{isEmail ? 'Engagement' : 'Last update'}</th>
                </tr>
              </thead>
              <tbody>
                {recipients.map((r) => (
                  <tr key={r.id}>
                    <td>{r.id}</td>
                    <td>{r.name}</td>
                    <td>{isEmail ? r.email || r.phone : r.phone}</td>
                    <td className="body-data-cell">{r.message}</td>
                    <td>
                      <StatusBadge type={getRecipientStatusBadgeType(r.sentStatus)} text={r.sentStatus} />
                      {r.failedReason && <div className="campaign-recipient-error">{r.failedReason}</div>}
                    </td>
                    <td className="campaign-recipient-time">{describeEngagement(r, isEmail)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        <div className="contacts-table-footer">
          <select
            className="contacts-pager-size-select"
            value={pageSize}
            aria-label="Rows per page"
            onChange={(e) => {
              setPageSize(Number(e.target.value))
              setPage(1)
            }}
          >
            {PAGE_SIZES.map((size) => (
              <option key={size} value={size}>{size}</option>
            ))}
          </select>

          <div className="pager-navigation">
            <span className="contacts-pager-info">
              Showing {startIndex} to {endIndex} of {recipientTotal} results
            </span>
            <div className="contacts-controls-left">
              <button type="button" className="btn-control-icon" disabled={page <= 1}
                onClick={() => setPage(page - 1)} aria-label="Previous page">
                <ChevronLeft size={16} />
              </button>
              <button type="button" className="btn-control-icon" disabled={page >= totalPages}
                onClick={() => setPage(page + 1)} aria-label="Next page">
                <ChevronRight size={16} />
              </button>
            </div>
          </div>
        </div>
      </div>
    </motion.div>
  )
}

const formatTime = (value: string) =>
  new Date(value).toLocaleString([], { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })

const describeEngagement = (r: CampaignRecipient, isEmail: boolean): string => {
  const has = (v?: string | null) => !!v && v !== '-'
  if (isEmail) {
    if (has(r.openedAt)) return `Opened ${formatTime(r.openedAt!)}`
    if (has(r.readAt)) return `Opened ${formatTime(r.readAt!)}`
    if (has(r.deliveredAt)) return `Sent ${formatTime(r.deliveredAt!)}`
    return '—'
  }
  if (has(r.readAt)) return `Read ${formatTime(r.readAt!)}`
  if (has(r.deliveredAt)) return `Delivered ${formatTime(r.deliveredAt!)}`
  return '—'
}

const getRecipientStatusBadgeType = (status: string) => {
  if (['Sent', 'Delivered', 'Read'].includes(status)) return 'success'
  if (['Failed', 'Bounced', 'Complained'].includes(status)) return 'error'
  return 'warning'
}

const getCampaignStatusBadgeType = (status: string) => {
  if (['Sent', 'Success'].includes(status)) return 'success'
  if (status === 'Paused' || status === 'AwaitingApproval') return 'warning'
  // Distinct from 'warning' (Paused): a campaign that partially failed has already run and needs attention.
  if (status === 'PartiallyFailed') return 'partial'
  if (['Failed', 'Cancelled'].includes(status)) return 'error'
  return 'info'
}

const formatCampaignStatus = (status: string) => {
  if (status === 'Sent') return 'Completed'
  if (status === 'Sending') return 'In Progress'
  if (status === 'PartiallyFailed') return 'Partially Failed'
  if (status === 'AwaitingApproval') return 'Awaiting Approval'
  return status
}

export default CampaignDetails
