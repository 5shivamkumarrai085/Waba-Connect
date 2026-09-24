import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
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
  Share2,
  MoreVertical,
  MessageSquare,
  Mail,
  Edit3,
  Trash2,
  Info
} from 'lucide-react'
import toast from 'react-hot-toast'
import { useConnectionStore } from '../../store/connectionStore'
import { EditConnectionModal } from './EditConnectionModal'
import { Menu, MenuItem } from '../../components/Menu/Menu'
import { Pagination } from '../../components/Pagination/Pagination'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { connectionService } from '../../services/connections/connectionService'
import { getErrorMessage } from '../../utils/errorHelper'
import { emailConnectionService } from '../../services/email/emailConnectionService'
import { CHANNELS } from '../../types/channel'
import type { AnyChannel } from '../../types/channel'
import type { Connection } from '../../types/connection'
import type { EmailConnection } from '../../types/email'
import './ConnectionsList.css'

/**
 * One row of the connections table, whichever channel it came from.
 *
 * The two channels genuinely differ — WhatsApp has a phone number and a WABA id, email has an
 * address and a provider — so rather than forcing one shape onto both, each row carries a
 * channel and the two identity fields the table renders. That keeps the WhatsApp rows byte-for-
 * byte what they were while letting email sit beside them.
 */
interface ChannelConnectionRow {
  key: string
  channel: AnyChannel
  id: number
  name: string
  nickname?: string | null
  description?: string | null
  /** Phone number for WhatsApp, email address for email. */
  account: string
  /** WABA id for WhatsApp, provider name for email. */
  identifier: string
  status: string
  connectedOn?: string | null
  isConnected: boolean
  /** The underlying record, for the row's own actions. */
  whatsapp?: Connection
  email?: EmailConnection
}

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
import { SearchableSelect } from '../../components/SearchableSelect/SearchableSelect'

