import React, { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Plus,
  Search,
  RefreshCw,
  Eye,
  Unlink,
  Link as LinkIcon,
  Phone,
  Headphones,
  Briefcase,
  Building2,
  PhoneCall,
  Store,
  CheckCircle2,
  Clock,
  Share2,
  MoreVertical,
  ChevronLeft,
  ChevronRight,
  MessageSquare,
  Edit3,
  Trash2
} from 'lucide-react'
import toast from 'react-hot-toast'
import { useConnectionStore } from '../../store/connectionStore'
import { EditConnectionModal } from './EditConnectionModal'
import { ConnectionDetail } from './ConnectionDetail'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import type { Connection } from '../../types/connection'
import './ConnectionsList.css'

export const ConnectionsList: React.FC = () => {
  const navigate = useNavigate()
  const {
    connections,
    dashboard,
    isLoading,
    fetchDashboard,
    disconnectConnection,
    updateConnection,
    deleteConnection
  } = useConnectionStore()

  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState('All Status')
  const [selectedConn, setSelectedConn] = useState<Connection | null>(null)
  const [editingConn, setEditingConn] = useState<Connection | null>(null)
  const [activeMenuId, setActiveMenuId] = useState<number | null>(null)

  const [disconnectTarget, setDisconnectTarget] = useState<Connection | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Connection | null>(null)

  useEffect(() => {
    fetchDashboard()
  }, [fetchDashboard])

  // Close dropdown menus on outside click or Escape
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setActiveMenuId(null)
        setEditingConn(null)
        setSelectedConn(null)
        setDisconnectTarget(null)
        setDeleteTarget(null)
      }
    }

    const handleClickOutside = (e: MouseEvent) => {
      const target = e.target as HTMLElement
      if (activeMenuId !== null && !target.closest('.conn-actions-cell')) {
        setActiveMenuId(null)
      }
    }

    document.addEventListener('keydown', handleEscape)
    document.addEventListener('mousedown', handleClickOutside)
    return () => {
      document.removeEventListener('keydown', handleEscape)
      document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [activeMenuId])

  const filteredConnections = connections.filter((conn: Connection) => {
    const matchesSearch =
      conn.name.toLowerCase().includes(search.toLowerCase()) ||
      (conn.phoneNumber && conn.phoneNumber.includes(search)) ||
      (conn.wabaId && conn.wabaId.includes(search))

    const matchesStatus =
      statusFilter === 'All Status' ||
      (statusFilter === 'Connected' && conn.isConnected) ||
      (statusFilter === 'Disconnected' && !conn.isConnected)

    return matchesSearch && matchesStatus
  })

  const handleConnectNew = () => {
    navigate('/connections/new')
  }

  const handleViewConnection = (conn: Connection) => {
    navigate(`/connect-waba?connectionId=${conn.id}`)
  }

  const handleDisconnectClick = (conn: Connection) => {
    setActiveMenuId(null)
    setDisconnectTarget(conn)
  }

  const confirmDisconnect = async () => {
    if (!disconnectTarget) return
    const conn = disconnectTarget
    setDisconnectTarget(null)
    try {
      await disconnectConnection(conn.id)
      toast.success('Connection status set to disconnected.')
      fetchDashboard()
    } catch {
      toast.error('Failed to disconnect connection.')
    }
  }

  const handleReconnect = (id: number) => {
    navigate(`/connect-waba?connectionId=${id}`)
  }

  const handleEditSave = async (id: number, name: string, description?: string) => {
    try {
      await updateConnection(id, name, description)
      toast.success('Connection updated successfully!')
      setEditingConn(null)
    } catch {
      toast.error('Failed to update connection.')
    }
  }

  const handleDeleteConnectionClick = (conn: Connection) => {
    setActiveMenuId(null)
    setDeleteTarget(conn)
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    const conn = deleteTarget
    setDeleteTarget(null)
    try {
      await deleteConnection(conn.id)
      toast.success(`Connection "${conn.name}" deleted (marked inactive).`)
      fetchDashboard()
    } catch {
      toast.error('Failed to delete connection.')
    }
  }

  const getRowIcon = (index: number) => {
    const icons = [Phone, Headphones, Briefcase, Building2, PhoneCall, Store]
    const IconComp = icons[index % icons.length]
    return <IconComp className="w-5 h-5" />
  }

  return (
    <div className="conn-page-wrapper fade-in">
      {/* Breadcrumb */}
      <div className="conn-page-breadcrumb">
        <span>Admin</span>
        <span>&gt;</span>
        <span className="conn-page-breadcrumb-active">WABA Connections</span>
      </div>

      {/* Page Header */}
      <div className="conn-page-header">
        <div className="conn-page-title-group">
          <h1>WABA Connections</h1>
          <p className="conn-page-subtitle">Connect and manage multiple WhatsApp Business Accounts.</p>
        </div>
        <button type="button" onClick={handleConnectNew} className="btn-connect-waba">
          <Plus className="w-4 h-4" />
          <span>Connect New WABA</span>
        </button>
      </div>

      {/* 4 KPI Cards Grid */}
      <div className="conn-kpi-grid">
        <div className="conn-kpi-card">
          <div className="conn-kpi-icon-wrapper blue">
            <MessageSquare className="w-6 h-6" />
          </div>
          <div className="conn-kpi-content">
            <span className="conn-kpi-label">Total WABA Connections</span>
            <span className="conn-kpi-value">{dashboard?.totalConnections ?? connections.length}</span>
            <span className="conn-kpi-desc">All connections</span>
          </div>
        </div>

        <div className="conn-kpi-card">
          <div className="conn-kpi-icon-wrapper green">
            <CheckCircle2 className="w-6 h-6" />
          </div>
          <div className="conn-kpi-content">
            <span className="conn-kpi-label">Connected</span>
            <span className="conn-kpi-value">
              {dashboard?.connectedCount ?? connections.filter((c: Connection) => c.isConnected).length}
            </span>
            <span className="conn-kpi-desc green">Active and ready</span>
          </div>
        </div>

        <div className="conn-kpi-card">
          <div className="conn-kpi-icon-wrapper amber">
            <Clock className="w-6 h-6" />
          </div>
          <div className="conn-kpi-content">
            <span className="conn-kpi-label">Disconnected</span>
            <span className="conn-kpi-value">
              {dashboard?.disconnectedCount ?? connections.filter((c: Connection) => !c.isConnected).length}
            </span>
            <span className="conn-kpi-desc amber">Need attention</span>
          </div>
        </div>

        <div className="conn-kpi-card">
          <div className="conn-kpi-icon-wrapper purple">
            <Share2 className="w-6 h-6" />
          </div>
          <div className="conn-kpi-content">
            <span className="conn-kpi-label">Total Connected Numbers</span>
            <span className="conn-kpi-value">
              {dashboard?.totalConnectedNumbers ?? connections.filter((c: Connection) => c.phoneNumber).length}
            </span>
            <span className="conn-kpi-desc">Phone numbers</span>
          </div>
        </div>
      </div>

      {/* Filter and Actions Card */}
      <div className="conn-filter-card">
        <div className="conn-search-wrapper">
          <Search className="conn-search-icon" />
          <input
            type="text"
            className="conn-search-input"
            placeholder="Search connections or phone numbers..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>

        <div className="conn-filter-controls">
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="conn-select-status"
          >
            <option value="All Status">All Status</option>
            <option value="Connected">Connected</option>
            <option value="Disconnected">Disconnected</option>
          </select>

          <button
            type="button"
            onClick={() => fetchDashboard()}
            disabled={isLoading}
            className="btn-refresh-conn"
          >
            <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
            <span>Refresh</span>
          </button>
        </div>
      </div>

      {/* Connections Data Table Card */}
      <div className="conn-table-card">
        <div className="conn-table-scroll">
          <table className="conn-data-table">
            <thead>
              <tr>
                <th>Connection Name</th>
                <th>Phone Number</th>
                <th>WABA ID</th>
                <th>Status</th>
                <th>Connected On</th>
                <th style={{ textAlign: 'right' }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredConnections.length === 0 ? (
                <tr>
                  <td colSpan={6} style={{ textAlign: 'center', padding: '3rem 1rem', color: '#94a3b8' }}>
                    No connections found. Click "+ Connect New WABA" to create your first connection.
                  </td>
                </tr>
              ) : (
                filteredConnections.map((conn: Connection, idx: number) => (
                  <tr key={conn.id}>
                    {/* Connection Name */}
                    <td>
                      <div className="conn-name-cell">
                        <div className={`conn-avatar-icon c${idx % 6}`}>
                          {getRowIcon(idx)}
                        </div>
                        <div>
                          <div>
                            <span className="conn-name-title">{conn.name}</span>
                            {idx === 0 && <span className="conn-primary-badge">Primary</span>}
                          </div>
                          <span className="conn-phone-subtext">{conn.phoneNumber || 'Unassigned'}</span>
                        </div>
                      </div>
                    </td>

                    {/* Phone Number */}
                    <td>
                      <div className="conn-phone-number">{conn.phoneNumber || '-'}</div>
                      <div className="conn-phone-id">Phone ID: {conn.phoneNumberId || 'N/A'}</div>
                    </td>

                    {/* WABA ID */}
                    <td>
                      <span className="conn-waba-id">{conn.wabaId || 'N/A'}</span>
                    </td>

                    {/* Status */}
                    <td>
                      <span className={`conn-status-badge ${conn.isConnected ? 'connected' : 'disconnected'}`}>
                        {conn.isConnected ? 'Connected' : 'Disconnected'}
                      </span>
                    </td>

                    {/* Connected On */}
                    <td>
                      <div style={{ fontSize: '0.8125rem', color: '#334155', fontWeight: 500 }}>
                        {conn.connectedOn ? new Date(conn.connectedOn).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }) : '-'}
                      </div>
                      <div style={{ fontSize: '0.75rem', color: '#94a3b8' }}>
                        {conn.connectedOn ? new Date(conn.connectedOn).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : ''}
                      </div>
                    </td>

                    {/* Actions */}
                    <td>
                      <div className="conn-actions-cell" style={{ position: 'relative' }}>
                        {conn.isConnected ? (
                          <>
                            <button
                              type="button"
                              onClick={() => handleViewConnection(conn)}
                              className="btn-action-view"
                            >
                              <Eye className="w-3.5 h-3.5" />
                              View
                            </button>
                            <button
                              type="button"
                              onClick={() => handleDisconnectClick(conn)}
                              className="btn-action-disconnect"
                            >
                              <Unlink className="w-3.5 h-3.5" />
                              Disconnect
                            </button>
                          </>
                        ) : (
                          <button
                            type="button"
                            onClick={() => handleReconnect(conn.id)}
                            className="btn-action-connect"
                          >
                            <LinkIcon className="w-3.5 h-3.5" />
                            Connect
                          </button>
                        )}


                        <button
                          type="button"
                          onClick={() => setActiveMenuId(activeMenuId === conn.id ? null : conn.id)}
                          className="btn-action-menu"
                        >
                          <MoreVertical className="w-4 h-4" />
                        </button>

                        {/* Three-Dot Options Dropdown */}
                        {activeMenuId === conn.id && (
                          <div className="conn-dropdown-menu">
                            <button
                              type="button"
                              onClick={() => {
                                setActiveMenuId(null)
                                setEditingConn(conn)
                              }}
                            >
                              <Edit3 className="w-3.5 h-3.5" />
                              Edit Name
                            </button>
                            <button
                              type="button"
                              onClick={() => handleDeleteConnectionClick(conn)}
                              className="delete-btn"
                            >
                              <Trash2 className="w-3.5 h-3.5" />
                              Delete
                            </button>
                          </div>
                        )}
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Footer Pagination */}
        <div className="conn-footer-bar">
          <span>Showing 1 to {filteredConnections.length} of {filteredConnections.length} connections</span>
          <div className="conn-pagination">
            <button type="button" disabled className="conn-page-btn">
              <ChevronLeft className="w-4 h-4" />
            </button>
            <span className="conn-page-btn active">1</span>
            <button type="button" disabled className="conn-page-btn">
              <ChevronRight className="w-4 h-4" />
            </button>
          </div>
        </div>
      </div>

      {/* Edit Connection Modal */}
      {editingConn && (
        <EditConnectionModal
          connection={editingConn}
          onClose={() => setEditingConn(null)}
          onSave={handleEditSave}
        />
      )}

      {/* Connection Detail Modal */}
      {selectedConn && (
        <ConnectionDetail
          connection={selectedConn}
          onClose={() => setSelectedConn(null)}
        />
      )}

      {/* Soft Disconnect Confirmation Modal */}
      <ConfirmationModal
        isOpen={!!disconnectTarget}
        title="Disconnect Connection"
        message={`Are you sure you want to soft-disconnect WABA Connection "${disconnectTarget?.name}"?`}
        confirmText="Disconnect"
        cancelText="Cancel"
        onConfirm={confirmDisconnect}
        onCancel={() => setDisconnectTarget(null)}
        isDestructive={true}
        showWarningIcon={true}
      />

      {/* Delete Connection Confirmation Modal */}
      <ConfirmationModal
        isOpen={!!deleteTarget}
        title="Delete Connection"
        message={`Are you sure you want to delete "${deleteTarget?.name}"?`}
        confirmText="Delete"
        cancelText="Cancel"
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
        isDestructive={true}
        showWarningIcon={true}
      />
    </div>
  )
}
