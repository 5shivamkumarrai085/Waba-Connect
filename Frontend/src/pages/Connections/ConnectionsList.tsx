import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps, cardHoverProps } from '../../utils/motion'
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
  MessageSquare,
  Edit3,
  Trash2
} from 'lucide-react'
import toast from 'react-hot-toast'
import { useConnectionStore } from '../../store/connectionStore'
import { EditConnectionModal } from './EditConnectionModal'
import { Menu, MenuItem } from '../../components/Menu/Menu'
import { Pagination } from '../../components/Pagination/Pagination'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { connectionService } from '../../services/connections/connectionService'
import { getErrorMessage } from '../../utils/errorHelper'
import type { Connection } from '../../types/connection'
import './ConnectionsList.css'

/**
 * Maps the server's status text to its badge class.
 *
 * Three states, not two. "Setup pending" is its own colour because it is its own problem — the
 * account authenticated fine and there is simply nothing to send from, which needs a different
 * action from a disconnected one.
 */
const statusClass = (status?: string): string => {
  if (status === 'Connected') return 'connected'
  if (status === 'Setup pending') return 'pending'
  return 'disconnected'
}
import Can from '../../components/Can/Can'

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
  const [editingConn, setEditingConn] = useState<Connection | null>(null)
  const [activeMenuId, setActiveMenuId] = useState<number | null>(null)
  const [syncingId, setSyncingId] = useState<number | null>(null)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(5)

  const [disconnectTarget, setDisconnectTarget] = useState<Connection | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Connection | null>(null)

  useEffect(() => {
    fetchDashboard()
  }, [fetchDashboard])

  // Escape closes the two confirmation targets. The row menu handles its own outside-click and
  // Escape now that it is the shared Menu — and the listener that used to do it here tested
  // `.closest('.conn-actions-cell')`, which stopped matching the moment the surface was portaled
  // out of that cell.
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      setEditingConn(null)
      setDisconnectTarget(null)
      setDeleteTarget(null)
    }

    document.addEventListener('keydown', handleEscape)
    return () => document.removeEventListener('keydown', handleEscape)
  }, [])

  const filteredConnections = connections.filter((conn: Connection) => {
    const matchesSearch =
      conn.name.toLowerCase().includes(search.toLowerCase()) ||
      (conn.phoneNumber && conn.phoneNumber.includes(search)) ||
      (conn.wabaId && conn.wabaId.includes(search))

    const matchesStatus =
      statusFilter === 'All Status' ||
      (statusFilter === 'Connected' && conn.status === 'Connected') ||
      (statusFilter === 'Setup pending' && conn.status === 'Setup pending') ||
      (statusFilter === 'Disconnected' && !conn.isConnected)

    return matchesSearch && matchesStatus
  })

  // Paged on the client. The connections endpoint returns every row in one response — there are
  // a handful of them, and a WABA account list does not grow the way a contact list does — so
  // slicing here is honest rather than a stand-in for server paging that ought to exist.
  const totalPages = Math.max(1, Math.ceil(filteredConnections.length / pageSize))
  const currentPage = Math.min(page, totalPages)
  const pagedConnections = filteredConnections.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  )

  const handleConnectNew = () => {
    navigate('/connections/new')
  }

  // Opens the full WhatsApp Business Account page for this connection.
  //
  // /connect-waba is not the onboarding wizard: it renders the wizard only while the account
  // is disconnected, and the complete account dashboard — token, permission scopes, webhook
  // URL, phone, health, QR, disconnect — once it is connected. That is the detail view.
  const handleViewConnection = (conn: Connection) => {
    setActiveMenuId(null)
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
      // disconnectConnection does not resolve until the dashboard has been refetched, so the
      // list is already current here. The extra fetchDashboard() that used to follow was both
      // redundant and — now that the fetch is deduped — a no-op.
      await disconnectConnection(conn.id)
      toast.success('Connection status set to disconnected.')
    } catch {
      toast.error('Failed to disconnect connection.')
    }
  }

  const handleReconnect = (id: number) => {
    navigate(`/connect-waba?connectionId=${id}`)
  }

  /**
   * Re-reads a connection's sender numbers from Meta.
   *
   * For the state this page used to hide: authenticated, flagged Connected, but with no number
   * attached — so Chat refused it as "Setup pending" while this table called it healthy. The
   * repair is one API call, not the whole connect wizard.
   */
  const handleSyncNumbers = async (conn: Connection) => {
    setSyncingId(conn.id)
    try {
      const result = await connectionService.syncConnectionNumbers(conn.id)
      // Meta answering with no numbers is a real answer, not a failure — the account genuinely
      // has none yet, and saying "synced" would send the user back to Chat to fail again.
      if (result.count > 0) toast.success(result.message)
      else toast.error(result.message)
      await fetchDashboard()
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not sync sender numbers.'))
    } finally {
      setSyncingId(null)
    }
  }

  const handleEditSave = async (id: number, name: string, description?: string, nickname?: string) => {
    try {
      await updateConnection(id, name, description, nickname)
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
    <motion.div className="conn-page-wrapper" {...pageTransitionProps}>
      {/* Page Header */}
      <div className="conn-page-header">
        <div className="conn-page-title-group">
          <h1>WABA Connections</h1>
          <p className="conn-page-subtitle">Connect and manage multiple WhatsApp Business Accounts.</p>
        </div>
        <Can permission="ConnectAccount.Connect">
          <button type="button" onClick={handleConnectNew} className="btn-connect-waba">
            <Plus className="w-4 h-4" />
            <span>Add Connection</span>
          </button>
        </Can>
      </div>

      {/* 4 KPI Cards Grid */}
      <div className="conn-kpi-grid">
        <motion.div className="conn-kpi-card" {...cardHoverProps}>
          <div className="conn-kpi-icon-wrapper blue">
            <MessageSquare className="w-6 h-6" />
          </div>
          <div className="conn-kpi-content">
            <span className="conn-kpi-label">Total WABA Connections</span>
            <span className="conn-kpi-value">{dashboard?.totalConnections ?? connections.length}</span>
            <span className="conn-kpi-desc">All connections</span>
          </div>
        </motion.div>

        <motion.div className="conn-kpi-card" {...cardHoverProps}>
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
        </motion.div>

        <motion.div className="conn-kpi-card" {...cardHoverProps}>
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
        </motion.div>

        <motion.div className="conn-kpi-card" {...cardHoverProps}>
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
        </motion.div>
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
            <option value="Setup pending">Setup pending</option>
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
                <th className="actions-col">Actions</th>
                <th>Connection Name</th>
                <th>Phone Number</th>
                <th>WABA ID</th>
                <th>Status</th>
                <th>Connected On</th>
              </tr>
            </thead>
            <tbody>
              {filteredConnections.length === 0 ? (
                <tr>
                  <td colSpan={6} className="conn-empty-cell">
                    No connections found. Use "Add Connection" to create your first one.
                  </td>
                </tr>
              ) : (
                pagedConnections.map((conn: Connection, idx: number) => (
                  <tr key={conn.id}>
                    {/* Actions lead the row, as on every other list — reaching the delete or
                        view control should not mean scrolling past five columns first. */}
                    <td className="actions-col">
                      <div className="conn-actions-cell">
                        {conn.isConnected ? (
                          <>
                            <Can permission="ConnectAccount.View">
                              <button
                                type="button"
                                onClick={() => handleViewConnection(conn)}
                                className="btn-action-view"
                              >
                                <Eye className="w-3.5 h-3.5" />
                                View
                              </button>
                            </Can>
                            {/* Only offered where it is the actual repair: a connection that is
                                authenticated but has no number attached. Offering it on a healthy
                                connection would be a button whose only outcome is "nothing
                                changed". */}
                            {!conn.hasPhoneNumber && (
                              <Can permission="ConnectAccount.Connect">
                                <button
                                  type="button"
                                  onClick={() => handleSyncNumbers(conn)}
                                  disabled={syncingId === conn.id}
                                  className="btn-action-connect"
                                >
                                  <RefreshCw
                                    className={`w-3.5 h-3.5 ${syncingId === conn.id ? 'animate-spin' : ''}`}
                                  />
                                  {syncingId === conn.id ? 'Syncing…' : 'Sync Number'}
                                </button>
                              </Can>
                            )}
                            <Can permission="ConnectAccount.Disconnect">
                              <button
                                type="button"
                                onClick={() => handleDisconnectClick(conn)}
                                className="btn-action-disconnect"
                              >
                                <Unlink className="w-3.5 h-3.5" />
                                Disconnect
                              </button>
                            </Can>
                          </>
                        ) : (
                          <Can permission="ConnectAccount.Connect">
                            <button
                              type="button"
                              onClick={() => handleReconnect(conn.id)}
                              className="btn-action-connect"
                            >
                              <LinkIcon className="w-3.5 h-3.5" />
                              Connect
                            </button>
                          </Can>
                        )}

                        {/* The shared Menu, as every other list uses. This was a hand-rolled
                            absolutely-positioned div living inside the table's own
                            `overflow-x: auto` container, so it was clipped by it; Menu portals
                            its surface out and flips when it would leave the viewport. */}
                        <Menu
                          open={activeMenuId === conn.id}
                          onOpenChange={(isOpen) => setActiveMenuId(isOpen ? conn.id : null)}
                          align="start"
                          offset={4}
                          className="contact-actions-dropdown"
                          ariaLabel="Connection actions"
                          trigger={(props) => (
                            <button
                              {...props}
                              type="button"
                              className="btn-action-menu"
                              aria-label="Connection actions"
                            >
                              <MoreVertical className="w-4 h-4" />
                            </button>
                          )}
                        >
                          {/* Hidden, not disabled: unlike the row menus built on fixed-height
                              lists, there is nothing here whose height must stay stable. */}
                          <Can permission="ConnectAccount.Edit">
                            <MenuItem
                              className="contact-actions-item"
                              onSelect={() => setEditingConn(conn)}
                            >
                              <Edit3 className="w-3.5 h-3.5" />
                              Edit Name
                            </MenuItem>
                          </Can>
                          <Can permission="ConnectAccount.Delete">
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              onSelect={() => handleDeleteConnectionClick(conn)}
                            >
                              <Trash2 className="w-3.5 h-3.5" />
                              Delete
                            </MenuItem>
                          </Can>
                        </Menu>
                      </div>
                    </td>

                    {/* Connection Name */}
                    <td>
                      <div className="conn-name-cell">
                        <div className={`conn-avatar-icon c${idx % 6}`}>
                          {getRowIcon(idx)}
                        </div>
                        <div>
                          <div>
                            <span className="conn-name-title">{conn.name}</span>
                            {conn.nickname && (
                              <span className="conn-nickname-badge">{conn.nickname}</span>
                            )}
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
                      {/* The server's own status, not a second derivation of it. This cell said
                          "Connected" for a connection with no sender number while Chat said
                          "Setup pending" about the same row — same instant, two answers. */}
                      <span className={`conn-status-badge ${statusClass(conn.status)}`}>
                        {conn.status || (conn.isConnected ? 'Connected' : 'Disconnected')}
                      </span>
                      {conn.isConnected && !conn.hasPhoneNumber && (
                        <div className="conn-status-hint">No sender number — cannot send or receive</div>
                      )}
                    </td>

                    {/* Connected On */}
                    <td>
                      <div className="conn-date-primary">
                        {conn.connectedOn ? new Date(conn.connectedOn).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }) : '-'}
                      </div>
                      <div className="conn-date-time">
                        {conn.connectedOn ? new Date(conn.connectedOn).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : ''}
                      </div>
                    </td>

                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* The shared pager. What was here reported "Showing 1 to N of N" with a hardcoded page
            number and both arrows permanently disabled — furniture that looked like pagination
            without being any. */}
        <div className="conn-footer-bar">
          <Pagination
            page={currentPage}
            pageSize={pageSize}
            totalCount={filteredConnections.length}
            totalPages={totalPages}
            onPage={setPage}
            onPageSize={(size) => { setPageSize(size); setPage(1) }}
            // Smaller steps than the default ladder. A WABA account list is short — an
            // organisation has a handful of connections, not thousands — so starting at 100
            // would make the control decorative.
            pageSizeOptions={[5, 10, 25, 50]}
          />
        </div>
      </div>

      {/* Edit Connection Modal — always rendered so it can play its exit
          animation; visibility is driven by isOpen, and the component
          latches the last non-null connection itself. */}
      <EditConnectionModal
        isOpen={!!editingConn}
        connection={editingConn}
        onClose={() => setEditingConn(null)}
        onSave={handleEditSave}
      />

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
    </motion.div>
  )
}
