import React, { useEffect, useMemo, useRef, useState } from 'react'
import { Check, ChevronDown, Search, X } from 'lucide-react'
import { Menu, MenuItem } from '../Menu/Menu'
import './SearchableSelect.css'

export interface SearchableSelectOption {
  value: string
  label: string
  /** Extra text to match on that is not shown in the row — an email behind a user's name, say. */
  keywords?: string
}

interface SearchableSelectProps {
  value: string
  onChange: (value: string) => void
  options: SearchableSelectOption[]
  /** Label for the "no choice made" row, e.g. "All Modules". Also the trigger's resting text. */
  placeholder?: string
  /**
   * The value that means "nothing chosen".
   *
   * Empty string by default, matching the empty <option> a native select carries. Some filter
   * screens in this app use the literal string "All" for the same idea, and they are drop-in
   * replacements only if the component can be told which sentinel they use.
   */
  allValue?: string
  /**
   * Suppresses the "all" row.
   *
   * Some selects have no empty option at all -- picking a connection, or a log file -- and others
   * already carry their own "All" entry inside the option list. Rendering an extra one would give
   * the user a choice the original control never offered, which is a behaviour change dressed as
   * a styling one.
   */
  hideAllOption?: boolean
  /** Rendered above the trigger, and wired to it for screen readers. */
  label?: string
  id?: string
  disabled?: boolean
  className?: string
  /**
   * Number of options from which the search box appears.
   *
   * Defaults to 1, i.e. always. An earlier version only showed it once the list was long enough to
   * scroll, on the reasoning that a search box over two options is clutter. In practice the
   * inconsistency was worse than the clutter: filter rows sit side by side, and one field
   * behaving differently from the one next to it reads as a bug rather than as a considered
   * choice. Raise it per call site if a particular list is genuinely better without one.
   */
  searchThreshold?: number
  emptyMessage?: string
}

/**
 * A single-select dropdown with an in-menu search box.
 *
 * <para>
 * The native `&lt;select&gt;` this replaces is fine for five options and hostile for fifty: the
 * browser's own popup cannot be searched beyond first-letter typeahead, it cannot be styled to
 * match the rest of the app, and on a long list — every module in the audit trail, every user who
 * has ever acted — finding the row means scrolling a system popup with no idea how far it runs.
 * </para>
 * <para>
 * Built on the shared `Menu` so it inherits the portaling and collision handling that stops a
 * dropdown being clipped inside a scroll container, and on the same option/check markup as
 * `MultiSelectChips`, so the single- and multi-select controls are visibly the same family.
 * </para>
 * <para>
 * Deliberately a drop-in for the `&lt;select&gt;` it replaces: `value` is a string, the empty
 * string means "nothing chosen", and `onChange` reports exactly what the select's
 * `event.target.value` would have. No call site has to change how it stores its filter.
 * </para>
 */
export const SearchableSelect: React.FC<SearchableSelectProps> = ({
  value,
  onChange,
  options,
  placeholder = 'All',
  allValue = '',
  hideAllOption = false,
  label,
  id,
  disabled = false,
  className,
  searchThreshold = 1,
  emptyMessage
}) => {
  const [isOpen, setIsOpen] = useState(false)
  const [query, setQuery] = useState('')
  const searchRef = useRef<HTMLInputElement>(null)

  const showSearch = options.length >= searchThreshold

  const selectedLabel = useMemo(
    () => (value === allValue ? '' : options.find((o) => o.value === value)?.label ?? ''),
    [options, value, allValue]
  )

  /**
   * Options matching what has been typed.
   *
   * Case-insensitive substring across the label, the underlying value and any extra keywords, so
   * a partial word finds its row wherever in the text it falls. Filtering is a view over the list
   * only — it never changes the selection, so the chosen value survives a term that hides it.
   */
  const visibleOptions = useMemo(() => {
    const term = query.trim().toLowerCase()
    if (!term) return options
    return options.filter(
      (o) =>
        o.label.toLowerCase().includes(term) ||
        o.value.toLowerCase().includes(term) ||
        (o.keywords ?? '').toLowerCase().includes(term)
    )
  }, [options, query])

  // A term left behind would silently hide most of the list the next time the menu is opened.
  useEffect(() => {
    if (!isOpen) setQuery('')
  }, [isOpen])

  const pick = (next: string) => {
    onChange(next)
    setIsOpen(false)
  }

  return (
    <Menu
      open={isOpen}
      onOpenChange={disabled ? () => {} : setIsOpen}
      align="start"
      offset={6}
      matchTriggerWidth
      className="ss-dropdown"
      ariaLabel={label ? `${label} options` : 'Options'}
      trigger={(props) => (
        <button
          {...props}
          id={id}
          type="button"
          disabled={disabled}
          className={`ss-trigger${isOpen ? ' is-open' : ''}${className ? ` ${className}` : ''}`}
        >
          <span className={`ss-trigger-text${selectedLabel ? ' is-selected' : ''}`}>
            {selectedLabel || placeholder}
          </span>
          <ChevronDown size={14} className="ss-trigger-chevron" />
        </button>
      )}
    >
      {showSearch && (
        <div className="ss-search">
          <Search size={13} className="ss-search-icon" />
          <input
            ref={searchRef}
            type="text"
            className="ss-search-input"
            placeholder="Search..."
            value={query}
            aria-label={label ? `Search ${label}` : 'Search options'}
            onChange={(e) => setQuery(e.target.value)}
            // Menu maps Home/End onto the option list. Inside a text box those keys belong to the
            // caret, so they stop here; Arrow Up/Down still travel on to the list, which is how you
            // get from the box into the results without reaching for the mouse.
            onKeyDown={(e) => {
              if (e.key === 'Home' || e.key === 'End') e.stopPropagation()
            }}
          />
          {query && (
            <button
              type="button"
              className="ss-search-clear"
              aria-label="Clear search"
              onClick={() => {
                setQuery('')
                searchRef.current?.focus()
              }}
            >
              <X size={12} />
            </button>
          )}
        </div>
      )}

      {/* The options scroll inside their own box rather than letting the surface grow.
          useAnchoredPosition sets an inline max-height on the surface from the space below the
          trigger, and an inline style beats any class -- so a list of forty modules opened forty
          rows tall on a tall window. Capping the list here instead leaves that viewport clamp
          intact as an outer limit, and keeps the search box pinned above the scroll rather than
          scrolling away with the first few options. */}
      <div className="ss-options">
        {/* The "all" row, matching the empty <option> the native select carried. Kept out of the
            filtered list so there is always a way back to unfiltered, however narrow the search. */}
        {!hideAllOption && (
          <MenuItem
            className="ss-item"
            aria-checked={value === allValue}
            onSelect={() => pick(allValue)}
          >
            <span className="ss-item-check">{value === allValue && <Check size={13} />}</span>
            <span className="ss-item-label">{placeholder}</span>
          </MenuItem>
        )}

        {options.length === 0 ? (
          <div className="ss-empty">{emptyMessage ?? 'Nothing to select yet.'}</div>
        ) : visibleOptions.length === 0 ? (
          <div className="ss-empty">No matches for &ldquo;{query.trim()}&rdquo;.</div>
        ) : (
          visibleOptions.map((option) => (
            <MenuItem
              key={option.value}
              className="ss-item"
              aria-checked={option.value === value}
              onSelect={() => pick(option.value)}
            >
              <span className="ss-item-check">
                {option.value === value && <Check size={13} />}
              </span>
              <span className="ss-item-label">{option.label}</span>
            </MenuItem>
          ))
        )}
      </div>
    </Menu>
  )
}

export default SearchableSelect
