import { apiClient, isRequestCancelled } from '../services/apiClient'

interface DownloadOptions {
  /** Query parameters. Kept out of the path so the request is deduped on the full key. */
  params?: Record<string, string | number | boolean | undefined>
  /**
   * A JSON request body, sent via POST instead of GET when present.
   *
   * The report export needs this: its filter set is a structured object with several
   * multi-select arrays, and encoding that into a query string would risk truncation by a proxy
   * and would put contact identifiers in server access logs. Every existing caller omits this
   * and keeps going through GET exactly as before.
   */
  data?: unknown
  /** Used when the server sends no Content-Disposition filename. */
  fallbackFilename: string
}

/**
 * Reads the filename the server chose.
 *
 * Prefers RFC 5987 `filename*=UTF-8''...`, which is the only form that can carry non-ASCII
 * characters, and falls back to the plain quoted `filename=`.
 */
const filenameFromDisposition = (disposition: unknown): string | null => {
  if (typeof disposition !== 'string') return null

  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposition)
  if (utf8?.[1]) {
    try {
      return decodeURIComponent(utf8[1].trim())
    } catch {
      // A malformed encoding is not worth failing the download over — fall through.
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(disposition)
  return plain?.[1]?.trim() || null
}

/**
 * An error response body arrives as a Blob when responseType is 'blob', so the usual
 * `error.response.data.message` lookup finds nothing and the user sees "[object Blob]".
 * This recovers the real message — for example the 403 naming the missing permission.
 */
const messageFromBlobError = async (error: any): Promise<string | null> => {
  const data = error?.response?.data
  if (!(data instanceof Blob)) return null

  try {
    const text = await data.text()
    const parsed = JSON.parse(text)
    return parsed?.message || parsed?.title || null
  } catch {
    return null
  }
}

/**
 * Downloads a file from the API and saves it.
 *
 * Goes through `apiClient` rather than `window.open` so the request interceptor attaches the
 * bearer token. The app stores its JWT in localStorage, not a cookie, so a plain browser
 * navigation to an authenticated endpoint is anonymous and answers 401 — which is exactly how
 * every reporting export and the contacts sample download came to be silently broken.
 *
 * Rejects with an Error carrying a usable message; callers surface it. A superseded request
 * (the patched `apiClient.get` aborts by URL path, so switching filters mid-download cancels
 * the first) resolves quietly instead, because that is not a failure.
 */
export const downloadFromApi = async (path: string, options: DownloadOptions): Promise<void> => {
  let response
  try {
    response = options.data !== undefined
      // Bypasses the patched `apiClient.get` (dedupe/cancel-by-path) deliberately: a POST export
      // is a one-off deliberate action, not a list read that gets re-issued as filters change.
      ? await apiClient.post(path, options.data, { responseType: 'blob', params: options.params })
      : await apiClient.get(path, { responseType: 'blob', params: options.params })
  } catch (error) {
    if (isRequestCancelled(error)) return
    const serverMessage = await messageFromBlobError(error)
    throw new Error(serverMessage || (error as Error)?.message || 'The download failed.')
  }

  const blob = response.data as Blob
  const filename =
    filenameFromDisposition(response.headers?.['content-disposition']) || options.fallbackFilename

  const objectUrl = URL.createObjectURL(blob)
  try {
    const link = document.createElement('a')
    link.href = objectUrl
    link.setAttribute('download', filename)
    document.body.appendChild(link)
    link.click()
    link.remove()
  } finally {
    // The existing client-side CSV export in ContactsList never revokes its object URL, so the
    // blob is pinned in memory for the life of the tab. Doing it properly here.
    URL.revokeObjectURL(objectUrl)
  }
}

export default downloadFromApi
