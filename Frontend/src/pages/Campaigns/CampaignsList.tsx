import React, { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useCampaignStore } from '../../store/campaignStore'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { ColumnSelector } from '../../components/ColumnSelector/ColumnSelector'
import { Plus, RefreshCw, Filter, ChevronLeft, ChevronRight } from 'lucide-react'
import toast from 'react-hot-toast'
import { Skeleton } from '../../components/Skeleton'
import './CampaignsList.css'
import { formatRelativeTime } from '../../utils/dateHelper'
import { templateService } from '../../services/templates/templateService'
import { contactService } from '../../services/contacts/contactService'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'

export const CampaignsList: React.FC = () => {
  const navigate = useNavigate()
  const {
    campaigns,
    isLoading,
    searchQuery,
    templateFilter,
    relationTypeFilter,
    createdAtFilter,
    currentPage,
    pageSize,
    
    setSearchQuery,
    setTemplateFilter,
    setRelationTypeFilter,
    setCreatedAtFilter,
    setCurrentPage,
    setPageSize,
    
    loadCampaigns,
    deleteCampaign
  } = useCampaignStore()

  const [showFilters, setShowFilters] = useState(true)
  const [visibleColumns, setVisibleColumns] = useState<Record<string, boolean>>({
    id: true,
    name: true,
    template: true,
    relation: true,
    total: true,
    delivered: true,
    read: true,
    createdAt: true
  })

  const [templates, setTemplates] = useState<any[]>([])
  const [relationTypes, setRelationTypes] = useState<any[]>([])
  const [deleteTarget, setDeleteTarget] = useState<{ id: number; name: string } | null>(null)

  useEffect(() => {
    loadCampaigns()
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

  // Local Filter logic
  const filteredCampaigns = campaigns.filter((c) => {
    // 1. General search bar query
    if (searchQuery) {
      const q = searchQuery.toLowerCase()
      const matchesSearch = 
        c.name.toLowerCase().includes(q) ||
        c.templateName.toLowerCase().includes(q)
      if (!matchesSearch) return false
    }

    // 2. Template Filter dropdown
    if (templateFilter !== 'All') {
      if (c.templateName !== templateFilter) return false
    }

    // 3. Relation Type Filter dropdown
    if (relationTypeFilter !== 'All') {
      if (c.relationType !== relationTypeFilter) return false
    }

    // 4. Created At Date filter matching relative time
    if (createdAtFilter) {
      const relativeDate = formatRelativeTime(c.createdAt).toLowerCase()
      if (!relativeDate.includes(createdAtFilter.toLowerCase())) return false
    }

    return true
  })

  // Pagination parameters
  const totalResults = filteredCampaigns.length
  const startIndex = (currentPage - 1) * pageSize
  const endIndex = Math.min(totalResults, startIndex + pageSize)
  const paginatedCampaigns = filteredCampaigns.slice(startIndex, endIndex)
  const totalPages = Math.max(1, Math.ceil(totalResults / pageSize))

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

  // Column definitions mapping
  const columnHeaders = [
    { key: 'id', label: 'ID' },
    { key: 'name', label: 'Campaign Name' },
    { key: 'template', label: 'Template' },
    { key: 'relation', label: 'Relation Type' },
    { key: 'total', label: 'Total' },
    { key: 'delivered', label: 'Delivered To' },
    { key: 'read', label: 'Read By' },
    { key: 'createdAt', label: 'Created At' }
  ]

  const toggleColumnVisibility = (colKey: string) => {
    setVisibleColumns(prev => ({
      ...prev,
      [colKey]: !prev[colKey]
    }))
  }

  return (
    <div className="fade-in">
      {/* Top Toolbar actions */}
      <div className="campaigns-toolbar">
        <button 
          type="button" 
          className="btn-toolbar"
          onClick={() => navigate('/campaigns/campaign/create')}
        >
          <Plus size={16} />
          <span>Create Campaign</span>
        </button>
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
              <select
                className="form-control"
                value={templateFilter}
                onChange={(e) => setTemplateFilter(e.target.value)}
              >
                <option value="All">All</option>
                {templates.map(t => (
                  <option key={t.id} value={t.name}>{t.name}</option>
                ))}
              </select>
            </div>

            <div className="filter-group">
              <span className="filter-label">Relation Type</span>
              <select
                className="form-control"
                value={relationTypeFilter}
                onChange={(e) => setRelationTypeFilter(e.target.value)}
              >
                <option value="All">All</option>
                {relationTypes.map(t => (
                  <option key={t.id} value={t.id}>{t.id}</option>
                ))}
                <option value="Csv_campaign">Csv_campaign</option>
              </select>
            </div>

            <div className="filter-group">
              <span className="filter-label">Created At</span>
              <input
                type="text"
                className="form-control"
                placeholder="Select a period"
                value={createdAtFilter}
                onChange={(e) => setCreatedAtFilter(e.target.value)}
              />
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
                  {columnHeaders.map((col) => {
                    const isVisible = visibleColumns[col.key] !== false
                    if (!isVisible) return null
                    return <th key={col.key}>{col.label}</th>
                  })}
                </tr>
              </thead>
              <tbody>
                {paginatedCampaigns.length === 0 ? (
                  <tr>
                    <td 
                      colSpan={columnHeaders.filter(c => visibleColumns[c.key] !== false).length}
                      className="no-records-row"
                    >
                      No records found
                    </td>
                  </tr>
                ) : (
                  paginatedCampaigns.map((camp) => (
                    <tr key={camp.id}>
                      {/* ID Column */}
                      {visibleColumns.id !== false && (
                        <td>{camp.id}</td>
                      )}

                      {/* Campaign Name Column with hover view/edit/delete menu */}
                      {visibleColumns.name !== false && (
                        <td>
                          <div className="campaign-name-cell">
                            <span className="campaign-title-text">
                              {camp.name}
                            </span>
                            <div className="campaign-hover-actions">
                              <span 
                                className="campaign-action-btn"
                                onClick={() => navigate(`/campaigns/campaign/details/${camp.id}`)}
                              >
                                View
                              </span>
                              <span className="action-divider">|</span>
                              <span 
                                className="campaign-action-btn"
                                onClick={() => navigate(`/campaigns/campaign/edit/${camp.id}`)}
                              >
                                Edit
                              </span>
                              <span className="action-divider">|</span>
                              <span 
                                className="campaign-action-btn"
                                onClick={() => handleDelete(camp.id, camp.name)}
                              >
                                Delete
                              </span>
                            </div>
                          </div>
                        </td>
                      )}

                      {/* Template Column */}
                      {visibleColumns.template !== false && (
                        <td>{camp.templateName}</td>
                      )}

                      {/* Relation Type Column (colored badge) */}
                      {visibleColumns.relation !== false && (
                        <td>
                          <span className={`relation-badge ${
                            camp.relationType === 'Lead' ? 'lead' : camp.relationType === 'Customer' ? 'customer' : 'csv'
                          }`}>
                            {camp.relationType}
                          </span>
                        </td>
                      )}

                      {/* Total Column */}
                      {visibleColumns.total !== false && (
                        <td>{camp.total}</td>
                      )}

                      {/* Delivered Column */}
                      {visibleColumns.delivered !== false && (
                        <td>{camp.deliveredTo}</td>
                      )}

                      {/* Read Column */}
                      {visibleColumns.read !== false && (
                        <td>{camp.readBy}</td>
                      )}

                      {/* Created At Column */}
                      {visibleColumns.createdAt !== false && (
                        <td>{formatRelativeTime(camp.createdAt)}</td>
                      )}
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
                disabled={currentPage === totalPages}
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
    </div>
  )
}
export default CampaignsList
