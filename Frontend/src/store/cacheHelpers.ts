/**
 * A small request cache with de-duplication and a time-to-live.
 *
 * Extracted from the shape `useActivityLogStore` and `useReportingStore` had already grown by
 * hand, rather than inventing a third one. Two behaviours matter:
 *
 * - **dedupe**: N callers asking for the same key while a request is in flight share one
 *   request. `connectionStore.fetchDashboard` is called from eight places, several of them on
 *   the same navigation, and every one used to be its own round trip.
 * - **ttl**: a repeat ask inside the window is served from memory. Set deliberately short —
 *   this exists to collapse bursts, not to hold data long enough to go stale.
 *
 * Deliberately not a general-purpose cache: there is no background revalidation and no
 * persistence. Anything that changes data calls `invalidate()` and refetches, so the cache is
 * never the reason a screen shows an old value.
 */
export interface RequestCache<T> {
  /** Runs `fetcher` unless a fresh value or an in-flight request already exists for `key`. */
  dedupe: (key: string, fetcher: () => Promise<T>, options?: { force?: boolean }) => Promise<T>
  /** Drops cached values. No argument clears everything; a string clears keys starting with it. */
  invalidate: (keyPrefix?: string) => void
  peek: (key: string) => T | undefined
}

export const createRequestCache = <T>({ ttlMs }: { ttlMs: number }): RequestCache<T> => {
  const values = new Map<string, { value: T; storedAt: number }>()
  const inFlight = new Map<string, Promise<T>>()

  return {
    dedupe: (key, fetcher, options) => {
      if (!options?.force) {
        const hit = values.get(key)
        if (hit && Date.now() - hit.storedAt < ttlMs) {
          return Promise.resolve(hit.value)
        }

        const pending = inFlight.get(key)
        if (pending) return pending
      }

      const request = fetcher()
        .then((value) => {
          values.set(key, { value, storedAt: Date.now() })
          return value
        })
        .finally(() => {
          inFlight.delete(key)
        })

      inFlight.set(key, request)
      return request
    },

    invalidate: (keyPrefix) => {
      if (keyPrefix === undefined) {
        values.clear()
        // In-flight requests are intentionally left alone: aborting them is what used to strand
        // callers. They will simply store a value nobody reads.
        return
      }

      for (const key of [...values.keys()]) {
        if (key.startsWith(keyPrefix)) values.delete(key)
      }
    },

    peek: (key) => values.get(key)?.value
  }
}
