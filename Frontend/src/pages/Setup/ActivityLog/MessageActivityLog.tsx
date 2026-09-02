import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { RefreshCw, Trash2, Eye, Search } from 'lucide-react'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import Can from '../../../components/Can/Can'
import usePermission from '../../../hooks/usePermission'
import { apiClient } from '../../../services/apiClient'
import { getErrorMessage } from '../../../utils/errorHelper'
import { formatAbsoluteDateTime } from '../../../utils/dateHelper'
import { pageTransitionProps } from '../../../utils/motion'
import { SearchableSelect } from '../../../components/SearchableSelect/SearchableSelect'

interface ActivityEntry {
  id: number
  category: string
  name?: string | null
  templateName?: string | null
  responseCode?: number | null
  relationType?: string | null
  contactPhone?: string | null
  isSuccess: boolean
  errorMessage?: string | null
  triggeredBy?: string | null
  /** Null for scheduler and webhook traffic, which has no originating request. */
  ipAddress?: string | null
  whatsAppMessageId?: string | null
  /** Redacted and capped server-side — see PayloadRedactor. Null for entries written before this shipped. */
  requestPayload?: string | null
  responsePayload?: string | null
  createdAt: string
}

/**
 * Pretty-prints a stored payload. Falls back to the raw string rather than throwing: a
 * truncated payload is deliberately not valid JSON, and showing it as-is is more useful than
 * showing nothing.
 */
const formatPayload = (payload?: string | null): string | null => {
  if (!payload) return null
  try {
    return JSON.stringify(JSON.parse(payload), null, 2)
  } catch {
    return payload
  }
}

