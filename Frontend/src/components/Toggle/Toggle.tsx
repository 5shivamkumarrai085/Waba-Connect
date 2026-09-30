import React, { useId } from 'react'
import './Toggle.css'

interface ToggleProps {
  checked: boolean
  onChange: (checked: boolean) => void
  disabled?: boolean
  /** Visible text beside the switch. */
  label?: string
  /** Accessible name when there is no visible label (e.g. a switch inside a table row). */
  ariaLabel?: string
  /** Why the switch is disabled, shown as a tooltip. */
  disabledReason?: string
}

/**
 * An on/off switch. A real checkbox with role="switch" underneath, so it is reachable with Tab,
 * toggled with Space, and announced as "switch, on/off" by screen readers.
 */
export const Toggle: React.FC<ToggleProps> = ({
  checked,
  onChange,
  disabled = false,
  label = '',
  ariaLabel,
  disabledReason
}) => {
  const id = useId()

  return (
    <label
      htmlFor={id}
      className={`toggle-switch-container${disabled ? ' disabled-toggle' : ''}`}
      title={disabled ? disabledReason : undefined}
    >
      <input
        id={id}
        type="checkbox"
        role="switch"
        className="toggle-switch-input"
        checked={checked}
        aria-checked={checked}
        aria-label={label ? undefined : ariaLabel}
        disabled={disabled}
        onChange={e => onChange(e.target.checked)}
        // Tables often open a row on click; toggling a switch must not do that too.
        onClick={e => e.stopPropagation()}
      />
      <span className="toggle-switch-slider" aria-hidden="true" />
      {label && <span className="form-toggle-label">{label}</span>}
    </label>
  )
}

export default Toggle
