import React, { useCallback, useEffect, useId, useState } from 'react'
import { AlertTriangle, CheckCircle2, Loader2, RefreshCw, Send, ShieldCheck, XCircle } from 'lucide-react'
import toast from 'react-hot-toast'
import { Skeleton } from '../../components/Skeleton'
import { Toggle } from '../../components/Toggle/Toggle'
import useReference from '../../hooks/useReference'
import { campaignService, type PreflightItem } from '../../services/campaigns/campaignService'
import { referenceService } from '../../services/referenceService'
import { useAuthStore } from '../../store/authStore'

export type { PreflightItem }

interface PreflightPanelProps {
  channel: 'email' | 'whatsapp'
  emailTemplateId?: number
  senderIdentityId?: number
  subjectOverride?: string
  templateId?: number
  isTransactional?: boolean
  variableNames: string[]
  override: boolean
  onOverrideChange: (value: boolean) => void
  /** Reports whether anything failed, so the wizard can block submission. */
  onResult: (hasFailures: boolean) => void
}

const LEVEL = {
  pass: { Icon: CheckCircle2, spoken: 'Passed' },
  warn: { Icon: AlertTriangle, spoken: 'Warning' },
  fail: { Icon: XCircle, spoken: 'Failed' },
} as const

const parseAddresses = (text: string) => text.split(/[,;\s]+/).map(a => a.trim()).filter(Boolean)

/**
 * Pre-flight: the sender domain's DNS and the message content, checked before the campaign is
 * created. Failures block sending (an administrator may override); warnings are advice. Email
 * campaigns can also be sent as a proof to a few addresses first.
 */
