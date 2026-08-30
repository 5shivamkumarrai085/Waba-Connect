import React, { useCallback, useEffect, useMemo, useState } from 'react'
import { motion } from 'framer-motion'
import { useNavigate, useParams } from 'react-router-dom'
import toast from 'react-hot-toast'
import * as Icons from 'lucide-react'
import { Check, ChevronDown, Info, X } from 'lucide-react'
import { Menu, MenuItem, type MenuTriggerProps } from '../../components/Menu/Menu'
import { Skeleton } from '../../components/Skeleton'
import { omniSettingsService } from '../../services/omniSettingsService'
import { getErrorMessage } from '../../utils/errorHelper'
import { isRequestCancelled } from '../../services/apiClient'
import usePermission from '../../hooks/usePermission'
import { pageTransitionProps } from '../../utils/motion'
import type {
  OmniSettingsField,
  OmniSettingsSchema,
  OmniSettingsSection,
  OmniSettingsValues
} from '../../types/omniSettings'
import './OmniConnectSettings.css'

/**
 * Resolves a section's declared icon name against lucide's set.
 *
 * The server names an icon; it does not ship one. A name the client cannot resolve falls back to a
 * neutral glyph rather than rendering nothing — an icon is decoration, and a missing one must not
 * cost the row it sits in.
 */
const iconFor = (name: string): React.ComponentType<{ size?: number | string }> => {
  const set = Icons as unknown as Record<string, React.ComponentType<{ size?: number | string }>>
  return set[name] ?? Icons.Settings
}

/** The editable state of one section, keyed by field. */
const initialValuesFor = (section: OmniSettingsSection): OmniSettingsValues => {
  const values: OmniSettingsValues = {}

  section.fields.forEach((field) => {
    switch (field.type) {
      case 'toggle':
        values[field.key] = field.value === true
        break
      case 'tags':
      case 'multiselect':
        values[field.key] = Array.isArray(field.value) ? [...(field.value as string[])] : []
        break
      case 'password':
        // Always starts blank. A stored secret is never sent to the client, and posting blank is
        // what tells the server to keep the one it already has.
        values[field.key] = ''
        break
      case 'number':
        values[field.key] = field.value ?? ''
        break
      default:
        values[field.key] = field.value ?? ''
    }
  })

  return values
}

/**
 * OmniConnect Settings.
 *
 * <para>
 * Every section, field, label, option list and validation rule comes from the server's schema —
 * this component knows how to render a *kind* of field, never which fields exist. Adding a setting
 * is a backend change; this file does not move.
 * </para>
 */
