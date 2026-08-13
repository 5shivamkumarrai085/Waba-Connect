import React from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'

interface PaginationProps {
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  onPage: (page: number) => void
  onPageSize: (pageSize: number) => void
  pageSizeOptions?: number[]
}

/**
 * Footer controls for a server-paged table.
 *
 * `totalCount` is the count over the whole filtered query, not the length of the fetched page —
 * so "of 12,458 results" is the truth rather than the size of the current slice.
 *
 * Shared by all three activity tabs. It was written inline for the audit table first; copying it
 * twice more for the sign-in tabs would have meant three places to fix the next off-by-one.
 * Styles live in ActivityLogs.css under the `.activity-pagination` prefix.
 */
export const Pagination: React.FC<PaginationProps> = ({
  page,
  pageSize,
  totalCount,
  totalPages,
  onPage,
  onPageSize,
  pageSizeOptions = [10, 25, 50, 100]
}) => {
  // An empty table has nothing to page through, and "Showing 1 to 0 of 0" reads like a bug.
  if (totalCount === 0) return null

  const firstRow = (page - 1) * pageSize + 1
  const lastRow = Math.min(page * pageSize, totalCount)

  return (
    <div className="activity-pagination">
      <div className="activity-pagination-summary">
        Showing {firstRow.toLocaleString()} to {lastRow.toLocaleString()} of{' '}
        {totalCount.toLocaleString()} results
      </div>
      <div className="activity-pagination-controls">
        <button
          type="button"
          className="activity-page-btn"
          onClick={() => onPage(page - 1)}
          disabled={page <= 1}
          aria-label="Previous page"
        >
          <ChevronLeft size={15} />
        </button>
        <span className="activity-page-indicator">
          Page {page} of {Math.max(totalPages, 1)}
        </span>
        <button
          type="button"
          className="activity-page-btn"
          onClick={() => onPage(page + 1)}
          disabled={page >= totalPages}
          aria-label="Next page"
        >
          <ChevronRight size={15} />
        </button>
        <label className="activity-page-size">
          <span>Rows</span>
          <select
            className="form-control"
            value={pageSize}
            onChange={(e) => onPageSize(Number(e.target.value))}
            aria-label="Rows per page"
          >
            {pageSizeOptions.map((size) => (
              <option key={size} value={size}>{size}</option>
            ))}
          </select>
        </label>
      </div>
    </div>
  )
}

export default Pagination
