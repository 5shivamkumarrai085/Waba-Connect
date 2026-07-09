import React from 'react'
import { Search } from 'lucide-react'
import './SearchBar.css'

interface SearchBarProps {
  value: string
  onChange: (value: string) => void
  placeholder?: string
}

export const SearchBar: React.FC<SearchBarProps> = ({
  value,
  onChange,
  placeholder = 'Search...'
}) => {
  return (
    <div className="search-bar-wrapper">
      <Search className="search-bar-icon" size={14} />
      <input
        type="text"
        className="search-bar-input"
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  )
}
