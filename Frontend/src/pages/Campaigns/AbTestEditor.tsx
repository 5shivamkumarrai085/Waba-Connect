import React, { useId } from 'react'
import { FlaskConical, Plus, Trash2, Trophy } from 'lucide-react'
import { Toggle } from '../../components/Toggle/Toggle'
import { Skeleton } from '../../components/Skeleton'
import useReference from '../../hooks/useReference'
import { referenceService } from '../../services/referenceService'

import type { AbTestForm, AbVariantForm } from './abTestForm'

export type { AbTestForm, AbVariantForm }

interface AbTestEditorProps {
  channel: 'email' | 'whatsapp'
  /** Templates variant B, C… can use (approved WhatsApp templates, or enabled email templates). */
  templates: { id: number; name: string }[]
  value: AbTestForm
  onChange: (value: AbTestForm) => void
  /** The campaign's own template, which is variant A. */
  baseTemplateName?: string | null
  /** How many recipients the campaign has so far, for the split preview. 0 while unknown. */
  audienceSize?: number
}

/** Variant letters after A, which is the campaign's own template. */
const letter = (index: number) => String.fromCharCode('B'.charCodeAt(0) + index)

/**
 * The split exactly as the server makes it (AbTestService.AssignAsync): the test group is the
 * share of the audience rounded up, at least one per variant, dealt evenly across the variants;
 * everyone else waits for the winner.
 */
const planSplit = (audience: number, percent: number, variants: number) => {
  if (audience <= 0) return null
  const test = Math.min(audience, Math.max(Math.min(audience, variants), Math.ceil((audience * percent) / 100)))
  const perVariant = Array.from({ length: variants }, (_, i) => Math.floor(test / variants) + (i < test % variants ? 1 : 0))
  return { test, held: audience - test, perVariant }
}

/**
 * A/B test settings: variant A is the template chosen above; B, C… are added here. A share of the
 * audience gets the variants first, and the rest receive whichever wins. Metrics and limits come
 * from the server (GET api/reference/campaign-options), which enforces the same values.
 */
