import React, { useEffect, useMemo, useRef, useState } from 'react'
import { motion } from 'framer-motion'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Filter, Loader2, Plus, RefreshCw, Trash2, Users } from 'lucide-react'
import toast from 'react-hot-toast'
import { pageTransitionProps } from '../../utils/motion'
import { Skeleton } from '../../components/Skeleton'
import { EmptyState } from '../../components/EmptyState/EmptyState'
import useReference from '../../hooks/useReference'
import { segmentService, type SegmentPreview, type SegmentRule, type SegmentRuleGroup } from '../../services/segments/segmentService'
import { contactService } from '../../services/contacts/contactService'
import { lookupService } from '../../services/setup/lookupService'
import {
  referenceService,
  type ConsentOptions,
  type SegmentFieldKind,
  type SegmentFieldOptions
} from '../../services/referenceService'
import { getErrorMessage } from '../../utils/errorHelper'
import './Segments.css'

type LookupKey = 'statuses' | 'types' | 'sources'
type LookupLists = Record<LookupKey, { value: string; label: string }[]>

// Starts with a rule that is already valid ("status is not empty" = everyone), so a new segment
// opens with a count rather than a validation message.
const emptyRules = (): SegmentRuleGroup => ({ match: 'all', rules: [{ field: 'status', op: 'notEmpty' }], groups: [] })

