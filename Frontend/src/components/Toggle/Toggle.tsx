import React from 'react'
import './Toggle.css'

interface ToggleProps {
  checked: boolean
  onChange: (checked: boolean) => void
  disabled?: boolean
  label?: string
}

export const Toggle: React.FC<ToggleProps> = ({
  checked,
  onChange,
  disabled = false,
  label = ''
}) => {
  const handleToggle = () => {
    if (!disabled) {
      onChange(!checked)
    }
  }

  return (
    <div className={`toggle-switch-container ${disabled ? 'disabled-toggle' : ''}`} onClick={handleToggle}>
      <input
        type="checkbox"
        className="toggle-switch-input"
        checked={checked}
        readOnly
        disabled={disabled}
      />
      <div className="toggle-switch-slider" />
      {label && <span className="form-toggle-label">{label}</span>}
    </div>
  )
}
export default Toggle
