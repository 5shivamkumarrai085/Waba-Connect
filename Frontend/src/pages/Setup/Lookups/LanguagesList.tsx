import React, { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Plus, RefreshCw, MoreVertical, Star, Languages as LanguagesIcon } from 'lucide-react'
import { Menu, MenuItem } from '../../../components/Menu/Menu'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import Can from '../../../components/Can/Can'
import usePermission from '../../../hooks/usePermission'
import { lookupService, type LanguageLookup } from '../../../services/setup/lookupService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'

const FORM_ID = 'language-form'

export const LanguagesList: React.FC = () => {
  const navigate = useNavigate()
  const { has } = usePermission()

  const [languages, setLanguages] = useState<LanguageLookup[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)
  const [editing, setEditing] = useState<LanguageLookup | null>(null)
  const [isModalOpen, setIsModalOpen] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<LanguageLookup | null>(null)

  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [isDefault, setIsDefault] = useState(false)

  const load = async (showSpinner = true) => {
    if (showSpinner) setIsLoading(true)
    try {
      setLanguages(await lookupService.getLanguages())
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load languages.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  const openCreate = () => {
    setEditing(null)
    setCode('')
    setName('')
    setIsActive(true)
    setIsDefault(false)
    setIsModalOpen(true)
  }

  const openEdit = (language: LanguageLookup) => {
    setEditing(language)
    setCode(language.code)
    setName(language.name)
    setIsActive(language.isActive)
    setIsDefault(language.isDefault)
    setIsModalOpen(true)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSubmitting(true)
    try {
      const payload = {
        code: code.trim().toLowerCase(),
        name: name.trim(),
        isActive,
        isDefault,
        sortOrder: editing?.sortOrder ?? languages.length
      }
      if (editing) {
        await lookupService.updateLanguage(editing.id, payload)
        toast.success('Language updated.')
      } else {
        await lookupService.createLanguage(payload)
        toast.success('Language created.')
      }
      setIsModalOpen(false)
      void load(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to save the language.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await lookupService.deleteLanguage(deleteTarget.id)
      toast.success(`Language '${deleteTarget.name}' deleted.`)
      void load(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to delete the language.'))
    } finally {
      setDeleteTarget(null)
    }
  }

  const canEdit = has('Language.Edit')
  const canDelete = has('Language.Delete')
  const canTranslate = has('Language.Translate')

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Languages</h1>
        <p>Languages offered across contacts, templates and user profiles.</p>
      </div>

      <div className="contacts-toolbar">
        <Can permission="Language.Create">
          <button type="button" className="btn-toolbar-primary" onClick={openCreate}>
            <Plus size={15} />
            <span>New Language</span>
          </button>
        </Can>
        <button type="button" className="btn-toolbar-tertiary" onClick={() => load()}>
          <RefreshCw size={15} />
          <span>Refresh</span>
        </button>
      </div>

      <div className="contacts-card">
        <div className="data-table-wrapper">
          {isLoading ? (
            <Skeleton variant="table" />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>ID</th>
                  <th>NAME</th>
                  <th>CODE</th>
                  <th className="text-center">TRANSLATIONS</th>
                  <th className="text-center">ACTIVE</th>
                  <th className="text-center">ACTIONS</th>
                </tr>
              </thead>
              <tbody>
                {languages.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="no-records-row">
                      <EmptyState
                        iconName="Languages"
                        title="No languages yet"
                        message="Add a language to offer it across the app."
                        action={has('Language.Create') ? { label: 'New Language', onClick: openCreate } : undefined}
                      />
                    </td>
                  </tr>
                ) : (
                  languages.map((language) => (
                    <tr key={language.id}>
                      <td>{language.id}</td>
                      <td>
                        <div className="setup-role-name-cell">
                          <button
                            type="button"
                            className="setup-user-name"
                            onClick={() => canEdit && openEdit(language)}
                            disabled={!canEdit}
                          >
                            {language.name}
                          </button>
                          {language.isDefault && (
                            <span className="setup-role-badge admin" title="Fallback language">
                              <Star size={11} />
                              Default
                            </span>
                          )}
                        </div>
                      </td>
                      <td><code>{language.code}</code></td>
                      <td className="text-center">{language.translationCount}</td>
                      <td className="text-center">
                        <span className={`setup-role-badge ${language.isActive ? 'admin' : 'muted'}`}>
                          {language.isActive ? 'Active' : 'Hidden'}
                        </span>
                      </td>
                      <td className="text-center">
                        <div className="contact-actions-menu-wrapper">
                          <Menu
                            open={openMenuId === language.id}
                            onOpenChange={(isOpen) => setOpenMenuId(isOpen ? language.id : null)}
                            align="end"
                            offset={4}
                            className="contact-actions-dropdown"
                            ariaLabel="Row actions"
                            trigger={(props) => (
                              <button {...props} type="button" className="contact-actions-trigger" aria-label="Row actions">
                                <MoreVertical size={16} />
                              </button>
                            )}
                          >
                            <MenuItem
                              className="contact-actions-item"
                              disabled={!canTranslate}
                              onSelect={() => navigate(`/setup/languages/${language.id}/translate`)}
                            >
                              Translate
                            </MenuItem>
                            <MenuItem className="contact-actions-item" disabled={!canEdit} onSelect={() => openEdit(language)}>
                              Edit
                            </MenuItem>
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              disabled={!canDelete || language.isDefault}
                              onSelect={() => setDeleteTarget(language)}
                            >
                              Delete
                            </MenuItem>
                          </Menu>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          )}
        </div>
      </div>

      <Modal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title={editing ? 'Edit Language' : 'New Language'}
        icon={<LanguagesIcon size={18} />}
        size="sm"
        footer={
          <>
            <button type="button" className="oc-dialog-btn oc-dialog-btn-secondary" onClick={() => setIsModalOpen(false)}>
              Cancel
            </button>
            <button
              type="submit"
              form={FORM_ID}
              className="oc-dialog-btn oc-dialog-btn-primary"
              disabled={isSubmitting || !name.trim() || !code.trim()}
            >
              {isSubmitting ? 'Saving…' : 'Submit'}
            </button>
          </>
        }
      >
        <form id={FORM_ID} onSubmit={handleSubmit} className="setup-modal-form">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="language-name">Name</label>
            <input
              id="language-name"
              className="setup-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Bahasa Melayu"
              data-autofocus
              required
            />
          </div>

          <div className="setup-field">
            <label className="setup-label required" htmlFor="language-code">Code</label>
            <input
              id="language-code"
              className="setup-input"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="ms"
              maxLength={10}
              required
            />
            <p className="setup-hint">Short code used by templates and user profiles, e.g. en, ms, zh.</p>
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Active</span>
              <p className="setup-hint">Hidden languages leave the pickers.</p>
            </div>
            <button
              type="button"
              className={`setup-switch ${isActive ? 'on' : 'off'}`}
              onClick={() => setIsActive((prev) => !prev)}
              aria-pressed={isActive}
              aria-label="Active"
            >
              <span className="setup-switch-knob" />
            </button>
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Default</span>
              <p className="setup-hint">The fallback used when nothing else is specified.</p>
            </div>
            <button
              type="button"
              className={`setup-switch ${isDefault ? 'on' : 'off'}`}
              onClick={() => setIsDefault((prev) => !prev)}
              aria-pressed={isDefault}
              aria-label="Default language"
              disabled={editing?.isDefault}
            >
              <span className="setup-switch-knob" />
            </button>
          </div>
        </form>
      </Modal>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Language"
        message={`Delete '${deleteTarget?.name}'? Its ${deleteTarget?.translationCount ?? 0} translation(s) will be removed too.`}
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default LanguagesList
