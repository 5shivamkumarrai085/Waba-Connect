import { create } from 'zustand'
import { contactService } from '../services/contacts/contactService'
import { createRequestCache } from './cacheHelpers'
import type { ContactStatus, ContactSource, ContactType, ContactGroup } from '../types/contacts'

/**
 * The database-driven contact lookups — status, type, source, group — in one place.
 *
 * These used to be fetched into each page's local `useState`. That made them unreachable from
 * anywhere else, so creating a type or a group in Setup could not reach an already-open
 * Contacts tab: the only way to see it was a full reload. Holding them centrally means the
 * Setup screens can call `invalidate()` after a save and every mounted page picks it up.
 *
 * The TTL is long because these change rarely; correctness comes from explicit invalidation,
 * not from the clock.
 */
const cache = createRequestCache<unknown>({ ttlMs: 5 * 60 * 1000 })

interface LookupState {
  statuses: ContactStatus[]
  sources: ContactSource[]
  types: ContactType[]
  groups: ContactGroup[]
  assignedUsers: any[]
  isLoaded: boolean

  /** Loads anything not already cached. Safe to call from every page on mount. */
  loadAll: (options?: { force?: boolean }) => Promise<void>
  /** Drops the cache and refetches. Call after any Setup mutation to these tables. */
  invalidate: () => Promise<void>
}

export const useLookupStore = create<LookupState>((set, get) => ({
  statuses: [],
  sources: [],
  types: [],
  groups: [],
  assignedUsers: [],
  isLoaded: false,

  loadAll: async (options) => {
    try {
      const [statuses, sources, types, groups, assignedUsers] = await Promise.all([
        cache.dedupe('statuses', () => contactService.getContactStatuses(), options) as Promise<ContactStatus[]>,
        cache.dedupe('sources', () => contactService.getContactSources(), options) as Promise<ContactSource[]>,
        cache.dedupe('types', () => contactService.getContactTypes(), options) as Promise<ContactType[]>,
        cache.dedupe('groups', () => contactService.getContactGroups(), options) as Promise<ContactGroup[]>,
        cache.dedupe('assignedUsers', () => contactService.getAssignedUsers(), options) as Promise<any[]>
      ])

      set({
        statuses: statuses ?? [],
        sources: sources ?? [],
        types: types ?? [],
        groups: groups ?? [],
        assignedUsers: assignedUsers ?? [],
        isLoaded: true
      })
    } catch {
      // A failed lookup costs labels and colours, not the page — rows still render their stored
      // value. Deliberately silent: several pages call this on mount and a toast per page would
      // be noise for something the user cannot act on.
      set({ isLoaded: true })
    }
  },

  invalidate: async () => {
    cache.invalidate()
    await get().loadAll({ force: true })
  }
}))

export default useLookupStore
