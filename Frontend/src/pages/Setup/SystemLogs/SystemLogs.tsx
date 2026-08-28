import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { RefreshCw, Trash2, Search, FileWarning } from 'lucide-react'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import Can from '../../../components/Can/Can'
import { apiClient } from '../../../services/apiClient'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'

interface LogFile {
  name: string
  displayName: string
  sizeBytes: number
  modifiedAt: string
  isToday: boolean
}

interface LogEntry {
  level: string
  timestamp: string
  message: string
  exception?: string | null
  environment: string
  properties: Record<string, string>
  rawJson: string
}

/**
 * Serilog's six levels, with the labels operators expect.
 *
 * Deliberately not the nine PSR-3 levels the reference UI shows — this app runs on Serilog and
 * can never emit EMERGENCY, ALERT, NOTICE or LOCAL. Rendering pills that permanently show zero
 * is the same trap the old activity-log page fell into.
 */
const LEVELS = [
  { key: 'Fatal', label: 'CRITICAL', tone: 'critical' },
  { key: 'Error', label: 'ERROR', tone: 'error' },
  { key: 'Warning', label: 'WARNING', tone: 'warning' },
  { key: 'Information', label: 'INFO', tone: 'info' },
  { key: 'Debug', label: 'DEBUG', tone: 'debug' },
  { key: 'Verbose', label: 'TRACE', tone: 'trace' }
]

