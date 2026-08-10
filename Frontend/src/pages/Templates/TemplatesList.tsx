import React, { useEffect, useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import { pageTransitionProps, fadeSlideUp, buttonHoverProps, transitions } from '../../utils/motion'
import { useTemplateStore } from '../../store/templateStore'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { ColumnSelector } from '../../components/ColumnSelector/ColumnSelector'
import toast from 'react-hot-toast'
import { Skeleton } from '../../components/Skeleton'
import {
  Download,
  Settings,
  RefreshCw,
  Filter,
  FilterX,
  ChevronLeft,
  ChevronRight
} from 'lucide-react'
import './TemplatesList.css'
import { useConnectionStore } from '../../store/connectionStore'
import Can from '../../components/Can/Can'

export const TemplatesList: React.FC = () => {
  const {
    templates,
    isLoading,
    isRefreshing,
    searchQuery,
    
    nameQuery,
    languageFilter,
    categoryFilter,
    typeFilter,
    statusFilter,
    
    currentPage,
    pageSize,
    
    languages,
    categories,
    statuses,
    types,
    
    setSearchQuery,
    setNameQuery,
    setLanguageFilter,
    setCategoryFilter,
    setTypeFilter,
    setStatusFilter,
    
    setCurrentPage,
    setPageSize,
    
    loadTemplates,
    refreshTemplates,
    loadFilterOptions
  } = useTemplateStore()

  // Filters default to hidden until toggled — matches the Filter-button pattern
  // already used in the Campaigns section (was previously always-visible below).
  const [showFilters, setShowFilters] = useState(false)

  // UI state for column visibility
  const [visibleColumns, setVisibleColumns] = useState<Record<string, boolean>>({
    id: true,
    name: true,
    languages: true,
    category: true,
    type: true,
    status: true,
    bodyText: true
  })
  const [expandedIds, setExpandedIds] = useState<Record<number, boolean>>({})
  const [sortKey, setSortKey] = useState<string>('')
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('asc')

  const { connections, fetchDashboard } = useConnectionStore()

  // Fetch connections first, then load templates from Meta for each connected connection
  useEffect(() => {
    const init = async () => {
      await fetchDashboard()
      loadFilterOptions()
    }
    init()
  }, [])

  // When connections change, load templates dynamically from Meta
  useEffect(() => {
    if (connections.length > 0) {
      const connectedIds = connections
        .filter(c => c.isConnected && c.phoneNumber)
        .map(c => c.id)
      loadTemplates(connectedIds)
    } else {
      // If connections haven't loaded yet, don't clear templates
    }
  }, [connections])

  // Filter templates locally based on ALL selected filters
  const filteredTemplates = templates.filter((t) => {
    // 1. General search box filter
    if (searchQuery) {
      const q = searchQuery.toLowerCase()
      const matchesSearch = 
        t.name.toLowerCase().includes(q) ||
        t.bodyText.toLowerCase().includes(q)
      if (!matchesSearch) return false
    }

    // 2. Specific Template Name filter
    if (nameQuery) {
      if (t.name !== nameQuery) return false
    }

    // 3. Languages select filter
    if (languageFilter !== 'All') {
      if (t.language.toLowerCase() !== languageFilter.toLowerCase()) return false
    }

    // 4. Category select filter
    if (categoryFilter !== 'All') {
      if (t.category.toLowerCase() !== categoryFilter.toLowerCase()) return false
    }

    // 5. Template Type select filter
    if (typeFilter !== 'All') {
      const templateType = (t.templateType || t.type || '').toLowerCase()
      if (templateType !== typeFilter.toLowerCase()) return false
    }

    // 6. Status select filter
    if (statusFilter !== 'All') {
      if (t.status.toLowerCase() !== statusFilter.toLowerCase()) return false
    }

    return true
  })

  // Sort logic — applied after filtering, before pagination
  const sortedTemplates = [...filteredTemplates].sort((a, b) => {
    if (!sortKey) return 0
    let aVal: string | number = ''
    let bVal: string | number = ''
    switch (sortKey) {
      case 'id': aVal = a.id; bVal = b.id; break
      case 'name': aVal = a.name.toLowerCase(); bVal = b.name.toLowerCase(); break
      case 'languages': aVal = (a.language || '').toLowerCase(); bVal = (b.language || '').toLowerCase(); break
      case 'category': aVal = (a.category || '').toLowerCase(); bVal = (b.category || '').toLowerCase(); break
      case 'type': aVal = (a.templateType || a.type || '').toLowerCase(); bVal = (b.templateType || b.type || '').toLowerCase(); break
      case 'status': aVal = (a.status || '').toLowerCase(); bVal = (b.status || '').toLowerCase(); break
      case 'bodyText': aVal = (a.bodyText || '').toLowerCase(); bVal = (b.bodyText || '').toLowerCase(); break
      default: return 0
    }
    if (aVal < bVal) return sortDirection === 'asc' ? -1 : 1
    if (aVal > bVal) return sortDirection === 'asc' ? 1 : -1
    return 0
  })

  // Handle column header click for sorting
  const handleSort = (key: string) => {
    if (sortKey === key) {
      setSortDirection(prev => prev === 'asc' ? 'desc' : 'asc')
    } else {
      setSortKey(key)
      setSortDirection('asc')
    }
  }

  // Pagination calculation
  const totalResults = sortedTemplates.length
  const startIndex = (currentPage - 1) * pageSize
  const endIndex = Math.min(totalResults, startIndex + pageSize)
  const paginatedTemplates = sortedTemplates.slice(startIndex, endIndex)
  const totalPages = Math.max(1, Math.ceil(totalResults / pageSize))

  // Helper to get connected connection IDs
  const getConnectedIds = () => connections
    .filter(c => c.isConnected && c.phoneNumber)
    .map(c => c.id)

  const handleLoadTemplates = async () => {
    const connectedIds = getConnectedIds()
    if (connectedIds.length === 0) {
      toast.error('No connected WhatsApp Business connection found. Connect a WABA number first.')
      return
    }
    const syncOk = await refreshTemplates()
    const loadOk = await loadTemplates(connectedIds)
    if (syncOk && loadOk) {
      toast.success('Templates loaded and synchronized from WABA!')
    } else {
      toast.error('Failed to sync templates from WhatsApp. Please try again.')
    }
  }

  const handleTemplateManagement = () => {
    // Open Meta Business Suite template manager in a new tab
    window.open('https://business.facebook.com/wa/manage/message-templates/', '_blank')
  }

  const handleRefresh = async () => {
    const connectedIds = getConnectedIds()
    if (connectedIds.length === 0) {
      toast.error('No connected WhatsApp Business connection found.')
      return
    }
    const ok = await loadTemplates(connectedIds)
    if (ok) {
      toast.success('Templates list refreshed successfully!')
    } else {
      toast.error('Failed to refresh templates. Please try again.')
    }
  }

  const areFiltersActive = nameQuery !== '' || languageFilter !== 'All' || categoryFilter !== 'All' ||
    typeFilter !== 'All' || statusFilter !== 'All'

  const handleClearFilters = () => {
    setNameQuery('')
    setLanguageFilter('All')
    setCategoryFilter('All')
    setTypeFilter('All')
    setStatusFilter('All')
  }

  const columnHeaders = [
    { key: 'id', label: 'ID' },
    { key: 'name', label: 'TEMPLATE NAME' },
    { key: 'languages', label: 'LANGUAGES' },
    { key: 'category', label: 'CATEGORY' },
    { key: 'type', label: 'TEMPLATE TYPE' },
    { key: 'status', label: 'STATUS' },
    { key: 'bodyText', label: 'BODY DATA' }
  ]

  const toggleColumnVisibility = (colKey: string) => {
    setVisibleColumns(prev => ({
      ...prev,
      [colKey]: !prev[colKey]
    }))
  }

  const isLoadingOrRefreshing = isLoading || isRefreshing

  return (
    <motion.div {...pageTransitionProps}>
      {/* Page header */}
      <motion.div
        className="templates-page-header"
        variants={fadeSlideUp}
        initial="hidden"
        animate="visible"
        transition={transitions.normal}
      >
        <h1>Templates</h1>
        <p>Create, manage and organize your message templates.</p>
      </motion.div>

      {/* Top action bar: sync/management/refresh actions + search */}
      <motion.div
        className="templates-toolbar"
        variants={fadeSlideUp}
        initial="hidden"
        animate="visible"
        transition={{ ...transitions.normal, delay: 0.05 }}
      >
        <div className="templates-toolbar-actions">
          {/* Load Templates triggers a WhatsApp-side sync, so it sits behind its own
              capability rather than behind Template.View. */}
          <Can permission="Template.LoadTemplate">
            <motion.button
              type="button"
              className="btn-toolbar btn-toolbar-primary"
              onClick={handleLoadTemplates}
              disabled={isLoadingOrRefreshing}
              {...buttonHoverProps}
            >
              <Download size={16} />
              <span>Load Templates</span>
            </motion.button>
          </Can>
          <motion.button
            type="button"
            className="btn-toolbar"
            onClick={handleTemplateManagement}
            {...buttonHoverProps}
          >
            <Settings size={16} />
            <span>Template Management</span>
          </motion.button>
          <motion.button
            type="button"
            className="btn-toolbar"
            onClick={handleRefresh}
            disabled={isLoadingOrRefreshing}
            {...buttonHoverProps}
          >
            <RefreshCw size={16} />
            <span>Refresh</span>
          </motion.button>
        </div>

        <SearchBar
          value={searchQuery}
          onChange={setSearchQuery}
          placeholder="Search templates"
        />
      </motion.div>

      {/* Main card covering filters, table headers & rows */}
      <div className="templates-card">
        {/* Table controls bar — column visibility + filter toggle */}
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
        </div>

        {/* Filter row — toggled by the Filter button above */}
        <AnimatePresence initial={false}>
        {showFilters && (
        <motion.div
          className="templates-filter-grid"
          initial={{ opacity: 0, height: 0 }}
          animate={{ opacity: 1, height: 'auto' }}
          exit={{ opacity: 0, height: 0 }}
          transition={transitions.normal}
        >
            {/* Filter 1: Template Name Select Dropdown */}
            <div className="filter-group">
              <span className="filter-label">Template Name</span>
              <select
                className="form-control"
                value={nameQuery}
                onChange={(e) => setNameQuery(e.target.value)}
              >
                <option value="">All</option>
                {Array.from(new Set(templates.map(t => t.name))).map(name => (
                  <option key={name} value={name}>{name}</option>
                ))}
              </select>
            </div>

            {/* Filter 2: Languages selection */}
            <div className="filter-group">
              <span className="filter-label">Languages</span>
              <select
                className="form-control"
                value={languageFilter}
                onChange={(e) => setLanguageFilter(e.target.value)}
              >
                <option value="All">All</option>
                {(languages || []).map(l => (
                  <option key={l.code} value={l.code}>{l.name}</option>
                ))}
              </select>
            </div>

            {/* Filter 3: Category selection */}
            <div className="filter-group">
              <span className="filter-label">Category</span>
              <select
                className="form-control"
                value={categoryFilter}
                onChange={(e) => setCategoryFilter(e.target.value)}
              >
                <option value="All">All</option>
                {(categories || []).map(c => (
                  <option key={c.id} value={c.name}>{c.name}</option>
                ))}
              </select>
            </div>

            {/* Filter 4: Template Type selection */}
            <div className="filter-group">
              <span className="filter-label">Template Type</span>
              <select
                className="form-control"
                value={typeFilter}
                onChange={(e) => setTypeFilter(e.target.value)}
              >
                <option value="All">All</option>
                {(types || []).map(t => (
                  <option key={t.id} value={t.name}>{t.name}</option>
                ))}
              </select>
            </div>

            {/* Filter 5: Status selection */}
            <div className="filter-group">
              <span className="filter-label">Status</span>
              <select
                className="form-control"
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
              >
                <option value="All">All</option>
                {(statuses || []).map(s => (
                  <option key={s.id} value={s.name}>{s.name}</option>
                ))}
              </select>
            </div>

            <div className="filter-group filter-group-clear">
              <motion.button
                type="button"
                className="btn-clear-filters"
                onClick={handleClearFilters}
                disabled={!areFiltersActive}
                whileHover={areFiltersActive ? { scale: 1.03 } : undefined}
                whileTap={areFiltersActive ? { scale: 0.97 } : undefined}
              >
                <FilterX size={14} />
                <span>Clear Filters</span>
              </motion.button>
            </div>
        </motion.div>
        )}
        </AnimatePresence>

        {/* Dynamic Responsiveness Table */}
        <div className="data-table-wrapper">
          {isLoadingOrRefreshing || !languages || !categories || !statuses || !types ? (
            <Skeleton variant="table" />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
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
                {paginatedTemplates.length === 0 ? (
                  <tr>
                    <td 
                      colSpan={columnHeaders.filter(c => visibleColumns[c.key] !== false).length}
                      className="no-records-row"
                    >
                      No records found
                    </td>
                  </tr>
                ) : (
                  paginatedTemplates.map((template, index) => (
                    <motion.tr
                      key={template.id}
                      initial={{ opacity: 0, y: 8 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ delay: index * 0.03, duration: 0.2, ease: 'easeOut' }}
                    >
                      {/* ID column - sequential numbering */}
                      {visibleColumns.id !== false && (
                        <td>{startIndex + index + 1}</td>
                      )}

                      {/* Template Name column */}
                      {visibleColumns.name !== false && (
                        <td>{template.name}</td>
                      )}

                      {/* Languages column */}
                      {visibleColumns.languages !== false && (
                        <td>{template.language}</td>
                      )}

                      {/* Category column */}
                      {visibleColumns.category !== false && (
                        <td>{template.category}</td>
                      )}

                      {/* Template Type column */}
                      {visibleColumns.type !== false && (
                        <td>{template.templateType || template.type}</td>
                      )}

                      {/* Status badge column */}
                      {visibleColumns.status !== false && (
                        <td>
                          <div className="template-status-cell">
                            <StatusBadge 
                              type={template.status?.toUpperCase() === 'APPROVED' ? 'approved' : template.status?.toUpperCase() === 'REJECTED' ? 'rejected' : 'pending'} 
                              text={template.status} 
                            />
                            {template.status?.toUpperCase() === 'REJECTED' && template.rejectReason && (
                              <div className="template-reject-reason" title={template.rejectReason}>
                                Reason: {template.rejectReason}
                              </div>
                            )}
                          </div>
                        </td>
                      )}

                      {/* Body Data text column */}
                      {visibleColumns.bodyText !== false && (
                        <td className="table-cell-truncate">
                          <div className="template-body-text-wrapper">
                            <div className={`body-text-content ${expandedIds[template.id] ? 'expanded' : 'collapsed'}`}>
                              {template.bodyText}
                            </div>
                            {template.bodyText && template.bodyText.length > 100 && (
                              <button
                                type="button"
                                className="btn-show-more"
                                onClick={() => {
                                  setExpandedIds((prev) => ({
                                    ...prev,
                                    [template.id]: !prev[template.id]
                                  }))
                                }}
                              >
                                {expandedIds[template.id] ? 'Show Less' : 'Show More'}
                              </button>
                            )}
                          </div>
                        </td>
                      )}
                    </motion.tr>
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
                disabled={currentPage === 1 || isLoadingOrRefreshing}
                onClick={() => setCurrentPage(currentPage - 1)}
                aria-label="Previous Page"
              >
                <ChevronLeft size={16} />
              </button>
              <button
                type="button"
                className="btn-control-icon"
                disabled={currentPage === totalPages || isLoadingOrRefreshing}
                onClick={() => setCurrentPage(currentPage + 1)}
                aria-label="Next Page"
              >
                <ChevronRight size={16} />
              </button>
            </div>
          </div>
        </div>
      </div>
    </motion.div>
  )
}
export default TemplatesList
