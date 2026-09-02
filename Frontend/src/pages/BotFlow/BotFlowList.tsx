import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { Modal } from '../../components/Modal/Modal'
import { useNavigate } from 'react-router-dom'
import { Plus, RefreshCw, ChevronLeft, ChevronRight, MoreVertical } from 'lucide-react'
import { Menu, MenuItem } from '../../components/Menu/Menu'
import { useBotFlowStore } from '../../store/botFlowStore'
import { useConnectionStore } from '../../store/connectionStore'
import { Toggle } from '../../components/Toggle/Toggle'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { toast } from 'react-hot-toast'
import './BotFlowList.css'
import Can from '../../components/Can/Can'
import usePermission from '../../hooks/usePermission'
import { SearchableSelect } from '../../components/SearchableSelect/SearchableSelect'

export const BotFlowList: React.FC = () => {
  const navigate = useNavigate()
  const { has } = usePermission()
  
  const {
    flows,
    totalCount,
    isLoading,
    page,
    pageSize,
    search,
    setPage,
    setPageSize,
    setSearch,
    fetchFlows,
    createFlow,
    updateFlow,
    deleteFlow,
    toggleFlowActive
  } = useBotFlowStore()
  const { connections, fetchConnections } = useConnectionStore()

  // UI state
  const [searchTerm, setSearchTerm] = useState(search)
  const [isModalOpen, setIsModalOpen] = useState(false)
  const [editingFlowId, setEditingFlowId] = useState<number | null>(null)

  // Form modal inputs
  const [modalName, setModalName] = useState('')
  const [modalDescription, setModalDescription] = useState('')
  const [modalConnectionId, setModalConnectionId] = useState<number | ''>('')
  const [formErrors, setFormErrors] = useState<Record<string, string>>({})

  // Delete modal state
  const [deleteModalOpen, setDeleteModalOpen] = useState(false)
  const [flowToDelete, setFlowToDelete] = useState<{ id: number; name: string } | null>(null)
  const [openActionsMenuId, setOpenActionsMenuId] = useState<number | null>(null)

  useEffect(() => {
    fetchFlows()
  }, [page, pageSize])

  useEffect(() => {
    fetchConnections()
  }, [fetchConnections])

  useEffect(() => {
    const delay = setTimeout(() => {
      setSearch(searchTerm)
      fetchFlows()
    }, 400)
    return () => clearTimeout(delay)
  }, [searchTerm])

  const handleOpenCreateModal = () => {
    setEditingFlowId(null)
    setModalName('')
    setModalDescription('')
    setModalConnectionId('')
    setFormErrors({})
    setIsModalOpen(true)
  }

  const handleOpenEditModal = (id: number, name: string, description?: string, connectionId?: number | null) => {
    setEditingFlowId(id)
    setModalName(name)
    setModalDescription(description || '')
    setModalConnectionId(connectionId ?? '')
    setFormErrors({})
    setIsModalOpen(true)
  }

  const handleModalSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    
    // Validate
    const errors: Record<string, string> = {}
    if (!modalName.trim()) {
      errors.name = 'Name is required'
    }
    
    if (Object.keys(errors).length > 0) {
      setFormErrors(errors)
      return
    }

    try {
      const connId = modalConnectionId === '' ? null : modalConnectionId
      if (editingFlowId !== null) {
        await updateFlow(editingFlowId, modalName.trim(), modalDescription.trim(), undefined, connId)
        toast.success('Bot flow updated successfully!')
      } else {
        await createFlow(modalName.trim(), modalDescription.trim(), connId)
        toast.success('Bot flow created successfully!')
      }
      setIsModalOpen(false)
    } catch (error) {
      toast.error('Failed to save bot flow.')
    }
  }

  const handleDeleteClick = (id: number, name: string) => {
    setFlowToDelete({ id, name })
    setDeleteModalOpen(true)
  }

  const confirmDelete = async () => {
    if (flowToDelete) {
      const success = await deleteFlow(flowToDelete.id)
      if (success) {
        toast.success(`Flow "${flowToDelete.name}" deleted successfully.`)
      } else {
        toast.error('Failed to delete flow.')
      }
    }
    setDeleteModalOpen(false)
    setFlowToDelete(null)
  }

  const totalPages = Math.ceil(totalCount / pageSize)

  return (
    <motion.div {...pageTransitionProps}>
      {/* Page hero — matches the host's banner treatment. Presentational only. */}
      <div className="omni-page-hero">
        <h1>Bot Flow</h1>
        <p>Design multi-step conversation flows for your WhatsApp bots.</p>
      </div>
      {/* Action buttons bar */}
      <div className="bot-flow-header-actions">
        <Can permission="BotFlow.Create">
          <button
            className="bot-flow-btn btn-primary"
            onClick={handleOpenCreateModal}
          >
            <Plus size={16} />
            Bot Flow
          </button>
        </Can>
      </div>

      {/* Toolbar card */}
      <div className="bot-flow-table-card">
        <div className="bot-flow-table-toolbar">
          <div className="toolbar-left">
            <button className="refresh-icon-btn" onClick={() => fetchFlows()}>
              <RefreshCw size={16} />
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

        {/* Data Table */}
        <div className="table-responsive">
          <table className="bot-flow-table">
            <thead>
              <tr>
                <th className="actions-col">ACTIONS</th>
                <th>ID</th>
                <th>NAME</th>
                <th>DESCRIPTION</th>
                <th>IS ACTIVE</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <tr>
                  <td colSpan={5} className="text-center py-4">
                    <div className="loader-spinner">Loading flows...</div>
                  </td>
                </tr>
              ) : flows.length === 0 ? (
                <tr>
                  <td colSpan={5} className="no-records-row">
                    No records found
                  </td>
                </tr>
              ) : (
                flows.map((flow) => (
                  <tr key={flow.id}>
                    <td className="actions-col">
                      <div className="contact-actions-menu-wrapper">
                        <Menu
                          open={openActionsMenuId === flow.id}
                          onOpenChange={(isOpen) =>
                            setOpenActionsMenuId(isOpen ? flow.id : null)
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
                          {/* Disabled rather than hidden — see the note in ContactsList.
                              "Flow" opens the designer, which saves as well as reads, so it
                              takes the edit grant rather than view. */}
                          <Can permission="BotFlow.Edit" mode="disable">
                            <MenuItem
                              className="contact-actions-item"
                              onSelect={() => navigate(`/bot-flow/designer/${flow.id}`)}
                            >
                              Flow
                            </MenuItem>
                          </Can>
                          <Can permission="BotFlow.Edit" mode="disable">
                            <MenuItem
                              className="contact-actions-item"
                              onSelect={() => handleOpenEditModal(flow.id, flow.name, flow.description, flow.connectionId)}
                            >
                              Edit
                            </MenuItem>
                          </Can>
                          <Can permission="BotFlow.Delete" mode="disable">
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              onSelect={() => handleDeleteClick(flow.id, flow.name)}
                            >
                              Delete
                            </MenuItem>
                          </Can>
                        </Menu>
                      </div>
                    </td>
                    <td>{flow.id}</td>
                    <td className="flow-name-cell">{flow.name}</td>
                    <td className="flow-desc-cell">{flow.description || ''}</td>
                    <td>
                      <Toggle
                        checked={flow.isActive}
                        disabled={!has('BotFlow.Edit')}
                        onChange={() => toggleFlowActive(flow.id)}
                      />
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination Toolbar */}
        {totalCount > 0 && (
          <div className="bot-flow-pagination">
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

      {/* Creation and editing modal popup */}
      <Modal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title="Bot Flow"
        size="md"
        footer={
          <>
            <button
              type="button"
              className="oc-dialog-btn oc-dialog-btn-secondary"
              onClick={() => setIsModalOpen(false)}
            >
              Cancel
            </button>
            <button
              type="submit"
              form="bot-flow-form"
              className="oc-dialog-btn oc-dialog-btn-primary"
            >
              Submit
            </button>
          </>
        }
      >
            <form id="bot-flow-form" onSubmit={handleModalSubmit}>
              <div className="popup-body">
                <div className="form-group">
                  <label className="required-label">Name</label>
                  <input
                    type="text"
                    value={modalName}
                    onChange={(e) => setModalName(e.target.value)}
                    placeholder="Enter flow name..."
                  />
                  {formErrors.name && <span className="error-text">{formErrors.name}</span>}
                </div>
                <div className="form-group">
                  <label>Description</label>
                  <textarea
                    rows={4}
                    value={modalDescription}
                    onChange={(e) => setModalDescription(e.target.value)}
                    placeholder="Enter flow description..."
                  />
                </div>
                <div className="form-group">
                  <label>Connection</label>
                  <SearchableSelect
                    label="Connection"
                    placeholder="All Connections"
                    value={modalConnectionId === '' ? '' : String(modalConnectionId)}
                    options={connections.map((conn) => ({
                      value: String(conn.id),
                      label: conn.name,
                      keywords: conn.phoneNumber ?? ''
                    }))}
                    onChange={(val) => setModalConnectionId(val ? Number(val) : '')}
                  />
                </div>
              </div>
            </form>
      </Modal>

      {/* Delete confirmation modal */}
      <ConfirmationModal
        isOpen={deleteModalOpen}
        title="Delete Bot Flow"
        message={`Are you sure you want to delete "${flowToDelete?.name}" bot flow? This action cannot be undone.`}
        confirmText="Delete"
        cancelText="Cancel"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => {
          setDeleteModalOpen(false)
          setFlowToDelete(null)
        }}
      />
    </motion.div>
  )
}
