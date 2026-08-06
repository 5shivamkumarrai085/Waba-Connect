import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import {
  Building2,
  CheckSquare,
  Smartphone,
  ShieldCheck,
  Search,
  Plus,
  Eye,
  Edit2,
  History,
  MessageSquare,
  Users
} from 'lucide-react'
import toast from 'react-hot-toast'
import { permissionService } from '../../services/permissions/permissionService'
import { connectionService } from '../../services/connections/connectionService'
import { AssignPermissionModal } from './AssignPermissionModal'
import type { DepartmentPermissionDashboard } from '../../types/permission'
import type { Connection } from '../../types/connection'
import './UserPermissionsList.css'
import './DepartmentPermissionsList.css'

export const DepartmentPermissionsList: React.FC = () => {
  const [dashboard, setDashboard] = useState<DepartmentPermissionDashboard | null>(null)
  const [connections, setConnections] = useState<Connection[]>([])
  const [loading, setLoading] = useState(true)

  // Filters
  const [searchQuery, setSearchQuery] = useState('')
  const [selectedConnId, setSelectedConnId] = useState<number | 'All'>('All')
  const [selectedStatus, setSelectedStatus] = useState('All Status')

  // Modal State
  const [showAssignModal, setShowAssignModal] = useState(false)

  const fetchData = async () => {
    setLoading(true)
    try {
      const [dashRes, connRes] = await Promise.all([
        permissionService.getDepartmentDashboard(),
        connectionService.getConnections()
      ])
      setDashboard(dashRes)
      setConnections(connRes)
    } catch {
      toast.error('Failed to load department permissions dashboard.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchData()
  }, [])

  const handleToggleStatus = async (id: number) => {
    try {
      await permissionService.toggleDepartmentPermissionStatus(id)
      toast.success('Department permission status updated.')
      fetchData()
    } catch {
      toast.error('Failed to update status.')
    }
  }

  const handleAssignDepartment = async (data: {
    departmentId: string
    departmentName: string
    description: string
    memberCount: number
    connectionIds: number[]
  }) => {
    try {
      await permissionService.assignDepartmentPermission(data)
      toast.success('Department permission assigned successfully!')
      fetchData()
    } catch {
      toast.error('Failed to assign department permission.')
    }
  }

  const filteredDepts = (dashboard?.departmentPermissions || []).filter((dept) => {
    const matchesSearch =
      dept.departmentName.toLowerCase().includes(searchQuery.toLowerCase()) ||
      dept.description.toLowerCase().includes(searchQuery.toLowerCase())
    const matchesConn = selectedConnId === 'All' || dept.connectionIds.includes(Number(selectedConnId))
    const matchesStatus =
      selectedStatus === 'All Status' ||
      (selectedStatus === 'Active' && dept.isActive) ||
      (selectedStatus === 'Inactive' && !dept.isActive)

    return matchesSearch && matchesConn && matchesStatus
  })

  return (
    <motion.div className="permission-page-container" {...pageTransitionProps}>
      {/* Breadcrumb & Header */}
      <div className="mb-2 text-xs font-semibold text-slate-500 flex items-center gap-1.5">
        <span>Admin</span>
        <span>&gt;</span>
        <span>Permissions</span>
        <span>&gt;</span>
        <span className="text-blue-600 font-bold">Department Connection Permission</span>
      </div>

      <div className="permission-header">
        <div className="permission-title-group">
          <h1>Department Connection Permission</h1>
          <p className="permission-subtitle">
            Assign and manage WABA connection access for departments.
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

      {/* 4 KPI Cards (Matching Image 3 Bottom Row) */}
      <div className="permission-kpi-grid">
        <div className="permission-kpi-card">
          <div className="permission-kpi-icon blue">
            <Building2 className="w-5 h-5" />
          </div>
          <div>
            <div className="permission-kpi-label">Total Departments</div>
            <div className="permission-kpi-value">{dashboard?.totalDepartments ?? 0}</div>
            <div className="permission-kpi-desc">All departments</div>
          </div>
        </div>

        <div className="permission-kpi-card">
          <div className="permission-kpi-icon green">
            <CheckSquare className="w-5 h-5" />
          </div>
          <div>
            <div className="permission-kpi-label">Departments with Access</div>
            <div className="permission-kpi-value">{dashboard?.departmentsWithAccess ?? 0}</div>
            <div className="permission-kpi-desc">Departments with at least one connection</div>
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
            <div className="permission-kpi-desc">Total active department-connection mappings</div>
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
              placeholder="Search departments by name..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>

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

        <button
          type="button"
          onClick={() => setShowAssignModal(true)}
          className="btn-assign-permission"
        >
          <Plus className="w-4 h-4" />
          <span>Assign Permission</span>
        </button>
      </div>

      {/* Table Card */}
      <div className="permission-table-card">
        {loading ? (
          <div className="p-8 text-center text-slate-400 text-sm">Loading department permissions...</div>
        ) : filteredDepts.length === 0 ? (
          <div className="p-8 text-center text-slate-500 text-sm">No department permissions match your filter criteria.</div>
        ) : (
          <table className="permission-table">
            <thead>
              <tr>
                <th>Department</th>
                <th>Description</th>
                <th>Assigned Connections</th>
                <th>Members</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredDepts.map((dept) => (
                <tr key={dept.id}>
                  <td>
                    <div className="user-cell">
                      <div className="dept-avatar-circle">
                        {dept.departmentName.charAt(0).toUpperCase()}
                      </div>
                      <span className="user-name-text">{dept.departmentName}</span>
                    </div>
                  </td>
                  <td>
                    <span className="text-xs text-slate-500 max-w-xs block truncate" title={dept.description}>
                      {dept.description || 'No description provided'}
                    </span>
                  </td>
                  <td>
                    <div className="conn-badges-wrapper">
                      {dept.connectionNames.slice(0, 2).map((name, idx) => (
                        <div key={idx} className="conn-wa-badge" title={name}>
                          <MessageSquare className="w-3.5 h-3.5" />
                        </div>
                      ))}
                      {dept.connectionNames.length > 2 && (
                        <span className="conn-more-pill">+{dept.connectionNames.length - 2} more</span>
                      )}
                    </div>
                  </td>
                  <td>
                    {/* No per-member avatar data exists on DepartmentPermission — only a
                        count. A generic icon + real count is honest; two hardcoded stock
                        photos previously stood in as if they were real members. */}
                    <div className="members-stack">
                      <span className="member-count-badge">
                        <Users className="w-3.5 h-3.5" />
                        {dept.memberCount} {dept.memberCount === 1 ? 'Member' : 'Members'}
                      </span>
                    </div>
                  </td>
                  <td>
                    <span
                      onClick={() => handleToggleStatus(dept.id)}
                      className={`status-pill cursor-pointer ${dept.isActive ? 'active' : 'inactive'}`}
                    >
                      <span className="status-dot" />
                      {dept.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td>
                    <div className="action-buttons">
                      <button
                        type="button"
                        onClick={() => toast.success(`Viewing permissions for ${dept.departmentName}`)}
                        className="icon-btn"
                        title="View Details"
                      >
                        <Eye className="w-4 h-4" />
                      </button>
                      <button
                        type="button"
                        onClick={() => setShowAssignModal(true)}
                        className="icon-btn"
                        title="Edit Permission"
                      >
                        <Edit2 className="w-4 h-4" />
                      </button>
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
        type="department"
        connections={connections}
        onClose={() => setShowAssignModal(false)}
        onAssignDepartment={handleAssignDepartment}
      />
    </motion.div>
  )
}
