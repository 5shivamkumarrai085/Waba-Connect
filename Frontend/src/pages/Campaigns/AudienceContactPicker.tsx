import React, { useEffect, useId, useMemo, useState } from 'react'
import { AlertCircle, Users } from 'lucide-react'
import { contactService } from '../../services/contacts/contactService'
import type { PagedResult } from '../../services/pagination'
import type { Contact, ContactSource, ContactStatus } from '../../types/contacts'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { Pagination } from '../../components/Pagination/Pagination'
import { Skeleton } from '../../components/Skeleton'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'

interface AudienceContactPickerProps {
  /** The contact types chosen on Basic Info (their stored values). */
  relationTypes: string[]
  /** Display names for type values, from the managed Contact types list. */
  typeLabels: Record<string, string>
  isEmailChannel: boolean
  statuses: ContactStatus[]
  sources: ContactSource[]
  selectAll: boolean
  onSelectAllChange: (value: boolean) => void
  selectedIds: number[]
  onSelectedIdsChange: (ids: number[]) => void
  /** Reports how many contacts "select all" would include, for the recipients badge. */
  onSelectAllCountChange: (count: number) => void
  onEditTypes: () => void
}

const PAGE_SIZES = [10, 25, 50, 100]

/**
 * The contacts step of the campaign wizard. Everything is filtered, searched and paged on the
 * server, so it works the same for fifty contacts or five million: the browser only ever holds
 * one page. Selected ids persist across pages; "Select all" is resolved by the server when the
 * campaign is saved, so it is never limited by what was loaded here.
 */
