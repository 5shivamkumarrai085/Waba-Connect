import React, { useCallback, useEffect, useMemo, useState } from 'react'
import { Shield, Check, Loader2 } from 'lucide-react'
import { Modal } from '../../components/Modal/Modal'
import { SearchableSelect } from '../../components/SearchableSelect/SearchableSelect'
import { Skeleton } from '../../components/Skeleton'
import { permissionService } from '../../services/permissions/permissionService'
import type { Connection } from '../../types/connection'
import type { PermissionCandidates } from '../../types/permission'
import './AssignPermissionModal.css'

interface AssignPermissionModalProps {
  isOpen?: boolean
  type: 'user' | 'department'
  connections: Connection[]
  onClose: () => void
  onAssignUser?: (data: { userId: string; userName: string; userEmail: string; departmentName: string; connectionIds: number[] }) => Promise<void>
  onAssignDepartment?: (data: { departmentId: string; departmentName: string; description: string; memberCount: number; connectionIds: number[] }) => Promise<void>
}

/**
 * Grants connections to an existing user, or to every member of a role ("department").
 *
 * Both are picked from real records: access is enforced against the signed-in account and its
 * role, so an assignment to a typed-in name would grant nothing.
 */
export const AssignPermissionModal: React.FC<AssignPermissionModalProps> = ({
  isOpen = true,
  type,
  connections,
  onClose,
  onAssignUser,
  onAssignDepartment
}) => {
  const [candidates, setCandidates] = useState<PermissionCandidates | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [selectedId, setSelectedId] = useState('')
  const [description, setDescription] = useState('')
  const [selectedConnIds, setSelectedConnIds] = useState<number[]>([])
  const [isSubmitting, setIsSubmitting] = useState(false)

  const loadCandidates = useCallback(() => {
    let active = true
    setLoadError(false)
    setCandidates(null)
    permissionService.getCandidates()
      .then(result => { if (active) setCandidates(result) })
      .catch(() => { if (active) setLoadError(true) })
    return () => { active = false }
  }, [])

  useEffect(() => {
    if (!isOpen) return
    return loadCandidates()
  }, [isOpen, loadCandidates])

  const options = useMemo(() => !candidates ? [] : type === 'user'
    ? candidates.users.map(u => ({
        value: String(u.id),
        label: `${u.name} — ${u.email}${u.isAdministrator ? ' (administrator)' : ''}`,
        keywords: `${u.name} ${u.email} ${u.roleName ?? ''}`
      }))
    : candidates.roles.map(r => ({
        value: String(r.id),
        label: `${r.name} (${r.memberCount} member${r.memberCount === 1 ? '' : 's'})`,
        keywords: r.name
      })),
  [type, candidates])

  const selectedUser = candidates?.users.find(u => String(u.id) === selectedId)
  const selectedRole = candidates?.roles.find(r => String(r.id) === selectedId)
  const allSelected = connections.length > 0 && selectedConnIds.length === connections.length

  const toggleConnection = (id: number) => {
    setSelectedConnIds(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id])
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (selectedConnIds.length === 0 || !selectedId) return
    setIsSubmitting(true)
    try {
      if (type === 'user' && onAssignUser && selectedUser) {
        await onAssignUser({
          userId: String(selectedUser.id),
          userName: selectedUser.name,
          userEmail: selectedUser.email,
          departmentName: selectedUser.roleName ?? '',
          connectionIds: selectedConnIds
        })
      } else if (type === 'department' && onAssignDepartment && selectedRole) {
        await onAssignDepartment({
          departmentId: String(selectedRole.id),
          departmentName: selectedRole.name,
          description: description.trim(),
          memberCount: selectedRole.memberCount,
          connectionIds: selectedConnIds
        })
      }
      onClose()
    } catch {
      // The page reports the failure.
    } finally {
      setIsSubmitting(false)
    }
  }

  const noun = type === 'user' ? 'user' : 'role'

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      size="md"
      icon={<Shield size={18} />}
      title={type === 'user' ? 'Assign User Access' : 'Assign Department Access'}
      subtitle={
        type === 'user'
          ? 'Give a user access to specific connections.'
          : 'Give every member of a role access to specific connections.'
      }
      footer={
        <>
          <button type="button" className="oc-dialog-btn oc-dialog-btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button
            type="submit"
            form="assign-permission-form"
            className="oc-dialog-btn oc-dialog-btn-primary"
            disabled={isSubmitting || selectedConnIds.length === 0 || !selectedId}
            title={!selectedId ? `Choose a ${noun} first` : selectedConnIds.length === 0 ? 'Choose at least one connection' : undefined}
          >
            {isSubmitting && <Loader2 size={15} className="assign-perm-spin" aria-hidden="true" />}
            {isSubmitting ? 'Assigning…' : `Assign ${selectedConnIds.length || ''} Connection${selectedConnIds.length === 1 ? '' : 's'}`.replace('  ', ' ')}
          </button>
        </>
      }
    >
      <form id="assign-permission-form" onSubmit={handleSubmit}>
        <div className="conn-modal-field">
          {candidates === null && !loadError ? (
            <Skeleton variant="text" count={2} />
          ) : (
            <SearchableSelect
              id="assign-permission-target"
              label={type === 'user' ? 'User' : 'Role (department)'}
              placeholder={type === 'user' ? 'Choose a user' : 'Choose a role'}
              value={selectedId}
              options={options}
              onChange={setSelectedId}
              hideAllOption
              emptyMessage={loadError ? `The ${noun} list could not be loaded.` : type === 'user' ? 'No users found.' : 'No roles found.'}
            />
          )}
          {loadError && (
            <p className="assign-perm-note assign-perm-error" role="alert">
              The {noun} list could not be loaded. <button type="button" className="assign-perm-link" onClick={loadCandidates}>Try again</button>
            </p>
          )}
          {type === 'user' && selectedUser?.isAdministrator && (
            <p className="assign-perm-note">Administrators already see every connection; this assignment applies only if they stop being one.</p>
          )}
        </div>

        {type === 'department' && (
          <div className="conn-modal-field">
            <label className="conn-modal-label" htmlFor="assign-dept-desc">
              Note <span className="assign-perm-optional">(optional)</span>
            </label>
            <input
              id="assign-dept-desc"
              type="text"
              autoComplete="off"
              placeholder="Handles customer support conversations…"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="conn-modal-input"
            />
          </div>
        )}

        <fieldset className="conn-modal-field assign-perm-fieldset">
          <legend className="assign-perm-legend">
            <span className="conn-modal-label">Allowed connections</span>
            {connections.length > 0 && (
              <button type="button" className="assign-perm-link" onClick={() => setSelectedConnIds(allSelected ? [] : connections.map(c => c.id))}>
                {allSelected ? 'Clear all' : 'Select all'}
              </button>
            )}
          </legend>
          <div className="assign-perm-connection-list">
            {connections.length === 0 ? (
              <p className="assign-perm-note">No connections yet. Add one under Connections first.</p>
            ) : (
              connections.map((conn) => {
                const isSelected = selectedConnIds.includes(conn.id)
                return (
                  <button
                    key={conn.id}
                    type="button"
                    aria-pressed={isSelected}
                    onClick={() => toggleConnection(conn.id)}
                    className={`assign-perm-row${isSelected ? ' is-selected' : ''}`}
                  >
                    <span className={`assign-perm-status-dot${conn.isConnected ? ' is-connected' : ''}`} aria-hidden="true" />
                    <span className="assign-perm-row-name">{conn.nickname || conn.name}</span>
                    <span className="assign-perm-row-phone">
                      {conn.phoneNumber || 'Setup pending'}
                      <span className="sr-only">{conn.isConnected ? ', connected' : ', not connected'}</span>
                    </span>
                    <Check size={16} className="assign-perm-check" aria-hidden="true" />
                  </button>
                )
              })
            )}
          </div>
          {selectedConnIds.length > 0 && (
            <p className="assign-perm-note" aria-live="polite">{selectedConnIds.length} of {connections.length} selected</p>
          )}
        </fieldset>
      </form>
    </Modal>
  )
}