export const AbTestEditor: React.FC<AbTestEditorProps> = ({ channel, templates, value, onChange, baseTemplateName, audienceSize = 0 }) => {
  const apiChannel = channel === 'email' ? 'Email' : 'WhatsApp'
  const options = useReference(() => referenceService.getCampaignOptions(apiChannel), `campaign-options:${apiChannel}`)
  const ids = { percent: useId(), metric: useId(), hours: useId(), title: useId() }

  const set = (patch: Partial<AbTestForm>) => onChange({ ...value, ...patch })
  const setVariant = (index: number, patch: Partial<AbVariantForm>) =>
    set({ variants: value.variants.map((v, i) => (i === index ? { ...v, ...patch } : v)) })

  const o = options.data
  const metric = value.metric || o?.abMetrics[0]?.value || ''
  const maxExtraVariants = o ? o.maxAbVariants - 1 : 0
  const percent = value.percent || o?.abTestPercent.default || 0
  const hours = value.decideAfterHours || o?.abDecideAfterHours.default || 0
  const variantCount = value.variants.length + 1
  const split = planSplit(audienceSize, percent, variantCount)
  const testShare = split ? (split.test / audienceSize) * 100 : percent
  const format = (n: number) => n.toLocaleString()
  const labels = ['A', ...value.variants.map((_, i) => letter(i))]

  const enable = (on: boolean) => {
    if (!on || !o) return set({ enabled: on })
    set({
      enabled: true,
      percent: value.percent || o.abTestPercent.default,
      decideAfterHours: value.decideAfterHours || o.abDecideAfterHours.default,
      metric: value.metric || o.abMetrics[0]?.value || ''
    })
  }

  return (
    <section className={`ab-editor${value.enabled ? ' is-on' : ''}`} aria-labelledby={ids.title}>
      <div className="ab-editor-head">
        <span className="ab-editor-icon" aria-hidden="true"><FlaskConical size={18} /></span>
        <div className="ab-editor-intro">
          <h4 id={ids.title} className="ab-editor-title">A/B test</h4>
          <p className="ab-editor-note">Send different versions to part of the audience first, then the best one to everyone else.</p>
        </div>
        <Toggle checked={value.enabled} onChange={enable} ariaLabel="A/B test this campaign" disabled={!o}
          disabledReason={options.error ?? 'Loading…'} />
      </div>

      {value.enabled && !o && <Skeleton variant="list" count={2} />}

      {value.enabled && o && (
        <div className="ab-editor-body">
          <ol className="ab-variant-grid" aria-label="Variants">
            <li className="ab-variant-card is-base">
              <span className="ab-variant-label" aria-hidden="true">A</span>
              <div className="ab-variant-fields">
                <span className="ab-variant-caption">The template chosen above</span>
                <span className="ab-variant-base">{baseTemplateName || 'Choose a template above first'}</span>
              </div>
            </li>
            {value.variants.map((variant, index) => {
              const templateValue = (channel === 'email' ? variant.emailTemplateId : variant.templateId) ?? ''
              return (
                <li key={index} className={`ab-variant-card${templateValue === '' ? ' is-incomplete' : ''}`}>
                  <span className="ab-variant-label" aria-hidden="true">{letter(index)}</span>
                  <div className="ab-variant-fields">
                    <select
                      className="form-control"
                      aria-label={`Variant ${letter(index)} template`}
                      value={templateValue}
                      onChange={e => {
                        const id = e.target.value ? Number(e.target.value) : undefined
                        setVariant(index, channel === 'email' ? { emailTemplateId: id } : { templateId: id })
                      }}
                    >
                      <option value="">Choose a template…</option>
                      {templates.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                    </select>
                    {channel === 'email' && (
                      <input
                        className="form-control"
                        aria-label={`Variant ${letter(index)} subject (optional)`}
                        placeholder="Different subject (optional)…"
                        autoComplete="off"
                        value={variant.subjectOverride ?? ''}
                        maxLength={998}
                        onChange={e => setVariant(index, { subjectOverride: e.target.value })}
                      />
                    )}
                  </div>
                  <button type="button" className="ab-icon-btn" aria-label={`Remove variant ${letter(index)}`}
                    title={value.variants.length === 1 ? 'An A/B test needs at least one extra variant' : 'Remove variant'}
                    disabled={value.variants.length === 1}
                    onClick={() => set({ variants: value.variants.filter((_, i) => i !== index) })}>
                    <Trash2 size={15} aria-hidden="true" />
                  </button>
                </li>
              )
            })}
          </ol>
          {value.variants.length < maxExtraVariants ? (
            <button type="button" className="btn-toolbar-tertiary ab-add" onClick={() => set({ variants: [...value.variants, {}] })}>
              <Plus size={14} aria-hidden="true" /> Add Variant {letter(value.variants.length)}
            </button>
          ) : (
            <p className="ab-editor-note">Up to {o.maxAbVariants} variants.</p>
          )}

          <div className="ab-split">
            <div className="ab-split-head">
              <label htmlFor={ids.percent}>Test share</label>
              <span className="ab-split-value">{percent}%</span>
            </div>
            <input id={ids.percent} type="range" className="ab-range"
              min={o.abTestPercent.min} max={o.abTestPercent.max} step={5} value={percent}
              aria-valuetext={`${percent}% of the audience gets a variant first`}
              onChange={e => set({ percent: Number(e.target.value) })} />
            <div className="ab-split-bar" aria-hidden="true">
              {labels.map((l, i) => (
                <span key={l} className={`ab-split-seg v${i % 5}`} style={{ flexBasis: `${testShare / variantCount}%` }}>{l}</span>
              ))}
              {testShare < 100 && <span className="ab-split-seg is-held" style={{ flexBasis: `${100 - testShare}%` }}><Trophy size={12} /> winner</span>}
            </div>
            <p className="ab-editor-note" aria-live="polite">
              {split
                ? <>{labels.map((l, i) => `${l}: ${format(split.perVariant[i])}`).join(' · ')}. {split.held > 0 ? `The other ${format(split.held)} get the winner.` : 'Everyone is in the test; there is no winner send.'}</>
                : <>{percent}% of the audience is split evenly across the {variantCount} variants; the rest get the winner. Pick the audience to see exact numbers.</>}
            </p>
          </div>

          <div className="ab-settings">
            <div className="ab-setting">
              <label htmlFor={ids.metric}>Winner decided by</label>
              <select id={ids.metric} className="form-control" value={metric} onChange={e => set({ metric: e.target.value })}>
                {o.abMetrics.map(m => <option key={m.value} value={m.value}>{m.label}</option>)}
              </select>
              <span className="ab-setting-hint">{o.abMetrics.find(m => m.value === metric)?.description}</span>
            </div>
            <div className="ab-setting">
              <label htmlFor={ids.hours}>Decide after</label>
              <div className="ab-suffix">
                <input id={ids.hours} type="number" inputMode="numeric" className="form-control"
                  min={o.abDecideAfterHours.min} max={o.abDecideAfterHours.max} value={hours}
                  onChange={e => set({ decideAfterHours: Number(e.target.value) })} />
                <span aria-hidden="true">hours</span>
              </div>
              <span className="ab-setting-hint">
                Counted from the first test message sent, between {o.abDecideAfterHours.min} and {o.abDecideAfterHours.max} hours.
                You can also pick the winner yourself on the campaign page.
              </span>
            </div>
          </div>
        </div>
      )}
    </section>
  )
}

export default AbTestEditor
