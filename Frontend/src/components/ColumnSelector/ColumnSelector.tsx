import React, { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import { Menu, MenuItem } from '../Menu/Menu'
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

  return (
    <Menu
      open={isOpen}
      onOpenChange={setIsOpen}
      align="start"
      offset={6}
      // Toggling several columns in a row is the whole point of this control,
      // so a pick must not dismiss it.
      closeOnSelect={false}
      className="column-selector-dropdown"
      ariaLabel="Column visibility"
      trigger={(props) => (
        <button
          {...props}
          type="button"
          className={`column-selector-toggle-btn ${isOpen ? 'active' : ''}`}
          title="Column Visibility"
          aria-label="Toggle column visibility dropdown"
        >
          {isOpen ? <EyeOff size={16} /> : <Eye size={16} />}
        </button>
      )}
    >
      {columns.map((col) => {
        const isVisible = visibleColumns[col.key] !== false
        return (
          <MenuItem
            key={col.key}
            className="column-selector-item"
            aria-checked={isVisible}
            onSelect={() => onToggle(col.key)}
          >
            <span className="column-selector-name">{col.label}</span>
            <span className="column-selector-icon">
              {isVisible ? <Eye size={14} /> : <EyeOff size={14} color="var(--error)" />}
            </span>
          </MenuItem>
        )
      })}
    </Menu>
  )
}
export default ColumnSelector
