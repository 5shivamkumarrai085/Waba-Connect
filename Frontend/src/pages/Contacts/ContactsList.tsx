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
import { InitiateChatModal } from '../../components/Modal/InitiateChatModal'
import { wabaService } from '../../services/waba/wabaService'
import { Skeleton } from '../../components/Skeleton'
import './ContactsList.css'
import { formatRelativeTime } from '../../utils/dateHelper'

const getAssignedName = (assignedTo?: string) => {
  if (!assignedTo) return 'Unassigned'
  const lower = assignedTo.toLowerCase()
  if (lower === 'superadmin') return 'superAdmin'
  if (lower === 'johnmicheal') return 'John Micheal'
  if (lower === 'gunaratnam') return 'Gunaratnam'
  return assignedTo
}

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
  const [deleteTarget, setDeleteTarget] = useState<{ id: number; name: string } | null>(null)
  const [isInitiateModalOpen, setIsInitiateModalOpen] = useState(false)
  const [selectedContactsForTemplate, setSelectedContactsForTemplate] = useState<Array<{ id: number; name: string; phone: string }>>([])

  // Custom filters states
  const [showFilters, setShowFilters] = useState(false)
  const [filterType, setFilterType] = useState('All')
  const [filterAssigned, setFilterAssigned] = useState('All')
  const [filterStatus, setFilterStatus] = useState('All')
  const [filterSource, setFilterSource] = useState('All')
  const [filterGroup, setFilterGroup] = useState('All')
  const [filterStartDate, setFilterStartDate] = useState('')
  const [filterEndDate, setFilterEndDate] = useState('')

  // Export popover state
  const [showExportDropdown, setShowExportDropdown] = useState(false)

  // Sort states
  const [sortCol, setSortCol] = useState<string>('id')
  const [sortOrd, setSortOrd] = useState<'asc' | 'desc'>('desc')

  // Dynamic dropdown options fetched from backend
  const [assignedUsers, setAssignedUsers] = useState<any[]>([])
  const [statuses, setStatuses] = useState<any[]>([])
  const [sources, setSources] = useState<any[]>([])
  const [groups, setGroups] = useState<any[]>([])
  const [contactTypes, setContactTypes] = useState<any[]>([])

  useEffect(() => {
    loadContacts()
    const fetchMetadata = async () => {
      try {
        const [usersData, statusesData, sourcesData, groupsData, typesData] = await Promise.all([
          contactService.getAssignedUsers(),
          contactService.getContactStatuses(),
          contactService.getContactSources(),
          contactService.getContactGroups(),
          contactService.getContactTypes(),
          wabaService.getDashboard()
        ])
        setAssignedUsers(usersData || [])
        setStatuses(statusesData || [])
        setSources(sourcesData || [])
        setGroups(groupsData || [])
        setContactTypes(typesData || [])
      } catch (err) {
        console.error('Failed to fetch metadata:', err)
      }
    }
    fetchMetadata()
  }, [])

  // Filter contacts locally based on search query and custom filter dropdowns
  const filteredContacts = contacts.filter((c: any) => {
    // 1. Search Query filter
    const q = searchQuery.toLowerCase()
    const contactName = c.name || `${c.firstName || ''} ${c.lastName || ''}`.trim()
    const matchesSearch = (
      contactName.toLowerCase().includes(q) ||
      (c.phone || '').includes(q) ||
      (c.type || '').toLowerCase().includes(q) ||
      (c.company && c.company.toLowerCase().includes(q))
    )
    if (!matchesSearch) return false

    // 2. Type filter
    if (filterType !== 'All') {
      const typeLower = (c.type || '').toLowerCase()
      if (typeLower !== filterType.toLowerCase()) return false
    }

    // 3. Assigned filter
    if (filterAssigned !== 'All') {
      const assignedVal = getAssignedName(c.assignedTo)
      if (assignedVal !== filterAssigned) return false
    }

    // 4. Status filter
    if (filterStatus !== 'All') {
      const statusLower = (c.status || '').toLowerCase()
      if (statusLower !== filterStatus.toLowerCase()) return false
    }

    // 5. Source filter
    if (filterSource !== 'All') {
      const sourceLower = (c.source || '').toLowerCase()
      if (sourceLower !== filterSource.toLowerCase()) return false
    }

    // 6. Group filter
    if (filterGroup !== 'All') {
      const groupList = Array.isArray(c.groups)
        ? c.groups.map((g: any) => g?.name || g?.groupName || '').filter(Boolean)
        : [];
      if (!groupList.includes(filterGroup)) return false
    }

    // 7. Date range filter
    if (filterStartDate) {
      const start = new Date(filterStartDate).getTime()
      const created = new Date(c.createdAt).getTime()
      if (created < start) return false
    }
    if (filterEndDate) {
      const end = new Date(filterEndDate).getTime() + 24 * 60 * 60 * 1000 - 1
      const created = new Date(c.createdAt).getTime()
      if (created > end) return false
    }

    return true
  })

  // Sort contacts locally
  const sortedContacts = [...filteredContacts].sort((a: any, b: any) => {
    let aVal = a[sortCol];
    let bVal = b[sortCol];

    if (sortCol === 'name') {
      aVal = (a.name || `${a.firstName || ''} ${a.lastName || ''}`).toLowerCase();
      bVal = (b.name || `${b.firstName || ''} ${b.lastName || ''}`).toLowerCase();
    } else if (typeof aVal === 'string') {
      aVal = aVal.toLowerCase();
      bVal = (bVal || '').toLowerCase();
    } else if (sortCol === 'createdAt') {
      aVal = new Date(aVal || 0).getTime();
      bVal = new Date(bVal || 0).getTime();
    }

    if (aVal === bVal) return 0;
    
    if (sortOrd === 'asc') {
      return aVal > bVal ? 1 : -1;
    } else {
      return aVal < bVal ? 1 : -1;
    }
  });

  // Pagination calculation
  const totalResults = sortedContacts.length
  const startIndex = (currentPage - 1) * pageSize
  const endIndex = Math.min(totalResults, startIndex + pageSize)
  const paginatedContacts = sortedContacts.slice(startIndex, endIndex)
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
    try {
      await deleteSelected()
      toast.success('Contact deleted successfully.')
    } catch (err) {
      toast.error('Failed to delete contacts.')
    }
  }

  const confirmSingleDelete = async () => {
    if (!deleteTarget) return
    const { id } = deleteTarget
    setDeleteTarget(null)
    try {
      await contactService.deleteContact(id)
      toast.success('Contact deleted successfully.')
      await loadContacts()
    } catch (err) {
      toast.error('Failed to delete contact.')
    }
  }

  const handleDeleteContact = async (id: number, name: string) => {
    setDeleteTarget({ id, name })
  }

  const handleBulkChat = async () => {
    if (selectedIds.length === 0) {
      toast.error('Please select at least one contact.')
      return
    }

    const activeSelectedContacts = contacts
      .filter((c: any) => selectedIds.includes(c.id) && c.active !== false && c.isActive !== false)
      .map((c: any) => ({
        id: c.id,
        name: c.name || `${c.firstName || ''} ${c.lastName || ''}`.trim(),
        phone: c.phone
      }))

    if (activeSelectedContacts.length === 0) {
      toast.error('No active contacts selected to initiate chat.', { duration: 3000 })
      return
    }

    await checkLimitAndOpenTemplateModal(activeSelectedContacts)
  }

  const checkLimitAndOpenTemplateModal = async (contactsToSet: Array<{ id: number; name: string; phone: string }>) => {
    try {
      const checkResult = await wabaService.checkLimitFast()
      if (checkResult.limitReached) {
        toast.error(checkResult.message || 'Daily message limit reached for this connection.', { duration: 4000 })
        return
      }
    } catch (err) {
      console.error('Error checking limit:', err)
    }
    setSelectedContactsForTemplate(contactsToSet)
    setIsInitiateModalOpen(true)
  }

  const handleExport = (format: 'csv' | 'xlsx', scope: 'all' | 'selected') => {
    const list = scope === 'selected' 
      ? contacts.filter(c => selectedIds.includes(c.id)) 
      : filteredContacts;
      
    if (list.length === 0) {
      toast.error('No contacts to export.');
      return;
    }

    const headers = ['ID', 'Name', 'Type', 'Phone', 'Assigned', 'Status', 'Source', 'Groups', 'Created At'];
    const dataRows = list.map(c => [
      c.id,
      c.name || `${c.firstName || ''} ${c.lastName || ''}`.trim(),
      c.type || '',
      c.phone || '',
      c.assignedTo || 'Unassigned',
      c.status || '',
      c.source || '',
      Array.isArray(c.groups) 
        ? c.groups.map((g: any) => g?.name || g?.groupName || '').filter(Boolean).join(', ')
        : (c.groups || ''),
      c.createdAt ? new Date(c.createdAt).toLocaleString() : ''
    ]);

    if (format === 'csv') {
      const csvContent = [
        headers.join(','),
        ...dataRows.map(row => row.map(val => `"${String(val).replace(/"/g, '""')}"`).join(','))
      ].join('\n');
      
      const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', `contacts_${scope}_${new Date().toISOString().slice(0,10)}.csv`);
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      toast.success('CSV file downloaded successfully!');
    } else {
      let html = '<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel" xmlns="http://www.w3.org/TR/REC-html40">';
      html += '<head><meta charset="utf-8" /><style>table { border-collapse: collapse; } th, td { border: 1px solid #ddd; padding: 8px; }</style></head><body>';
      html += '<table><thead><tr>';
      headers.forEach(h => { html += `<th>${h}</th>`; });
      html += '</tr></thead><tbody>';
      dataRows.forEach(row => {
        html += '<tr>';
        row.forEach(val => { html += `<td>${val}</td>`; });
        html += '</tr>';
      });
      html += '</tbody></table></body></html>';

      const blob = new Blob([html], { type: 'application/vnd.ms-excel;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', `contacts_${scope}_${new Date().toISOString().slice(0,10)}.xlsx`);
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      toast.success('XLSX file downloaded successfully!');
    }
    setShowExportDropdown(false);
  }

  const handleFilterToggle = () => {
    setShowFilters(prev => !prev)
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
            <div className="export-popover-anchor">
              <button 
                type="button" 
                className={`btn-control-icon ${showExportDropdown ? 'active' : ''}`}
                onClick={() => setShowExportDropdown(!showExportDropdown)}
                title="Export Spreadsheet"
                aria-label="Export spreadsheet"
              >
                <FileSpreadsheet size={16} />
              </button>
              {showExportDropdown && (
                <div className="export-menu-popover">
                  <div className="export-row">
                    <span className="export-format">XLSX</span>
                    <button type="button" className="export-action" onClick={() => handleExport('xlsx', 'all')}>
                      ({filteredContacts.length}) All
                    </button>
                    <button 
                      type="button" 
                      className="export-action" 
                      onClick={() => handleExport('xlsx', 'selected')}
                      disabled={selectedIds.length === 0}
                    >
                      ({selectedIds.length}) Selected
                    </button>
                  </div>
                  <div className="export-row">
                    <span className="export-format">Csv</span>
                    <button type="button" className="export-action" onClick={() => handleExport('csv', 'all')}>
                      ({filteredContacts.length}) All
                    </button>
                    <button 
                      type="button" 
                      className="export-action" 
                      onClick={() => handleExport('csv', 'selected')}
                      disabled={selectedIds.length === 0}
                    >
                      ({selectedIds.length}) Selected
                    </button>
                  </div>
                </div>
              )}
            </div>
            
            {/* Custom eye visibility selector dropdown */}
            <ColumnSelector
              columns={columnHeaders}
              visibleColumns={visibleColumns}
              onToggle={toggleColumnVisibility}
            />

            <button 
              type="button" 
              className={`btn-control-icon ${showFilters ? 'active' : ''}`}
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

        {showFilters && (
          <div className="contacts-filters-grid">
            <div className="filter-field">
              <label>Type</label>
              <select
                className="contacts-pager-size-select width-full"
                value={filterType}
                onChange={(e) => setFilterType(e.target.value)}
              >
                <option value="All">All</option>
                {contactTypes.map((t: any) => (
                  <option key={t.id} value={t.id}>
                    {t.name.charAt(0).toUpperCase() + t.name.slice(1)}
                  </option>
                ))}
              </select>
            </div>

            <div className="filter-field">
              <label>Assigned</label>
              <select
                className="contacts-pager-size-select width-full"
                value={filterAssigned}
                onChange={(e) => setFilterAssigned(e.target.value)}
              >
                <option value="All">All</option>
                {assignedUsers.map((user: any) => {
                  const label = getAssignedName(user.name);
                  return (
                    <option key={user.id} value={label}>
                      {label}
                    </option>
                  )
                })}
              </select>
            </div>

            <div className="filter-field">
              <label>Status</label>
              <select
                className="contacts-pager-size-select width-full"
                value={filterStatus}
                onChange={(e) => setFilterStatus(e.target.value)}
              >
                <option value="All">All</option>
                {statuses.map((status: any) => (
                  <option key={status.id} value={status.name}>
                    {status.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="filter-field">
              <label>Source</label>
              <select
                className="contacts-pager-size-select width-full"
                value={filterSource}
                onChange={(e) => setFilterSource(e.target.value)}
              >
                <option value="All">All</option>
                {sources.map((source: any) => (
                  <option key={source.id} value={source.name}>
                    {source.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="filter-field">
              <label>group</label>
              <select
                className="contacts-pager-size-select width-full"
                value={filterGroup}
                onChange={(e) => setFilterGroup(e.target.value)}
              >
                <option value="All">All</option>
                {groups.map((group: any) => (
                  <option key={group.id} value={group.name || group.groupName}>
                    {group.name || group.groupName}
                  </option>
                ))}
              </select>
            </div>

            <div className="filter-field">
              <label>Created At</label>
              <div className="date-filter-inputs">
                <input
                  type="date"
                  className="contacts-pager-size-select padding-date"
                  value={filterStartDate}
                  onChange={(e) => setFilterStartDate(e.target.value)}
                />
                <span className="date-separator">to</span>
                <input
                  type="date"
                  className="contacts-pager-size-select padding-date"
                  value={filterEndDate}
                  onChange={(e) => setFilterEndDate(e.target.value)}
                />
              </div>
            </div>
          </div>
        )}

        {/* Dynamic Responsiveness Table */}
        <div className="data-table-wrapper">
          {isLoading ? (
            <Skeleton variant="table" />
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
                    
                    const handleHeaderClick = () => {
                      if (!isSortable) return
                      if (sortCol === col.key) {
                        setSortOrd(sortOrd === 'asc' ? 'desc' : 'asc')
                      } else {
                        setSortCol(col.key)
                        setSortOrd('asc')
                      }
                    }

                    return (
                      <th 
                        key={col.key} 
                        className={`col-width-${col.key} ${isSortable ? 'sortable-header' : ''}`}
                        onClick={handleHeaderClick}
                      >
                        <div className="header-cell-content">
                          <span>{col.label}</span>
                          {isSortable && (
                            <span className={`sort-icon ${sortCol === col.key ? 'active' : ''}`}>
                              {sortCol === col.key ? (sortOrd === 'asc' ? '▲' : '▼') : '⇅'}
                            </span>
                          )}
                        </div>
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {paginatedContacts.length === 0 ? (
                  <tr>
                    <td 
                      colSpan={1 + columnHeaders.filter(c => visibleColumns[c.key] !== false).length}
                      className="no-records-row"
                    >
                      No records found
                    </td>
                  </tr>
                ) : (
                  paginatedContacts.map((contact, index) => {
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

                        {/* ID column - sequential numbering */}
                        {visibleColumns.id !== false && (
                          <td>{startIndex + index + 1}</td>
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
                          <td className="text-center" title={getAssignedName(contact.assignedTo)}>
                            <div title={getAssignedName(contact.assignedTo)} style={{ display: 'inline-block' }}>
                              <Avatar name="" size="small" />
                            </div>
                          </td>
                        )}

                        {/* Initiate Chat column (WhatsApp green outlined icon) */}
                        {visibleColumns.initiateChat !== false && (
                          <td className="text-center">
                            <span 
                              className="contact-whatsapp-icon"
                              onClick={() => {
                                if (!contact.active) {
                                  toast.error('Cannot send message to an inactive contact.', { duration: 3000 })
                                  return
                                }
                                checkLimitAndOpenTemplateModal([{
                                  id: contact.id,
                                  name: contact.name || `${contact.firstName || ''} ${contact.lastName || ''}`.trim(),
                                  phone: contact.phone
                                }])
                              }}
                              title="Start WhatsApp Chat"
                            >
                              <svg viewBox="0 0 448 512" width="18" height="18" fill="currentColor" style={{ display: 'inline-block', verticalAlign: 'middle' }}>
                                <path d="M380.9 97.1C339 55.1 283.2 32 223.9 32c-122.4 0-222 99.6-222 222 0 39.1 10.2 77.3 29.6 111L32 503l139.7-36.6c32.7 17.8 69.4 27.2 107.1 27.2 122.4 0 222-99.6 222-222 0-59.3-23.2-115-65.1-115.5zm-157 341.6c-33.2 0-65.7-8.9-94-25.7l-6.7-4-83.1 21.8 22.2-81-4.4-7c-18.4-29.3-28.1-63.1-28.1-97.9 0-101.9 83-184.8 185-184.8 49.3 0 95.7 19.2 130.6 54.1 34.8 34.9 54 81.2 54 130.6 0 102-83 184.8-185 184.8zm110.2-151c-6-3-35.6-17.6-41.2-19.6-5.5-2-9.6-3-13.6 3-4 6-15.6 19.6-19.1 23.6-3.5 4-7 4.5-13 1.5-6-3-25.3-9.3-48.2-29.8-17.8-15.9-29.8-35.5-33.3-41.5-3.5-6-.4-9.2 2.7-12.2 2.7-2.7 6-7 9-10.5 3-3.5 4-6 6-10 2-4 1-7.5-.5-10.5-1.5-3-13.6-32.8-18.6-45-5-12-10-10.4-13.6-10.6-3.5-.2-7.6-.2-11.6-.2-4 0-10.6 1.5-16.1 7.5-5.5 6-21.1 20.6-21.1 50.2 0 29.7 21.6 58.3 24.6 62.3 3 4 42.5 64.9 103 91 14.4 6.2 25.6 9.9 34.3 12.7 14.5 4.6 27.7 4 38.1 2.5 11.6-1.7 35.6-14.6 40.6-28.7 5-14 5-26.1 3.5-28.7-1.5-2.6-5.5-4.1-11.5-7.1z" />
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

                        {/* Group column with VIP Lead / Customer colors */}
                        {visibleColumns.group !== false && (() => {
                          const groupsArray = Array.isArray(contact.groups)
                            ? contact.groups.map((g: any) => g?.name || g?.groupName || '').filter(Boolean)
                            : typeof contact.groups === 'string' && contact.groups.trim() !== ''
                              ? contact.groups.split(',').map(s => s.trim())
                              : [];

                          const hasGroup = groupsArray.length > 0;

                          return (
                            <td>
                              {hasGroup ? (
                                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px' }}>
                                  {groupsArray.map((gName, idx) => {
                                    const nameLower = gName.toLowerCase();
                                    let badgeClass = 'group-badge-default';
                                    if (nameLower.includes('vip lead')) {
                                      badgeClass = 'group-badge-vip-lead';
                                    } else if (nameLower.includes('vip customer')) {
                                      badgeClass = 'group-badge-vip-customer';
                                    }
                                    return (
                                      <span key={idx} className={badgeClass}>
                                        {gName}
                                      </span>
                                    );
                                  })}
                                </div>
                              ) : (
                                <span className="contact-group-orange">Groups not found</span>
                              )}
                            </td>
                          );
                        })()}

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
                          <td>{formatRelativeTime(contact.createdAt)}</td>
                        )}
                      </tr>
                    )
                  })
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
        isOpen={isDeleteModalOpen || deleteTarget !== null}
        title="Delete Contact"
        message={
          deleteTarget 
            ? `Are you sure you want to delete contact "${deleteTarget.name}"?` 
            : `Are you sure you want to delete ${selectedIds.length} selected contacts? This action cannot be undone.`
        }
        confirmText="Delete"
        isDestructive={true}
        onConfirm={deleteTarget ? confirmSingleDelete : confirmBulkDelete}
        onCancel={() => {
          setIsDeleteModalOpen(false)
          setDeleteTarget(null)
        }}
      />

      <InitiateChatModal
        isOpen={isInitiateModalOpen}
        onClose={() => {
          setIsInitiateModalOpen(false)
          setSelectedContactsForTemplate([])
        }}
        contacts={selectedContactsForTemplate}
      />
    </div>
  )
}
export default ContactsList
