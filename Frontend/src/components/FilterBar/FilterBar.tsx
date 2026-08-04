import React from 'react'
import { motion } from 'framer-motion'
import './FilterBar.css'

interface FilterBarProps {
  options: string[]
  activeOption: string
  onChange: (option: any) => void
  // Optional display-label override per option value (e.g. { week: 'This Week' }).
  // Options without an entry (or when the prop is omitted entirely) render as-is,
  // so existing callers keep their exact current appearance.
  labels?: Record<string, string>
}

export const FilterBar: React.FC<FilterBarProps> = ({
  options,
  activeOption,
  onChange,
  labels
}) => {
  return (
    <div className="filter-bar">
      {options.map((option) => (
        <motion.button
          key={option}
          className={`filter-btn ${activeOption === option ? 'active' : ''}`}
          onClick={() => onChange(option)}
          whileHover={{ scale: 1.03 }}
          whileTap={{ scale: 0.97 }}
          transition={{ duration: 0.12 }}
        >
          {labels?.[option] ?? option}
          {activeOption === option && (
            <motion.div
              className="filter-btn-indicator"
              layoutId="filter-active-indicator"
              transition={{ type: 'spring', stiffness: 400, damping: 30 }}
            />
          )}
        </motion.button>
      ))}
    </div>
  )
}
