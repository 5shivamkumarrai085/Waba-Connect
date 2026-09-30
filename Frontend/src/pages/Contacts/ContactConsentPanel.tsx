import React, { useCallback, useEffect, useId, useState } from 'react'
import { History, Loader2, ShieldCheck } from 'lucide-react'
import toast from 'react-hot-toast'
import Can from '../../components/Can/Can'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'
import { Skeleton } from '../../components/Skeleton'
import useReference from '../../hooks/useReference'
import { consentService, type ContactConsent, type ConsentEvent } from '../../services/contacts/consentService'
import { referenceService, labelOf } from '../../services/referenceService'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'

interface ContactConsentPanelProps {
  contactId: number
}

const ALL_TOPICS = 'all'
const topicLabel = (topic: string) => (topic === ALL_TOPICS ? 'All messages' : topic.charAt(0).toUpperCase() + topic.slice(1))
const titleCase = (text: string) => text.replace(/\b\w/g, c => c.toUpperCase())
const statusBadge = (status: string) => (status === 'OptedIn' ? 'success' : 'error')

/**
 * A contact's consent: the current answer per channel and topic, the full history with where each
 * answer came from, and — for users allowed to — recording an opt-in or opt-out with a note.
 * Channels, states and source names come from the server (GET api/reference/consent-options).
 */