const formatSize = (bytes: number) => {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export const SystemLogs: React.FC = () => {
  const [files, setFiles] = useState<LogFile[]>([])
  const [selectedFile, setSelectedFile] = useState('')
  const [entries, setEntries] = useState<LogEntry[]>([])
  const [levelCounts, setLevelCounts] = useState<Record<string, number>>({})
  const [totalCount, setTotalCount] = useState(0)
  const [truncated, setTruncated] = useState(false)

  const [level, setLevel] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(15)

  const [isLoading, setIsLoading] = useState(true)
  const [viewing, setViewing] = useState<LogEntry | null>(null)
  const [expanded, setExpanded] = useState<Set<number>>(new Set())
  const [confirm, setConfirm] = useState<'delete' | 'clear' | null>(null)

  const loadFiles = async () => {
    try {
      const response = await apiClient.get('/setup/logs/files')
      const list: LogFile[] = response.data?.data ?? []
      setFiles(list)
      if (list.length > 0 && !list.some((f) => f.name === selectedFile)) {
        setSelectedFile(list[0].name)
      }
      if (list.length === 0) setIsLoading(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load log files.'))
      setIsLoading(false)
    }
  }

  const loadEntries = async () => {
    if (!selectedFile) return
    setIsLoading(true)
    try {
      const response = await apiClient.request({
        method: 'GET',
        url: '/setup/logs/entries',
        params: { file: selectedFile, level: level || undefined, search: search || undefined, page, pageSize }
      })
      const data = response.data?.data
      setEntries(data?.items ?? [])
      setLevelCounts(data?.levelCounts ?? {})
      setTotalCount(data?.totalCount ?? 0)
      setTruncated(data?.truncated ?? false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to read the log file.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void loadFiles() }, [])

  useEffect(() => {
    void loadEntries()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedFile, level, page, pageSize])

  useEffect(() => {
    const timer = setTimeout(() => { setPage(1); void loadEntries() }, 400)
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search])

  const confirmAction = async () => {
    try {
      if (confirm === 'delete') {
        const response = await apiClient.delete(`/setup/logs/files/${selectedFile}`)
        toast.success(response.data?.message ?? 'Log file deleted.')
      } else if (confirm === 'clear') {
        const response = await apiClient.delete('/setup/logs/files')
        toast.success(response.data?.message ?? 'Logs cleared.')
      }
      await loadFiles()
      void loadEntries()
    } catch (error) {
      // Expected when targeting today's file — Serilog holds it open.
      toast.error(getErrorMessage(error, 'The action could not be completed.'))
    } finally {
      setConfirm(null)
    }
  }

  const toggleExpanded = (index: number) =>
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(index)) next.delete(index)
      else next.add(index)
      return next
    })

  const currentFile = files.find((f) => f.name === selectedFile)
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Log Viewer</h1>
        <p>Application logs written by the server, newest entries first.</p>
      </div>

      <div className="contacts-card syslog-card">
        <div className="syslog-toolbar">
          <select
            className="contacts-pager-size-select syslog-file-select"
            value={selectedFile}
            onChange={(e) => { setSelectedFile(e.target.value); setPage(1) }}
            disabled={files.length === 0}
          >
            {files.length === 0 && <option value="">No log files</option>}
            {files.map((file) => (
              <option key={file.name} value={file.name}>
                {file.displayName} ({formatSize(file.sizeBytes)}){file.isToday ? ' — active' : ''}
              </option>
            ))}
          </select>

          <div className="syslog-toolbar-right">
            <Can permission="SystemLog.Delete">
              <button
                type="button"
                className="btn-bulk-delete"
                onClick={() => setConfirm('delete')}
                disabled={!currentFile || currentFile.isToday}
                title={currentFile?.isToday ? "Today's file is in use and rolls over at midnight" : 'Delete this log file'}
              >
                <Trash2 size={15} />
                <span>Delete</span>
              </button>
            </Can>
            <button type="button" className="btn-toolbar-tertiary" onClick={() => { void loadFiles(); void loadEntries() }}>
              <RefreshCw size={15} />
              <span>Refresh</span>
            </button>
            <Can permission="SystemLog.Clear">
              <button type="button" className="btn-bulk-delete" onClick={() => setConfirm('clear')}>
                <Trash2 size={15} />
                <span>Clear all logs</span>
              </button>
            </Can>
          </div>
        </div>

        <div className="syslog-filters">
          <div className="setup-search syslog-search">
            <Search size={15} className="setup-search-icon" />
            <input
              type="text"
              placeholder="Search logs..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>

          <select
            className="contacts-pager-size-select"
            value={pageSize}
            onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1) }}
          >
            {[15, 30, 50, 100].map((n) => <option key={n} value={n}>{n} per page</option>)}
          </select>
        </div>

        {/* Live counts come back with the entries, so the pills need no second request. */}
        <div className="syslog-levels">
          <button
            type="button"
            className={`syslog-level ${level === '' ? 'is-active' : ''}`}
            onClick={() => { setLevel(''); setPage(1) }}
          >
            ALL
          </button>
          {LEVELS.map((l) => (
            <button
              key={l.key}
              type="button"
              className={`syslog-level syslog-level-${l.tone} ${level === l.key ? 'is-active' : ''}`}
              onClick={() => { setLevel(level === l.key ? '' : l.key); setPage(1) }}
            >
              {l.label}
              <span className="syslog-level-count">{levelCounts[l.key] ?? 0}</span>
            </button>
          ))}
        </div>

        {truncated && (
          <div className="syslog-truncated">
            <FileWarning size={14} />
            <span>This file is very large — only the most recent portion was read.</span>
          </div>
        )}

        <div className="data-table-wrapper">
          {isLoading ? (
            <Skeleton variant="table" />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th className="actions-col">ACTIONS</th>
                  <th>LEVEL</th>
                  <th>DATE</th>
                  <th>CONTENT</th>
                </tr>
              </thead>
              <tbody>
                {entries.length === 0 ? (
                  <tr>
                    <td colSpan={4} className="no-records-row">
                      <EmptyState
                        iconName="FileBarChart2"
                        title={files.length === 0 ? 'No log files yet' : 'No matching entries'}
                        message={files.length === 0
                          ? 'Logs appear here once the server has written some.'
                          : 'Nothing in this file matches your filters.'}
                      />
                    </td>
                  </tr>
                ) : (
                  entries.map((entry, index) => (
                    <tr key={`${entry.timestamp}-${index}`}>
                      <td className="actions-col">
                        <button type="button" className="syslog-view" onClick={() => setViewing(entry)}>
                          View
                        </button>
                      </td>
                      <td>
                        <span className={`syslog-badge syslog-badge-${entry.level.toLowerCase()}`}>
                          {LEVELS.find((l) => l.key === entry.level)?.label ?? entry.level.toUpperCase()}
                        </span>
                      </td>
                      <td className="syslog-date">{new Date(entry.timestamp).toLocaleString()}</td>
                      <td>
                        <div className={`syslog-message ${expanded.has(index) ? 'is-expanded' : ''}`}>
                          {entry.message}
                        </div>
                        {entry.message.length > 110 && (
                          <button type="button" className="syslog-showmore" onClick={() => toggleExpanded(index)}>
                            {expanded.has(index) ? 'Show less' : 'Show more'}
                          </button>
                        )}
                      </td>
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
              Showing {(page - 1) * pageSize + 1} to {Math.min(page * pageSize, totalCount)} of {totalCount} entries
            </span>
            <div className="pager-navigation">
              <button type="button" className="btn-control-icon" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                Prev
              </button>
              <button type="button" className="btn-control-icon" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
                Next
              </button>
            </div>
          </div>
        )}
      </div>

      <Modal
        isOpen={viewing !== null}
        onClose={() => setViewing(null)}
        title={`Log Entry: ${viewing ? new Date(viewing.timestamp).toLocaleString() : ''}`}
        size="lg"
      >
        {viewing && (
          <div className="syslog-detail">
            <div className="activity-detail-row">
              <span>Level</span>
              <span>
                <span className={`syslog-badge syslog-badge-${viewing.level.toLowerCase()}`}>
                  {LEVELS.find((l) => l.key === viewing.level)?.label ?? viewing.level}
                </span>
              </span>
            </div>
            <div className="activity-detail-row"><span>Date</span><span>{new Date(viewing.timestamp).toLocaleString()}</span></div>
            <div className="activity-detail-row"><span>Environment</span><span>{viewing.environment}</span></div>

            <div className="syslog-detail-section">
              <strong>Content</strong>
              <p className="syslog-detail-message">{viewing.message}</p>
            </div>

            {viewing.exception && (
              <div className="activity-detail-error">
                <strong>Exception</strong>
                <pre className="syslog-detail-json">{viewing.exception}</pre>
              </div>
            )}

            <div className="syslog-detail-section">
              <strong>JSON Data</strong>
              {/* Pretty-printed on the server so nothing is guessed at here. */}
              <pre className="syslog-detail-json">{viewing.rawJson}</pre>
            </div>
          </div>
        )}
      </Modal>

      <ConfirmationModal
        isOpen={confirm !== null}
        title={confirm === 'clear' ? 'Clear All Logs' : 'Delete Log File'}
        message={confirm === 'clear'
          ? "Delete every log file? Today's active file is kept because the server is still writing to it. The deletion itself is recorded in the audit trail."
          : `Delete ${selectedFile}? This cannot be undone.`}
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={confirmAction}
        onCancel={() => setConfirm(null)}
      />
    </motion.div>
  )
}

export default SystemLogs