export const OmniConnectSettings: React.FC = () => {
  const navigate = useNavigate()
  const { sectionKey } = useParams<{ sectionKey?: string }>()
  const { has } = usePermission()
  const canEdit = has('OmniSettings.Edit')

  const [schema, setSchema] = useState<OmniSettingsSchema | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isSaving, setIsSaving] = useState(false)
  const [values, setValues] = useState<OmniSettingsValues>({})

  const sections = schema?.sections ?? []

  // The section in the URL, falling back to the first. Driven by the route so a section is
  // linkable and survives a refresh, which a piece of local state would not.
  const activeSection = useMemo<OmniSettingsSection | null>(() => {
    if (sections.length === 0) return null
    return sections.find((s) => s.key === sectionKey) ?? sections[0]
  }, [sections, sectionKey])

  const load = useCallback(async () => {
    try {
      const next = await omniSettingsService.getSchema()
      setSchema(next)
      return next
    } catch (err) {
      if (isRequestCancelled(err)) return null
      toast.error(getErrorMessage(err, 'Could not load settings.'))
      return null
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  // Re-seeds the form whenever the active section changes, or when a save returns fresh values.
  useEffect(() => {
    if (activeSection) setValues(initialValuesFor(activeSection))
  }, [activeSection])

  const setValue = (key: string, value: unknown) => {
    setValues((prev) => ({ ...prev, [key]: value }))
  }

  const handleSave = async () => {
    if (!activeSection) return

    setIsSaving(true)
    try {
      const next = await omniSettingsService.saveSection(activeSection.key, values)
      setSchema(next)

      // Re-seed from what the server actually stored, not from what was typed — it normalises
      // values, and a secret field has to return to blank so the next save does not resend it.
      const saved = next.sections.find((s) => s.key === activeSection.key)
      if (saved) setValues(initialValuesFor(saved))

      toast.success('Settings saved.')
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not save these settings.'))
    } finally {
      setIsSaving(false)
    }
  }

  if (isLoading) {
    return (
      <motion.div {...pageTransitionProps}>
        <h1 className="omni-settings-title">OmniConnect Settings</h1>
        <div className="omni-settings-layout">
          <div className="omni-settings-rail">
            <Skeleton variant="text" count={8} />
          </div>
          <div className="omni-settings-panel">
            <Skeleton variant="title" width={220} />
            <div style={{ marginTop: 20 }}>
              <Skeleton variant="text" count={4} />
            </div>
          </div>
        </div>
      </motion.div>
    )
  }

  return (
    <motion.div {...pageTransitionProps}>
      <h1 className="omni-settings-title">OmniConnect Settings</h1>

      <div className="omni-settings-layout">
        <nav className="omni-settings-rail" aria-label="Settings sections">
          {sections.map((section) => {
            const Icon = iconFor(section.icon)
            const isActive = activeSection?.key === section.key

            return (
              <button
                key={section.key}
                type="button"
                className={`omni-settings-rail-item${isActive ? ' is-active' : ''}`}
                aria-current={isActive ? 'page' : undefined}
                onClick={() => navigate(`/omniconnect-settings/${section.key}`)}
              >
                <Icon size={17} />
                <span>{section.label}</span>
              </button>
            )
          })}
        </nav>

        {activeSection && (
          <section className="omni-settings-panel" key={activeSection.key}>
            <header className="omni-settings-panel-head">
              <h2>{activeSection.label}</h2>
              <p>{activeSection.description}</p>
            </header>

            <div className="omni-settings-body">
              <div className="omni-settings-grid">
                {activeSection.fields.map((field) => (
                  <Field
                    key={field.key}
                    field={field}
                    value={values[field.key]}
                    values={values}
                    disabled={!canEdit}
                    onChange={(next) => setValue(field.key, next)}
                  />
                ))}
              </div>

              {activeSection.notes.map((note, index) => (
                <div key={index} className={`omni-settings-note is-${note.tone}`}>
                  {note.tone === 'warning' ? <strong>Note:</strong> : <Info size={15} />}
                  <span>{note.text}</span>
                </div>
              ))}
            </div>

            <footer className="omni-settings-foot">
              <button
                type="button"
                className="omni-settings-save"
                onClick={handleSave}
                disabled={!canEdit || isSaving}
                title={!canEdit ? "You don't have permission to change these settings." : undefined}
              >
                {isSaving ? 'Saving…' : 'Save Changes'}
              </button>
            </footer>
          </section>
        )}
      </div>
    </motion.div>
  )
}

// ── Field renderers ─────────────────────────────────────────────────────────

interface FieldProps {
  field: OmniSettingsField
  value: unknown
  /**
   * The whole section's current values.
   *
   * A field's requirement can depend on a sibling — the OpenAI key is mandatory only once OpenAI
   * is switched on — so the marker has to be computed against the form as it stands, not against
   * this field alone. It is the same rule the server enforces on save; showing it here just means
   * the reader is not told about it only after being refused.
   */
  values: OmniSettingsValues
  disabled: boolean
  onChange: (value: unknown) => void
}

/** Whether this field is required right now, given what the rest of the section holds. */
const isRequiredNow = (field: OmniSettingsField, values: OmniSettingsValues): boolean =>
  field.required || (field.requiredWhenKey ? values[field.requiredWhenKey] === true : false)

/** The red asterisk, shown only when the field is actually required at this moment. */
const RequiredMark: React.FC<{ field: OmniSettingsField; values: OmniSettingsValues }> = ({
  field,
  values
}) => (isRequiredNow(field, values) ? <span className="omni-required">*</span> : null)

/**
 * One field, dispatched on its declared type.
 *
 * An unknown type renders nothing rather than throwing: the server may publish a field kind this
 * build predates, and one unrecognised control must not take the page down with it.
 */
const Field: React.FC<FieldProps> = ({ field, value, values, disabled, onChange }) => {
  switch (field.type) {
    case 'toggle':
      return (
        <div className="omni-field omni-field-wide">
          <span className="omni-field-label">{field.label}</span>
          <button
            type="button"
            role="switch"
            aria-checked={value === true}
            aria-label={field.label}
            disabled={disabled}
            className={`omni-toggle${value === true ? ' is-on' : ''}`}
            onClick={() => onChange(!(value === true))}
          >
            <span className="omni-toggle-knob">{value === true ? <Check size={10} /> : <X size={10} />}</span>
          </button>
          {field.helper && <span className="omni-field-helper">{field.helper}</span>}
        </div>
      )

    case 'select':
      return (
        <div className="omni-field">
          <label htmlFor={field.key}>
            <RequiredMark field={field} values={values} />
            {field.label}
          </label>
          <select
            id={field.key}
            className="omni-input"
            value={(value as string) ?? ''}
            disabled={disabled}
            onChange={(e) => onChange(e.target.value)}
          >
            {/* Explicit empty option: without one, a field with no stored value would display the
                first option while holding nothing, and look configured when it is not. */}
            <option value="">Select…</option>
            {field.options.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          {field.helper && <span className="omni-field-helper">{field.helper}</span>}
        </div>
      )

    case 'multiselect':
      return <MultiSelectField field={field} value={value} values={values} disabled={disabled} onChange={onChange} />

    case 'tags':
      return <TagsField field={field} value={value} values={values} disabled={disabled} onChange={onChange} />

    case 'number':
      return (
        <div className="omni-field">
          <label htmlFor={field.key}>
            <RequiredMark field={field} values={values} />
            {field.label}
          </label>
          <div className="omni-input-group">
            <input
              id={field.key}
              type="number"
              className="omni-input"
              value={(value as number | string) ?? ''}
              min={field.min ?? undefined}
              max={field.max ?? undefined}
              disabled={disabled}
              onChange={(e) => onChange(e.target.value === '' ? '' : Number(e.target.value))}
            />
            {field.unit && <span className="omni-input-unit">{field.unit}</span>}
          </div>
          {field.helper && <span className="omni-field-helper">{field.helper}</span>}
        </div>
      )

    case 'password':
      return (
        <div className="omni-field omni-field-wide">
          <label htmlFor={field.key}>
            <RequiredMark field={field} values={values} />
            {field.label}
          </label>
          <input
            id={field.key}
            type="password"
            className="omni-input"
            autoComplete="new-password"
            placeholder={field.hasValue ? '•••••••• (a key is saved)' : field.placeholder ?? ''}
            value={(value as string) ?? ''}
            disabled={disabled}
            onChange={(e) => onChange(e.target.value)}
          />
          {field.helper && <span className="omni-field-helper">{field.helper}</span>}
        </div>
      )

    case 'text':
      return (
        <div className="omni-field omni-field-wide">
          <label htmlFor={field.key}>
            <RequiredMark field={field} values={values} />
            {field.label}
          </label>
          <input
            id={field.key}
            type="text"
            className="omni-input"
            placeholder={field.placeholder ?? ''}
            value={(value as string) ?? ''}
            disabled={disabled}
            onChange={(e) => onChange(e.target.value)}
          />
          {field.helper && <span className="omni-field-helper">{field.helper}</span>}
        </div>
      )

    default:
      return null
  }
}

/** A checkable dropdown whose selection is echoed as removable chips. */
const MultiSelectField: React.FC<FieldProps> = ({ field, value, values, disabled, onChange }) => {
  const [isOpen, setIsOpen] = useState(false)
  const selected = Array.isArray(value) ? (value as string[]) : []

  const labelFor = useMemo(() => {
    const map = new Map(field.options.map((o) => [o.value, o.label]))
    return (v: string) => map.get(v) ?? v
  }, [field.options])

  const toggle = (option: string) =>
    onChange(selected.includes(option) ? selected.filter((v) => v !== option) : [...selected, option])

  const trigger = (props: MenuTriggerProps) => (
    <button {...props} type="button" className="omni-input omni-select-trigger" disabled={disabled}>
      <span>
        {selected.length} event{selected.length === 1 ? '' : 's'} selected
      </span>
      <ChevronDown size={14} />
    </button>
  )

  return (
    <div className="omni-field omni-field-wide">
      <label>
        <RequiredMark field={field} values={values} />
        {field.label}
      </label>
      {field.helper && <span className="omni-field-helper">{field.helper}</span>}

      {selected.length > 0 && (
        <div className="omni-chips">
          {selected.map((option) => (
            <span key={option} className="omni-chip">
              {labelFor(option)}
              <button
                type="button"
                className="omni-chip-remove"
                disabled={disabled}
                onClick={() => toggle(option)}
                aria-label={`Remove ${labelFor(option)}`}
              >
                <X size={11} />
              </button>
            </span>
          ))}
        </div>
      )}

      <Menu
        open={isOpen && !disabled}
        onOpenChange={setIsOpen}
        align="start"
        offset={6}
        matchTriggerWidth
        // Choosing several events is the point of the control, so a pick must not dismiss it.
        closeOnSelect={false}
        className="omni-select-dropdown"
        ariaLabel={field.label}
        trigger={trigger}
      >
        {field.options.map((option) => {
          const isChecked = selected.includes(option.value)
          return (
            <MenuItem
              key={option.value}
              className="omni-select-item"
              aria-checked={isChecked}
              onSelect={() => toggle(option.value)}
            >
              <span className={`omni-checkbox${isChecked ? ' is-checked' : ''}`}>
                {isChecked && <Check size={11} strokeWidth={3} />}
              </span>
              <span>{option.label}</span>
            </MenuItem>
          )
        })}
      </Menu>
    </div>
  )
}

/** Free-text keywords, committed on Enter and shown as removable chips. */
const TagsField: React.FC<FieldProps> = ({ field, value, values, disabled, onChange }) => {
  const [draft, setDraft] = useState('')
  const tags = Array.isArray(value) ? (value as string[]) : []

  const commit = () => {
    const trimmed = draft.trim()
    if (!trimmed) return

    // Case-insensitive: "Stop" and "stop" are the same keyword to anyone typing one at a customer,
    // and storing both would make the list look broken.
    if (!tags.some((t) => t.toLowerCase() === trimmed.toLowerCase())) {
      onChange([...tags, trimmed])
    }
    setDraft('')
  }

  return (
    <div className="omni-field omni-field-wide">
      <label htmlFor={field.key}>
        <RequiredMark field={field} values={values} />
        {field.label}
      </label>
      <input
        id={field.key}
        type="text"
        className="omni-input"
        placeholder={field.placeholder ?? 'Type and press Enter..'}
        value={draft}
        disabled={disabled}
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault()
            commit()
          }
          // Backspace on an empty box removes the last chip — the behaviour every tag input has,
          // and its absence is felt immediately.
          if (e.key === 'Backspace' && draft === '' && tags.length > 0) {
            onChange(tags.slice(0, -1))
          }
        }}
        // Committed on blur too: a keyword typed and left uncommitted would be silently dropped by
        // the save, which reads as the save having failed.
        onBlur={commit}
      />

      {tags.length > 0 && (
        <div className="omni-chips">
          {tags.map((tag) => (
            <span key={tag} className="omni-chip is-solid">
              {tag}
              <button
                type="button"
                className="omni-chip-remove"
                disabled={disabled}
                onClick={() => onChange(tags.filter((t) => t !== tag))}
                aria-label={`Remove ${tag}`}
              >
                <X size={11} />
              </button>
            </span>
          ))}
        </div>
      )}

      {field.helper && <span className="omni-field-helper">{field.helper}</span>}
    </div>
  )
}

export default OmniConnectSettings
