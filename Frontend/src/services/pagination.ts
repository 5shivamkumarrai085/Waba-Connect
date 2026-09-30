import { apiClient } from './apiClient'

/** The server's page-size ceiling (PagedRequest.MaxPageSize). Asking for more returns this many. */
export const MAX_PAGE_SIZE = 200

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

/** Reads the backend's `{ data: { items, totalCount, page, pageSize } }` envelope. */
export const readPaged = <T>(body: unknown, map: (row: any) => T = (row) => row as T): PagedResult<T> => {
  const data = (body as { data?: any })?.data ?? {}
  const items: T[] = (data.items ?? []).map(map)
  const pageSize = data.pageSize || items.length || 1
  const totalCount = data.totalCount ?? items.length
  return {
    items,
    totalCount,
    page: data.page ?? 1,
    pageSize,
    totalPages: Math.max(1, Math.ceil(totalCount / pageSize)),
  }
}

/**
 * Walks every page of a list endpoint, for small reference lists (templates, lookups, groups)
 * that a screen genuinely needs in full. Bounded by `maxItems`: a list that outgrows it belongs
 * in a server-paged table, not in memory — so hitting the bound is reported, not silently hidden.
 */
export const fetchAllPages = async <T>(
  url: string,
  params: Record<string, unknown> = {},
  map: (row: any) => T = (row) => row as T,
  maxItems = 5_000,
): Promise<T[]> => {
  const all: T[] = []

  for (let page = 1; all.length < maxItems; page++) {
    const response = await apiClient.get(url, { params: { ...params, page, pageSize: MAX_PAGE_SIZE } })
    const result = readPaged(response.data, map)
    all.push(...result.items)

    if (result.items.length < MAX_PAGE_SIZE || page >= result.totalPages) return all
  }

  if (import.meta.env.DEV) {
    console.warn(`fetchAllPages(${url}) stopped at ${maxItems} items; this list needs server-side paging.`)
  }
  return all
}
