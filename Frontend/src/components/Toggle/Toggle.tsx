import React from 'react'
import { motion } from 'framer-motion'
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
    <motion.div
      className={`toggle-switch-container ${disabled ? 'disabled-toggle' : ''}`}
      onClick={handleToggle}
      whileTap={{ scale: 0.9 }}
    >
      <input
        type="checkbox"
        className="toggle-switch-input"
        checked={checked}
        readOnly
        disabled={disabled}
      />
      <motion.div className="toggle-switch-slider" layout transition={{ type: 'spring', stiffness: 500, damping: 30 }} />
      {label && <span className="form-toggle-label">{label}</span>}
    </motion.div>
  )
}
export default Toggle