export const AudienceContactPicker: React.FC<AudienceContactPickerProps> = ({
  relationTypes, typeLabels, isEmailChannel, statuses, sources,
  selectAll, onSelectAllChange, selectedIds, onSelectedIdsChange, onSelectAllCountChange, onEditTypes
}) => {
  const id = useId()
  const [typeCounts, setTypeCounts] = useState<Record<string, number> | null>(null)
  const [status, setStatus] = useState('')
  const [source, setSource] = useState('')
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZES[1])
  const [result, setResult] = useState<PagedResult<Contact> | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [reload, setReload] = useState(0)

  const typesParam = relationTypes.join(',')

  useEffect(() => {
    contactService.getTypeCounts().then(setTypeCounts).catch(() => setTypeCounts({}))
  }, [])

  // How many "select all" includes: every active contact of the chosen types.
  const selectAllCount = useMemo(
    () => relationTypes.reduce((sum, t) => sum + (typeCounts?.[t] ?? 0), 0),
    [relationTypes, typeCounts]
  )
  useEffect(() => { onSelectAllCountChange(selectAllCount) }, [selectAllCount, onSelectAllCountChange])

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300)
    return () => clearTimeout(timer)
  }, [search])

  // A new filter starts from the first page.
  useEffect(() => { setPage(1) }, [typesParam, status, source, debouncedSearch, pageSize])

  useEffect(() => {
    if (selectAll || relationTypes.length === 0) return
    let active = true
    setLoading(true)
    setError(null)
    contactService.getContactsPage({ page, pageSize, type: typesParam, status, source, search: debouncedSearch, sortBy: 'name', sortDescending: false })
      .then(r => { if (active) setResult(r) })
      .catch(err => { if (active) setError(err instanceof Error ? err.message : 'Contacts could not be loaded.') })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [selectAll, typesParam, status, source, debouncedSearch, page, pageSize, reload, relationTypes.length])

  const rows = result?.items ?? []
  const pageIds = rows.map(c => c.id)
  const allOnPage = pageIds.length > 0 && pageIds.every(x => selectedIds.includes(x))
  const someOnPage = pageIds.some(x => selectedIds.includes(x))

  const togglePage = (checked: boolean) =>
    onSelectedIdsChange(checked
      ? Array.from(new Set([...selectedIds, ...pageIds]))
      : selectedIds.filter(x => !pageIds.includes(x)))

  const toggleOne = (contactId: number) =>
    onSelectedIdsChange(selectedIds.includes(contactId) ? selectedIds.filter(x => x !== contactId) : [...selectedIds, contactId])

  const label = (value: string) => typeLabels[value] ?? value
  const format = (n: number) => n.toLocaleString()

  return (
    <div className="audience-picker">
      <section className="audience-types" aria-labelledby={`${id}-types`}>
        <div className="audience-types-head">
          <h4 id={`${id}-types`}>Contact types</h4>
          <button type="button" className="audience-link" onClick={onEditTypes}>Change</button>
        </div>
        {relationTypes.length === 0 ? (
          <p className="audience-hint">No contact type is selected. Choose at least one on Basic Info.</p>
        ) : (
          <ul className="audience-type-list">
            {relationTypes.map(t => (
              <li key={t}>
                <span className="audience-type-name">{label(t)}</span>
                <span className="audience-type-count">{typeCounts ? format(typeCounts[t] ?? 0) : '…'}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <label className={`audience-all${selectAll ? ' is-on' : ''}`} htmlFor={`${id}-all`}>
        <input id={`${id}-all`} type="checkbox" checked={selectAll} onChange={e => onSelectAllChange(e.target.checked)} />
        <span className="audience-all-text">
          <strong>Send to every contact of these types</strong>
          <span>Resolved when the campaign sends, so contacts added later are included too.</span>
        </span>
        <span className="audience-all-count" aria-label={`${format(selectAllCount)} contacts`}>
          <Users size={16} aria-hidden="true" /> {format(selectAllCount)}
        </span>
      </label>

      {!selectAll && relationTypes.length > 0 && (
        <div className="audience-list">
          <div className="audience-toolbar">
            <div className="audience-filter">
              <label className="form-label" htmlFor={`${id}-status`}>Status</label>
              <select id={`${id}-status`} className="form-control" value={status} onChange={e => setStatus(e.target.value)}>
                <option value="">All statuses</option>
                {statuses.map(s => <option key={s.id} value={String(s.id)}>{s.name}</option>)}
              </select>
            </div>
            <div className="audience-filter">
              <label className="form-label" htmlFor={`${id}-source`}>Source</label>
              <select id={`${id}-source`} className="form-control" value={source} onChange={e => setSource(e.target.value)}>
                <option value="">All sources</option>
                {sources.map(s => <option key={s.id} value={String(s.id)}>{s.name}</option>)}
              </select>
            </div>
            <div className="audience-search">
              <SearchBar value={search} onChange={setSearch} placeholder="Search name, phone or email…" />
            </div>
          </div>

          <div className="audience-selected" aria-live="polite">
            <strong>{format(selectedIds.length)}</strong> selected
            {selectedIds.length > 0 && (
              <button type="button" className="audience-link" onClick={() => onSelectedIdsChange([])}>Clear</button>
            )}
          </div>

          {error ? (
            <div className="audience-error" role="alert">
              <AlertCircle size={16} aria-hidden="true" />
              <span>{error}</span>
              <button type="button" className="audience-link" onClick={() => setReload(r => r + 1)}>Try again</button>
            </div>
          ) : loading && !result ? (
            <Skeleton variant="table" />
          ) : rows.length === 0 ? (
            <p className="audience-empty">No contacts match these filters.</p>
          ) : (
            <div className="data-table-wrapper audience-table" aria-busy={loading}>
              <table className="data-table">
                <thead>
                  <tr>
                    <th className="checkbox-cell">
                      <input type="checkbox" aria-label="Select everyone on this page" checked={allOnPage}
                        ref={el => { if (el) el.indeterminate = !allOnPage && someOnPage }}
                        onChange={e => togglePage(e.target.checked)} />
                    </th>
                    <th>Name</th>
                    <th>Type</th>
                    <th>{isEmailChannel ? 'Email' : 'Phone'}</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map(c => {
                    const name = c.name || `${c.firstName || ''} ${c.lastName || ''}`.trim()
                    return (
                      <tr key={c.id} className={selectedIds.includes(c.id) ? 'is-selected' : undefined}>
                        <td className="checkbox-cell">
                          <input type="checkbox" aria-label={`Select ${name}`} checked={selectedIds.includes(c.id)} onChange={() => toggleOne(c.id)} />
                        </td>
                        <td className="audience-name">{name}</td>
                        <td><span className="audience-type-tag">{label(c.type)}</span></td>
                        <td className="audience-address">
                          {isEmailChannel
                            ? (c.email || <span className="contacts-missing-value">No email address</span>)
                            : c.phone}
                        </td>
                        <td><StatusBadge type="info" text={statuses.find(s => String(s.id) === c.status)?.name ?? c.status} /></td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}

          {result && (
            <Pagination page={page} pageSize={pageSize} totalCount={result.totalCount} totalPages={result.totalPages}
              onPage={setPage} onPageSize={setPageSize} pageSizeOptions={PAGE_SIZES} itemLabel="contacts" />
          )}
        </div>
      )}
    </div>
  )
}

export default AudienceContactPicker