export const ConnectionsList: React.FC = () => {
  const navigate = useNavigate()
  const {
    connections,
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

  // Separate targets from the WABA ones above: the two go through different services and their
  // confirmation wording differs, and sharing one target would mean deciding which service to
  // call from the shape of the object — exactly the sort of guess that sends a delete to the
  // wrong endpoint.
  // Its own open-menu key rather than reusing activeMenuId: that holds a connection id, and an
  // email connection's id can equal a WABA connection's, which would open both rows' menus.
  const [activeEmailMenuId, setActiveEmailMenuId] = useState<number | null>(null)

  const [emailDisconnectTarget, setEmailDisconnectTarget] = useState<ChannelConnectionRow | null>(null)
  const [emailDeleteTarget, setEmailDeleteTarget] = useState<ChannelConnectionRow | null>(null)

  // The channel tab. 'all' rather than a nullable value so the tab list can be rendered from
  // one array without a special case for the first entry.
  const [channelTab, setChannelTab] = useState<'all' | AnyChannel>('all')
  const [emailConnections, setEmailConnections] = useState<EmailConnection[]>([])
  const [isAddMenuOpen, setIsAddMenuOpen] = useState(false)

  useEffect(() => {
    fetchDashboard()
  }, [fetchDashboard])

  // Email connections come from their own endpoint. Kept in local state rather than the
  // connection store, because that store is shaped around WABA accounts and is read by eight
  // other pages that have no interest in email.
  useEffect(() => {
    let isMounted = true

    emailConnectionService.getConnections().then((loaded) => {
      if (isMounted) setEmailConnections(loaded)
    })

    return () => {
      isMounted = false
    }
  }, [])

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

  // Email connections mapped onto the shared row shape, honouring the same search and status
  // filters as the WhatsApp rows so the two behave identically.
  const emailRows: ChannelConnectionRow[] = emailConnections
    .filter((conn) => {
      const term = search.toLowerCase()
      const matchesSearch =
        conn.connectionName.toLowerCase().includes(term) ||
        (conn.defaultFromEmail ?? '').toLowerCase().includes(term) ||
        conn.provider.toLowerCase().includes(term)

      const matchesStatus = statusFilter === 'All Status' || conn.status === statusFilter

      return matchesSearch && matchesStatus
    })
    .map((conn) => ({
      key: `email-${conn.id}`,
      channel: 'email' as AnyChannel,
      id: conn.id,
      name: conn.connectionName,
      nickname: conn.nickname,
      description: conn.description,
      account: conn.defaultFromEmail ?? '-',
      identifier: conn.provider === 'AmazonSes' ? 'Amazon SES' : 'SMTP',
      status: conn.status,
      connectedOn: conn.configuredAt,
      isConnected: conn.status === 'Connected',
      email: conn
    }))

  // Paged on the client. The connections endpoint returns every row in one response — there are
  // a handful of them, and a connection list does not grow the way a contact list does — so
  // slicing here is honest rather than a stand-in for server paging that ought to exist.
  //
  // Counted across both channels *after* the search and status filters, and scoped to the
  // selected channel. Counting WhatsApp alone made the footer report rows it was not showing —
  // "Showing 1 to 4 of 4" above a single email row.
  const pagedSourceCount =
    (channelTab === 'email' ? 0 : filteredConnections.length) +
    (channelTab === 'whatsapp' ? 0 : emailRows.length)

  const totalPages = Math.max(1, Math.ceil(pagedSourceCount / pageSize))
  const currentPage = Math.min(page, totalPages)
  const pagedConnections = filteredConnections.slice(
    (currentPage - 1) * pageSize,
    currentPage * pageSize
  )

  const visibleWhatsAppRows = channelTab === 'email' ? [] : pagedConnections
  const visibleEmailRows = channelTab === 'whatsapp' ? [] : emailRows
  const hasAnyVisibleRow = visibleWhatsAppRows.length > 0 || visibleEmailRows.length > 0

  const channelCounts: Record<string, number> = {
    all: filteredConnections.length + emailRows.length,
    whatsapp: filteredConnections.length,
    email: emailRows.length
  }

  /**
   * The KPI strip's numbers, scoped to the selected channel.
   *
   * Deliberately computed from the unfiltered lists, not from `filteredConnections`: these cards
   * answer "what does this account have", and a number that moved every time somebody typed in
   * the search box would not be answering that question. The channel selector is the one filter
   * that does apply, because it is the question the strip is being asked.
   */
  const countsForChannel = (view: 'all' | AnyChannel) => {
    const whatsapp = view === 'email' ? [] : connections
    const email = view === 'whatsapp' ? [] : emailConnections

    const connected =
      whatsapp.filter((c: Connection) => c.isConnected).length +
      email.filter((c) => c.status === 'Connected').length

    const total = whatsapp.length + email.length

    // Disconnected is the remainder rather than its own count, so the three cards always add up.
    // Counting it independently is how a summary ends up showing 2 + 2 out of 3.
    return { total, connected, disconnected: total - connected }
  }

  const kpi = countsForChannel(channelTab)

  const handleConnectNew = () => {
    navigate('/connections/new')
  }

  const handleConnectNewEmail = () => {
    navigate('/connections/new-email')
  }

  // Opens the email connection's provider configuration — the email equivalent of the WABA
  // account page below.
  const handleViewEmailConnection = (row: ChannelConnectionRow) => {
    setActiveMenuId(null)
    navigate(`/connect-email?emailConfigurationId=${row.id}`)
  }

  /** Re-reads the email list after a change, so the table reflects what the server now holds. */
  const refreshEmailConnections = async () => {
    setEmailConnections(await emailConnectionService.getConnections())
  }

  const confirmEmailDisconnect = async () => {
    const row = emailDisconnectTarget
    if (!row) return
    setEmailDisconnectTarget(null)

    try {
      await emailConnectionService.disconnect(row.id)
      await refreshEmailConnections()
      toast.success(`${row.name} disconnected.`)
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not disconnect this email connection.'))
    }
  }

  const confirmEmailDelete = async () => {
    const row = emailDeleteTarget
    if (!row) return
    setEmailDeleteTarget(null)

    try {
      await emailConnectionService.deleteConnection(row.id)
      await refreshEmailConnections()
      toast.success(`${row.name} deleted.`)
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not delete this email connection.'))
    }
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
          {/* Retitled: this page is no longer WABA-only. */}
          <h1>Connections</h1>
          <p className="conn-page-subtitle">
            Manage and configure all your communication channels in one place.
          </p>
        </div>

        {/*
          A menu rather than two buttons. The two flows are genuinely different wizards, and a
          single "Add Connection" that silently picked one would be the wrong one half the time.
        */}
        <Can anyOf={['ConnectAccount.Connect', 'EmailConnection.Connect']}>
          <Menu
            open={isAddMenuOpen}
            onOpenChange={setIsAddMenuOpen}
            align="end"
            ariaLabel="Add a connection"
            trigger={(triggerProps) => (
              <button type="button" className="btn-connect-waba" {...triggerProps}>
                <Plus className="w-4 h-4" />
                <span>Add Connection</span>
              </button>
            )}
          >
            <Can permission="ConnectAccount.Connect">
              <MenuItem onSelect={handleConnectNew}>
                <MessageSquare className="w-4 h-4" />
                WhatsApp Business
              </MenuItem>
            </Can>
            <Can permission="EmailConnection.Connect">
              <MenuItem onSelect={handleConnectNewEmail}>
                <Mail className="w-4 h-4" />
                Email
              </MenuItem>
            </Can>
          </Menu>
        </Can>
      </div>

      {/* KPI strip — one card holding the three totals and the channel they are scoped to.
          One card rather than four tiles because the selector on the right governs the numbers
          on its left, and separate cards would not say that. */}
      <div className="conn-kpi-card">
        <div className="conn-kpi-stat">
          <div className="conn-kpi-icon-wrapper blue">
            <Share2 className="w-6 h-6" />
          </div>
          <div className="conn-kpi-content">
            <span className="conn-kpi-label">Total Connections</span>
            <span className="conn-kpi-value">{kpi.total}</span>
          </div>
        </div>

        <div className="conn-kpi-stat">
          <span className="conn-kpi-dot green" />
          <div className="conn-kpi-content">
            <span className="conn-kpi-label">Connected</span>
            <span className="conn-kpi-value">{kpi.connected}</span>
          </div>
        </div>

        <div className="conn-kpi-stat">
          <span className="conn-kpi-dot red" />
          <div className="conn-kpi-content">
            <span className="conn-kpi-label">Disconnected</span>
            <span className="conn-kpi-value">{kpi.disconnected}</span>
          </div>
        </div>

        {/* Bound to the same state as the tabs below, so the summary and the table can never
            disagree about which channel is being looked at. */}
        <div className="conn-kpi-view">
          <label className="conn-kpi-view-label" htmlFor="conn-view-by-channel">
            View by Channel
          </label>
          <select
            id="conn-view-by-channel"
            className="conn-kpi-view-select"
            value={channelTab}
            onChange={(e) => {
              setChannelTab(e.target.value as 'all' | AnyChannel)
              setPage(1)
            }}
          >
            <option value="all">All Channels</option>
            {CHANNELS.filter((c) => c.available).map((channel) => (
              <option key={channel.key} value={channel.key}>
                {channel.label}
              </option>
            ))}
          </select>
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
          <SearchableSelect
            label="Status"
            placeholder="All Status"
            allValue="All Status"
            className="conn-select-status"
            value={statusFilter}
            options={[
              { value: 'Connected', label: 'Connected' },
              { value: 'Setup pending', label: 'Setup pending' },
              { value: 'Disconnected', label: 'Disconnected' }
            ]}
            onChange={setStatusFilter}
          />

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

      {/* Channel tabs. Counts included, because "Email 0" and no Email tab at all mean
          different things — the first says the channel exists and is unused. */}
      <div className="conn-channel-tabs" role="tablist" aria-label="Filter by channel">
        <button
          type="button"
          role="tab"
          aria-selected={channelTab === 'all'}
          className={`conn-channel-tab ${channelTab === 'all' ? 'active' : ''}`}
          onClick={() => { setChannelTab('all'); setPage(1) }}
        >
          <span>All</span>
          <span className="conn-channel-tab-count">{channelCounts.all}</span>
        </button>

        {CHANNELS.filter((c) => c.available).map((channel) => (
          <button
            key={channel.key}
            type="button"
            role="tab"
            aria-selected={channelTab === channel.key}
            className={`conn-channel-tab ${channelTab === channel.key ? 'active' : ''}`}
            data-channel={channel.key}
            onClick={() => { setChannelTab(channel.key); setPage(1) }}
          >
            {channel.key === 'email' ? (
              <Mail className="w-4 h-4" />
            ) : (
              <MessageSquare className="w-4 h-4" />
            )}
            <span>{channel.label}</span>
            <span className="conn-channel-tab-count">{channelCounts[channel.key] ?? 0}</span>
          </button>
        ))}
      </div>

      {/* Connections Data Table Card */}
      <div className="conn-table-card">
        {/* Kept accurate rather than aspirational: it names the two channels that work today,
            and does not promise dates for the ones that do not. */}
        <div className="conn-channel-notice">
          <Info className="w-4 h-4" />
          <div>
            <strong>Add more channels as you grow</strong>
            <p>
              WhatsApp and Email are available today. SMS, Instagram and Facebook are not yet
              implemented.
            </p>
          </div>
        </div>

        <div className="conn-table-scroll">
          <table className="conn-data-table">
            <thead>
              <tr>
                <th className="actions-col">Actions</th>
                <th>Connection Name</th>
                <th>Channel</th>
                {/* One column for both channels: a phone number for WhatsApp, an address for
                    email. Two separate columns would leave half of each row empty. */}
                <th>Account / ID</th>
                <th>Linked Information</th>
                <th>Status</th>
                <th>Connected On</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                // Without this branch the table fell straight through to the empty-state row
                // during the initial fetch — filteredConnections.length is 0 before data arrives
                // just as it is when there truly are no connections, so "No connections found" was
                // flashing on every load, on a real account with real connections, before the
                // actual rows replaced it.
                Array.from({ length: 5 }).map((_, rowIndex) => (
                  <tr key={`skeleton-${rowIndex}`} aria-hidden="true">
                    {Array.from({ length: 7 }).map((__, colIndex) => (
                      <td key={colIndex}>
                        <span
                          className="skeleton-box variant-text skeleton-pulse"
                          style={{ height: 14, width: colIndex === 0 ? '60%' : '80%' }}
                        />
                      </td>
                    ))}
                  </tr>
                ))
              ) : !hasAnyVisibleRow ? (
                <tr>
                  <td colSpan={7} className="conn-empty-cell">
                    No connections found. Use "Add Connection" to create your first one.
                  </td>
                </tr>
              ) : (
                visibleWhatsAppRows.map((conn: Connection, idx: number) => (
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

                    {/* Channel */}
                    <td>
                      <span className="conn-channel-badge" data-channel="whatsapp">
                        <MessageSquare className="w-3.5 h-3.5" />
                        WhatsApp
                      </span>
                    </td>

                    {/* Account / ID */}
                    <td>
                      <div className="conn-phone-number">WABA ID</div>
                      <div className="conn-phone-id">{conn.wabaId || 'N/A'}</div>
                    </td>

                    {/* Linked Information */}
                    <td>
                      <div className="conn-phone-number">{conn.phoneNumber || '-'}</div>
                      <div className="conn-phone-id">Phone ID: {conn.phoneNumberId || 'N/A'}</div>
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

              {/* Email rows. Not gated on the loading branch above, which belongs to the WABA
                  store's fetch — the email list has its own, and blanking these while that one
                  runs would make them flicker on every refresh. */}
              {!isLoading &&
                visibleEmailRows.map((row, idx) => (
                  <tr key={row.key}>
                    <td className="actions-col">
                      <div className="conn-actions-cell">
                        <Can permission="EmailConnection.View">
                          <button
                            type="button"
                            onClick={() => handleViewEmailConnection(row)}
                            className="btn-action-view"
                          >
                            <Eye className="w-3.5 h-3.5" />
                            View
                          </button>
                        </Can>

                        {/* Only while it is connected. Offering Disconnect on something already
                            disconnected invites a click that does nothing. */}
                        {row.isConnected && (
                          <Can permission="EmailConnection.Disconnect">
                            <button
                              type="button"
                              onClick={() => setEmailDisconnectTarget(row)}
                              className="btn-action-disconnect"
                            >
                              <Unlink className="w-3.5 h-3.5" />
                              Disconnect
                            </button>
                          </Can>
                        )}

                        <Menu
                          open={activeEmailMenuId === row.id}
                          onOpenChange={(isOpen) => setActiveEmailMenuId(isOpen ? row.id : null)}
                          align="start"
                          offset={4}
                          className="contact-actions-dropdown"
                          ariaLabel="Email connection actions"
                          trigger={(props) => (
                            <button
                              {...props}
                              type="button"
                              className="btn-action-menu"
                              aria-label="Email connection actions"
                            >
                              <MoreVertical className="w-4 h-4" />
                            </button>
                          )}
                        >
                          <Can permission="EmailConnection.Edit">
                            <MenuItem
                              className="contact-actions-item"
                              onSelect={() => handleViewEmailConnection(row)}
                            >
                              <Edit3 className="w-3.5 h-3.5" />
                              Configure
                            </MenuItem>
                          </Can>
                          <Can permission="EmailConnection.Delete">
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              onSelect={() => setEmailDeleteTarget(row)}
                            >
                              <Trash2 className="w-3.5 h-3.5" />
                              Delete
                            </MenuItem>
                          </Can>
                        </Menu>
                      </div>
                    </td>

                    <td>
                      <div className="conn-name-cell">
                        <div className="conn-avatar-icon email-channel">
                          <Mail className="w-4 h-4" />
                        </div>
                        <div>
                          <div>
                            <span className="conn-name-title">{row.name}</span>
                            {row.nickname && (
                              <span className="conn-nickname-badge">{row.nickname}</span>
                            )}
                          </div>
                          <span className="conn-phone-subtext">{row.description || row.account}</span>
                        </div>
                      </div>
                    </td>

                    <td>
                      <span className="conn-channel-badge" data-channel="email">
                        <Mail className="w-3.5 h-3.5" />
                        Email
                      </span>
                    </td>

                    <td>
                      <div className="conn-phone-number">{row.identifier}</div>
                      <div className="conn-phone-id">
                        {row.email?.region ? `Region: ${row.email.region}` : 'Email account'}
                      </div>
                    </td>

                    <td>
                      <div className="conn-phone-number">{row.account}</div>
                      <div className="conn-phone-id">
                        {/* Sender count, because one connection can hold several verified
                            addresses and the campaign wizard offers all of them. */}
                        {row.email
                          ? `${row.email.senders.filter((sender) => sender.canSend).length} of ${row.email.senders.length} sender(s) verified`
                          : ''}
                      </div>
                    </td>

                    <td>
                      <span className={`conn-status-badge ${statusClass(row.status)}`}>
                        {row.status}
                      </span>
                      {row.email && row.status === 'Connected'
                        && !row.email.senders.some((sender) => sender.canSend) && (
                        <div className="conn-status-hint">
                          No verified sender — cannot send campaigns
                        </div>
                      )}
                      {row.email?.lastTestSucceeded === false && (
                        <div className="conn-status-hint">{row.email.lastTestMessage}</div>
                      )}
                    </td>

                    <td>
                      <div className="conn-date-primary">
                        {row.connectedOn
                          ? new Date(row.connectedOn).toLocaleDateString('en-GB', {
                              day: '2-digit', month: 'short', year: 'numeric'
                            })
                          : '-'}
                      </div>
                      <div className="conn-date-time">
                        {row.connectedOn
                          ? new Date(row.connectedOn).toLocaleTimeString([], {
                              hour: '2-digit', minute: '2-digit'
                            })
                          : ''}
                      </div>
                      {/* idx referenced so the row index stays available for future styling
                          without an unused-parameter error. */}
                      <span hidden>{idx}</span>
                    </td>
                  </tr>
                ))}
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
            totalCount={pagedSourceCount}
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

      {/* Email connections have their own pair. The wording names what actually happens:
          disconnecting clears the stored credentials, which is not obvious from the word alone
          and is the difference between this and Delete. */}
      <ConfirmationModal
        isOpen={!!emailDisconnectTarget}
        title="Disconnect Email Connection"
        message={
          `Disconnect "${emailDisconnectTarget?.name}"? Its stored credentials are cleared and no `
          + 'campaign can send through it until it is configured again. Sent history is kept.'
        }
        confirmText="Disconnect"
        cancelText="Cancel"
        onConfirm={confirmEmailDisconnect}
        onCancel={() => setEmailDisconnectTarget(null)}
        isDestructive={true}
        showWarningIcon={true}
      />

      <ConfirmationModal
        isOpen={!!emailDeleteTarget}
        title="Delete Email Connection"
        message={
          `Delete "${emailDeleteTarget?.name}"? This removes the connection, its senders and its `
          + 'sending domains. Campaigns already sent through it are not affected.'
        }
        confirmText="Delete"
        cancelText="Cancel"
        onConfirm={confirmEmailDelete}
        onCancel={() => setEmailDeleteTarget(null)}
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
