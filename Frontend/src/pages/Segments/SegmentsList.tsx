import React, { useCallback, useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { useNavigate, Link, useSearchParams } from 'react-router-dom'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import toast from 'react-hot-toast'
import { pageTransitionProps } from '../../utils/motion'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../components/EmptyState/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { Pagination } from '../../components/Pagination/Pagination'
import Can from '../../components/Can/Can'
import usePermission from '../../hooks/usePermission'
import { segmentService, type Segment } from '../../services/segments/segmentService'
import { formatAbsoluteDateTime, formatRelativeTime } from '../../utils/dateHelper'
import { getErrorMessage } from '../../utils/errorHelper'
import './Segments.css'

const PAGE_SIZES = [10, 25, 50]

/** Saved, rule-based audiences. Membership is recomputed whenever a segment is used. */
export const SegmentsList: React.FC = () => {
  const navigate = useNavigate()
  const { has } = usePermission()
  const canManage = has('Segment.Manage')
  const [segments, setSegments] = useState<Segment[]>([])
  const [totalCount, setTotalCount] = useState(0)
  // Search, page and page size live in the URL, so a refresh or a shared link keeps them.
  const [params, setParams] = useSearchParams()
  const search = params.get('q') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)
  const pageSize = PAGE_SIZES.includes(Number(params.get('size'))) ? Number(params.get('size')) : PAGE_SIZES[1]
  const setQuery = (patch: { q?: string; page?: number; size?: number }) => {
    const next = new URLSearchParams(params)
    const apply = (key: string, value: string | number | undefined, fallback: string | number) => {
      if (value === undefined) return
      if (value === '' || value === fallback) next.delete(key)
      else next.set(key, String(value))
    }
    apply('q', patch.q, '')
    apply('page', patch.page, 1)
    apply('size', patch.size, PAGE_SIZES[1])
    setParams(next, { replace: true })
  }
  const setSearch = (q: string) => setQuery({ q, page: 1 })
  const setPage = (p: number) => setQuery({ page: p })
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Segment | null>(null)

  const load = useCallback(async () => {
    setIsLoading(true)
    setError(null)
    try {
      const result = await segmentService.list({ search, page, pageSize })
      setSegments(result.items)
      setTotalCount(result.totalCount)
    } catch (e) {
      setError(getErrorMessage(e, 'Segments could not be loaded.'))
    } finally {
      setIsLoading(false)
    }
  }, [search, page, pageSize])

  useEffect(() => {
    const timer = setTimeout(() => void load(), search ? 300 : 0)
    return () => clearTimeout(timer)
  }, [load, search])

  const handleDelete = async () => {
    if (!deleteTarget) return
    const target = deleteTarget
    setDeleteTarget(null)
    try {
      await segmentService.remove(target.id)
      toast.success(`Segment “${target.name}” deleted.`)
      await load()
    } catch (e) {
      toast.error(getErrorMessage(e, 'The segment could not be deleted.'))
    }
  }

  const numbers = new Intl.NumberFormat()
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  const content = () => {
    if (isLoading && segments.length === 0) return <Skeleton variant="table" />
    if (error) {
      return <EmptyState iconName="AlertCircle" title="Segments could not be loaded" message={error}
        action={{ label: 'Try Again', onClick: () => void load() }} />
    }
    if (segments.length === 0) {
      return search
        ? <EmptyState iconName="SlidersHorizontal" title="No matching segments" message={`Nothing matches “${search}”.`}
            action={{ label: 'Clear Search', onClick: () => setSearch('') }} />
        : <EmptyState iconName="SlidersHorizontal" title="No segments yet"
            message="Create one to target people by what they did, not just who they are. Segments update by themselves."
            action={canManage ? { label: 'New Segment', onClick: () => navigate('/segments/new') } : undefined} />
    }
    return (
      <table className="data-table">
        <thead>
          <tr>
            <th scope="col">Name</th>
            <th scope="col" className="text-right">Contacts</th>
            <th scope="col">Updated</th>
            <th scope="col" className="text-right"><span className="sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          {segments.map(segment => (
            <tr key={segment.id}>
              <td className="segments-name-cell">
                <Link className="segments-name" to={`/segments/${segment.id}`}>{segment.name}</Link>
                {segment.description && <span className="segments-desc">{segment.description}</span>}
              </td>
              <td className="text-right" title={segment.countedAt ? `Counted ${formatAbsoluteDateTime(segment.countedAt)}` : 'Not counted yet'}>
                {segment.cachedCount != null ? numbers.format(segment.cachedCount) : '—'}
              </td>
              <td>
                <time dateTime={segment.updatedAt ?? segment.createdAt} title={formatAbsoluteDateTime(segment.updatedAt ?? segment.createdAt)}>
                  {formatRelativeTime(segment.updatedAt ?? segment.createdAt)}
                </time>
              </td>
              <td className="segments-actions">
                <Can permission="Segment.Manage">
                  <Link className="segments-icon-btn" to={`/segments/${segment.id}`} aria-label={`Edit ${segment.name}`} title="Edit">
                    <Pencil size={15} aria-hidden="true" />
                  </Link>
                  <button type="button" className="segments-icon-btn is-danger" aria-label={`Delete ${segment.name}`} title="Delete"
                    onClick={() => setDeleteTarget(segment)}>
                    <Trash2 size={15} aria-hidden="true" />
                  </button>
                </Can>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    )
  }

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Segments</h1>
        <p>Audiences defined by rules — contact fields, consent and engagement. Membership is worked out again every time a campaign uses one.</p>
      </div>

      <div className="contacts-toolbar segments-toolbar">
        <Can permission="Segment.Manage">
          <button type="button" className="btn-toolbar-primary" onClick={() => navigate('/segments/new')}>
            <Plus size={15} aria-hidden="true" />
            <span>New Segment</span>
          </button>
        </Can>
        <SearchBar value={search} onChange={setSearch} placeholder="Search segments…" />
      </div>

      <div className="contacts-card">
        <div className="data-table-wrapper">{content()}</div>
        {totalCount > 0 && (
          <Pagination
            page={page}
            pageSize={pageSize}
            totalCount={totalCount}
            totalPages={totalPages}
            onPage={setPage}
            onPageSize={size => setQuery({ size, page: 1 })}
            pageSizeOptions={PAGE_SIZES}
            itemLabel="segments"
          />
        )}
      </div>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Segment"
        message={`Delete “${deleteTarget?.name ?? ''}”? Campaigns already sent keep their recipients.`}
        confirmText="Delete Segment"
        isDestructive
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default SegmentsList