/** The rule builder: fields, operators and values, with a live member count and sample. */
export const SegmentEditor: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const segmentId = id && id !== 'new' ? Number(id) : null

  const catalogue = useReference(referenceService.getSegmentFields, 'segment-fields')
  const consent = useReference(referenceService.getConsentOptions, 'consent-options')

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [rules, setRules] = useState<SegmentRuleGroup>(emptyRules)
  const [loadingSegment, setLoadingSegment] = useState(segmentId !== null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [dirty, setDirty] = useState(false)
  const [nameError, setNameError] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [preview, setPreview] = useState<SegmentPreview | null>(null)
  const [previewError, setPreviewError] = useState<string | null>(null)
  const [counting, setCounting] = useState(false)
  const [saving, setSaving] = useState(false)
  const [groups, setGroups] = useState<{ id: number | string; name: string }[]>([])
  const [topics, setTopics] = useState<string[]>([])
  const [lookups, setLookups] = useState<LookupLists>({ statuses: [], types: [], sources: [] })
  const abortRef = useRef<AbortController | null>(null)
  const nameRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    contactService.getContactGroups().then((g: { id: number | string; name: string }[]) => setGroups(g ?? [])).catch(() => setGroups([]))
    referenceService.getConsentTopics().then(setTopics).catch(() => setTopics([]))
    const toOptions = (list: { value: string; name: string; isActive: boolean }[]) =>
      list.filter(l => l.isActive).map(l => ({ value: l.value, label: l.name }))
    Promise.all([lookupService.getStatuses(), lookupService.getTypes(), lookupService.getSources()])
      .then(([statuses, types, sources]) => setLookups({ statuses: toOptions(statuses), types: toOptions(types), sources: toOptions(sources) }))
      .catch(() => setLookups({ statuses: [], types: [], sources: [] }))
  }, [])

  useEffect(() => {
    if (!segmentId) return
    setLoadingSegment(true)
    setLoadError(null)
    segmentService.get(segmentId)
      .then(s => {
        setName(s.name)
        setDescription(s.description ?? '')
        setRules(s.rules?.rules?.length ? { match: s.rules.match ?? 'all', rules: s.rules.rules, groups: s.rules.groups ?? [] } : emptyRules())
      })
      .catch(e => setLoadError(getErrorMessage(e, 'The segment could not be loaded.')))
      .finally(() => setLoadingSegment(false))
  }, [segmentId])

  // Leaving with unsaved changes asks first (browser-level: reload, close tab).
  useEffect(() => {
    if (!dirty) return
    const warn = (e: BeforeUnloadEvent) => { e.preventDefault() }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  // Live count, debounced; a newer edit cancels the request still in flight.
  const rulesKey = useMemo(() => JSON.stringify(rules), [rules])
  useEffect(() => {
    const timer = setTimeout(async () => {
      abortRef.current?.abort()
      const controller = new AbortController()
      abortRef.current = controller
      setCounting(true)
      try {
        const result = await segmentService.preview(JSON.parse(rulesKey), controller.signal)
        setPreview(result)
        setPreviewError(null)
      } catch (e) {
        if (!controller.signal.aborted) {
          setPreview(null)
          setPreviewError(e instanceof Error ? e.message : 'The segment could not be counted.')
        }
      } finally {
        if (!controller.signal.aborted) setCounting(false)
      }
    }, 400)
    return () => clearTimeout(timer)
  }, [rulesKey])

  const kindOf = (fields: SegmentFieldOptions, field: string): SegmentFieldKind =>
    fields.fields.find(f => f.value === field)?.kind ?? 'text'

  const defaultConsentValue = (c: ConsentOptions | null) =>
    `${c?.channels[0]?.value ?? ''}:${topics[0] ?? 'all'}:${c?.statuses[0]?.value ?? ''}`

  const defaultRule = (fields: SegmentFieldOptions, field: string): SegmentRule => {
    const kind = kindOf(fields, field)
    return {
      field,
      op: fields.operators[kind][0].value,
      value: kind === 'consent' ? defaultConsentValue(consent.data) : '',
      days: kind === 'date' || kind === 'activity' ? fields.days.default : undefined,
    }
  }

  /** Initials for the sample list's avatars. */
  const initials = (name: string) => name.split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0]?.toUpperCase()).join('') || '?'

  const edit = (next: (r: SegmentRuleGroup) => SegmentRuleGroup) => {
    setRules(next)
    setDirty(true)
  }

  const updateRule = (index: number, patch: Partial<SegmentRule>) =>
    edit(r => ({ ...r, rules: r.rules.map((rule, i) => (i === index ? { ...rule, ...patch } : rule)) }))

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaveError(null)
    if (!name.trim()) {
      setNameError('Give the segment a name.')
      nameRef.current?.focus()
      return
    }
    setSaving(true)
    try {
      await segmentService.save(segmentId, { name: name.trim(), description: description.trim() || undefined, rules })
      setDirty(false)
      toast.success(`Segment “${name.trim()}” saved.`)
      navigate('/segments')
    } catch (err) {
      setSaveError(err instanceof Error ? err.message : 'The segment could not be saved.')
    } finally {
      setSaving(false)
    }
  }

  const leave = () => {
    if (dirty && !window.confirm('Leave without saving? Your changes to this segment will be lost.')) return
    navigate('/segments')
  }

  const numbers = new Intl.NumberFormat()

  const renderRule = (fields: SegmentFieldOptions, rule: SegmentRule, index: number) => {
    const field = fields.fields.find(f => f.value === rule.field)
    const kind = field?.kind ?? 'text'
    const [cChannel, cTopic, cStatus] = (rule.value || defaultConsentValue(consent.data)).split(':')
    const needsValue = !((kind === 'text' || kind === 'number') && (rule.op === 'empty' || rule.op === 'notEmpty'))
    const lookup = field?.lookup ? lookups[field.lookup] : null
    const n = index + 1

    return (
      <li key={index} className="sg-rule">
        <span className={`sg-connector${index === 0 ? ' is-first' : ''}`} aria-hidden="true">
          {index === 0 ? 'Where' : rules.match === 'all' ? 'And' : 'Or'}
        </span>
        <div className="sg-rule-controls">
          <select className="form-control" value={rule.field} aria-label={`Rule ${n} field`}
            onChange={e => updateRule(index, defaultRule(fields, e.target.value))}>
            {fields.fields.map(f => <option key={f.value} value={f.value}>{f.label}</option>)}
          </select>
          <select className="form-control" value={rule.op} aria-label={`Rule ${n} condition`} onChange={e => updateRule(index, { op: e.target.value })}>
            {fields.operators[kind].map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>

          {kind === 'group' && (
            <select className="form-control" value={rule.value ?? ''} aria-label={`Rule ${n} group`} onChange={e => updateRule(index, { value: e.target.value })}>
              <option value="">Choose a group…</option>
              {groups.map(g => <option key={g.id} value={String(g.id)}>{g.name}</option>)}
            </select>
          )}

          {(kind === 'date' || kind === 'activity') && (
            <span className="sg-inline">
              <input type="number" inputMode="numeric" min={fields.days.min} max={fields.days.max} className="form-control sg-number"
                value={rule.days ?? fields.days.default} aria-label={`Rule ${n} number of days`}
                onChange={e => updateRule(index, { days: Number(e.target.value) })} />
              <span>days{kind === 'date' && rule.op === 'olderThanDays' ? ' ago' : ''}</span>
            </span>
          )}

          {kind === 'number' && needsValue && (
            <span className="sg-inline">
              <input type="number" inputMode="numeric" min={fields.ages.min} max={fields.ages.max} className="form-control sg-number"
                value={rule.value ?? ''} placeholder={String(fields.ages.default)} aria-label={`Rule ${n} age in years`}
                onChange={e => updateRule(index, { value: e.target.value })} />
              <span>years</span>
            </span>
          )}

          {kind === 'consent' && consent.data && (
            <span className="sg-inline sg-consent">
              <select className="form-control" value={cStatus} aria-label={`Rule ${n} consent`}
                onChange={e => updateRule(index, { value: `${cChannel}:${cTopic}:${e.target.value}` })}>
                {consent.data.statuses.map(s => <option key={s.value} value={s.value}>{s.label.toLowerCase()}</option>)}
              </select>
              <span>to</span>
              <select className="form-control" value={cChannel} aria-label={`Rule ${n} channel`}
                onChange={e => updateRule(index, { value: `${e.target.value}:${cTopic}:${cStatus}` })}>
                {consent.data.channels.map(c => <option key={c.value} value={c.value}>{c.label}</option>)}
              </select>
              <select className="form-control" value={cTopic} aria-label={`Rule ${n} topic`}
                onChange={e => updateRule(index, { value: `${cChannel}:${e.target.value}:${cStatus}` })}>
                <option value="all">all messages</option>
                {topics.map(t => <option key={t} value={t}>{t}</option>)}
              </select>
            </span>
          )}

          {(kind === 'text' || kind === 'tag') && needsValue && (
            lookup && lookup.length > 0 && (rule.op === 'eq' || rule.op === 'neq') ? (
              <select className="form-control" value={rule.value ?? ''} aria-label={`Rule ${n} value`} onChange={e => updateRule(index, { value: e.target.value })}>
                <option value="">Choose…</option>
                {lookup.map(l => <option key={l.value} value={l.value}>{l.label}</option>)}
              </select>
            ) : (
              <input className="form-control" value={rule.value ?? ''} aria-label={`Rule ${n} value`} placeholder="Value…" autoComplete="off"
                onChange={e => updateRule(index, { value: e.target.value })} />
            )
          )}
        </div>

        <button type="button" className="segments-icon-btn is-danger" aria-label={`Remove rule ${n}`} title={rules.rules.length === 1 ? 'A segment needs at least one rule' : 'Remove rule'}
          disabled={rules.rules.length === 1}
          onClick={() => edit(r => ({ ...r, rules: r.rules.filter((_, i) => i !== index) }))}>
          <Trash2 size={15} aria-hidden="true" />
        </button>
      </li>
    )
  }

  const title = segmentId ? 'Edit Segment' : 'New Segment'
  const fields = catalogue.data

  return (
    <motion.div {...pageTransitionProps}>
      <div className="wizard-header omni-page-hero form-page-hero segment-hero">
        <button type="button" className="btn-back" onClick={leave} aria-label="Back to segments">
          <ArrowLeft size={18} aria-hidden="true" />
        </button>
        <div>
          <h1>{title}</h1>
          <p>The contacts who match these rules at the moment a campaign sends are the ones who receive it.</p>
        </div>
      </div>

      {loadError || catalogue.error ? (
        <div className="contacts-card">
          <EmptyState iconName="AlertCircle" title="The segment could not be opened" message={loadError ?? catalogue.error ?? ''}
            action={{ label: 'Back to Segments', onClick: () => navigate('/segments') }} />
        </div>
      ) : loadingSegment || !fields ? (
        <div className="sg-layout">
          <div className="sg-main"><div className="sg-panel"><Skeleton variant="list" count={4} /></div></div>
          <div className="sg-preview"><Skeleton variant="card" /></div>
        </div>
      ) : (
        <form className="sg-layout" onSubmit={handleSave} noValidate>
          <div className="sg-main">
            {saveError && <div role="alert" className="segment-form-error">{saveError}</div>}

            <section className="sg-panel" aria-labelledby="sg-details-title">
              <h2 id="sg-details-title" className="sg-panel-title">Details</h2>
              <div className="sg-fields">
                <div className="sg-field">
                  <label className="form-label" htmlFor="segment-name">Name <span className="sg-required" aria-hidden="true">*</span></label>
                  <input id="segment-name" ref={nameRef} name="segment-name" className="form-control" value={name} maxLength={200} autoComplete="off"
                    aria-invalid={!!nameError} aria-describedby={nameError ? 'segment-name-error' : undefined}
                    onChange={e => { setName(e.target.value); setDirty(true); if (nameError) setNameError(null) }} placeholder="Engaged Mumbai leads…" />
                  {nameError && <p id="segment-name-error" className="sg-error">{nameError}</p>}
                </div>
                <div className="sg-field">
                  <label className="form-label" htmlFor="segment-desc">Description <span className="segment-optional">(optional)</span></label>
                  <input id="segment-desc" name="segment-description" className="form-control" value={description} maxLength={1000} autoComplete="off"
                    onChange={e => { setDescription(e.target.value); setDirty(true) }} placeholder="Who this audience is, and what it is for…" />
                </div>
              </div>
            </section>

            <fieldset className="sg-panel sg-rules-panel">
              <legend className="sg-panel-title">Rules</legend>
              <div className="sg-match" role="radiogroup" aria-label="Which rules a contact must match">
                <span className="sg-match-text">Contacts who match</span>
                {(['all', 'any'] as const).map(m => (
                  <label key={m} className={`sg-match-option${rules.match === m ? ' is-active' : ''}`}>
                    <input type="radio" name="segment-match" value={m} checked={rules.match === m}
                      onChange={() => edit(r => ({ ...r, match: m }))} />
                    {m === 'all' ? 'All rules' : 'Any rule'}
                  </label>
                ))}
              </div>

              <ol className="sg-rules">
                {rules.rules.map((rule, index) => renderRule(fields, rule, index))}
              </ol>

              <button type="button" className="sg-add" onClick={() => edit(r => ({ ...r, rules: [...r.rules, defaultRule(fields, fields.fields[0].value)] }))}>
                <Plus size={15} aria-hidden="true" /> Add Rule
              </button>
            </fieldset>

            <div className="sg-footer">
              <span className="sg-footer-note">{dirty ? 'Unsaved changes' : ''}</span>
              <button type="button" className="btn btn-secondary" onClick={leave}>Cancel</button>
              <button type="submit" className="btn btn-primary" disabled={saving}>
                {saving && <Loader2 size={15} className="segment-spin" aria-hidden="true" />}
                {saving ? 'Saving…' : 'Save Segment'}
              </button>
            </div>
          </div>

          <aside className="sg-preview" aria-live="polite" aria-busy={counting}>
            <div className="sg-preview-head">
              <span className="sg-preview-icon" aria-hidden="true"><Filter size={16} /></span>
              <span>Live preview</span>
              <span className={`sg-preview-state${counting ? ' is-busy' : ''}`}>
                {counting ? <><RefreshCw size={12} className="segment-spin" aria-hidden="true" /> Updating…</> : 'Up to date'}
              </span>
            </div>
            {previewError ? (
              <p className="sg-error" role="alert">{previewError}</p>
            ) : (
              <div className="sg-count">
                <strong>{counting && !preview ? '…' : numbers.format(preview?.count ?? 0)}</strong>
                <span>contact{preview?.count === 1 ? '' : 's'} match now</span>
              </div>
            )}
            <p className="sg-preview-note">Re-evaluated when a campaign sends, so the audience stays current.</p>
            {preview && preview.sample.length > 0 ? (
              <>
                <p className="sg-sample-title">For example</p>
                <ul className="sg-sample">
                  {preview.sample.map(c => (
                    <li key={c.id}>
                      <span className="sg-avatar" aria-hidden="true">{initials(c.name)}</span>
                      <span className="sg-sample-text"><span>{c.name}</span><small>{c.email || c.phone}</small></span>
                    </li>
                  ))}
                </ul>
              </>
            ) : preview && preview.count === 0 && !counting ? (
              <p className="sg-empty"><Users size={16} aria-hidden="true" /> No contacts match these rules yet.</p>
            ) : null}
          </aside>
        </form>
      )}
    </motion.div>
  )
}

export default SegmentEditor
