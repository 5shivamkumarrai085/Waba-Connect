import React, { useMemo, useState } from 'react'
import { Check, ChevronDown, X } from 'lucide-react'
import { Menu, MenuItem } from '../Menu/Menu'
import './MultiSelectChips.css'

export interface MultiSelectOption {
  value: string
  label: string
}

interface MultiSelectChipsProps {
  label: string
  options: MultiSelectOption[]
  selected: string[]
  onChange: (selected: string[]) => void
  placeholder?: string
  /** Shown under the trigger when there are no options to pick from yet. */
  emptyMessage?: string
}

/**
 * A labelled dropdown of checkable options, with the current selection rendered as removable
 * chips underneath.
 *
 * Built on the shared `Menu` rather than a new dropdown primitive — every other list-page filter
 * in the app already gets its portaling, collision handling and keyboard nav from there, and a
 * bespoke one here would immediately be the one filter control that clips inside a scroll
 * container.
 */
export const MultiSelectChips: React.FC<MultiSelectChipsProps> = ({
  label,
  options,
  selected,
  onChange,
  placeholder = 'Any',
  emptyMessage
}) => {
  const [isOpen, setIsOpen] = useState(false)

  const selectedSet = useMemo(() => new Set(selected), [selected])
  const labelFor = useMemo(() => {
    const map = new Map(options.map((o) => [o.value, o.label]))
    return (value: string) => map.get(value) ?? value
  }, [options])

  const toggle = (value: string) => {
    onChange(selectedSet.has(value) ? selected.filter((v) => v !== value) : [...selected, value])
  }

  const remove = (value: string) => onChange(selected.filter((v) => v !== value))

  return (
    <div className="ms-chips-field">
      <label className="ms-chips-label">{label}</label>

      <Menu
        open={isOpen}
        onOpenChange={setIsOpen}
        align="start"
        offset={6}
        matchTriggerWidth
        // Picking several values is the whole point of this control, so a selection must not
        // dismiss the menu — the same reasoning as ColumnSelector.
        closeOnSelect={false}
        className="ms-chips-dropdown"
        ariaLabel={`${label} options`}
        trigger={(props) => (
          <button
            {...props}
            type="button"
            className={`ms-chips-trigger${isOpen ? ' is-open' : ''}`}
          >
            <span className="ms-chips-trigger-text">
              {selected.length === 0 ? placeholder : `${selected.length} selected`}
            </span>
            <ChevronDown size={14} />
          </button>
        )}
      >
        {options.length === 0 ? (
          <div className="ms-chips-empty">{emptyMessage ?? 'Nothing to select yet.'}</div>
        ) : (
          options.map((option) => {
            const isSelected = selectedSet.has(option.value)
            return (
              <MenuItem
                key={option.value}
                className="ms-chips-item"
                aria-checked={isSelected}
                onSelect={() => toggle(option.value)}
              >
                <span className="ms-chips-item-check">{isSelected && <Check size={13} />}</span>
                <span className="ms-chips-item-label">{option.label}</span>
              </MenuItem>
            )
          })
        )}
      </Menu>

      {selected.length > 0 && (
        <div className="ms-chips-row">
          {selected.map((value) => (
            <span key={value} className="ms-chip">
              {labelFor(value)}
              <button
                type="button"
                className="ms-chip-remove"
                onClick={() => remove(value)}
                aria-label={`Remove ${labelFor(value)}`}
              >
                <X size={11} />
              </button>
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

export default MultiSelectChips
