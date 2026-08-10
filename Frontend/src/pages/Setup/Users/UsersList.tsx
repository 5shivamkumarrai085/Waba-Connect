import React, { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Plus, RefreshCw, MoreVertical, Search, ShieldCheck } from 'lucide-react'
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
import type { SetupUserListItem, UserDashboard } from '../../../types/setup'
import { resolveMediaUrl } from '../../../utils/mediaUrl'

export const UsersList: React.FC = () => {
  const navigate = useNavigate()
  const { has } = usePermission()

  const [users, setUsers] = useState<SetupUserListItem[]>([])
  const [dashboard, setDashboard] = useState<UserDashboard | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [searchQuery, setSearchQuery] = useState('')
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<SetupUserListItem | null>(null)

  const loadUsers = async (showSpinner = true) => {
    if (showSpinner) setIsLoading(true)
    try {
      const [list, stats] = await Promise.all([
        setupService.getUsers(),
        setupService.getUserDashboard()
      ])
      setUsers(list)
      setDashboard(stats)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load users.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => {
    void loadUsers()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const filteredUsers = useMemo(() => {
    const query = searchQuery.trim().toLowerCase()
    if (!query) return users
    return users.filter((user) =>
      user.fullName.toLowerCase().includes(query) ||
      user.email.toLowerCase().includes(query) ||
      (user.phoneNumber ?? '').toLowerCase().includes(query) ||
      (user.roleName ?? '').toLowerCase().includes(query)
    )
  }, [users, searchQuery])

  const handleToggleActive = async (user: SetupUserListItem) => {
    try {
      const updated = await setupService.toggleUserActive(user.id)
      setUsers((prev) => prev.map((u) => (u.id === user.id ? { ...u, isActive: updated.isActive } : u)))
      toast.success(updated.isActive ? 'User activated.' : 'User deactivated.')
      void loadUsers(false)
    } catch (error) {
      // Expected here: deactivating yourself, or the last remaining administrator.
      toast.error(getErrorMessage(error, 'Failed to update user status.'))
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await setupService.deleteUser(deleteTarget.id)
      toast.success(`${deleteTarget.fullName} deleted.`)
      setDeleteTarget(null)
      void loadUsers(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to delete user.'))
      setDeleteTarget(null)
    }
  }

  const canEdit = has('User.Edit')
  const canDelete = has('User.Delete')

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>User</h1>
        <p>Manage staff accounts, their roles and what they can access.</p>
      </div>

      {dashboard && (
        <div className="setup-kpi-grid">
          <div className="setup-kpi-card">
            <span className="setup-kpi-label">Total Users</span>
            <span className="setup-kpi-value">{dashboard.totalUsers}</span>
          </div>
          <div className="setup-kpi-card">
            <span className="setup-kpi-label">Active</span>
            <span className="setup-kpi-value">{dashboard.activeUsers}</span>
          </div>
          <div className="setup-kpi-card">
            <span className="setup-kpi-label">Inactive</span>
            <span className="setup-kpi-value">{dashboard.inactiveUsers}</span>
          </div>
          <div className="setup-kpi-card">
            <span className="setup-kpi-label">Administrators</span>
            <span className="setup-kpi-value">{dashboard.administratorCount}</span>
          </div>
        </div>
      )}

      <div className="contacts-toolbar">
        <Can permission="User.Create">
          <button type="button" className="btn-toolbar-primary" onClick={() => navigate('/setup/users/new')}>
            <Plus size={15} />
            <span>New User</span>
          </button>
        </Can>
        <button type="button" className="btn-toolbar-tertiary" onClick={() => loadUsers()}>
          <RefreshCw size={15} />
          <span>Refresh</span>
        </button>
      </div>

      <div className="contacts-card">
        <div className="contacts-controls-row">
          <div className="contacts-controls-left" />
          <div className="contacts-controls-right">
            <div className="setup-search">
              <Search size={15} className="setup-search-icon" />
              <input
                type="text"
                placeholder="Search users..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
              />
            </div>
          </div>
        </div>

        <div className="data-table-wrapper">
          {isLoading ? (
            <Skeleton variant="table" />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>ID</th>
                  <th>NAME</th>
                  <th>PHONE</th>
                  <th>EMAIL</th>
                  <th>ROLE</th>
                  <th className="text-center">STATUS</th>
                  <th>CREATED AT</th>
                  <th className="text-center">ACTIONS</th>
                </tr>
              </thead>
              <tbody>
                {filteredUsers.length === 0 ? (
                  <tr>
                    <td colSpan={8} className="no-records-row">
                      {users.length === 0 ? (
                        <EmptyState
                          iconName="Users"
                          title="No users yet"
                          message="Create the first staff account to get started."
                          action={has('User.Create')
                            ? { label: 'New User', onClick: () => navigate('/setup/users/new') }
                            : undefined}
                        />
                      ) : (
                        <EmptyState
                          iconName="SearchX"
                          title="No matching users"
                          message="No user matches your search."
                          action={{ label: 'Clear search', onClick: () => setSearchQuery('') }}
                        />
                      )}
                    </td>
                  </tr>
                ) : (
                  filteredUsers.map((user) => (
                    <tr key={user.id}>
                      {/* The real record id, not a row number: an admin referencing a user in
                          a support conversation needs the value the API actually uses. */}
                      <td>{user.id}</td>
                      <td>
                        <div className="setup-user-cell">
                          <div className="setup-user-avatar">
                            {user.profileImageUrl
                              ? <img src={resolveMediaUrl(user.profileImageUrl)} alt="" />
                              : <span>{user.fullName.charAt(0).toUpperCase()}</span>}
                          </div>
                          <button
                            type="button"
                            className="setup-user-name"
                            onClick={() => canEdit && navigate(`/setup/users/${user.id}`)}
                            disabled={!canEdit}
                          >
                            {user.fullName}
                          </button>
                        </div>
                      </td>
                      <td>{user.phoneNumber || '—'}</td>
                      <td>{user.email}</td>
                      <td>
                        {user.isAdministrator ? (
                          <span className="setup-role-badge admin">
                            <ShieldCheck size={12} />
                            Administrator
                          </span>
                        ) : user.roleName ? (
                          <span className="setup-role-badge">{user.roleName}</span>
                        ) : (
                          <span className="setup-role-badge muted">Custom</span>
                        )}
                      </td>
                      <td className="text-center">
                        <button
                          type="button"
                          className={`setup-status-toggle ${user.isActive ? 'on' : 'off'}`}
                          onClick={() => canEdit && handleToggleActive(user)}
                          disabled={!canEdit}
                          aria-label={user.isActive ? 'Deactivate user' : 'Activate user'}
                          title={user.isActive ? 'Active' : 'Inactive'}
                        >
                          <span className="setup-status-knob" />
                        </button>
                      </td>
                      <td>{formatAbsoluteDateTime(user.createdAt)}</td>
                      <td className="text-center">
                        <div className="contact-actions-menu-wrapper">
                          <Menu
                            open={openMenuId === user.id}
                            onOpenChange={(isOpen) => setOpenMenuId(isOpen ? user.id : null)}
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
                            {/* Disabled rather than hidden: items vanishing changes the menu's
                                height between rows, which reads as a rendering bug. */}
                            <MenuItem
                              className="contact-actions-item"
                              disabled={!canEdit}
                              onSelect={() => navigate(`/setup/users/${user.id}`)}
                            >
                              Edit
                            </MenuItem>
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              disabled={!canDelete}
                              onSelect={() => setDeleteTarget(user)}
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
        title="Delete User"
        message={`Are you sure you want to delete ${deleteTarget?.fullName}? This cannot be undone.`}
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default UsersList
