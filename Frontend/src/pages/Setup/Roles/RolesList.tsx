import React, { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Plus, RefreshCw, MoreVertical, ShieldCheck, Lock } from 'lucide-react'
import { Menu, MenuItem } from '../../../components/Menu/Menu'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import Can from '../../../components/Can/Can'
import usePermission from '../../../hooks/usePermission'
import { setupService } from '../../../services/setup/setupService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { formatAbsoluteDateTime } from '../../../utils/dateHelper'
import { pageTransitionProps } from '../../../utils/motion'
import type { SetupRoleListItem } from '../../../types/setup'

export const RolesList: React.FC = () => {
  const navigate = useNavigate()
  const { has } = usePermission()

  const [roles, setRoles] = useState<SetupRoleListItem[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<SetupRoleListItem | null>(null)

  const loadRoles = async (showSpinner = true) => {
    if (showSpinner) setIsLoading(true)
    try {
      setRoles(await setupService.getRoles())
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load roles.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => {
    void loadRoles()
  }, [])

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await setupService.deleteRole(deleteTarget.id)
      toast.success(`Role '${deleteTarget.name}' deleted.`)
      setDeleteTarget(null)
      void loadRoles(false)
    } catch (error) {
      // Expected here: built-in roles, and roles still assigned to users.
      toast.error(getErrorMessage(error, 'Failed to delete the role.'))
      setDeleteTarget(null)
    }
  }

  const canEdit = has('Role.Edit')
  const canDelete = has('Role.Delete')

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Role</h1>
        <p>Group permissions into roles and assign them to users.</p>
      </div>

      <div className="contacts-toolbar">
        <Can permission="Role.Create">
          <button type="button" className="btn-toolbar-primary" onClick={() => navigate('/setup/roles/new')}>
            <Plus size={15} />
            <span>New Role</span>
          </button>
        </Can>
        <button type="button" className="btn-toolbar-tertiary" onClick={() => loadRoles()}>
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
                  <th>DESCRIPTION</th>
                  <th className="text-center">USERS</th>
                  <th className="text-center">PERMISSIONS</th>
                  <th>CREATED AT</th>
                  <th className="text-center">ACTIONS</th>
                </tr>
              </thead>
              <tbody>
                {roles.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="no-records-row">
                      <EmptyState
                        iconName="ShieldCheck"
                        title="No roles yet"
                        message="Create a role to group permissions together."
                        action={has('Role.Create')
                          ? { label: 'New Role', onClick: () => navigate('/setup/roles/new') }
                          : undefined}
                      />
                    </td>
                  </tr>
                ) : (
                  roles.map((role) => (
                    <tr key={role.id}>
                      {/* Real record id — see UsersList for the reasoning. */}
                      <td>{role.id}</td>
                      <td>
                        <div className="setup-role-name-cell">
                          <button
                            type="button"
                            className="setup-user-name"
                            onClick={() => canEdit && navigate(`/setup/roles/${role.id}`)}
                            disabled={!canEdit}
                          >
                            {role.name}
                          </button>
                          {role.isAdministrator && (
                            <span className="setup-role-badge admin">
                              <ShieldCheck size={12} />
                              Full access
                            </span>
                          )}
                          {role.isSystem && (
                            <span className="setup-role-badge muted" title="Built-in role — cannot be deleted">
                              <Lock size={11} />
                              Built-in
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="setup-truncate" title={role.description ?? ''}>
                        {role.description || '—'}
                      </td>
                      <td className="text-center">{role.userCount}</td>
                      <td className="text-center">
                        {role.isAdministrator ? 'All' : role.permissionCount}
                      </td>
                      <td>{formatAbsoluteDateTime(role.createdAt)}</td>
                      <td className="text-center">
                        <div className="contact-actions-menu-wrapper">
                          <Menu
                            open={openMenuId === role.id}
                            onOpenChange={(isOpen) => setOpenMenuId(isOpen ? role.id : null)}
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
                              disabled={!canEdit}
                              onSelect={() => navigate(`/setup/roles/${role.id}`)}
                            >
                              Edit
                            </MenuItem>
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              disabled={!canDelete || role.isSystem}
                              title={role.isSystem ? 'Built-in roles cannot be deleted' : undefined}
                              onSelect={() => setDeleteTarget(role)}
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

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Role"
        message={`Are you sure you want to delete the role '${deleteTarget?.name}'? This cannot be undone.`}
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default RolesList
