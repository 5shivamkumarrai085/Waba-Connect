import React, { useState } from 'react'
import { Shield, Check } from 'lucide-react'
import { Modal } from '../../components/Modal/Modal'
import type { Connection } from '../../types/connection'
import './AssignPermissionModal.css'

interface AssignPermissionModalProps {
  isOpen?: boolean
  type: 'user' | 'department'
  connections: Connection[]
  onClose: () => void
  onAssignUser?: (data: { userId: string; userName: string; userEmail: string; departmentName: string; connectionIds: number[] }) => Promise<void>
  onAssignDepartment?: (data: { departmentId: string; departmentName: string; description: string; memberCount: number; connectionIds: number[] }) => Promise<void>
}

export const AssignPermissionModal: React.FC<AssignPermissionModalProps> = ({
  isOpen = true,
  type,
  connections,
  onClose,
  onAssignUser,
  onAssignDepartment
}) => {
  const [userName, setUserName] = useState('')
  const [userEmail, setUserEmail] = useState('')
  const [departmentName, setDepartmentName] = useState('Sales')
  const [description, setDescription] = useState('')
  const [memberCount, setMemberCount] = useState(5)
  const [selectedConnIds, setSelectedConnIds] = useState<number[]>([])
  const [isSubmitting, setIsSubmitting] = useState(false)

  const toggleConnection = (id: number) => {
    if (selectedConnIds.includes(id)) {
      setSelectedConnIds(selectedConnIds.filter((x) => x !== id))
    } else {
      setSelectedConnIds([...selectedConnIds, id])
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (selectedConnIds.length === 0) return
    setIsSubmitting(true)
    try {
      if (type === 'user' && onAssignUser) {
        await onAssignUser({
          userId: `usr-${Date.now()}`,
          userName: userName.trim() || 'New User',
          userEmail: userEmail.trim() || 'user@example.com',
          departmentName,
          connectionIds: selectedConnIds
        })
      } else if (type === 'department' && onAssignDepartment) {
        await onAssignDepartment({
          departmentId: `dept-${Date.now()}`,
          departmentName,
          description: description.trim() || `${departmentName} Department Connections`,
          memberCount,
          connectionIds: selectedConnIds
        })
      }
      onClose()
    } catch {
      // Handled silently
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      size="md"
      icon={<Shield size={18} />}
      title={type === 'user' ? 'Assign User Permission' : 'Assign Department Permission'}
      subtitle={
        type === 'user'
          ? 'Assign WABA connections to an individual user.'
          : 'Assign WABA connections to an entire department.'
      }
      footer={
        <>
          <button
            type="button"
            className="oc-dialog-btn oc-dialog-btn-secondary"
            onClick={onClose}
          >
            Cancel
          </button>
          <button
            type="submit"
            form="assign-permission-form"
            className="oc-dialog-btn oc-dialog-btn-primary"
            disabled={isSubmitting || selectedConnIds.length === 0}
          >
            {isSubmitting ? 'Assigning...' : 'Assign Permission'}
          </button>
        </>
      }
    >
      <form id="assign-permission-form" onSubmit={handleSubmit}>
        {type === 'user' ? (
          <>
            <div className="conn-modal-field">
              <label className="conn-modal-label" htmlFor="assign-user-name">
                User Full Name
              </label>
              <input
                id="assign-user-name"
                type="text"
                required
                placeholder="e.g. Aman Kumar"
                value={userName}
                onChange={(e) => setUserName(e.target.value)}
                className="conn-modal-input"
                data-autofocus
              />
            </div>

            <div className="conn-modal-field">
              <label className="conn-modal-label" htmlFor="assign-user-email">
                User Email Address
              </label>
              <input
                id="assign-user-email"
                type="email"
                required
                placeholder="e.g. aman.kumar@example.com"
                value={userEmail}
                onChange={(e) => setUserEmail(e.target.value)}
                className="conn-modal-input"
              />
            </div>

            <div className="conn-modal-field">
              <label className="conn-modal-label" htmlFor="assign-user-dept">
                Department
              </label>
              <select
                id="assign-user-dept"
                value={departmentName}
                onChange={(e) => setDepartmentName(e.target.value)}
                className="conn-modal-input"
              >
                <option value="Sales">Sales</option>
                <option value="Support">Support</option>
                <option value="Marketing">Marketing</option>
                <option value="Operations">Operations</option>
              </select>
            </div>
          </>
        ) : (
          <>
            <div className="conn-modal-field">
              <label className="conn-modal-label" htmlFor="assign-dept-name">
                Department Name
              </label>
              <input
                id="assign-dept-name"
                type="text"
                required
                placeholder="e.g. Sales"
                value={departmentName}
                onChange={(e) => setDepartmentName(e.target.value)}
                className="conn-modal-input"
                data-autofocus
              />
            </div>

            <div className="conn-modal-field">
              <label className="conn-modal-label" htmlFor="assign-dept-desc">
                Department Description
              </label>
              <input
                id="assign-dept-desc"
                type="text"
                placeholder="e.g. Handles all sales related queries and leads"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                className="conn-modal-input"
              />
            </div>

            <div className="conn-modal-field">
              <label className="conn-modal-label" htmlFor="assign-dept-members">
                Member Count
              </label>
              <input
                id="assign-dept-members"
                type="number"
                min={1}
                value={memberCount}
                onChange={(e) => setMemberCount(Number(e.target.value))}
                className="conn-modal-input"
              />
            </div>
          </>
        )}

        <div className="conn-modal-field">
          <label className="conn-modal-label">Select Allowed Connections</label>
          <div className="assign-perm-connection-list">
            {connections.length === 0 ? (
              <p className="assign-perm-empty">No connections available.</p>
            ) : (
              connections.map((conn) => {
                const isSelected = selectedConnIds.includes(conn.id)
                return (
                  <button
                    key={conn.id}
                    type="button"
                    onClick={() => toggleConnection(conn.id)}
                    className={`assign-perm-row${isSelected ? ' is-selected' : ''}`}
                  >
                    <span
                      className={`assign-perm-status-dot${conn.isConnected ? ' is-connected' : ''}`}
                    />
                    <span className="assign-perm-row-name">{conn.name}</span>
                    <span className="assign-perm-row-phone">
                      {conn.phoneNumber || 'Setup pending'}
                    </span>
                    {isSelected && <Check size={16} className="assign-perm-check" />}
                  </button>
                )
              })
            )}
          </div>
        </div>
      </form>
    </Modal>
  )
}
