import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate } from 'react-router-dom'
import { Plus, RefreshCw, Filter, ChevronLeft, ChevronRight, MoreVertical } from 'lucide-react'
import { Menu, MenuItem } from '../../components/Menu/Menu'
import { useTemplateBotStore } from '../../store/templateBotStore'
import { Toggle } from '../../components/Toggle/Toggle'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { ColumnSelector } from '../../components/ColumnSelector/ColumnSelector'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { toast } from 'react-hot-toast'
import './TemplateBotList.css'
import Can from '../../components/Can/Can'
import usePermission from '../../hooks/usePermission'

export const TemplateBotList: React.FC = () => {
  const navigate = useNavigate()
  const { has } = usePermission()
  
  const {
    bots,
    totalCount,
    isLoading,
    page,
    pageSize,
    relationType,
    isActive,
    search,
    setPage,
    setPageSize,
    setRelationType,
    setIsActive,
    setSearch,
    fetchBots,
    deleteBot,
    cloneBot,
    toggleBotActive
  } = useTemplateBotStore()

  // UI state
  const [searchTerm, setSearchTerm] = useState(search)
  const [isFilterVisible, setIsFilterVisible] = useState(false)
  const [deleteModalOpen, setDeleteModalOpen] = useState(false)
  const [botToDelete, setBotToDelete] = useState<{ id: number; name: string } | null>(null)
  const [openActionsMenuId, setOpenActionsMenuId] = useState<number | null>(null)

  // Columns toggle options
  const [visibleColumns, setVisibleColumns] = useState<Record<string, boolean>>({
    id: true,
    name: true,
    replyType: true,
    triggerKeyword: true,
    relationType: true,
    active: true,
    createdAt: true
  })

  const columns = [
    { key: 'id', label: 'ID' },
    { key: 'name', label: 'Name' },
    { key: 'replyType', label: 'Reply Type' },
    { key: 'triggerKeyword', label: 'Trigger Keyword' },
    { key: 'relationType', label: 'Relation Type' },
    { key: 'active', label: 'Active' },
    { key: 'createdAt', label: 'Created At' }
  ]

  // Fetch data on load/param change
  useEffect(() => {
    fetchBots()
  }, [page, pageSize, relationType, isActive])

  // Search debounce/handler
  useEffect(() => {
    const delay = setTimeout(() => {
      setSearch(searchTerm)
      fetchBots()
    }, 400)
    return () => clearTimeout(delay)
  }, [searchTerm])

  // Clone success message toast
  useEffect(() => {
    if (sessionStorage.getItem('template_bot_clone_success') === 'true') {
      toast.success('Bot clone successfully')
      sessionStorage.removeItem('template_bot_clone_success')
    }
  }, [])
  const toggleColumnVisibility = (colKey: string) => {
    setVisibleColumns(prev => ({
      ...prev,
      [colKey]: !prev[colKey]
    }))
  }
  const handleDeleteClick = (id: number, name: string) => {
    setBotToDelete({ id, name })
    setDeleteModalOpen(true)
  }

  const confirmDelete = async () => {
    if (botToDelete) {
      const success = await deleteBot(botToDelete.id)
      if (success) {
        toast.success(`Bot "${botToDelete.name}" deleted successfully.`)
      } else {
        toast.error('Failed to delete template bot.')
      }
    }
    setDeleteModalOpen(false)
    setBotToDelete(null)
  }

  const handleCloneClick = async (id: number) => {
    try {
      const cloned = await cloneBot(id)
      // Store flag for clone success toast
      sessionStorage.setItem('template_bot_clone_success', 'true')
      navigate(`/template-bot/bot/${cloned.id}`)
    } catch (error) {
      toast.error('Failed to clone template bot.')
    }
  }

  const formatRelativeDate = (dateStr: string) => {
    if (!dateStr) return ''
    try {
      const date = new Date(dateStr)
      const now = new Date()
      const diffMs = now.getTime() - date.getTime()
      
      const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24))
      if (diffDays <= 0) return 'Today'
      if (diffDays === 1) return 'Yesterday'
      if (diffDays < 30) return `${diffDays} days ago`
      
      const diffWeeks = Math.floor(diffDays / 7)
      if (diffWeeks < 4) return `${diffWeeks} weeks ago`
      
      const diffMonths = Math.floor(diffDays / 30)
      return `${diffMonths} month${diffMonths > 1 ? 's' : ''} ago`
    } catch (e) {
      return 'Today'
    }
  }

  const totalPages = Math.ceil(totalCount / pageSize)

  return (
    <motion.div {...pageTransitionProps}>
      {/* Top Action buttons */}
      <div className="template-bot-header-actions">
        <Can permission="TemplateBot.Create">
          <button
            className="template-bot-btn btn-primary"
            onClick={() => navigate('/template-bot/bot')}
          >
            <Plus size={16} />
            Template Bot
          </button>
        </Can>
        <button className="template-bot-btn btn-secondary" onClick={() => fetchBots()}>
          <RefreshCw size={16} />
          Refresh
        </button>
      </div>

      {/* Main filter container */}
      <div className="template-bot-table-card">
        <div className="template-bot-table-toolbar">
          <div className="toolbar-left">
            <ColumnSelector
              columns={columns}
              visibleColumns={visibleColumns}
              onToggle={toggleColumnVisibility}
            />
            <button 
              className={`filter-toggle-btn ${isFilterVisible ? 'active' : ''}`}
              onClick={() => setIsFilterVisible(!isFilterVisible)}
            >
              <Filter size={16} />
            </button>
          </div>
          <div className="toolbar-right">
            <SearchBar
              value={searchTerm}
              onChange={setSearchTerm}
              placeholder="Search..."
            />
          </div>
        </div>

        {/* Dynamic filters area */}
        {isFilterVisible && (
          <div className="template-bot-filters-drawer">
            <div className="filter-group">
              <label>Relation Type</label>
              <select 
                value={relationType} 
                onChange={(e) => setRelationType(e.target.value)}
              >
                <option value="All">All</option>
                <option value="Lead">Lead</option>
                <option value="Customer">Customer</option>
              </select>
            </div>
            <div className="filter-group">
              <label>Status</label>
              <select
                value={isActive === undefined ? 'All' : isActive ? 'Active' : 'Inactive'}
                onChange={(e) => {
                  const val = e.target.value
                  if (val === 'All') setIsActive(undefined)
                  else if (val === 'Active') setIsActive(true)
                  else setIsActive(false)
                }}
              >
                <option value="All">All</option>
                <option value="Active">Active</option>
                <option value="Inactive">Inactive</option>
              </select>
            </div>
          </div>
        )}

        {/* Table View */}
        <div className="table-responsive">
          <table className="template-bot-table">
            <thead>
              <tr>
                {visibleColumns.id !== false && <th>ID</th>}
                {visibleColumns.name !== false && <th>NAME</th>}
                {visibleColumns.replyType !== false && <th>REPLY TYPE</th>}
                {visibleColumns.triggerKeyword !== false && <th>TRIGGER KEYWORD</th>}
                {visibleColumns.relationType !== false && <th>RELATION TYPE</th>}
                {visibleColumns.active !== false && <th>ACTIVE</th>}
                {visibleColumns.createdAt !== false && <th>CREATED AT</th>}
                <th style={{ textAlign: 'center' }}>ACTIONS</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <tr>
                  <td colSpan={8} className="text-center py-4">
                    <div className="loader-spinner">Loading bots...</div>
                  </td>
                </tr>
              ) : bots.length === 0 ? (
                <tr>
                  <td
                    colSpan={
                      (visibleColumns.id !== false ? 1 : 0) +
                      (visibleColumns.name !== false ? 1 : 0) +
                      (visibleColumns.replyType !== false ? 1 : 0) +
                      (visibleColumns.triggerKeyword !== false ? 1 : 0) +
                      (visibleColumns.relationType !== false ? 1 : 0) +
                      (visibleColumns.active !== false ? 1 : 0) +
                      (visibleColumns.createdAt !== false ? 1 : 0) +
                      1
                    }
                    className="no-records-row"
                  >
                    No records found
                  </td>
                </tr>
              ) : (
                bots.map((bot) => (
                  <tr key={bot.id}>
                    {/* ID Column */}
                    {visibleColumns.id !== false && (
                      <td>{bot.id}</td>
                    )}

                    {/* Name Column */}
                    {visibleColumns.name !== false && (
                      <td>
                        <div className="name-cell-group">
                          <span
                            className="bot-name-text"
                            style={{ cursor: 'pointer' }}
                            onClick={() => navigate(`/template-bot/bot/${bot.id}?view=true`)}
                          >
                            {bot.name}
                          </span>
                        </div>
                      </td>
                    )}

                    {/* Reply Type Column */}
                    {visibleColumns.replyType !== false && (
                      <td>{bot.replyType}</td>
                    )}

                    {/* Trigger Keyword Column */}
                    {visibleColumns.triggerKeyword !== false && (
                      <td>{bot.triggerKeyword}</td>
                    )}

                    {/* Relation Type Badge Column */}
                    {visibleColumns.relationType !== false && (
                      <td>
                        <span className={`relation-badge ${
                          bot.relationType.toLowerCase() === 'lead' ? 'lead' : 'customer'
                        }`}>
                          {bot.relationType}
                        </span>
                      </td>
                    )}

                    {/* Toggle Active Column */}
                    {visibleColumns.active !== false && (
                      <td>
                        <Toggle
                          checked={bot.isActive}
                          disabled={!has('TemplateBot.Edit')}
                          onChange={() => toggleBotActive(bot.id)}
                        />
                      </td>
                    )}

                    {/* Relative Created At Column */}
                    {visibleColumns.createdAt !== false && (
                      <td>{formatRelativeDate(bot.createdAt)}</td>
                    )}

                    {/* Actions column — three-dot dropdown, matching ContactsList's pattern */}
                    <td className="text-center">
                      <div className="contact-actions-menu-wrapper">
                        <Menu
                          open={openActionsMenuId === bot.id}
                          onOpenChange={(isOpen) =>
                            setOpenActionsMenuId(isOpen ? bot.id : null)
                          }
                          align="end"
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
                            onSelect={() => navigate(`/template-bot/bot/${bot.id}?view=true`)}
                          >
                            View
                          </MenuItem>
                          {/* Disabled rather than hidden — see the note in ContactsList. */}
                          <Can permission="TemplateBot.Edit" mode="disable">
                            <MenuItem
                              className="contact-actions-item"
                              onSelect={() => navigate(`/template-bot/bot/${bot.id}`)}
                            >
                              Edit
                            </MenuItem>
                          </Can>
                          <Can permission="TemplateBot.Clone" mode="disable">
                            <MenuItem
                              className="contact-actions-item"
                              onSelect={() => handleCloneClick(bot.id)}
                            >
                              Clone
                            </MenuItem>
                          </Can>
                          <Can permission="TemplateBot.Delete" mode="disable">
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              onSelect={() => handleDeleteClick(bot.id, bot.name)}
                            >
                              Delete
                            </MenuItem>
                          </Can>
                        </Menu>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination Toolbar */}
        {totalCount > 0 && (
          <div className="template-bot-pagination">
            <div className="page-size-selector">
              <select
                value={pageSize}
                onChange={(e) => setPageSize(Number(e.target.value))}
              >
                <option value={10}>10</option>
                <option value={20}>20</option>
                <option value={50}>50</option>
              </select>
            </div>
            <div className="pagination-info">
              Showing {Math.min(totalCount, (page - 1) * pageSize + 1)} to{' '}
              {Math.min(totalCount, page * pageSize)} of {totalCount} Results
            </div>
            <div className="pagination-buttons">
              <button 
                disabled={page === 1} 
                onClick={() => setPage(page - 1)}
              >
                <ChevronLeft size={16} />
              </button>
              <button 
                disabled={page === totalPages} 
                onClick={() => setPage(page + 1)}
              >
                <ChevronRight size={16} />
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Delete confirmation modal */}
      <ConfirmationModal
        isOpen={deleteModalOpen}
        title="Delete Template Bot"
        message={`Are you sure you want to delete "${botToDelete?.name}" template bot? This action cannot be undone.`}
        confirmText="Delete"
        cancelText="Cancel"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => {
          setDeleteModalOpen(false)
          setBotToDelete(null)
        }}
      />
    </motion.div>
  )
}
