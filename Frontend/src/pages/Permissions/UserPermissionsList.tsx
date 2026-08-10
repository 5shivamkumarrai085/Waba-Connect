import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import {
  Users,
  UserCheck,
  Smartphone,
  ShieldCheck,
  Search,
  Plus,
  Eye,
  Edit2,
  History,
  MessageSquare
} from 'lucide-react'
import toast from 'react-hot-toast'
import { permissionService } from '../../services/permissions/permissionService'
import { connectionService } from '../../services/connections/connectionService'
import { AssignPermissionModal } from './AssignPermissionModal'
import type { UserPermissionDashboard } from '../../types/permission'
import type { Connection } from '../../types/connection'
import './UserPermissionsList.css'
import Can from '../../components/Can/Can'

export const UserPermissionsList: React.FC = () => {
  const [dashboard, setDashboard] = useState<UserPermissionDashboard | null>(null)
  const [connections, setConnections] = useState<Connection[]>([])
  const [loading, setLoading] = useState(true)

  // Filters
  const [searchQuery, setSearchQuery] = useState('')
  const [selectedDept, setSelectedDept] = useState('All Departments')
  const [selectedConnId, setSelectedConnId] = useState<number | 'All'>('All')
  const [selectedStatus, setSelectedStatus] = useState('All Status')

  // Modal State
  const [showAssignModal, setShowAssignModal] = useState(false)

  const fetchData = async () => {
    setLoading(true)
    try {
      const [dashRes, connRes] = await Promise.all([
        permissionService.getUserDashboard(),
        connectionService.getConnections()
      ])
      setDashboard(dashRes)
      setConnections(connRes)
    } catch {
      toast.error('Failed to load user permissions dashboard.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchData()
  }, [])

  const handleToggleStatus = async (id: number) => {
    try {
      await permissionService.toggleUserPermissionStatus(id)
      toast.success('User permission status updated.')
      fetchData()
    } catch {
      toast.error('Failed to update status.')
    }
  }

  const handleAssignUser = async (data: { userId: string; userName: string; userEmail: string; departmentName: string; connectionIds: number[] }) => {
    try {
      await permissionService.assignUserPermission(data)
      toast.success('Permission assigned successfully!')
      fetchData()
    } catch {
      toast.error('Failed to assign user permission.')
    }
  }

  // Filter calculations
  const filteredUsers = (dashboard?.userPermissions || []).filter((user) => {
    const matchesSearch =
      user.userName.toLowerCase().includes(searchQuery.toLowerCase()) ||
      user.userEmail.toLowerCase().includes(searchQuery.toLowerCase())
    const matchesDept = selectedDept === 'All Departments' || user.departmentName.toLowerCase() === selectedDept.toLowerCase()
    const matchesConn = selectedConnId === 'All' || user.connectionIds.includes(Number(selectedConnId))
    const matchesStatus =
      selectedStatus === 'All Status' ||
      (selectedStatus === 'Active' && user.isActive) ||
      (selectedStatus === 'Inactive' && !user.isActive)

    return matchesSearch && matchesDept && matchesConn && matchesStatus
  })

  return (
    <motion.div className="permission-page-container" {...pageTransitionProps}>
      {/* Breadcrumb & Header */}
      <div className="mb-2 text-xs font-semibold text-slate-500 flex items-center gap-1.5">
        <span>Admin</span>
        <span>&gt;</span>
        <span>Permissions</span>
        <span>&gt;</span>
        <span className="text-blue-600 font-bold">User Connection Permission</span>
      </div>

      <div className="permission-header">
        <div className="permission-title-group">
          <h1>User Connection Permission</h1>
          <p className="permission-subtitle">
            Assign and manage WABA connection access for individual users.
          </p>
        </div>
        <button
          type="button"
          onClick={() => toast.success('Permission history coming soon')}
          className="btn-permission-history"
        >
          <History size={16} />
          <span>Permission History</span>
        </button>
      </div>

      {/* 4 KPI Cards (Matching Image 3 Top Row) */}
      <div className="permission-kpi-grid">
        <div className="permission-kpi-card">
          <div className="permission-kpi-icon blue">
            <Users className="w-5 h-5" />
          </div>
          <div>
            <div className="permission-kpi-label">Total Users</div>
            <div className="permission-kpi-value">{dashboard?.totalUsers ?? 0}</div>
            <div className="permission-kpi-desc">All registered users</div>
          </div>
        </div>

        <div className="permission-kpi-card">
          <div className="permission-kpi-icon green">
            <UserCheck className="w-5 h-5" />
          </div>
          <div>
            <div className="permission-kpi-label">Users with Access</div>
            <div className="permission-kpi-value">{dashboard?.usersWithAccess ?? 0}</div>
            <div className="permission-kpi-desc">Users with at least one connection</div>
          </div>
        </div>

        <div className="permission-kpi-card">
          <div className="permission-kpi-icon amber">
            <Smartphone className="w-5 h-5" />
          </div>
          <div>
            <div className="permission-kpi-label">Total Connections</div>
            <div className="permission-kpi-value">{dashboard?.totalConnections ?? 0}</div>
            <div className="permission-kpi-desc">All WABA connections</div>
          </div>
        </div>

        <div className="permission-kpi-card">
          <div className="permission-kpi-icon emerald">
            <ShieldCheck className="w-5 h-5" />
          </div>
          <div>
            <div className="permission-kpi-label">Active Permissions</div>
            <div className="permission-kpi-value">{dashboard?.activePermissions ?? 0}</div>
            <div className="permission-kpi-desc">Total active user-connection mappings</div>
          </div>
        </div>
      </div>

      {/* Filter Card Bar */}
      <div className="permission-filter-card">
        <div className="permission-filter-left">
          <div className="permission-search-input-wrapper">
            <Search className="w-4 h-4 permission-search-icon" />
            <input
              type="text"
              placeholder="Search users by name, email or phone..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>

          <select
            value={selectedDept}
            onChange={(e) => setSelectedDept(e.target.value)}
            className="permission-select"
          >
            <option value="All Departments">Select Department</option>
            <option value="All Departments">All Departments</option>
            <option value="Sales">Sales</option>
            <option value="Support">Support</option>
            <option value="Marketing">Marketing</option>
          </select>

          <select
            value={selectedConnId}
            onChange={(e) => setSelectedConnId(e.target.value === 'All' ? 'All' : Number(e.target.value))}
            className="permission-select"
          >
            <option value="All">Select Connection</option>
            <option value="All">All Connections</option>
            {connections.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>

          <select
            value={selectedStatus}
            onChange={(e) => setSelectedStatus(e.target.value)}
            className="permission-select"
          >
            <option value="All Status">Permission Status</option>
            <option value="All Status">All Status</option>
            <option value="Active">Active</option>
            <option value="Inactive">Inactive</option>
          </select>
        </div>

        <Can permission="ConnectionAccess.Assign">
          <button
            type="button"
            onClick={() => setShowAssignModal(true)}
            className="btn-assign-permission"
          >
            <Plus className="w-4 h-4" />
            <span>Assign Permission</span>
          </button>
        </Can>
      </div>

      {/* Table Card */}
      <div className="permission-table-card">
        {loading ? (
          <div className="p-8 text-center text-slate-400 text-sm">Loading user permissions...</div>
        ) : filteredUsers.length === 0 ? (
          <div className="p-8 text-center text-slate-500 text-sm">No user permissions match your filter criteria.</div>
        ) : (
          <table className="permission-table">
            <thead>
              <tr>
                <th>User</th>
                <th>Department</th>
                <th>Assigned Connections</th>
                <th>Permission Scope</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredUsers.map((user) => (
                <tr key={user.id}>
                  <td>
                    <div className="user-cell">
                      <div className="user-avatar-circle">
                        {user.userName.charAt(0).toUpperCase()}
                      </div>
                      <div>
                        <span className="user-name-text">{user.userName}</span>
                        <span className="user-email-text">{user.userEmail}</span>
                      </div>
                    </div>
                  </td>
                  <td>
                    <span className="font-medium text-slate-700">{user.departmentName}</span>
                  </td>
                  <td>
                    <div className="conn-badges-wrapper">
                      {user.connectionNames.slice(0, 2).map((name, idx) => (
                        <div key={idx} className="conn-wa-badge" title={name}>
                          <MessageSquare className="w-3.5 h-3.5" />
                        </div>
                      ))}
                      {user.connectionNames.length > 2 && (
                        <span className="conn-more-pill">+{user.connectionNames.length - 2} more</span>
                      )}
                    </div>
                  </td>
                  <td>
                    <span className="scope-pill cursor-pointer hover:underline">
                      {user.permissionScopeText}
                    </span>
                  </td>
                  <td>
                    <span
                      onClick={() => handleToggleStatus(user.id)}
                      className={`status-pill cursor-pointer ${user.isActive ? 'active' : 'inactive'}`}
                    >
                      <span className="status-dot" />
                      {user.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td>
                    <div className="action-buttons">
                      <button
                        type="button"
                        onClick={() => toast.success(`Viewing permissions for ${user.userName}`)}
                        className="icon-btn"
                        title="View Details"
                      >
                        <Eye className="w-4 h-4" />
                      </button>
                      <Can permission="ConnectionAccess.Assign">
                        <button
                          type="button"
                          onClick={() => setShowAssignModal(true)}
                          className="icon-btn"
                          title="Edit Permission"
                        >
                          <Edit2 className="w-4 h-4" />
                        </button>
                      </Can>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <AssignPermissionModal
        isOpen={showAssignModal}
        type="user"
        connections={connections}
        onClose={() => setShowAssignModal(false)}
        onAssignUser={handleAssignUser}
      />
    </motion.div>
  )
}