export const PreflightPanel: React.FC<PreflightPanelProps> = ({
  channel, emailTemplateId, senderIdentityId, subjectOverride, templateId, isTransactional,
  variableNames, override, onOverrideChange, onResult
}) => {
  const isAdministrator = useAuthStore(s => s.user?.isAdministrator ?? false)
  const apiChannel = channel === 'email' ? 'Email' : 'WhatsApp'
  const options = useReference(() => referenceService.getCampaignOptions(apiChannel), `campaign-options:${apiChannel}`)
  const [items, setItems] = useState<PreflightItem[] | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [proofTo, setProofTo] = useState('')
  const [sendingProof, setSendingProof] = useState(false)
  const titleId = useId()
  const proofId = useId()

  const variableKey = variableNames.join('|')

  const run = useCallback(async () => {
    setIsLoading(true)
    setError(null)
    try {
      const result = await campaignService.precheck({
        channel: apiChannel,
        emailTemplateId,
        senderIdentityId,
        subjectOverride: subjectOverride || null,
        templateId,
        isTransactional: isTransactional ?? false,
        variableNames: variableKey ? variableKey.split('|') : []
      })
      setItems(result)
      onResult(result.some(i => i.level === 'fail'))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The pre-flight check could not run.')
      onResult(false)
    } finally {
      setIsLoading(false)
    }
  }, [apiChannel, emailTemplateId, senderIdentityId, subjectOverride, templateId, isTransactional, variableKey, onResult])

  useEffect(() => {
    void run()
  }, [run])

  const proofAddresses = parseAddresses(proofTo)
  const maxProof = options.data?.maxProofAddresses ?? 0
  const tooManyProofs = maxProof > 0 && proofAddresses.length > maxProof

  const sendProof = async () => {
    if (proofAddresses.length === 0 || tooManyProofs || !emailTemplateId || !senderIdentityId) return
    setSendingProof(true)
    try {
      toast.success(await campaignService.sendProof({ emailTemplateId, senderIdentityId, subjectOverride: subjectOverride || null, toAddresses: proofAddresses }))
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'The proof could not be sent.')
    } finally {
      setSendingProof(false)
    }
  }

  const count = (level: PreflightItem['level']) => items?.filter(i => i.level === level).length ?? 0
  const failures = count('fail')
  // What needs attention first; the passed checks fold away under a disclosure.
  const attention = items?.filter(i => i.level !== 'pass').sort((a, b) => (a.level === b.level ? 0 : a.level === 'fail' ? -1 : 1)) ?? []
  const passed = items?.filter(i => i.level === 'pass') ?? []

  const renderItem = (item: PreflightItem) => {
    const { Icon, spoken } = LEVEL[item.level]
    return (
      <li key={item.key} className={`preflight-item is-${item.level}`}>
        <Icon size={16} aria-hidden="true" />
        <div>
          <strong><span className="sr-only">{spoken}: </span>{item.title}</strong>
          <span>{item.detail}</span>
        </div>
      </li>
    )
  }

  return (
    <section className="wizard-panel preflight-panel" aria-labelledby={titleId} aria-busy={isLoading}>
      <header className="wizard-panel-head">
        <span className="wizard-panel-icon" aria-hidden="true"><ShieldCheck size={18} /></span>
        <div className="wizard-panel-intro">
          <h4 id={titleId} className="ab-editor-title">Pre-flight check</h4>
          <p className="ab-editor-note">Content and sender-domain checks, run on the server before the campaign is saved.</p>
        </div>
        <button type="button" className="btn-toolbar-tertiary" onClick={() => void run()} disabled={isLoading}>
          {isLoading ? <Loader2 size={14} className="wizard-spin" aria-hidden="true" /> : <RefreshCw size={14} aria-hidden="true" />}
          {isLoading ? 'Checking…' : 'Run Again'}
        </button>
      </header>

      {items && (
        <div className="preflight-summary" aria-live="polite">
          <span className="preflight-count is-pass"><CheckCircle2 size={14} aria-hidden="true" /> {count('pass')} passed</span>
          <span className="preflight-count is-warn"><AlertTriangle size={14} aria-hidden="true" /> {count('warn')} warning{count('warn') === 1 ? '' : 's'}</span>
          <span className="preflight-count is-fail"><XCircle size={14} aria-hidden="true" /> {failures} failed</span>
        </div>
      )}

      {error && <p className="wizard-panel-error" role="alert">{error}</p>}
      {items === null && isLoading && <Skeleton variant="list" count={3} />}

      {attention.length > 0 && <ul className="preflight-list">{attention.map(renderItem)}</ul>}

      {passed.length > 0 && (
        <details className="preflight-passed" open={attention.length === 0}>
          <summary>{passed.length} check{passed.length === 1 ? '' : 's'} passed</summary>
          <ul className="preflight-list">{passed.map(renderItem)}</ul>
        </details>
      )}

      {failures > 0 && (
        isAdministrator ? (
          <div className="preflight-override">
            <div>
              <strong>Send anyway</strong>
              <p className="ab-editor-note">The failed checks are recorded in the audit log with your name.</p>
            </div>
            <Toggle checked={override} onChange={onOverrideChange} ariaLabel="Send anyway despite failed checks" />
          </div>
        ) : (
          <p className="wizard-panel-error">Fix the failed checks to continue, or ask an administrator to approve sending anyway.</p>
        )
      )}

      {channel === 'email' && emailTemplateId && senderIdentityId && (
        <div className="preflight-proof">
          <label className="form-label" htmlFor={proofId}>Send a proof</label>
          <div className="preflight-proof-row">
            <input id={proofId} type="email" multiple className="form-control" autoComplete="off" spellCheck={false}
              placeholder="you@example.com, colleague@example.com…" aria-describedby={`${proofId}-hint`} aria-invalid={tooManyProofs}
              value={proofTo} onChange={e => setProofTo(e.target.value)} />
            <button type="button" className="btn-toolbar-tertiary" disabled={sendingProof || proofAddresses.length === 0 || tooManyProofs} onClick={sendProof}>
              {sendingProof ? <Loader2 size={14} className="wizard-spin" aria-hidden="true" /> : <Send size={14} aria-hidden="true" />}
              {sendingProof ? 'Sending…' : 'Send Proof'}
            </button>
          </div>
          <p id={`${proofId}-hint`} className={`ab-editor-note${tooManyProofs ? ' wizard-panel-error-text' : ''}`}>
            {tooManyProofs ? `At most ${maxProof} addresses.` : maxProof ? `Up to ${maxProof} addresses, separated by commas. Sample values fill the merge fields.` : ''}
          </p>
        </div>
      )}
    </section>
  )
}

export default PreflightPanel
