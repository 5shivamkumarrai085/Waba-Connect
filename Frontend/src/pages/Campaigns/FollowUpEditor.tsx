import React, { useEffect, useState } from 'react'
import { Plus, Repeat, Trash2 } from 'lucide-react'
import useReference from '../../hooks/useReference'
import { referenceService } from '../../services/referenceService'
import { templateService } from '../../services/templates/templateService'
import { emailTemplateService } from '../../services/email/emailTemplateService'
import { emailConnectionService } from '../../services/email/emailConnectionService'
import { connectionService } from '../../services/connections/connectionService'

export interface FollowUpForm {
  condition: string
  delayHours: number
  action: string
  channel: 'Email' | 'WhatsApp'
  templateId?: number
  emailTemplateId?: number
  senderIdentityId?: number
  connectionId?: number
  subjectOverride?: string
  tag?: string
}

interface FollowUpEditorProps {
  /** The campaign's own channel: decides which conditions exist (opens are email-only, reads WhatsApp-only). */
  channel: 'email' | 'whatsapp'
  value: FollowUpForm[]
  onChange: (value: FollowUpForm[]) => void
}

interface Pickers {
  waTemplates: { id: number; name: string }[]
  waConnections: { id: number; name: string }[]
  emailTemplates: { id: number; name: string }[]
  senders: { id: number; label: string }[]
  failed: boolean
}

/**
 * Follow-ups: "N hours later, to recipients who did (not) do X, send Y" — on either channel — or
 * tag them. Each becomes its own campaign when due, so approval, consent and limits still apply.
 * Conditions, actions and limits come from the server (GET api/reference/campaign-options).
 */