export const ContactConsentPanel: React.FC<ContactConsentPanelProps> = ({ contactId }) => {
  const options = useReference(referenceService.getConsentOptions, 'consent-options')
  const ids = { channel: useId(), topic: useId(), note: useId() }
  const [current, setCurrent] = useState<ContactConsent[]>([])
  const [history, setHistory] = useState<ConsentEvent[]>([])
  const [topics, setTopics] = useState<string[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [channel, setChannel] = useState('')
  const [topic, setTopic] = useState(ALL_TOPICS)
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState<string | null>(null)

  const load = useCallback(async () => {
    setIsLoading(true)
    setLoadError(null)
    try {
      const data = await consentService.get(contactId)
      setCurrent(data.current)
      setHistory(data.history)
    } catch (error) {
      setLoadError(error instanceof Error ? error.message : 'Consent could not be loaded.')
    } finally {
      setIsLoading(false)
    }
  }, [contactId])

  useEffect(() => {
    void load()
    referenceService.getConsentTopics().then(setTopics).catch(() => setTopics([]))
  }, [load])

  const effectiveChannel = channel || options.data?.channels[0]?.value || ''

  const record = async (status: string) => {
    setSaving(status)
    try {
      const changed = await consentService.set(contactId, { channel: effectiveChannel, topic, status: status as 'OptedIn' | 'OptedOut', note: note.trim() || undefined })
      toast.success(changed ? `Marked as ${labelOf(options.data?.statuses, status).toLowerCase()}.` : 'No change: the contact already has that answer.')
      setNote('')
      await load()
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'The consent could not be recorded. Try again.')
    } finally {
      setSaving(null)
    }
  }

  const sourceOf = (source: string) => labelOf(options.data?.sources, source)
  const statusOf = (status: string) => labelOf(options.data?.statuses, status)
  const channelOf = (value: string) => labelOf(options.data?.channels, value)

  return (
    <div className="audit-detail">
      <section className="audit-detail-section" aria-busy={isLoading}>
        <h3 className="audit-detail-section-title">
          <ShieldCheck size={12} aria-hidden="true" />
          <span>Current Consent</span>
        </h3>
        {loadError ? (
          <p className="audit-detail-empty consent-error" role="alert">
            {loadError} <button type="button" className="consent-link" onClick={() => void load()}>Try again</button>
          </p>
        ) : isLoading ? (
          <Skeleton variant="list" count={2} />
        ) : current.length === 0 ? (
          <p className="audit-detail-empty">
            No consent recorded yet. Email follows the unsubscribe list; WhatsApp marketing needs an opt-in if Settings › Compliance requires one.
          </p>
        ) : (
          <ul className="consent-list">
            {current.map(c => (
              <li key={`${c.channel}-${c.topic}`} className="consent-row">
                <StatusBadge type={statusBadge(c.status)} text={statusOf(c.status)} />
                <span className="consent-what">{channelOf(c.channel)} · {topicLabel(c.topic)}</span>
                <span className="consent-meta">
                  {sourceOf(c.source)} · <time dateTime={c.updatedAt}>{formatAbsoluteDateTime(c.updatedAt)}</time>
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <Can permission="Consent.Manage">
        <section className="audit-detail-section">
          <h3 className="audit-detail-section-title"><span>Record a Change</span></h3>
          {options.data ? (
            <div className="consent-form">
              <div className="consent-field">
                <label className="form-label" htmlFor={ids.channel}>Channel</label>
                <select id={ids.channel} className="form-control" value={effectiveChannel} onChange={e => setChannel(e.target.value)}>
                  {options.data.channels.map(c => <option key={c.value} value={c.value}>{c.label}</option>)}
                </select>
              </div>
              <div className="consent-field">
                <label className="form-label" htmlFor={ids.topic}>Topic</label>
                <select id={ids.topic} className="form-control" value={topic} onChange={e => setTopic(e.target.value)}>
                  <option value={ALL_TOPICS}>{topicLabel(ALL_TOPICS)}</option>
                  {topics.map(t => <option key={t} value={t}>{topicLabel(t)}</option>)}
                </select>
              </div>
              <div className="consent-field consent-note">
                <label className="form-label" htmlFor={ids.note}>How it was given <span className="consent-optional">(optional)</span></label>
                <input id={ids.note} className="form-control" autoComplete="off"
                  placeholder="Asked by phone on 12 Oct…" value={note} maxLength={500} onChange={e => setNote(e.target.value)} />
              </div>
              <div className="consent-actions">
                {[...options.data.statuses].reverse().map(s => (
                  <button key={s.value} type="button" disabled={saving !== null}
                    className={`oc-dialog-btn ${s.value === 'OptedIn' ? 'oc-dialog-btn-primary' : 'oc-dialog-btn-secondary'}`}
                    onClick={() => record(s.value)}>
                    {saving === s.value && <Loader2 size={14} className="consent-spin" aria-hidden="true" />}
                    Mark as {titleCase(s.label)}
                  </button>
                ))}
              </div>
            </div>
          ) : options.error ? (
            <p className="audit-detail-empty consent-error">{options.error} <button type="button" className="consent-link" onClick={options.retry}>Try again</button></p>
          ) : (
            <Skeleton variant="list" count={2} />
          )}
        </section>
      </Can>

      <section className="audit-detail-section">
        <h3 className="audit-detail-section-title">
          <History size={12} aria-hidden="true" />
          <span>History</span>
          {history.length > 0 && <span className="audit-msg-count">{history.length}</span>}
        </h3>
        {isLoading ? (
          <Skeleton variant="list" count={3} />
        ) : history.length === 0 ? (
          <p className="audit-detail-empty">No consent changes yet.</p>
        ) : (
          <ol className="consent-history">
            {history.map((e, i) => (
              <li key={`${e.occurredAt}-${i}`}>
                <span className="consent-history-head">
                  <StatusBadge type={statusBadge(e.status)} text={statusOf(e.status)} />
                  <span>{channelOf(e.channel)} · {topicLabel(e.topic)}</span>
                </span>
                <span className="consent-meta">
                  {sourceOf(e.source)} · <time dateTime={e.occurredAt}>{formatAbsoluteDateTime(e.occurredAt)}</time>{e.ipAddress ? ` · ${e.ipAddress}` : ''}
                </span>
              </li>
            ))}
          </ol>
        )}
      </section>
    </div>
  )
}

export default ContactConsentPanel
