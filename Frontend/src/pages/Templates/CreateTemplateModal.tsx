import React, { useEffect, useMemo, useRef, useState } from 'react'
import { Plus, Trash2, Send, Loader2, AlertCircle } from 'lucide-react'
import toast from 'react-hot-toast'
import Modal from '../../components/Modal/Modal'
import { ChoicePills } from '../../components/ChoicePills/ChoicePills'
import { SearchableSelect } from '../../components/SearchableSelect/SearchableSelect'
import { Skeleton } from '../../components/Skeleton'
import { EmptyState } from '../../components/EmptyState/EmptyState'
import { WhatsAppPreview } from '../../components/WhatsAppPreview/WhatsAppPreview'
import useReference from '../../hooks/useReference'
import { referenceService, type TemplateOptions } from '../../services/referenceService'
import { templateService } from '../../services/templates/templateService'
import type { CreateTemplateInput, TemplateButton, TemplateButtonType, TemplateLanguage } from '../../types/templates'
import './CreateTemplateModal.css'

interface ConnectionOption {
  id: number
  name: string
}

interface CreateTemplateModalProps {
  isOpen: boolean
  onClose: () => void
  languages: TemplateLanguage[]
  connections: ConnectionOption[]
  /** After Meta accepted the submission, so the list can be re-read. */
  onSubmitted: () => void
}

/** {{1}}, {{2}}… in the body, in order, without repeats. */
const bodyPositions = (body: string): number[] =>
  [...new Set([...body.matchAll(/\{\{(\d+)\}\}/g)].map(m => Number(m[1])))].sort((a, b) => a - b)

/** Names are lower-case with underscores; typing "Order Update" becomes "order_update". */
const toTemplateName = (value: string, max: number) => value.toLowerCase().replace(/\s+/g, '_').replace(/[^a-z0-9_]/g, '').slice(0, max)

/** The template language that matches the viewer's own, else the first offered. */
const defaultLanguage = (languages: TemplateLanguage[]) => {
  const browser = (typeof navigator !== 'undefined' ? navigator.languages : []).map(l => l.toLowerCase())
  return languages.find(l => browser.includes(l.code.toLowerCase()))?.code
    ?? languages.find(l => browser.some(b => b.split('-')[0] === l.code.toLowerCase().split('_')[0]))?.code
    ?? languages[0]?.code
    ?? ''
}

interface FormState {
  name: string
  category: string
  language: string
  header: string
  body: string
  footer: string
  positions: number[]
  samples: Record<number, string>
  buttons: TemplateButton[]
  connectionId: number | ''
}

/** Everything wrong with the form, in reading order. Empty when it can be submitted. */
const validate = (o: TemplateOptions, f: FormState): string[] => {
  const list: string[] = []
  if (!new RegExp(o.namePattern).test(f.name)) list.push('Give the template a name using lower-case letters, digits and underscores.')
  if (!f.category) list.push('Choose a category.')
  if (!f.language) list.push('Choose a language.')
  if (!f.body.trim()) list.push('Write the message body.')
  if (f.body.length > o.maxBodyLength) list.push(`Shorten the body to ${o.maxBodyLength} characters.`)
  if (f.header.length > o.maxHeaderLength) list.push(`Shorten the header to ${o.maxHeaderLength} characters.`)
  if (f.footer.length > o.maxFooterLength) list.push(`Shorten the footer to ${o.maxFooterLength} characters.`)
  if (f.positions.some((p, i) => p !== i + 1)) list.push('Number the variables {{1}}, {{2}}, … without gaps.')
  if (f.positions.some(p => !f.samples[p]?.trim())) list.push('Give a sample value for every variable — Meta reviews the template with them.')
  f.buttons.forEach((b, i) => {
    const n = i + 1
    if (b.type !== 'COPY_CODE' && !b.text.trim()) list.push(`Give button ${n} a label.`)
    if (b.type === 'URL' && !/^https:\/\/\S+$/i.test(b.url ?? '')) list.push(`Give button ${n} a link starting with https://.`)
    if (b.type === 'PHONE_NUMBER' && !/^\+?\d{6,15}$/.test(b.phone_number ?? '')) list.push(`Give button ${n} a phone number in international format, e.g. +919876543210.`)
    if (b.type === 'COPY_CODE' && !b.example?.trim()) list.push(`Give button ${n} an example code.`)
  })
  if (!f.connectionId) list.push('Choose the WhatsApp account to create it on.')
  return list
}