export const FollowUpEditor: React.FC<FollowUpEditorProps> = ({ channel, value, onChange }) => {
  const apiChannel = channel === 'email' ? 'Email' : 'WhatsApp'
  const options = useReference(() => referenceService.getCampaignOptions(apiChannel), `campaign-options:${apiChannel}`)
  const [pickers, setPickers] = useState<Pickers>({ waTemplates: [], waConnections: [], emailTemplates: [], senders: [], failed: false })
  const hasRules = value.length > 0

  useEffect(() => {
    if (!hasRules) return
    let active = true
    Promise.allSettled([
      templateService.getTemplates(),
      connectionService.getConnections(),
      emailTemplateService.getTemplates(true),
      emailConnectionService.getConnections(),
    ]).then(([wa, conns, et, ec]) => {
      if (!active) return
      setPickers({
        waTemplates: wa.status === 'fulfilled' ? wa.value.filter(t => t.status?.toLowerCase() === 'approved').map(t => ({ id: t.id, name: t.name })) : [],
        waConnections: conns.status === 'fulfilled' ? conns.value.filter(c => c.isConnected).map(c => ({ id: c.id, name: c.nickname || c.name })) : [],
        emailTemplates: et.status === 'fulfilled' ? et.value.map(t => ({ id: t.id, name: t.name })) : [],
        senders: ec.status === 'fulfilled'
          ? ec.value.flatMap(c => c.senders.filter(s => s.canSend).map(s => ({ id: s.id, label: `${s.displayName} <${s.emailAddress}>` })))
          : [],
        failed: [wa, conns, et, ec].some(r => r.status === 'rejected'),
      })
    })
    return () => { active = false }
  }, [hasRules])

  const o = options.data
  const update = (index: number, patch: Partial<FollowUpForm>) =>
    onChange(value.map((rule, i) => (i === index ? { ...rule, ...patch } : rule)))

  const add = () => {
    if (!o) return
    onChange([...value, {
      condition: o.followUpConditions[0].value,
      delayHours: o.followUpDelayHours.default,
      action: o.followUpActions[0].value,
      channel: apiChannel,
    }])
  }

  return (
    <section className="followup-editor" aria-labelledby="followup-title">
      <header className="followup-head wizard-panel-head">
        <span className="wizard-panel-icon" aria-hidden="true"><Repeat size={18} /></span>
        <div className="wizard-panel-intro">
          <h4 id="followup-title" className="ab-editor-title">Follow-ups</h4>
          <p className="ab-editor-note">
            Optional. For example: 48 hours later, send a WhatsApp reminder to everyone who did not open the email.
          </p>
        </div>
        {o && value.length < o.maxFollowUps && (
          <button type="button" className="btn-toolbar-tertiary" onClick={add}><Plus size={14} aria-hidden="true" /> Add Follow-up</button>
        )}
      </header>

      {options.error && <p className="followup-error" role="alert">{options.error} <button type="button" className="followup-link" onClick={options.retry}>Try again</button></p>}
      {pickers.failed && hasRules && <p className="followup-error" role="status">Some templates or senders could not be loaded. Reopen this step to try again.</p>}

      {o && (
        <ol className="followup-rules">
          {value.map((rule, index) => {
            const n = index + 1
            return (
              <li key={index} className="followup-rule">
                <div className="followup-line">
                  <span>After</span>
                  <input type="number" inputMode="numeric" className="form-control followup-hours"
                    min={o.followUpDelayHours.min} max={o.followUpDelayHours.max} value={rule.delayHours}
                    aria-label={`Follow-up ${n}: delay in hours`} onChange={e => update(index, { delayHours: Number(e.target.value) })} />
                  <span>hours, to recipients who</span>
                  <select className="form-control" value={rule.condition} aria-label={`Follow-up ${n}: condition`}
                    onChange={e => update(index, { condition: e.target.value })}>
                    {o.followUpConditions.map(c => <option key={c.value} value={c.value}>{c.label}</option>)}
                  </select>
                  <button type="button" className="ab-icon-btn" aria-label={`Remove follow-up ${n}`} title="Remove follow-up"
                    onClick={() => onChange(value.filter((_, i) => i !== index))}>
                    <Trash2 size={15} aria-hidden="true" />
                  </button>
                </div>

                <div className="followup-line">
                  <select className="form-control" value={rule.action} aria-label={`Follow-up ${n}: action`}
                    onChange={e => update(index, { action: e.target.value })}>
                    {o.followUpActions.map(a => <option key={a.value} value={a.value}>{a.label}</option>)}
                  </select>

                  {rule.action === 'tag' ? (
                    <input className="form-control" placeholder="Tag name…" value={rule.tag ?? ''} maxLength={100} autoComplete="off"
                      aria-label={`Follow-up ${n}: tag`} onChange={e => update(index, { tag: e.target.value.replace(/,/g, '') })} />
                  ) : (
                    <>
                      <select className="form-control" value={rule.channel} aria-label={`Follow-up ${n}: channel`}
                        onChange={e => update(index, { channel: e.target.value as 'Email' | 'WhatsApp', templateId: undefined, emailTemplateId: undefined })}>
                        <option value="WhatsApp">as a WhatsApp template</option>
                        <option value="Email">as an email</option>
                      </select>
                      {rule.channel === 'WhatsApp' ? (
                        <>
                          <select className="form-control" value={rule.templateId ?? ''} aria-label={`Follow-up ${n}: WhatsApp template`}
                            onChange={e => update(index, { templateId: e.target.value ? Number(e.target.value) : undefined })}>
                            <option value="">Template…</option>
                            {pickers.waTemplates.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                          </select>
                          {channel === 'email' && (
                            <select className="form-control" value={rule.connectionId ?? ''} aria-label={`Follow-up ${n}: WhatsApp connection`}
                              onChange={e => update(index, { connectionId: e.target.value ? Number(e.target.value) : undefined })}>
                              <option value="">From connection…</option>
                              {pickers.waConnections.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                            </select>
                          )}
                        </>
                      ) : (
                        <>
                          <select className="form-control" value={rule.emailTemplateId ?? ''} aria-label={`Follow-up ${n}: email template`}
                            onChange={e => update(index, { emailTemplateId: e.target.value ? Number(e.target.value) : undefined })}>
                            <option value="">Template…</option>
                            {pickers.emailTemplates.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                          </select>
                          <select className="form-control" value={rule.senderIdentityId ?? ''} aria-label={`Follow-up ${n}: sender`}
                            onChange={e => update(index, { senderIdentityId: e.target.value ? Number(e.target.value) : undefined })}>
                            <option value="">From…</option>
                            {pickers.senders.map(s => <option key={s.id} value={s.id}>{s.label}</option>)}
                          </select>
                        </>
                      )}
                    </>
                  )}
                </div>
              </li>
            )
          })}
        </ol>
      )}
    </section>
  )
}

export default FollowUpEditor
