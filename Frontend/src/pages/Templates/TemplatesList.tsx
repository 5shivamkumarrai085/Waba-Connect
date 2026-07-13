import React, { useEffect, useState } from 'react'
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
  ChevronLeft,
  ChevronRight
} from 'lucide-react'
import './TemplatesList.css'

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

  // UI state for showing/hiding filters and columns
  const [showFilters, setShowFilters] = useState(true)
  const [visibleColumns, setVisibleColumns] = useState<Record<string, boolean>>({
    id: true,
    name: true,
    languages: true,
    category: true,
    type: true,
    status: true,
    bodyText: true
  })

  useEffect(() => {
    loadTemplates()
    loadFilterOptions()
  }, [])

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
      if (t.language !== languageFilter) return false
    }

    // 4. Category select filter
    if (categoryFilter !== 'All') {
      if (t.category !== categoryFilter) return false
    }

    // 5. Template Type select filter
    if (typeFilter !== 'All') {
      if (t.type !== typeFilter) return false
    }

    // 6. Status select filter
    if (statusFilter !== 'All') {
      if (t.status !== statusFilter) return false
    }

    return true
  })

  // Pagination calculation
  const totalResults = filteredTemplates.length
  const startIndex = (currentPage - 1) * pageSize
  const endIndex = Math.min(totalResults, startIndex + pageSize)
  const paginatedTemplates = filteredTemplates.slice(startIndex, endIndex)
  const totalPages = Math.max(1, Math.ceil(totalResults / pageSize))

  const handleLoadTemplates = async () => {
    await refreshTemplates()
    toast.success('Templates loaded and synchronized from WABA!')
  }

  const handleTemplateManagement = () => {
    // Open Meta Business Suite template manager in a new tab (Screenshot 2)
    window.open('https://business.facebook.com/wa/manage/message-templates/', '_blank')
  }

  const handleRefresh = async () => {
    await loadTemplates()
    toast.success('Templates list refreshed successfully!')
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
    <div className="fade-in">
      {/* Top action buttons */}
      <div className="templates-toolbar">
        <button 
          type="button" 
          className="btn-toolbar"
          onClick={handleLoadTemplates}
          disabled={isLoadingOrRefreshing}
        >
          <Download size={16} />
          <span>Load Templates</span>
        </button>
        <button 
          type="button" 
          className="btn-toolbar"
          onClick={handleTemplateManagement}
        >
          <Settings size={16} />
          <span>Template Management</span>
        </button>
        <button 
          type="button" 
          className="btn-toolbar btn-toolbar-refresh"
          onClick={handleRefresh}
          disabled={isLoadingOrRefreshing}
        >
          <RefreshCw size={16} />
          <span>Refresh</span>
        </button>
      </div>

      {/* Main card covering filters, table headers & rows */}
      <div className="templates-card">
        {/* Table controls bar */}
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

        {/* 5 Column filter section grid */}
        {showFilters && (
          <div className="templates-filter-grid fade-in">
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
          </div>
        )}

        {/* Dynamic Responsiveness Table */}
        <div className="data-table-wrapper">
          {isLoadingOrRefreshing || !languages || !categories || !statuses || !types ? (
            <Skeleton variant="table" />
          ) : paginatedTemplates.length === 0 ? (
            <div className="data-table-empty">
              <p>No message templates found matching criteria.</p>
            </div>
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
                {paginatedTemplates.map((template) => (
                  <tr key={template.id}>
                    {/* ID column */}
                    {visibleColumns.id !== false && (
                      <td>{template.id}</td>
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
                      <td>{template.type}</td>
                    )}

                    {/* Status badge column */}
                    {visibleColumns.status !== false && (
                      <td>
                        <StatusBadge 
                          type={template.status === 'APPROVED' ? 'approved' : template.status === 'REJECTED' ? 'rejected' : 'pending'} 
                          text={template.status} 
                        />
                      </td>
                    )}

                    {/* Body Data text column */}
                    {visibleColumns.bodyText !== false && (
                      <td className="table-cell-truncate" title={template.bodyText}>
                        {template.bodyText}
                      </td>
                    )}
                  </tr>
                ))}
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
    </div>
  )
}
export default TemplatesList
