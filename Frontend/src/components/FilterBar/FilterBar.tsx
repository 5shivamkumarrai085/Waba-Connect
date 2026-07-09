import React from 'react'
import './FilterBar.css'

interface FilterBarProps {
  options: string[]
  activeOption: string
  onChange: (option: any) => void
}

export const FilterBar: React.FC<FilterBarProps> = ({
  options,
  activeOption,
  onChange
}) => {
  return (
    <div className="filter-bar">
      {options.map((option) => (
        <button
          key={option}
          className={`filter-btn ${activeOption === option ? 'active' : ''}`}
          onClick={() => onChange(option)}
        >
          {option}
        </button>
      ))}
    </div>
  )
}
