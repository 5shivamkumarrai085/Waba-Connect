import React, { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Plus, Trash2, Info, ArrowLeft } from 'lucide-react'
import { lookupService, type LanguageLookup, type TranslationEntry } from '../../../services/setup/lookupService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'

interface EditableEntry extends TranslationEntry {
  /** Stable key for React across reorders and additions. */
  rowId: string
}

let rowCounter = 0
const nextRowId = () => `row-${++rowCounter}`

/**
 * Key/value editor for one language's translations.
 *
 * The app's own interface strings are not yet wired to read from here — extracting every
 * hardcoded string across the UI is a separate project that was scoped out. This is the
 * durable half: the store, the API and the editor exist, so the content can be prepared and
 * consumed whenever that work happens. The notice on the page says so plainly rather than
 * implying the app is already translated.
 */
export const TranslateScreen: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const languageId = id ? parseInt(id, 10) : null

  const [language, setLanguage] = useState<LanguageLookup | null>(null)
  const [entries, setEntries] = useState<EditableEntry[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [isSaving, setIsSaving] = useState(false)

  useEffect(() => {
    const load = async () => {
      if (!languageId) return
      setIsLoading(true)
      try {
        const [languages, translations] = await Promise.all([
          lookupService.getLanguages(),
          lookupService.getTranslations(languageId)
        ])

        const match = languages.find((l) => l.id === languageId) ?? null
        if (!match) {
          toast.error('Language not found.')
          navigate('/setup/languages', { replace: true })
          return
        }

        setLanguage(match)
        setEntries(translations.map((t) => ({ ...t, rowId: nextRowId() })))
      } catch (error) {
        toast.error(getErrorMessage(error, 'Failed to load translations.'))
        navigate('/setup/languages', { replace: true })
      } finally {
        setIsLoading(false)
      }
    }
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [languageId])

  const updateEntry = (rowId: string, field: 'key' | 'value', next: string) =>
    setEntries((prev) => prev.map((entry) => (entry.rowId === rowId ? { ...entry, [field]: next } : entry)))

  const addRow = () => setEntries((prev) => [...prev, { rowId: nextRowId(), key: '', value: '' }])

  const removeRow = (rowId: string) =>
    setEntries((prev) => prev.filter((entry) => entry.rowId !== rowId))

  const handleSave = async () => {
    if (!languageId) return

    const withKeys = entries.filter((entry) => entry.key.trim())
    const duplicates = withKeys
      .map((entry) => entry.key.trim().toLowerCase())
      .filter((key, index, all) => all.indexOf(key) !== index)

    if (duplicates.length > 0) {
      toast.error(`Duplicate key: ${duplicates[0]}`)
      return
    }

    setIsSaving(true)
    try {
      // Rows removed in the UI are sent with an empty value, which the API treats as a delete.
      const removed = entries
        .filter((entry) => entry.key.trim() && !entry.value.trim())
        .map((entry) => ({ key: entry.key.trim(), value: '' }))

      const kept = withKeys
        .filter((entry) => entry.value.trim())
        .map((entry) => ({ key: entry.key.trim(), value: entry.value }))

      await lookupService.saveTranslations(languageId, [...kept, ...removed])
      toast.success('Translations saved.')
      navigate('/setup/languages')
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to save translations.'))
    } finally {
      setIsSaving(false)
    }
  }

  if (isLoading) {
    return <div className="setup-form-loading">Loading…</div>
  }

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Translate — {language?.name}</h1>
        <p>Manage translated strings for this language.</p>
      </div>

      <div className="setup-info-box setup-translate-notice">
        <Info size={16} />
        <div>
          <strong>Stored, not yet applied</strong>
          <p>
            These translations are saved and available through the API. The app&apos;s own interface
            text still renders in English — wiring it up is a separate piece of work.
          </p>
        </div>
      </div>

      <div className="contacts-card setup-translate-card">
        <div className="setup-translate-head">
          <span>Key</span>
          <span>Translation</span>
          <span />
        </div>

        {entries.length === 0 ? (
          <div className="setup-role-users-empty">No translations yet. Add your first key below.</div>
        ) : (
          entries.map((entry) => (
            <div className="setup-translate-row" key={entry.rowId}>
              <input
                className="setup-input"
                value={entry.key}
                onChange={(e) => updateEntry(entry.rowId, 'key', e.target.value)}
                placeholder="contacts.title"
              />
              <input
                className="setup-input"
                value={entry.value}
                onChange={(e) => updateEntry(entry.rowId, 'value', e.target.value)}
                placeholder="Translated text"
              />
              <button
                type="button"
                className="setup-translate-remove"
                onClick={() => removeRow(entry.rowId)}
                aria-label="Remove translation"
                title="Remove"
              >
                <Trash2 size={15} />
              </button>
            </div>
          ))
        )}

        <button type="button" className="btn-toolbar-secondary setup-translate-add" onClick={addRow}>
          <Plus size={14} />
          <span>Add key</span>
        </button>
      </div>

      <div className="setup-form-actions">
        <button type="button" className="btn-toolbar-tertiary" onClick={() => navigate('/setup/languages')}>
          <ArrowLeft size={14} />
          <span>Back</span>
        </button>
        <button type="button" className="btn-toolbar-primary" onClick={handleSave} disabled={isSaving}>
          {isSaving ? 'Saving…' : 'Save Translations'}
        </button>
      </div>
    </motion.div>
  )
}

export default TranslateScreen
