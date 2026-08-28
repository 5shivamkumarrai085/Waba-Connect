import React from 'react'
import { X } from 'lucide-react'
import './DateRangePicker.css'

interface DateRangePickerProps {
  from: string | null | undefined
  to: string | null | undefined
  onChange: (range: { from: string | null; to: string | null }) => void
  /** Bounds the pickers to when data actually exists, so nobody picks a range that returns nothing. */
  min?: string | null
  max?: string | null
}

/**
 * A labelled From/To date pair with a single Clear control.
 *
 * Three pages (Activity Logs, Campaigns, Contacts) each hand-roll their own pair of
 * `<input type="date">` with no way to clear both at once. Pulled out here rather than left
 * duplicated a fourth time.
 */
export const DateRangePicker: React.FC<DateRangePickerProps> = ({ from, to, onChange, min, max }) => {
  const hasValue = Boolean(from || to)

  return (
    <div className="date-range-picker">
      <div className="date-range-field">
        <label htmlFor="date-range-from">From</label>
        <input
          id="date-range-from"
          type="date"
          className="form-control"
          value={from ?? ''}
          min={min ?? undefined}
          max={to ?? max ?? undefined}
          onChange={(e) => onChange({ from: e.target.value || null, to: to ?? null })}
        />
      </div>
      <div className="date-range-field">
        <label htmlFor="date-range-to">To</label>
        <input
          id="date-range-to"
          type="date"
          className="form-control"
          value={to ?? ''}
          min={from ?? min ?? undefined}
          max={max ?? undefined}
          onChange={(e) => onChange({ from: from ?? null, to: e.target.value || null })}
        />
      </div>
      {hasValue && (
        <button
          type="button"
          className="date-range-clear"
          onClick={() => onChange({ from: null, to: null })}
          aria-label="Clear date range"
          title="Clear date range"
        >
          <X size={14} />
        </button>
      )}
    </div>
  )
}

export default DateRangePicker
