import React, { useEffect, useRef, useState } from 'react'
import { MoreVertical, Loader2 } from 'lucide-react'
import { Menu, MenuItem } from '../../../components/Menu/Menu'
import { Modal } from '../../../components/Modal/Modal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'

export interface ConfigColumn<T> {
  header: string
  cell: (row: T) => React.ReactNode
  className?: string
  title?: (row: T) => string | undefined
}

export interface ConfigAction<T> {
  label: string
  onSelect: (row: T) => void
  destructive?: boolean
  disabled?: (row: T) => boolean
}

interface ConfigTableProps<T extends { id: number }> {
  rows: T[]
  columns: ConfigColumn<T>[]
  actions: ConfigAction<T>[]
  rowLabel: (row: T) => string
  isLoading: boolean
  loadError: string | null
  onRetry: () => void
  empty: { iconName: string; title: string; message: string; action?: { label: string; onClick: () => void } }
}

/** The list every Send Money settings screen shares: an actions menu per row, loading, error and empty states. */
export function ConfigTable<T extends { id: number }>({ rows, columns, actions, rowLabel, isLoading, loadError, onRetry, empty }: ConfigTableProps<T>) {
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)

  if (isLoading) return <div className="data-table-wrapper"><Skeleton variant="table" /></div>
  if (loadError) {
    return (
      <div className="data-table-wrapper">
        <EmptyState iconName="AlertCircle" title="This list could not be loaded" message={loadError} action={{ label: 'Try Again', onClick: onRetry }} />
      </div>
    )
  }

  return (
    <div className="data-table-wrapper">
      <table className="data-table">
        <thead>
          <tr>
            <th className="actions-col" scope="col">Actions</th>
            {columns.map(c => <th key={c.header} scope="col" className={c.className}>{c.header}</th>)}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length + 1} className="no-records-row">
                <EmptyState iconName={empty.iconName} title={empty.title} message={empty.message} action={empty.action} />
              </td>
            </tr>
          ) : rows.map(row => (
            <tr key={row.id}>
              <td className="actions-col">
                <div className="contact-actions-menu-wrapper">
                  <Menu
                    open={openMenuId === row.id}
                    onOpenChange={isOpen => setOpenMenuId(isOpen ? row.id : null)}
                    align="start"
                    offset={4}
                    className="contact-actions-dropdown"
                    ariaLabel={`Actions for ${rowLabel(row)}`}
                    trigger={props => (
                      <button {...props} type="button" className="contact-actions-trigger" aria-label={`Actions for ${rowLabel(row)}`}>
                        <MoreVertical size={16} aria-hidden="true" />
                      </button>
                    )}
                  >
                    {actions.map(a => (
                      <MenuItem key={a.label} className="contact-actions-item" destructive={a.destructive}
                        disabled={a.disabled?.(row) ?? false} onSelect={() => a.onSelect(row)}>
                        {a.label}
                      </MenuItem>
                    ))}
                  </Menu>
                </div>
              </td>
              {columns.map(c => <td key={c.header} className={c.className} title={c.title?.(row)}>{c.cell(row)}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

interface FormModalProps {
  isOpen: boolean
  onClose: () => void
  title: string
  subtitle?: string
  icon?: React.ReactNode
  formId: string
  error: string | null
  isSubmitting: boolean
  canSubmit: boolean
  submitLabel: string
  onSubmit: () => void
  children: React.ReactNode
}

/** A right-hand drawer holding one settings form; the save error is focused when it appears. */
export const FormModal: React.FC<FormModalProps> = ({ isOpen, onClose, title, subtitle, icon, formId, error, isSubmitting, canSubmit, submitLabel, onSubmit, children }) => {
  const errorRef = useRef<HTMLDivElement>(null)
  useEffect(() => { if (error) errorRef.current?.focus() }, [error])

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={title}
      subtitle={subtitle}
      icon={icon}
      placement="right"
      size="md"
      footer={
        <button type="submit" form={formId} className="oc-dialog-btn oc-dialog-btn-primary" disabled={!canSubmit || isSubmitting}>
          {isSubmitting && <Loader2 size={15} className="sm-spin" aria-hidden="true" />}
          {isSubmitting ? 'Saving…' : submitLabel}
        </button>
      }
    >
      <form id={formId} className="setup-modal-form" noValidate onSubmit={e => { e.preventDefault(); onSubmit() }}>
        {error && <div ref={errorRef} tabIndex={-1} role="alert" className="sm-form-error">{error}</div>}
        {children}
      </form>
    </Modal>
  )
}

/** A label with a live "n / max" counter, for the limits WhatsApp enforces on Flow text. */
export const CountedLabel: React.FC<{ htmlFor: string; text: string; value: string; max: number; required?: boolean }> = ({ htmlFor, text, value, max, required }) => (
  <div className="sm-counted-label">
    <label className={`setup-label${required ? ' required' : ''}`} htmlFor={htmlFor}>{text}</label>
    <span className={`sm-counter${value.length > max ? ' sm-counter-over' : ''}`} aria-live="polite">{value.length} / {max}</span>
  </div>
)

/** "12.5" → 12.5, "" → null. */
export const toNumber = (value: string): number | null => {
  const trimmed = value.trim()
  if (trimmed === '') return null
  const n = Number(trimmed)
  return Number.isFinite(n) ? n : null
}

export const fromNumber = (value: number | null | undefined): string => (value === null || value === undefined ? '' : String(value))
