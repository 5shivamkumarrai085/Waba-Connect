import React from 'react'
import { CheckCircle, AlertTriangle } from 'lucide-react'
import './CsvRowErrors.css'

/**
 * Matches the backend's CsvRowError (Models/DTOs/Common/CsvDtos.cs), which both the contacts
 * importer and the bulk-campaign importer report through.
 */
export interface CsvRowErrorItem {
  rowNumber: number
  column?: string | null
  value: string
  reason: string
}

interface CsvRowErrorsProps {
  /** Summary line. Omit to render only the error list. */
  summary?: React.ReactNode
  /** Renders the summary in a warning palette — for "nothing was imported" outcomes. */
  summaryIsWarning?: boolean
  errors: CsvRowErrorItem[]
  /**
   * Guards against a pathological file. A 5,000-row file where every row fails would otherwise
   * put 5,000 list items in the DOM; the count still tells the user the true scale.
   */
  maxVisibleErrors?: number
}

/**
 * Shows what a CSV upload actually did, and names every row it could not use.
 *
 * Shared so the two importers cannot drift apart. The contacts importer previously had no
 * error UI at all — a rejected file produced one toast reading "wrong format csv file", which
 * gave the user no way to find the offending row.
 */
export const CsvRowErrors: React.FC<CsvRowErrorsProps> = ({
  summary,
  summaryIsWarning = false,
  errors,
  maxVisibleErrors = 100
}) => {
  const visible = errors.slice(0, maxVisibleErrors)
  const hidden = errors.length - visible.length

  return (
    <>
      {summary && (
        <div className={`csv-summary-box fade-in${summaryIsWarning ? ' is-empty' : ''}`}>
          {summaryIsWarning
            ? <AlertTriangle size={16} className="csv-summary-icon" />
            : <CheckCircle size={16} className="csv-summary-icon" />}
          <div className="csv-summary-content">
            <span className="csv-summary-title">Note:</span>
            <p className="csv-summary-text">{summary}</p>
          </div>
        </div>
      )}

      {errors.length > 0 && (
        <div className="csv-errors-box fade-in">
          <span className="csv-errors-title">Row Errors:</span>
          <ul className="csv-errors-list">
            {visible.map((e, idx) => (
              <li key={`${e.rowNumber}-${e.column ?? ''}-${idx}`}>
                Row {e.rowNumber}{e.column ? ` (${e.column})` : ''}: "{e.value}" — {e.reason}
              </li>
            ))}
          </ul>
          {hidden > 0 && (
            <p className="csv-errors-truncated">
              …and {hidden} more row{hidden === 1 ? '' : 's'} with errors.
            </p>
          )}
        </div>
      )}
    </>
  )
}

export default CsvRowErrors