export const MessageActivityLog: React.FC = () => {
  const { has } = usePermission()

  // Categories come from the log itself rather than a literal list. A hardcoded
  // ['All','Campaign','TemplateBot','InitiateChat'] meant any category introduced by a new send
  // path would silently be unfilterable.
  const [categories, setCategories] = useState<string[]>(['All'])
  const [entries, setEntries] = useState<ActivityEntry[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize] = useState(20)
  const [category, setCategory] = useState('All')
  const [search, setSearch] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const [selectedIds, setSelectedIds] = useState<number[]>([])
  const [viewing, setViewing] = useState<ActivityEntry | null>(null)
  const [confirm, setConfirm] = useState<'clear' | 'bulk' | null>(null)

  const load = async (showSpinner = true) => {
    if (showSpinner) setIsLoading(true)
    try {
      const response = await apiClient.request({
        method: 'GET',
        url: '/setup/activity-log',
        params: { search: search || undefined, category, page, pageSize }
      })
      setEntries(response.data?.data?.items ?? [])
      setTotalCount(response.data?.data?.totalCount ?? 0)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load the activity log.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, category])

  // Loaded once. "All" is prepended here rather than returned by the API, because it is a UI
  // affordance meaning "no filter", not a category any row actually carries.
  useEffect(() => {
    void (async () => {
      try {
        const response = await apiClient.get('/setup/activity-log/categories')
        const fetched: string[] = response.data?.data ?? []
        setCategories(['All', ...fetched])
      } catch {
        // A failed lookup leaves the filter at "All", which shows everything — strictly better
        // than blocking the page over a dropdown.
      }
    })()
  }, [])

  // Debounced so typing doesn't fire a request per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => {
      setPage(1)
      void load(false)
    }, 400)
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search])

  const handleDelete = async (id: number) => {
    try {
      await apiClient.delete(`/setup/activity-log/${id}`)
      toast.success('Log entry deleted.')
      void load(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to delete the entry.'))
    }
  }

  const confirmAction = async () => {
    try {
      if (confirm === 'clear') {
        await apiClient.delete('/setup/activity-log/clear')
        toast.success('Activity log cleared.')
      } else if (confirm === 'bulk') {
        await apiClient.post('/setup/activity-log/bulk-delete', { ids: selectedIds })
        toast.success(`${selectedIds.length} entries deleted.`)
        setSelectedIds([])
      }
      void load(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'The action could not be completed.'))
    } finally {
      setConfirm(null)
    }
  }

  const toggleSelect = (id: number) =>
    setSelectedIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]))

  const canDelete = has('ActivityLog.Delete')
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Activity Log</h1>
        <p>Every outbound template message and what WhatsApp returned.</p>
      </div>

      <div className="contacts-toolbar">
        <button type="button" className="btn-toolbar-tertiary" onClick={() => load()}>
          <RefreshCw size={15} />
          <span>Refresh</span>
        </button>
        <Can permission="ActivityLog.Clear">
          <button type="button" className="btn-bulk-delete" onClick={() => setConfirm('clear')}>
            <Trash2 size={15} />
            <span>Clear Log</span>
          </button>
        </Can>
        <Can permission="ActivityLog.Delete">
          <button
            type="button"
            className="btn-bulk-delete"
            disabled={selectedIds.length === 0}
            onClick={() => setConfirm('bulk')}
          >
            <Trash2 size={15} />
            <span>Bulk Delete({selectedIds.length})</span>
          </button>
        </Can>
      </div>

      <div className="contacts-card">
        <div className="contacts-controls-row">
          <div className="contacts-controls-left">
            <SearchableSelect
              label="Category"
              placeholder="All categories"
              allValue="All"
              hideAllOption
              value={category}
              options={categories.map((c) => ({
                value: c,
                label: c === 'All' ? 'All categories' : c
              }))}
              onChange={(val) => { setCategory(val); setPage(1) }}
            />
          </div>
          <div className="contacts-controls-right">
            <div className="setup-search">
              <Search size={15} className="setup-search-icon" />
              <input
                type="text"
                placeholder="Search id, category, name, template, phone, relation type..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
          </div>
        </div>

        <div className="data-table-wrapper">
          {isLoading ? (
            <Skeleton variant="table" />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  {/* Checkbox stays first, actions second: selection is a property of the row,
                      and the actions act on what is selected. */}
                  <th className="checkbox-cell" />
                  <th className="actions-col">ACTION</th>
                  <th>ID</th>
                  <th>CATEGORY</th>
                  <th>NAME</th>
                  <th>TEMPLATE NAME</th>
                  <th className="text-center">RESPONSE CODE</th>
                  <th>RELATION TYPE</th>
                  <th>CREATED AT</th>
                </tr>
              </thead>
              <tbody>
                {entries.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="no-records-row">
                      <EmptyState
                        iconName="History"
                        title="No activity yet"
                        message="Sends from campaigns, template bots and initiate-chat appear here."
                      />
                    </td>
                  </tr>
                ) : (
                  entries.map((entry) => (
                    <tr key={entry.id}>
                      <td className="checkbox-cell">
                        <input
                          type="checkbox"
                          checked={selectedIds.includes(entry.id)}
                          onChange={() => toggleSelect(entry.id)}
                          disabled={!canDelete}
                          aria-label={`Select entry ${entry.id}`}
                        />
                      </td>
                      <td className="actions-col">
                        <div className="activity-log-actions">
                          <button
                            type="button"
                            className="btn-control-icon"
                            onClick={() => setViewing(entry)}
                            aria-label="View entry"
                            title="View"
                          >
                            <Eye size={15} />
                          </button>
                          {canDelete && (
                            <button
                              type="button"
                              className="btn-control-icon activity-log-delete"
                              onClick={() => handleDelete(entry.id)}
                              aria-label="Delete entry"
                              title="Delete"
                            >
                              <Trash2 size={15} />
                            </button>
                          )}
                        </div>
                      </td>
                      <td>{entry.id}</td>
                      <td>{entry.category}</td>
                      <td className="setup-truncate" title={entry.name ?? ''}>{entry.name || '—'}</td>
                      <td>{entry.templateName || '—'}</td>
                      <td className="text-center">
                        {/* Colour follows success, not the raw number: a null code means the
                            request never reached Meta at all. */}
                        <span className={`setup-role-badge ${entry.isSuccess ? 'admin' : 'muted'}`}>
                          {entry.responseCode ?? '—'}
                        </span>
                      </td>
                      <td>
                        {entry.relationType
                          ? <span className="setup-role-badge">{entry.relationType}</span>
                          : '—'}
                      </td>
                      <td>{formatAbsoluteDateTime(entry.createdAt)}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          )}
        </div>

        {totalCount > 0 && (
          <div className="contacts-table-footer">
            <span className="contacts-pager-info">
              Showing {(page - 1) * pageSize + 1} to {Math.min(page * pageSize, totalCount)} of {totalCount} Results
            </span>
            <div className="pager-navigation">
              <button
                type="button"
                className="btn-control-icon"
                disabled={page <= 1}
                onClick={() => setPage((p) => p - 1)}
              >
                Prev
              </button>
              <button
                type="button"
                className="btn-control-icon"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>

      <Modal
        isOpen={viewing !== null}
        onClose={() => setViewing(null)}
        title={`Activity Entry #${viewing?.id ?? ''}`}
        size="md"
      >
        {viewing && (
          <div className="activity-detail">
            <div className="activity-detail-row"><span>Category</span><span>{viewing.category}</span></div>
            <div className="activity-detail-row"><span>Name</span><span>{viewing.name || '—'}</span></div>
            <div className="activity-detail-row"><span>Template</span><span>{viewing.templateName || '—'}</span></div>
            <div className="activity-detail-row"><span>Recipient</span><span>{viewing.contactPhone || '—'}</span></div>
            <div className="activity-detail-row"><span>Relation type</span><span>{viewing.relationType || '—'}</span></div>
            <div className="activity-detail-row"><span>Response code</span><span>{viewing.responseCode ?? 'No response'}</span></div>
            <div className="activity-detail-row"><span>Result</span><span>{viewing.isSuccess ? 'Delivered to WhatsApp' : 'Failed'}</span></div>
            <div className="activity-detail-row"><span>Triggered by</span><span>{viewing.triggeredBy || '—'}</span></div>
            <div className="activity-detail-row">
              <span>IP address</span>
              <span className="activity-detail-mono">{viewing.ipAddress || '—'}</span>
            </div>
            <div className="activity-detail-row"><span>Message ID</span><span className="activity-detail-mono">{viewing.whatsAppMessageId || '—'}</span></div>
            <div className="activity-detail-row"><span>Time</span><span>{formatAbsoluteDateTime(viewing.createdAt)}</span></div>
            {viewing.errorMessage && (
              <div className="activity-detail-error">
                <strong>Error</strong>
                <p>{viewing.errorMessage}</p>
              </div>
            )}

            {formatPayload(viewing.requestPayload) && (
              <div className="activity-payload-block">
                <strong>Raw content sent</strong>
                <pre>{formatPayload(viewing.requestPayload)}</pre>
              </div>
            )}

            {formatPayload(viewing.responsePayload) && (
              <div className="activity-payload-block">
                <strong>Response from WhatsApp</strong>
                <pre>{formatPayload(viewing.responsePayload)}</pre>
              </div>
            )}

            {!viewing.requestPayload && !viewing.responsePayload && (
              <p className="activity-payload-empty">
                No payload was captured for this entry — it predates payload logging.
              </p>
            )}
          </div>
        )}
      </Modal>

      <ConfirmationModal
        isOpen={confirm !== null}
        title={confirm === 'clear' ? 'Clear Activity Log' : 'Delete Entries'}
        message={confirm === 'clear'
          ? 'Delete every entry in the activity log? This cannot be undone — the fact that it was cleared is recorded in the audit trail.'
          : `Delete ${selectedIds.length} selected entries? This cannot be undone.`}
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={confirmAction}
        onCancel={() => setConfirm(null)}
      />
    </motion.div>
  )
}

export default MessageActivityLog