/**
 * Creates a WhatsApp template here and submits it to Meta for review. Media headers, carousels and
 * authentication templates need assets or layouts Meta provides, so those are made in WhatsApp
 * Manager and loaded by sync.
 */
export const CreateTemplateModal: React.FC<CreateTemplateModalProps> = ({ isOpen, onClose, languages, connections, onSubmitted }) => {
  const options = useReference(referenceService.getTemplateOptions, 'template-options')

  const [name, setName] = useState('')
  const [language, setLanguage] = useState('')
  const [category, setCategory] = useState('')
  const [header, setHeader] = useState('')
  const [body, setBody] = useState('')
  const [footer, setFooter] = useState('')
  const [samples, setSamples] = useState<Record<number, string>>({})
  const [buttons, setButtons] = useState<TemplateButton[]>([])
  const [chosenConnection, setConnectionId] = useState<number | ''>('')
  const [savedId, setSavedId] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [showProblems, setShowProblems] = useState(false)
  const errorRef = useRef<HTMLDivElement>(null)

  // Lists arrive after the dialog mounts; with nothing chosen yet, sensible defaults apply.
  const connectionId: number | '' = chosenConnection === '' ? (connections[0]?.id ?? '') : chosenConnection
  const effectiveLanguage = language || defaultLanguage(languages)
  const effectiveCategory = category || options.data?.categories[0]?.value || ''

  useEffect(() => {
    if (error) errorRef.current?.focus()
  }, [error])

  const positions = useMemo(() => bodyPositions(body), [body])
  const o = options.data

  const problems = useMemo(() => o ? validate(o, {
    name, category: effectiveCategory, language: effectiveLanguage, header, body, footer, positions, samples, buttons, connectionId
  }) : [], [o, name, effectiveCategory, effectiveLanguage, header, body, footer, positions, samples, buttons, connectionId])

  const reset = () => {
    setName(''); setHeader(''); setBody(''); setFooter(''); setSamples({}); setButtons([])
    setLanguage(''); setCategory(''); setSavedId(null); setError(null); setShowProblems(false)
  }

  const close = () => {
    if (busy) return
    reset()
    onClose()
  }

  const updateButton = (index: number, patch: Partial<TemplateButton>) =>
    setButtons(list => list.map((b, i) => (i === index ? { ...b, ...patch } : b)))

  const countOf = (type: string) => buttons.filter(b => b.type === type).length
  const firstAddableType = o?.buttonTypes.find(t => countOf(t.value) < t.maxCount)?.value as TemplateButtonType | undefined
  const labelForType = (type: string) => o?.buttonTypes.find(t => t.value === type)?.label ?? type

  const submit = async () => {
    if (!o) return
    if (problems.length > 0 || !connectionId) {
      setShowProblems(true)
      return
    }
    const input: CreateTemplateInput = {
      name,
      language: effectiveLanguage,
      category: effectiveCategory,
      bodyText: body,
      headerType: header.trim() ? 'Text' : 'None',
      headerContent: header.trim() || undefined,
      footerText: footer.trim() || undefined,
      variables: positions.map(p => ({ position: p, sampleValue: samples[p]?.trim() })),
      buttons: buttons.map(b => ({ ...b, text: b.text.trim() }))
    }
    setBusy(true)
    setError(null)
    try {
      // Saved first, so a refused submission can be corrected and sent again without a
      // duplicate-name error.
      const saved = savedId ? await templateService.updateTemplate(savedId, input) : await templateService.createTemplate(input)
      setSavedId(saved.id)
      const submitted = await templateService.submitTemplate(saved.id, connectionId)
      toast.success(`“${name}” submitted to Meta — ${submitted.status?.toLowerCase() ?? 'pending'} review.`)
      reset()
      onClose()
      onSubmitted()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The template could not be submitted.')
    } finally {
      setBusy(false)
    }
  }

  const previewBody = positions.reduce((text, p) => text.split(`{{${p}}}`).join(samples[p]?.trim() || `{{${p}}}`), body)
  const connectionName = connections.find(c => c.id === connectionId)?.name

  const renderForm = (opts: TemplateOptions) => (
    <div className="tpl-create">
      <div className="tpl-create-form">
        {error && <div ref={errorRef} tabIndex={-1} role="alert" className="tpl-create-error">{error}</div>}

        <div className="setup-field">
          <label className="setup-label required" htmlFor="tpl-name">Name</label>
          <input id="tpl-name" name="template-name" className="setup-input" value={name} data-autofocus autoComplete="off" spellCheck={false}
            translate="no" maxLength={opts.maxNameLength} aria-describedby="tpl-name-hint"
            onChange={e => setName(toTemplateName(e.target.value, opts.maxNameLength))} placeholder="order_update…" />
          <p id="tpl-name-hint" className="setup-hint">Lower-case letters, digits and underscores. Spaces become underscores.</p>
        </div>

        <div className="setup-field">
          <span className="setup-label required">Category</span>
          <ChoicePills
            ariaLabel="Category"
            options={opts.categories.map(c => ({ value: c.value, label: c.label }))}
            selected={[effectiveCategory]}
            onChange={v => setCategory(String(v[0] ?? ''))}
          />
          <p className="setup-hint">{opts.categories.find(c => c.value === effectiveCategory)?.description}</p>
        </div>

        <SearchableSelect
          id="tpl-language"
          label="Language"
          value={effectiveLanguage}
          options={languages.map(l => ({ value: l.code, label: l.name }))}
          onChange={setLanguage}
          hideAllOption
          emptyMessage="No languages available — load templates first."
        />

        <div className="setup-field">
          <label className="setup-label" htmlFor="tpl-header">Header <span className="tpl-optional">(optional)</span></label>
          <input id="tpl-header" name="template-header" className="setup-input" value={header} maxLength={opts.maxHeaderLength} autoComplete="off"
            onChange={e => setHeader(e.target.value)} placeholder="Your order has shipped…" />
          <p className="setup-hint tpl-counter">{header.length} / {opts.maxHeaderLength}</p>
        </div>

        <div className="setup-field">
          <label className="setup-label required" htmlFor="tpl-body">Body</label>
          <textarea id="tpl-body" name="template-body" className="setup-input setup-textarea" rows={5} value={body} maxLength={opts.maxBodyLength}
            aria-describedby="tpl-body-hint" onChange={e => setBody(e.target.value)} placeholder="Hi {{1}}, your order {{2}} is on its way…" />
          <p id="tpl-body-hint" className="setup-hint tpl-hint-row">
            <span>Use {'{{1}}'}, {'{{2}}'}… for values filled in per recipient.</span>
            <span className="tpl-counter">{body.length} / {opts.maxBodyLength}</span>
          </p>
        </div>

        {positions.length > 0 && (
          <fieldset className="tpl-create-samples">
            <legend className="setup-label">Sample values</legend>
            {positions.map(p => (
              <div key={p} className="setup-field">
                <label className="setup-label required" htmlFor={`tpl-sample-${p}`}>{`{{${p}}}`}</label>
                <input id={`tpl-sample-${p}`} className="setup-input" value={samples[p] ?? ''} autoComplete="off"
                  onChange={e => setSamples(s => ({ ...s, [p]: e.target.value }))} />
              </div>
            ))}
          </fieldset>
        )}

        <div className="setup-field">
          <label className="setup-label" htmlFor="tpl-footer">Footer <span className="tpl-optional">(optional)</span></label>
          <input id="tpl-footer" name="template-footer" className="setup-input" value={footer} maxLength={opts.maxFooterLength} autoComplete="off"
            onChange={e => setFooter(e.target.value)} placeholder="Reply STOP to opt out…" />
          <p className="setup-hint tpl-counter">{footer.length} / {opts.maxFooterLength}</p>
        </div>

        <fieldset className="tpl-create-buttons">
          <legend className="tpl-create-buttons-head">
            <span className="setup-label">Buttons <span className="tpl-optional">(optional, up to {opts.maxButtons})</span></span>
          </legend>
          {buttons.map((b, i) => {
            const type = opts.buttonTypes.find(t => t.value === b.type)
            return (
              <div key={i} className="tpl-create-button">
                <select className="setup-input" aria-label={`Button ${i + 1} type`} value={b.type}
                  onChange={e => updateButton(i, { type: e.target.value as TemplateButtonType, url: undefined, phone_number: undefined, example: undefined })}>
                  {opts.buttonTypes.map(t => (
                    <option key={t.value} value={t.value} disabled={t.value !== b.type && countOf(t.value) >= t.maxCount}>{t.label}</option>
                  ))}
                </select>
                {b.type !== 'COPY_CODE' && (
                  <input className="setup-input" aria-label={`Button ${i + 1} label`} placeholder="Label…" maxLength={opts.maxButtonLabelLength}
                    autoComplete="off" value={b.text} onChange={e => updateButton(i, { text: e.target.value })} />
                )}
                {b.type === 'URL' && (
                  <input className="setup-input" type="url" inputMode="url" spellCheck={false} translate="no" aria-label={`Button ${i + 1} link`}
                    placeholder="https://example.com/track/{{1}}…" autoComplete="off" value={b.url ?? ''} onChange={e => updateButton(i, { url: e.target.value })} />
                )}
                {b.type === 'PHONE_NUMBER' && (
                  <input className="setup-input" type="tel" inputMode="tel" aria-label={`Button ${i + 1} phone number`} placeholder="+919876543210…"
                    autoComplete="off" value={b.phone_number ?? ''} onChange={e => updateButton(i, { phone_number: e.target.value })} />
                )}
                {b.type === 'COPY_CODE' && (
                  <input className="setup-input" aria-label={`Button ${i + 1} example code`} placeholder="Example code, e.g. SAVE20…" spellCheck={false}
                    autoComplete="off" value={b.example ?? ''} onChange={e => updateButton(i, { example: e.target.value })} />
                )}
                <button type="button" className="tpl-remove" aria-label={`Remove button ${i + 1}${b.text ? ` (${b.text})` : ''}`}
                  onClick={() => setButtons(list => list.filter((_, j) => j !== i))}>
                  <Trash2 size={16} aria-hidden="true" />
                </button>
                {type && <p className="setup-hint tpl-button-hint">{type.description}</p>}
              </div>
            )
          })}
          <button type="button" className="btn-toolbar-tertiary tpl-add-button"
            disabled={buttons.length >= opts.maxButtons || !firstAddableType}
            onClick={() => firstAddableType && setButtons(list => [...list, { type: firstAddableType, text: '' }])}>
            <Plus size={14} aria-hidden="true" /> Add Button
          </button>
        </fieldset>

        <SearchableSelect
          id="tpl-connection"
          label="WhatsApp account"
          value={connectionId ? String(connectionId) : ''}
          options={connections.map(c => ({ value: String(c.id), label: c.name }))}
          onChange={v => setConnectionId(v ? Number(v) : '')}
          hideAllOption
          placeholder="Choose an account…"
          emptyMessage="Connect a WhatsApp Business number first."
        />

        {showProblems && problems.length > 0 && (
          <div className="tpl-create-problems" role="status" aria-live="polite">
            <AlertCircle size={16} aria-hidden="true" />
            <div>
              <p>Before submitting:</p>
              <ul>{problems.map(p => <li key={p}>{p}</li>)}</ul>
            </div>
          </div>
        )}
      </div>

      <aside className="tpl-create-preview" aria-label="Preview">
        <p className="tpl-preview-label">Preview</p>
        <WhatsAppPreview
          senderName={connectionName}
          headerText={header.trim() || undefined}
          bodyText={previewBody}
          footerText={footer.trim() || undefined}
          buttons={buttons.map(b => ({ type: b.type, text: b.type === 'COPY_CODE' ? labelForType(b.type) : (b.text || labelForType(b.type)) }))}
          emptyMessage="Start writing the body to see the message."
        />
      </aside>
    </div>
  )

  return (
    <Modal
      isOpen={isOpen}
      onClose={close}
      size="xl"
      title="New WhatsApp Template"
      subtitle="Created on your WhatsApp Business Account and reviewed by Meta, usually within minutes."
      closeOnBackdrop={!busy}
      footer={
        <>
          <button type="button" className="oc-dialog-btn oc-dialog-btn-secondary" onClick={close} disabled={busy}>Cancel</button>
          <button type="button" className="oc-dialog-btn oc-dialog-btn-primary" onClick={submit} disabled={busy || !o}>
            {busy ? <Loader2 size={15} className="tpl-spin" aria-hidden="true" /> : <Send size={15} aria-hidden="true" />}
            {busy ? 'Submitting…' : 'Submit to Meta'}
          </button>
        </>
      }
    >
      {options.loading && <Skeleton variant="card" />}
      {options.error && (
        <EmptyState iconName="AlertCircle" title="The template form could not be loaded" message={options.error}
          action={{ label: 'Try Again', onClick: options.retry }} />
      )}
      {o && renderForm(o)}
    </Modal>
  )
}

export default CreateTemplateModal
