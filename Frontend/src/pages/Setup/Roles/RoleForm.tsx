import React, { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { ShieldCheck, Users, Info, Lock } from 'lucide-react'
import PermissionMatrix from '../../../components/PermissionMatrix/PermissionMatrix'
import { setupService } from '../../../services/setup/setupService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'
import type { PermissionCatalog, RoleUser, SetupRolePayload } from '../../../types/setup'

export const RoleForm: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const roleId = id ? parseInt(id, 10) : null
  const isEditMode = roleId !== null

  const [form, setForm] = useState<SetupRolePayload>({
    name: '',
    description: '',
    isAdministrator: false,
    permissionKeys: []
  })
  const [catalog, setCatalog] = useState<PermissionCatalog | null>(null)
  const [users, setUsers] = useState<RoleUser[]>([])
  const [isSystem, setIsSystem] = useState(false)
  const [isLoading, setIsLoading] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    const load = async () => {
      setIsLoading(true)
      try {
        const catalogData = await setupService.getPermissionCatalog()
        setCatalog(catalogData)

        if (isEditMode && roleId) {
          const role = await setupService.getRole(roleId)
          setForm({
            name: role.name,
            description: role.description ?? '',
            isAdministrator: role.isAdministrator,
            permissionKeys: role.permissionKeys
          })
          setUsers(role.users)
          setIsSystem(role.isSystem)
        }
      } catch (error) {
        toast.error(getErrorMessage(error, 'Failed to load the role form.'))
        // A role that no longer exists must not leave a blank "edit" form on screen — that
        // invites a save against a missing record. Send them back to the list instead.
        if (isEditMode) navigate('/setup/roles', { replace: true })
      } finally {
        setIsLoading(false)
      }
    }
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [roleId])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSubmitting(true)
    try {
      const payload: SetupRolePayload = { ...form, description: form.description || undefined }

      if (isEditMode && roleId) {
        await setupService.updateRole(roleId, payload)
        toast.success('Role updated successfully.')
      } else {
        await setupService.createRole(payload)
        toast.success('Role created successfully.')
      }
      navigate('/setup/roles')
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to save the role.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  if (isLoading) {
    return <div className="setup-form-loading">Loading…</div>
  }

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>{isEditMode ? 'Edit Role' : 'Add Role'}</h1>
        <p>Choose which features and capabilities this role grants.</p>
      </div>

      <form className="setup-form" onSubmit={handleSubmit}>
        <div className="setup-form-grid setup-form-grid-role">
          <section className="setup-panel">
            <div className="setup-panel-body">
              <div className="setup-field">
                <label className="setup-label required" htmlFor="roleName">Role</label>
                <input
                  id="roleName"
                  className="setup-input"
                  value={form.name}
                  onChange={(e) => setForm((prev) => ({ ...prev, name: e.target.value }))}
                  placeholder="Enter role name"
                  required
                />
                {isSystem && (
                  <p className="setup-hint">
                    <Lock size={11} /> Built-in role. It can be renamed and re-scoped, but not deleted.
                  </p>
                )}
              </div>

              <div className="setup-field">
                <label className="setup-label" htmlFor="roleDescription">Description</label>
                <input
                  id="roleDescription"
                  className="setup-input"
                  value={form.description ?? ''}
                  onChange={(e) => setForm((prev) => ({ ...prev, description: e.target.value }))}
                  placeholder="What is this role for?"
                />
              </div>

              <div className="setup-toggle-item setup-admin-toggle">
                <div>
                  <span className="setup-toggle-label">Administrator Access</span>
                  <p className="setup-hint">
                    Grants every permission, including any added in future updates.
                  </p>
                </div>
                <button
                  type="button"
                  className={`setup-switch ${form.isAdministrator ? 'on' : 'off'}`}
                  onClick={() => setForm((prev) => ({ ...prev, isAdministrator: !prev.isAdministrator }))}
                  aria-pressed={form.isAdministrator}
                  aria-label="Administrator access"
                >
                  <span className="setup-switch-knob" />
                </button>
              </div>

              {form.isAdministrator ? (
                <div className="setup-info-box">
                  <Info size={16} />
                  <div>
                    <strong>Administrator Information</strong>
                    <p>Users with this role have full access to all features and settings.</p>
                  </div>
                </div>
              ) : (
                <PermissionMatrix
                  catalog={catalog}
                  value={form.permissionKeys}
                  onChange={(next) => setForm((prev) => ({ ...prev, permissionKeys: next }))}
                />
              )}
            </div>
          </section>

          <section className="setup-panel">
            <header className="setup-panel-head">
              <Users size={18} />
              <h2>List of users using this role</h2>
            </header>

            <div className="setup-panel-body">
              {users.length === 0 ? (
                <div className="setup-role-users-empty">
                  {isEditMode ? 'No users are assigned to this role.' : 'Save the role to start assigning users.'}
                </div>
              ) : (
                <ul className="setup-role-users">
                  {users.map((user) => (
                    <li className="setup-role-user" key={user.id}>
                      <div className="setup-user-avatar small">
                        <span>{user.fullName.charAt(0).toUpperCase()}</span>
                      </div>
                      <div className="setup-role-user-info">
                        <span className="setup-role-user-name">{user.fullName}</span>
                        <span className="setup-role-user-email">{user.email}</span>
                      </div>
                      {!user.isActive && <span className="setup-role-badge muted">Inactive</span>}
                    </li>
                  ))}
                </ul>
              )}

              {isEditMode && users.length > 0 && (
                <p className="setup-hint">
                  <ShieldCheck size={11} /> Changes to this role apply to all {users.length} user
                  {users.length === 1 ? '' : 's'} immediately.
                </p>
              )}
            </div>
          </section>
        </div>

        <div className="setup-form-actions">
          <button type="button" className="btn-toolbar-tertiary" onClick={() => navigate('/setup/roles')}>
            Cancel
          </button>
          <button type="submit" className="btn-toolbar-primary" disabled={isSubmitting}>
            {isSubmitting ? 'Saving…' : 'Save Changes'}
          </button>
        </div>
      </form>
    </motion.div>
  )
}

export default RoleForm
