import { useEffect, useState } from 'react'

export interface ReferenceState<T> {
  data: T | null
  error: string | null
  loading: boolean
  /** Try again after a failure. */
  retry: () => void
}

/**
 * Loads one reference catalogue (see referenceService) for a component. Catalogues are cached in
 * the service, so every component asking for the same one shares a single request.
 */
export function useReference<T>(load: () => Promise<T>, key: string): ReferenceState<T> {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let active = true
    setError(null)
    load()
      .then(value => { if (active) setData(value) })
      .catch(() => { if (active) setError('These options could not be loaded.') })
    return () => { active = false }
    // `load` is a new closure on every render; `key` identifies what it loads.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, attempt])

  return { data, error, loading: data === null && error === null, retry: () => setAttempt(a => a + 1) }
}

export default useReference
