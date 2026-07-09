import React, { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useContactStore } from '../../store/contactStore'
import { contactService } from '../../services/contacts/contactService'
import { ColumnSelector } from '../../components/ColumnSelector/ColumnSelector'
import { Toggle } from '../../components/Toggle/Toggle'
import { Avatar } from '../../components/Avatar/Avatar'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { 
  Plus, 
  RefreshCw, 
  FileSpreadsheet, 
  Filter, 
  ChevronLeft, 
  ChevronRight
} from 'lucide-react'
import toast from 'react-hot-toast'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { Skeleton } from '../../components/Skeleton'
import './ContactsList.css'

export const ContactsList: React.FC = () => {
  const navigate = useNavigate()
  const {
    contacts,
    isLoading,
    searchQuery,
    selectedIds,
    currentPage,
    pageSize,
    visibleColumns,
    
    setSearchQuery,
    toggleRowSelection,
    toggleAllRowSelection,
    toggleColumnVisibility,
    setCurrentPage,
    setPageSize,
    
    loadContacts,
    deleteSelected,
    toggleContactActive
  } = useContactStore()

  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false)

  useEffect(() => {
    loadContacts()
  }, [])

  // Filter contacts locally based on search query
  const filteredContacts = contacts.filter((c: any) => {
    const q = searchQuery.toLowerCase()
    const contactName = c.name || `${c.firstName || ''} ${c.lastName || ''}`.trim()
    return (
      contactName.toLowerCase().includes(q) ||
      (c.phone || '').includes(q) ||
      (c.type || '').toLowerCase().includes(q) ||
      (c.company && c.company.toLowerCase().includes(q))
    )
  })

  // Pagination calculation
  const totalResults = filteredContacts.length
  const startIndex = (currentPage - 1) * pageSize
  const endIndex = Math.min(totalResults, startIndex + pageSize)
  const paginatedContacts = filteredContacts.slice(startIndex, endIndex)
  const totalPages = Math.max(1, Math.ceil(totalResults / pageSize))

  const handleRefresh = async () => {
    await loadContacts()
    toast.success('Contacts refreshed successfully!')
  }

  const handleBulkDeleteClick = () => {
    setIsDeleteModalOpen(true)
  }

  const confirmBulkDelete = async () => {
    setIsDeleteModalOpen(false)
    await deleteSelected()
    toast.success('Selected contacts deleted.')
  }

  const handleDeleteContact = async (id: number, name: string) => {
    if (window.confirm(`Are you sure you want to delete contact "${name}"?`)) {
      try {
        await contactService.deleteContact(id)
        toast.success('Contact deleted successfully!')
        await loadContacts()
      } catch (err) {
        toast.error('Failed to delete contact.')
      }
    }
  }

  const handleBulkChat = () => {
    toast.success(`Initiating chats with ${selectedIds.length} contacts via WhatsApp...`)
  }

  const handleExportSheet = () => {
    toast.success('Exporting contacts to spreadsheet CSV...')
  }

  const handleFilterToggle = () => {
    toast.success('Entity filters active. Filtering by Lead status & WhatsApp sources.')
  }

  // Column headers list mapping for selector dropdown
  const columnHeaders = [
    { key: 'id', label: 'ID' },
    { key: 'name', label: 'Name' },
    { key: 'type', label: 'Type' },
    { key: 'phone', label: 'Phone' },
    { key: 'assigned', label: 'Assigned' },
    { key: 'initiateChat', label: 'Initiate Chat' },
    { key: 'status', label: 'Status' },
    { key: 'source', label: 'Source' },
    { key: 'group', label: 'Group' },
    { key: 'active', label: 'Active' },
    { key: 'createdAt', label: 'Created At' }
  ]

  // Render header checkbox status
  const isAllSelected = paginatedContacts.length > 0 && paginatedContacts.every(c => selectedIds.includes(c.id))

  return (
    <div className="fade-in">
      {/* Top action buttons */}
      <div className="contacts-toolbar">
        <button 
          type="button" 
          className="btn-toolbar"
          onClick={() => navigate('/contacts/contact')}
        >
          <Plus size={16} />
          <span>New Contact</span>
        </button>
        <button 
          type="button" 
          className="btn-toolbar"
          onClick={() => navigate('/contacts/import')}
        >
          <Plus size={16} />
          <span>Import Contacts</span>
        </button>
        <button 
          type="button" 
          className="btn-toolbar"
          onClick={handleRefresh}
        >
          <RefreshCw size={16} />
          <span>Refresh</span>
        </button>
      </div>

      {/* Main card covering controls, table headers & rows */}
      <div className="contacts-card">
        {/* Table controls bar */}
        <div className="contacts-controls-row">
          <div className="contacts-controls-left">
            <button 
              type="button" 
              className="btn-control-icon"
              onClick={handleExportSheet}
              title="Export Spreadsheet"
              aria-label="Export spreadsheet"
            >
              <FileSpreadsheet size={16} />
            </button>
            
            {/* Custom eye visibility selector dropdown */}
            <ColumnSelector
              columns={columnHeaders}
              visibleColumns={visibleColumns}
              onToggle={toggleColumnVisibility}
            />

            <button 
              type="button" 
              className="btn-control-icon"
              onClick={handleFilterToggle}
              title="Filter List"
              aria-label="Filter list"
            >
              <Filter size={16} />
            </button>

            <button
              type="button"
              className="btn-bulk-delete"
              disabled={selectedIds.length === 0}
              onClick={handleBulkDeleteClick}
            >
              Bulk Delete({selectedIds.length})
            </button>

            <button
              type="button"
              className="btn-bulk-chat"
              disabled={selectedIds.length === 0}
              onClick={handleBulkChat}
            >
              Initiate Chat({selectedIds.length})
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

        {/* Dynamic Responsiveness Table */}
        <div className="data-table-wrapper">
          {isLoading ? (
            <Skeleton variant="table" />
          ) : paginatedContacts.length === 0 ? (
            <div className="data-table-empty">
              <p>No contacts found matching criteria.</p>
            </div>
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th className="checkbox-cell">
                    <input
                      type="checkbox"
                      checked={isAllSelected}
                      onChange={toggleAllRowSelection}
                    />
                  </th>
                  {columnHeaders.map((col) => {
                    const isVisible = visibleColumns[col.key] !== false
                    if (!isVisible) return null
                    const isSortable = col.key !== 'initiateChat' && col.key !== 'group'
                    return (
                      <th key={col.key} className={`col-width-${col.key}`}>
                        <div className="header-cell-content">
                          <span>{col.label}</span>
                          {isSortable && (
                            <span className={`sort-icon ${col.key === 'createdAt' ? 'active' : ''}`}>
                              {col.key === 'createdAt' ? '▲' : '⇅'}
                            </span>
                          )}
                        </div>
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {paginatedContacts.map((contact) => {
                  const isRowSelected = selectedIds.includes(contact.id)
                  
                  return (
                    <tr key={contact.id}>
                      <td className="checkbox-cell">
                        <input
                          type="checkbox"
                          checked={isRowSelected}
                          onChange={() => toggleRowSelection(contact.id)}
                        />
                      </td>

                      {/* ID column */}
                      {visibleColumns.id !== false && (
                        <td>{contact.id}</td>
                      )}

                      {/* Name column with link styling */}
                      {visibleColumns.name !== false && (
                        <td>
                          <div className="contact-name-cell">
                            <span 
                              className="contact-link-name"
                              onClick={() => navigate(`/contacts/contact/edit/${contact.id}?view=true`)}
                            >
                              {contact.name || `${contact.firstName || ''} ${contact.lastName || ''}`.trim()}
                            </span>
                            <div className="contact-hover-actions">
                              <span 
                                className="contact-action-btn"
                                onClick={() => navigate(`/contacts/contact/edit/${contact.id}?view=true`)}
                              >
                                View
                              </span>
                              <span className="action-divider">|</span>
                              <span 
                                className="contact-action-btn"
                                onClick={() => navigate(`/contacts/contact/edit/${contact.id}`)}
                              >
                                Edit
                              </span>
                              <span className="action-divider">|</span>
                              <span 
                                className="contact-action-btn"
                                onClick={() => handleDeleteContact(contact.id, contact.name || `${contact.firstName || ''} ${contact.lastName || ''}`.trim())}
                              >
                                Delete
                              </span>
                            </div>
                          </div>
                        </td>
                      )}

                      {/* Type column */}
                      {visibleColumns.type !== false && (
                        <td>{contact.type}</td>
                      )}

                      {/* Phone column */}
                      {visibleColumns.phone !== false && (
                        <td>{contact.phone}</td>
                      )}

                      {/* Assigned avatar column */}
                      {visibleColumns.assigned !== false && (
                        <td className="text-center">
                          <Avatar name="" size="small" />
                        </td>
                      )}

                      {/* Initiate Chat column (WhatsApp green bubble icon) */}
                      {visibleColumns.initiateChat !== false && (
                        <td className="text-center">
                          <span 
                            className="contact-whatsapp-icon"
                            onClick={() => console.log(`Opening chat window for: ${contact.phone}`)}
                            title="Start WhatsApp Chat"
                          >
                            <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor">
                              <path d="M.057 24l1.687-6.163c-1.041-1.804-1.588-3.849-1.587-5.946C.06 5.348 5.397.01 12.008.01c3.202.001 6.212 1.246 8.477 3.514 2.266 2.268 3.507 5.28 3.505 8.484-.004 6.657-5.34 11.997-11.953 11.997-2.005-.001-3.973-.502-5.73-1.464L0 24zm6.59-4.846c1.6.95 3.198 1.483 4.85 1.486 5.435.002 9.859-4.383 9.862-9.794.002-2.622-1.018-5.086-2.87-6.941C16.576 2.05 14.133 1.03 11.518 1.03c-5.412 0-9.82 4.384-9.824 9.795-.002 1.71.458 3.38 1.332 4.887l-.99 3.615 3.73-.977zm11.306-6.837c-.3-.15-1.77-.875-2.045-.975-.275-.1-.475-.15-.675.15-.2.3-.775.975-.95 1.175-.175.2-.35.225-.65.075-.3-.15-1.265-.467-2.41-1.485-.89-.79-1.492-1.77-1.667-2.07-.175-.3-.02-.463.13-.61.137-.133.3-.35.45-.525.15-.175.2-.3.3-.5.1-.2.05-.375-.025-.525-.075-.15-.675-1.625-.925-2.225-.244-.588-.49-.508-.675-.518-.175-.01-.375-.01-.575-.01-.2 0-.525.075-.8 1.025-.275.3-.8 1.625-1.125 2.275-.325.65-.65 1.3-.9 1.95a6.015 6.015 0 00-.5 2.525c0 1.25.625 2.45 1.15 3.125.175.225 3.163 4.83 7.663 6.775 1.07.462 1.905.738 2.555.945 1.076.342 2.055.294 2.83.178.863-.128 2.65-.65 3.025-1.625.375-.975.375-1.8.263-1.975-.113-.175-.3-.275-.6-.425z"/>
                            </svg>
                          </span>
                        </td>
                      )}

                      {/* Status column */}
                      {visibleColumns.status !== false && (
                        <td className="text-center">
                          <StatusBadge 
                            type={contact.status} 
                            text={contact.status} 
                          />
                        </td>
                      )}

                      {/* Source column */}
                      {visibleColumns.source !== false && (
                        <td>{contact.source}</td>
                      )}

                      {/* Group column */}
                      {visibleColumns.group !== false && (
                        <td>
                          <span className={contact.groups === 'Groups not found' ? 'contact-group-orange' : ''}>
                            {Array.isArray(contact.groups) 
                              ? contact.groups.map((g: any) => g?.name || g?.groupName || '').join(', ') 
                              : (typeof contact.groups === 'object' && contact.groups !== null ? (contact.groups as any).name || (contact.groups as any).groupName : contact.groups)}
                          </span>
                        </td>
                      )}

                      {/* Active toggle column */}
                      {visibleColumns.active !== false && (
                        <td className="text-center">
                          <Toggle
                            checked={contact.active}
                            onChange={() => toggleContactActive(contact.id)}
                          />
                        </td>
                      )}

                      {/* Created At column */}
                      {visibleColumns.createdAt !== false && (
                        <td>{contact.createdAt}</td>
                      )}
                    </tr>
                  )
                })}
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
        isOpen={isDeleteModalOpen}
        title="Delete Contacts"
        message={`Are you sure you want to delete ${selectedIds.length} selected contacts? This action cannot be undone.`}
        confirmText="Delete"
        isDestructive={true}
        onConfirm={confirmBulkDelete}
        onCancel={() => setIsDeleteModalOpen(false)}
      />
    </div>
  )
}
export default ContactsList
