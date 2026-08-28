import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate } from 'react-router-dom'
import { Plus, RefreshCw, Filter, ChevronLeft, ChevronRight, MoreVertical } from 'lucide-react'
import { Menu, MenuItem } from '../../components/Menu/Menu'
import { useMessageBotStore } from '../../store/messageBotStore'
import { Toggle } from '../../components/Toggle/Toggle'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { ColumnSelector } from '../../components/ColumnSelector/ColumnSelector'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { toast } from 'react-hot-toast'
import './MessageBotList.css'
import Can from '../../components/Can/Can'
import usePermission from '../../hooks/usePermission'

export const MessageBotList: React.FC = () => {
  const navigate = useNavigate()
  const { has } = usePermission()
  
  const {
    bots,
    totalCount,
    isLoading,
    page,
    pageSize,
    searchQuery,
    relationTypeFilter,
    isActiveFilter,
    setPage,
    setPageSize,
    setSearchQuery,
    setRelationTypeFilter,
    setIsActiveFilter,
    loadBots,
    deleteBot,
    cloneBot,
    toggleBotActive
  } = useMessageBotStore()

  const [showFilters, setShowFilters] = useState(false)
  const [deleteModalOpen, setDeleteModalOpen] = useState(false)
  const [botToDelete, setBotToDelete] = useState<{ id: number; name: string } | null>(null)
  const [openActionsMenuId, setOpenActionsMenuId] = useState<number | null>(null)

  const [visibleColumns, setVisibleColumns] = useState<Record<string, boolean>>({
    id: true,
    name: true,
    type: true,
    triggerKeyword: true,
    relationType: true,
    active: true,
    createdAt: true
  })

  useEffect(() => {
    loadBots()
  }, [])

  const handleRefresh = async () => {
    await loadBots()
    toast.success('Message bots list refreshed successfully!')
  }

  const handleDeleteClick = (id: number, name: string) => {
    setBotToDelete({ id, name })
    setDeleteModalOpen(true)
  }

  const handleDeleteConfirm = async () => {
    if (botToDelete) {
      const success = await deleteBot(botToDelete.id)
      if (success) {
        toast.success(`Bot "${botToDelete.name}" deleted successfully.`)
      } else {
        toast.error('Failed to delete message bot.')
      }
    }
    setDeleteModalOpen(false)
    setBotToDelete(null)
  }

  const handleCloneClick = async (id: number) => {
    try {
      const cloned = await cloneBot(id)
      // Navigate to the edit view of the newly cloned bot and show the clone success toast message matching Screenshot 8
      sessionStorage.setItem('bot_clone_success', 'true')
      navigate(`/message-bot/bot/${cloned.id}`)
    } catch (error) {
      toast.error('Failed to clone message bot.')
    }
  }

  const formatRelativeTime = (dateString: string): string => {
    const date = new Date(dateString)
    const now = new Date()
    const diffMs = now.getTime() - date.getTime()
    
    const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24))
    const diffWeeks = Math.floor(diffDays / 7)
    const diffMonths = Math.floor(diffDays / 30)

    if (diffDays < 1) return 'Today'
    if (diffDays === 1) return 'Yesterday'
    if (diffDays < 7) return `${diffDays} days ago`
    if (diffWeeks === 1) return '1 week ago'
    if (diffWeeks < 4) return `${diffWeeks} weeks ago`
    if (diffMonths === 1) return '1 month ago'
    return `${diffMonths} months ago`
  }

  const columnHeaders = [
    { key: 'id', label: 'ID' },
    { key: 'name', label: 'NAME' },
    { key: 'type', label: 'TYPE' },
    { key: 'triggerKeyword', label: 'TRIGGER KEYWORD' },
    { key: 'relationType', label: 'RELATION TYPE' },
    { key: 'active', label: 'ACTIVE' },
    { key: 'createdAt', label: 'CREATED AT' }
  ]

  const toggleColumnVisibility = (colKey: string) => {
    setVisibleColumns(prev => ({
      ...prev,
      [colKey]: !prev[colKey]
    }))
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const startIndex = (page - 1) * pageSize
  const endIndex = Math.min(totalCount, startIndex + pageSize)

  return (
    <motion.div {...pageTransitionProps}>
      {/* Top Toolbar actions */}
      <div className="message-bots-toolbar">
        <Can permission="MessageBot.Create">
          <button
            type="button"
            className="btn-toolbar"
            onClick={() => navigate('/message-bot/bot')}
          >
            <Plus size={16} />
            <span>Message Bot</span>
          </button>
        </Can>
        <button
          type="button" 
          className="btn-toolbar btn-toolbar-refresh"
          onClick={handleRefresh}
          disabled={isLoading}
        >
          <RefreshCw size={16} />
          <span>Refresh</span>
        </button>
      </div>

      {/* Grid listing card */}
      <div className="message-bots-card">
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
          <div className="message-bots-filter-row fade-in">
            <div className="filter-group">
              <span className="filter-label">Relation Type</span>
              <select
                className="form-control"
                value={relationTypeFilter}
                onChange={(e) => setRelationTypeFilter(e.target.value)}
              >
                <option value="">All</option>
                <option value="Lead">Lead</option>
                <option value="Customer">Customer</option>
              </select>
            </div>

            <div className="filter-group">
              <span className="filter-label">Status</span>
              <select
                className="form-control"
                value={isActiveFilter === null ? '' : isActiveFilter ? 'true' : 'false'}
                onChange={(e) => {
                  const val = e.target.value
                  setIsActiveFilter(val === '' ? null : val === 'true')
                }}
              >
                <option value="">All</option>
                <option value="true">Active</option>
                <option value="false">Inactive</option>
              </select>
            </div>
          </div>
        )}

        {/* Datatable rows */}
        <div className="data-table-wrapper">
          {isLoading ? (
            <div className="data-table-empty">
              <p>Loading message bots...</p>
            </div>
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th className="actions-col">Actions</th>
                  {columnHeaders.map((col) => {
                    const isVisible = visibleColumns[col.key] !== false
                    if (!isVisible) return null
                    return <th key={col.key}>{col.label}</th>
                  })}
                </tr>
              </thead>
              <tbody>
                {bots.length === 0 ? (
                  <tr>
                    <td
                      colSpan={columnHeaders.filter(c => visibleColumns[c.key] !== false).length + 1}
                      className="no-records-row"
                    >
                      No records found
                    </td>
                  </tr>
                ) : (
                  bots.map((bot) => (
                  <tr key={bot.id}>
                    <td className="actions-col">
                      <div className="contact-actions-menu-wrapper">
                        <Menu
                          open={openActionsMenuId === bot.id}
                          onOpenChange={(isOpen) =>
                            setOpenActionsMenuId(isOpen ? bot.id : null)
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
                            onSelect={() => navigate(`/message-bot/bot/${bot.id}?view=true`)}
                          >
                            View
                          </MenuItem>
                          {/* Disabled rather than hidden — see the note in ContactsList. */}
                          <Can permission="MessageBot.Edit" mode="disable">
                            <MenuItem
                              className="contact-actions-item"
                              onSelect={() => navigate(`/message-bot/bot/${bot.id}`)}
                            >
                              Edit
                            </MenuItem>
                          </Can>
                          <Can permission="MessageBot.Clone" mode="disable">
                            <MenuItem
                              className="contact-actions-item"
                              onSelect={() => handleCloneClick(bot.id)}
                            >
                              Clone
                            </MenuItem>
                          </Can>
                          <Can permission="MessageBot.Delete" mode="disable">
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
                    {/* ID Column */}
                    {visibleColumns.id !== false && (
                      <td>{bot.id}</td>
                    )}

                    {/* Name Column */}
                    {visibleColumns.name !== false && (
                      <td>
                        <div className="message-bot-name-cell">
                          <span
                            className="message-bot-title-text"
                            style={{ cursor: 'pointer' }}
                            onClick={() => navigate(`/message-bot/bot/${bot.id}?view=true`)}
                          >
                            {bot.name}
                          </span>
                        </div>
                      </td>
                    )}

                    {/* Type Column */}
                    {visibleColumns.type !== false && (
                      <td>{bot.replyType}</td>
                    )}

                    {/* Trigger Keyword Column */}
                    {visibleColumns.triggerKeyword !== false && (
                      <td>{bot.triggerKeyword}</td>
                    )}

                    {/* Relation Type Column (colored badge) */}
                    {visibleColumns.relationType !== false && (
                      <td>
                        <span className={`relation-badge ${
                          bot.relationType === 'Lead' ? 'lead' : 'customer'
                        }`}>
                          {bot.relationType}
                        </span>
                      </td>
                    )}

                    {/* Active Switch Column */}
                    {visibleColumns.active !== false && (
                      <td>
                        <Toggle
                          checked={bot.isActive}
                          disabled={!has('MessageBot.Edit')}
                          onChange={() => toggleBotActive(bot.id)}
                        />
                      </td>
                    )}

                    {/* Created At Column */}
                    {visibleColumns.createdAt !== false && (
                      <td>{formatRelativeTime(bot.createdAt)}</td>
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
              Showing {totalCount > 0 ? startIndex + 1 : 0} to {endIndex} of {totalCount} Results
            </span>

            {/* Pagination buttons */}
            <div className="contacts-controls-left">
              <button
                type="button"
                className="btn-control-icon"
                disabled={page === 1}
                onClick={() => setPage(page - 1)}
                aria-label="Previous Page"
              >
                <ChevronLeft size={16} />
              </button>
              <button
                type="button"
                className="btn-control-icon"
                disabled={page === totalPages}
                onClick={() => setPage(page + 1)}
                aria-label="Next Page"
              >
                <ChevronRight size={16} />
              </button>
            </div>
          </div>
        </div>
      </div>

      <ConfirmationModal
        isOpen={deleteModalOpen}
        title="Delete Message Bot"
        message={`Are you sure you want to delete message bot "${botToDelete?.name}"?`}
        confirmText="Delete"
        cancelText="Cancel"
        onConfirm={handleDeleteConfirm}
        onCancel={() => setDeleteModalOpen(false)}
        isDestructive={true}
      />
    </motion.div>
  )
}

export default MessageBotList
