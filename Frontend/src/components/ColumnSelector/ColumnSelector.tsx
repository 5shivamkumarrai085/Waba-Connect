import React, { useState, useRef, useEffect } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import './ColumnSelector.css'

interface ColumnSelectorProps {
  columns: { key: string; label: string }[]
  visibleColumns: Record<string, boolean>
  onToggle: (key: string) => void
}

export const ColumnSelector: React.FC<ColumnSelectorProps> = ({
  columns,
  visibleColumns,
  onToggle
}) => {
  const [isOpen, setIsOpen] = useState(false)
  const wrapperRef = useRef<HTMLDivElement>(null)

  // Close dropdown on click outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (wrapperRef.current && !wrapperRef.current.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [])

  return (
    <div className="column-selector-wrapper" ref={wrapperRef}>
      <button
        type="button"
        className={`column-selector-toggle-btn ${isOpen ? 'active' : ''}`}
        onClick={() => setIsOpen(!isOpen)}
        title="Column Visibility"
        aria-label="Toggle column visibility dropdown"
      >
        {isOpen ? <EyeOff size={16} /> : <Eye size={16} />}
      </button>

      {isOpen && (
        <div className="column-selector-dropdown">
          {columns.map((col) => {
            const isVisible = visibleColumns[col.key] !== false
            return (
              <div
                key={col.key}
                className="column-selector-item"
                onClick={() => onToggle(col.key)}
              >
                <span className="column-selector-name">{col.label}</span>
                <span className="column-selector-icon">
                  {isVisible ? <Eye size={14} /> : <EyeOff size={14} color="var(--error)" />}
                </span>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
export default ColumnSelector
