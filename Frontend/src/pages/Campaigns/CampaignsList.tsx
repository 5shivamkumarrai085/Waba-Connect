import React, { useEffect, useState } from 'react'
import { campaignService } from '../../services/campaigns/campaignService'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate } from 'react-router-dom'
import { useCampaignStore } from '../../store/campaignStore'
import { formatRelativeTime } from '../../utils/dateHelper'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { ColumnSelector } from '../../components/ColumnSelector/ColumnSelector'
import { Plus, RefreshCw, Filter, ChevronLeft, ChevronRight, MoreVertical, ShieldCheck } from 'lucide-react'
import { Menu, MenuItem } from '../../components/Menu/Menu'
import toast from 'react-hot-toast'
import { Skeleton } from '../../components/Skeleton'
import './CampaignsList.css'
import { templateService } from '../../services/templates/templateService'
import { contactService } from '../../services/contacts/contactService'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import Can from '../../components/Can/Can'
import { SearchableSelect } from '../../components/SearchableSelect/SearchableSelect'
import { useCampaignEvents } from '../../hooks/useCampaignEvents'

export const CampaignsList: React.FC = () => {
  const navigate = useNavigate()
  // Subscribe to real-time campaign updates globally
  useCampaignEvents()

  const {
    campaigns,
    isLoading,
    searchQuery,
    templateFilter,
    relationTypeFilter,
    statusFilter,
    currentPage,
    pageSize,
    totalCount,
    sortKey,
    sortDescending,
    createdFrom,
    createdTo,

    setSearchQuery,
    setTemplateFilter,
    setStatusFilter,
    setRelationTypeFilter,
    setCurrentPage,
    setPageSize,
    setSort,
    setCreatedRange,

    loadCampaigns,
    deleteCampaign
  } = useCampaignStore()

  const [showFilters, setShowFilters] = useState(false)
  const [visibleColumns, setVisibleColumns] = useState<Record<string, boolean>>({
    id: true,
    name: true,
    source: true,
    template: true,
    relation: true,
    total: true,
    delivered: true,
    read: true,
    createdAt: true
  })

  // Row actions dropdown — which row's three-dot menu is open
  const [openActionsMenuId, setOpenActionsMenuId] = useState<number | null>(null)

  const [templates, setTemplates] = useState<any[]>([])
  const [relationTypes, setRelationTypes] = useState<any[]>([])
  const [deleteTarget, setDeleteTarget] = useState<{ id: number; name: string } | null>(null)

  // Filtering, sorting and paging run on the server; the page re-queries whenever any of them
  // changes. Typing in the search box is debounced so each keystroke is not a request.
  useEffect(() => {
    const timer = setTimeout(() => void loadCampaigns(), searchQuery ? 300 : 0)
    return () => clearTimeout(timer)
  }, [loadCampaigns, searchQuery, templateFilter, relationTypeFilter, statusFilter, createdFrom, createdTo, sortKey, sortDescending, currentPage, pageSize])

  // Maker-checker: how many campaigns wait for a second user's approval. Re-read whenever the
  // list is, so approving one elsewhere is reflected on the next refresh.
  const [pendingApprovals, setPendingApprovals] = useState(0)
  useEffect(() => {
    campaignService.getPendingApprovalCount().then(setPendingApprovals).catch(() => setPendingApprovals(0))
  }, [campaigns])

  useEffect(() => {
    const fetchMetadata = async () => {
      try {
        const [templatesData, relationTypesData] = await Promise.all([
          templateService.getTemplates(),
          contactService.getContactTypes()
        ])
        setTemplates(templatesData || [])
        setRelationTypes(relationTypesData || [])
      } catch (err) {
        console.error('Failed to fetch filter metadata:', err)
      }
    }
    fetchMetadata()
  }, [])

  // Server-side paging: `campaigns` is already the requested page.
  const totalResults = totalCount
  const startIndex = (currentPage - 1) * pageSize
  const endIndex = Math.min(totalResults, startIndex + campaigns.length)
  const paginatedCampaigns = campaigns
  const totalPages = Math.max(1, Math.ceil(totalResults / pageSize))
  const handleSort = (key: string) => setSort(key)
  const sortDirection = sortDescending ? 'desc' : 'asc'

  const handleRefresh = async () => {
    await loadCampaigns()
    toast.success('Campaigns refreshed successfully!')
  }

  const handleDelete = (id: number, name: string) => {
    setDeleteTarget({ id, name })
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    const { id } = deleteTarget
    setDeleteTarget(null)
    try {
      await deleteCampaign(id)
      toast.success('Campaign deleted successfully!')
    } catch (err: any) {
      toast.error('Failed to delete campaign.')
    }
  }

  const columnHeaders = [
    { key: 'id', label: 'ID' },
    { key: 'name', label: 'Campaign Name' },
    { key: 'source', label: 'Type' },
    { key: 'template', label: 'Template' },
    { key: 'relation', label: 'Relation Type' },
    { key: 'total', label: 'Total' },
    { key: 'delivered', label: 'Sent / Delivered' },
    { key: 'read', label: 'Opened / Read' },
    { key: 'createdAt', label: 'Created At' }
  ]

  const toggleColumnVisibility = (colKey: string) => {
    setVisibleColumns(prev => ({
      ...prev,
      [colKey]: !prev[colKey]
    }))
  }

  return (
    <motion.div {...pageTransitionProps}>
      {/* Page hero — matches the host's banner treatment. Presentational only. */}
      <div className="omni-page-hero">
        <h1>Campaigns</h1>
        <p>Create, schedule and track your WhatsApp and email campaigns.</p>
      </div>
      {/* Top Toolbar actions */}
      <div className="campaigns-toolbar">
        <Can permission="Campaign.Create">
          <button
            type="button"
            className="btn-toolbar"
            onClick={() => navigate('/campaigns/campaign/create')}
          >
            <Plus size={16} />
            <span>Create Campaign</span>
          </button>
        </Can>
        {(pendingApprovals > 0 || statusFilter === 'AwaitingApproval') && (
          <button
            type="button"
            className={`btn-toolbar btn-toolbar-approvals${statusFilter === 'AwaitingApproval' ? ' is-active' : ''}`}
            onClick={() => setStatusFilter(statusFilter === 'AwaitingApproval' ? '' : 'AwaitingApproval')}
            aria-pressed={statusFilter === 'AwaitingApproval'}
            title={statusFilter === 'AwaitingApproval' ? 'Show all campaigns' : 'Show campaigns waiting for approval'}
          >
            <ShieldCheck size={16} aria-hidden="true" />
            <span>Pending Approval</span>
            <span className="approvals-count-badge" aria-hidden="true">{new Intl.NumberFormat().format(pendingApprovals)}</span>
            <span className="sr-only">({pendingApprovals} waiting)</span>
          </button>
        )}
        <button
          type="button"
          className="btn-toolbar btn-toolbar-refresh"
          onClick={handleRefresh}
        >
          <RefreshCw size={16} />
          <span>Refresh</span>
        </button>
      </div>

      {/* Grid listing card */}
      <div className="campaigns-card">
        {/* Control toolbar */}
        <div className="contacts-controls-row">
          <div className="contacts-controls-left">
            <ColumnSelector
              columns={columnHeaders}
              visibleColumns={visibleColumns}
              onToggle={toggleColumnVisibility}
            />

            <button
              type="button"
              className={`btn-control-icon ${showFilters ? 'active' : ''}`}
              onClick={() => setShowFilters(!showFilters)}
              title="Toggle Filters"
              aria-label="Toggle filters"
            >
              <Filter size={16} />
            </button>
          </div>

          <div className="contacts-controls-right">
            <SearchBar
              value={searchQuery}
              onChange={setSearchQuery}
              placeholder="Search..."
            />
          </div>
        </div>

        {/* Dynamic filters panel */}
        {showFilters && (
          <div className="campaigns-filter-row fade-in">
            <div className="filter-group">
              <span className="filter-label">Template</span>
              <SearchableSelect
                label="Template"
                placeholder="All"
                allValue="All"
                value={templateFilter}
                options={templates.map(t => ({ value: t.name, label: t.name }))}
                onChange={setTemplateFilter}
              />
            </div>

            <div className="filter-group">
              <span className="filter-label">Relation Type</span>
              <SearchableSelect
                label="Relation Type"
                placeholder="All"
                allValue="All"
                value={relationTypeFilter}
                options={[
                  ...relationTypes.map(t => ({ value: String(t.id), label: String(t.id) })),
                  { value: 'Csv_campaign', label: 'Csv_campaign' }
                ]}
                onChange={setRelationTypeFilter}
              />
            </div>

            <div className="filter-group">
              <span className="filter-label">Created At</span>
              <div className="date-filter-inputs">
                <input
                  type="date"
                  className="form-control padding-date"
                  value={createdFrom}
                  onChange={(e) => setCreatedRange(e.target.value, createdTo)}
                />
                <span className="date-separator">to</span>
                <input
                  type="date"
                  className="form-control padding-date"
                  value={createdTo}
                  onChange={(e) => setCreatedRange(createdFrom, e.target.value)}
                />
              </div>
            </div>
          </div>
        )}

        {/* Datatable rows */}
        <div className="data-table-wrapper">
          {isLoading ? (
            <Skeleton variant="table" />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th className="actions-col">Actions</th>
                  {columnHeaders.map((col) => {
                    const isVisible = visibleColumns[col.key] !== false
                    if (!isVisible) return null
                    return (
                      <th
                        key={col.key}
                        onClick={() => handleSort(col.key)}
                        style={{ cursor: 'pointer', userSelect: 'none' }}
                      >
                        {col.label}
                        {sortKey === col.key && (
                          <span style={{ marginLeft: '4px', fontSize: '10px' }}>
                            {sortDirection === 'asc' ? '▲' : '▼'}
                          </span>
                        )}
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {paginatedCampaigns.length === 0 ? (
                  <tr>
                    <td
                      colSpan={columnHeaders.filter(c => visibleColumns[c.key] !== false).length + 1}
                      className="no-records-row"
                    >
                      No records found
                    </td>
                  </tr>
                ) : (
                  paginatedCampaigns.map((camp, index) => (
                    <tr key={camp.id}>
                      <td className="actions-col">
                        <div className="contact-actions-menu-wrapper">
                          <Menu
                            open={openActionsMenuId === camp.id}
                            onOpenChange={(isOpen) =>
                              setOpenActionsMenuId(isOpen ? camp.id : null)
                            }
                            align="start"
                            offset={4}
                            className="contact-actions-dropdown"
                            ariaLabel="Row actions"
                            trigger={(props) => (
                              <button
                                {...props}
                                type="button"
                                className="contact-actions-trigger"
                                aria-label="Row actions"
                              >
                                <MoreVertical size={16} />
                              </button>
                            )}
                          >
                            <MenuItem
                              className="contact-actions-item"
                              onSelect={() => navigate(`/campaigns/campaign/details/${camp.id}`)}
                            >
                              View
                            </MenuItem>
                            {/* Disabled rather than hidden — see the note in ContactsList. */}
                            <Can permission="Campaign.Edit" mode="disable">
                              <MenuItem
                                className="contact-actions-item"
                                onSelect={() => navigate(`/campaigns/campaign/edit/${camp.id}`)}
                              >
                                Edit
                              </MenuItem>
                            </Can>
                            <Can permission="Campaign.Delete" mode="disable">
                              <MenuItem
                                destructive
                                className="contact-actions-item"
                                onSelect={() => handleDelete(camp.id, camp.name)}
                              >
                                Delete
                              </MenuItem>
                            </Can>
                          </Menu>
                        </div>
                      </td>
                      {/* ID Column - sequential numbering */}
                      {visibleColumns.id !== false && (
                        <td>{startIndex + index + 1}</td>
                      )}

                      {/* Campaign Name Column */}
                      {visibleColumns.name !== false && (
                        <td>
                          <div className="campaign-name-cell">
                            <div className="campaign-name-row">
                              <span
                                className="campaign-title-text"
                                style={{ cursor: 'pointer' }}
                                onClick={() => navigate(`/campaigns/campaign/details/${camp.id}`)}
                              >
                                {camp.name}
                              </span>
                              {camp.connectionNickname && (
                                <span className="connection-nickname-badge" title={camp.connectionName || undefined}>
                                  {camp.connectionNickname}
                                </span>
                              )}
                            </div>
                          </div>
                        </td>
                      )}

                      {/* Type Column — driven by camp.isBulkCampaign (plain bool, not an
                          enum); "Standard"/"Bulk Upload" reads clearer than "Normal"/"Bulk". */}
                      {visibleColumns.source !== false && (
                        <td>
                          <span className={`relation-badge ${camp.isBulkCampaign ? 'csv' : 'lead'}`}>
                            {camp.isBulkCampaign ? 'Bulk Upload' : 'Standard'}
                          </span>
                        </td>
                      )}

                      {/* Template Column */}
                      {visibleColumns.template !== false && (
                        <td>{camp.templateName}</td>
                      )}

                      {/* Relation Type Column (colored badge) */}
                      {visibleColumns.relation !== false && (
                        <td>
                          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px' }}>
                            {camp.relationType.split(',').map(t => t.trim()).filter(Boolean).map((type) => (
                              <span
                                key={type}
                                className={`relation-badge ${
                                  type === 'Lead' ? 'lead' : type === 'Customer' ? 'customer' : type === 'Vendor' ? 'vendor' : 'csv'
                                }`}
                              >
                                {type}
                              </span>
                            ))}
                          </div>
                        </td>
                      )}

                      {/* Total Column */}
                      {visibleColumns.total !== false && (
                        <td>{camp.total}</td>
                      )}

                      {/* Sent / Delivered Column — Email uses emailStats.sent, WhatsApp uses deliveredTo */}
                      {visibleColumns.delivered !== false && (
                        <td>
                          {camp.channel?.toLowerCase() === 'email'
                            ? (camp.emailStats?.sent ?? camp.sentCount ?? camp.deliveredTo ?? 0)
                            : camp.deliveredTo}
                        </td>
                      )}

                      {/* Opened / Read Column — Email uses emailStats.opened, WhatsApp uses readBy */}
                      {visibleColumns.read !== false && (
                        <td>
                          {camp.channel?.toLowerCase() === 'email'
                            ? (camp.emailStats?.opened ?? camp.openedCount ?? camp.readBy ?? 0)
                            : camp.readBy}
                        </td>
                      )}

                      {/* Created At Column */}
                      {visibleColumns.createdAt !== false && (
                        <td>{formatRelativeTime(camp.createdAt)}</td>
                      )}

                      {/* Actions column — three-dot dropdown, matching ContactsList's pattern */}
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          )}
        </div>

        {/* Footer info & size selector & page indicators */}
        <div className="contacts-table-footer">
          <div>
            <select
              className="contacts-pager-size-select"
              value={pageSize}
              onChange={(e) => setPageSize(Number(e.target.value))}
            >
              <option value={5}>5</option>
              <option value={10}>10</option>
              <option value={20}>20</option>
            </select>
          </div>

          <div className="pager-navigation">
            <span className="contacts-pager-info">
              Showing {totalResults > 0 ? startIndex + 1 : 0} to {endIndex} of {totalResults} Results
            </span>

            {/* Pagination buttons */}
            <div className="contacts-controls-left">
              <button
                type="button"
                className="btn-control-icon"
                disabled={currentPage === 1}
                onClick={() => setCurrentPage(currentPage - 1)}
                aria-label="Previous Page"
              >
                <ChevronLeft size={16} />
              </button>
              <button
                type="button"
                className="btn-control-icon"
                disabled={currentPage >= totalPages}
                onClick={() => setCurrentPage(currentPage + 1)}
                aria-label="Next Page"
              >
                <ChevronRight size={16} />
              </button>
            </div>
          </div>
        </div>
      </div>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Campaign"
        message={
          deleteTarget
            ? `Are you sure you want to delete campaign "${deleteTarget.name}"? Scheduled messages will be cancelled.`
            : ''
        }
        confirmText="Delete"
        isDestructive={true}
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}
export default CampaignsList
