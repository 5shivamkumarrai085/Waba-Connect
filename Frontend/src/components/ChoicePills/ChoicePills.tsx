import React from 'react'
import './ChoicePills.css'

export interface ChoicePillOption {
  value: string | number
  label: string
  /** Small uppercase chip inside the pill — the connection nickname, for instance. */
  badge?: string | null
  /** Dimmer trailing text, e.g. a phone number or email address. */
  hint?: string | null
  disabled?: boolean
  /** Explains a disabled option, surfaced as the pill's title attribute. */
  disabledReason?: string | null
}

interface ChoicePillsProps {
  options: ChoicePillOption[]
  selected: Array<string | number>
  onChange: (selected: Array<string | number>) => void
  /** Single-select behaves like a radio group: picking one replaces the selection. */
  multiple?: boolean
  /** Rendered in place of the pills when there is nothing to choose from. */
  emptyMessage?: string
  ariaLabel?: string
}

/**
 * The pill selector used for sender connections and relation types.
 *
 * Extracted because this markup was duplicated verbatim between CampaignWizard and BulkCampaign,
 * each with roughly twenty lines of inline styles and hardcoded hex — which is why those pills
 * are unreadable in dark mode. One component, tokens only, fixes both and gives the email channel
 * somewhere to reuse rather than a third copy.
 *
 * Buttons rather than checkboxes: the visual is a toggle group, and the existing markup was
 * already buttons, so this keeps the keyboard behaviour operators are used to.
 */
export const ChoicePills: React.FC<ChoicePillsProps> = ({
  options,
  selected,
  onChange,
  multiple = false,
  emptyMessage,
  ariaLabel
}) => {
  const toggle = (value: string | number) => {
    if (!multiple) {
      // Selecting the current value clears it, matching how the connection picker behaved
      // before — an operator could deselect to reset the template list.
      onChange(selected.includes(value) ? [] : [value])
      return
    }

    onChange(
      selected.includes(value) ? selected.filter((v) => v !== value) : [...selected, value]
    )
  }

  if (options.length === 0 && emptyMessage) {
    return <p className="choice-pills-empty">{emptyMessage}</p>
  }

  return (
    <div className="choice-pills" role="group" aria-label={ariaLabel}>
      {options.map((option) => {
        const isSelected = selected.includes(option.value)

        return (
          <button
            key={option.value}
            type="button"
            className={`choice-pill ${isSelected ? 'selected' : ''}`}
            aria-pressed={isSelected}
            disabled={option.disabled}
            title={option.disabled ? option.disabledReason ?? undefined : undefined}
            onClick={() => toggle(option.value)}
          >
            {isSelected && (
              <span className="choice-pill-check" aria-hidden="true">
                ✓
              </span>
            )}
            <span className="choice-pill-label">{option.label}</span>
            {option.badge && <span className="choice-pill-badge">{option.badge}</span>}
            {option.hint && <span className="choice-pill-hint">{option.hint}</span>}
          </button>
        )
      })}
    </div>
  )
}

export default ChoicePills
