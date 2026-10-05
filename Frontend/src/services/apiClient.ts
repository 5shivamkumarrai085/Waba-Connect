import axios, { type AxiosError, type AxiosRequestConfig, type InternalAxiosRequestConfig } from 'axios'

/**
 * The API origin. Configured per environment through VITE_API_BASE_URL; the localhost value is a
 * development convenience only and is never what a production build should rely on.
 */
export const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL || 'https://waba-connect-api-wnir.onrender.com/api'

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  /**
   * Axios ships with NO timeout — a request whose connection is accepted but never answered
   * waits forever. 60s is deliberately generous (some report endpoints are slow) but finite, so
   * every request eventually settles and every caller's catch block eventually runs.
   */
  timeout: 60_000,
})

/** Where the tokens live. Read directly here to avoid importing the auth store, which imports
 *  this module — a cycle that would leave `apiClient` undefined at module init. */
export const AUTH_TOKEN_STORAGE_KEY = 'waba_auth_token'
export const REFRESH_TOKEN_STORAGE_KEY = 'waba_refresh_token'

export interface SessionTokens {
  token: string
  refreshToken: string
}

interface SessionHandlers {
  /** The session could not be renewed: sign the user out. */
  onUnauthorized?: () => void
  /** New tokens were issued by a refresh; keep the store in step. */
  onTokensRefreshed?: (tokens: SessionTokens) => void
  /** The server requires a password change before anything else. */
  onPasswordChangeRequired?: () => void
}

let handlers: SessionHandlers = {}

/** Registered once by the auth store; indirection avoids the store <-> client import cycle. */
export const setSessionHandlers = (next: SessionHandlers) => {
  handlers = next
}

export const storeSessionTokens = (tokens: SessionTokens | null) => {
  if (tokens) {
    localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, tokens.token)
    localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, tokens.refreshToken)
  } else {
    localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY)
    localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY)
  }
}

/* ------------------------------------------------------------------------------------------------
 * Token refresh
 *
 * Access tokens are short-lived (15 minutes). They are renewed with the single-use refresh token:
 *  - proactively, just before expiry, so a request never goes out with a token about to lapse;
 *  - reactively, once, when a request still comes back 401.
 * Concurrent callers share one refresh ("single flight"): the refresh token is single-use, and two
 * parallel refreshes would make the server treat the second as a replayed, stolen token.
 * --------------------------------------------------------------------------------------------- */

const REFRESH_MARGIN_MS = 60_000
let refreshInFlight: Promise<string | null> | null = null

/** Milliseconds-since-epoch expiry of a JWT, or null if it cannot be read. */
const tokenExpiry = (token: string): number | null => {
  try {
    const payload = token.split('.')[1]
    const json = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')))
    return typeof json.exp === 'number' ? json.exp * 1000 : null
  } catch {
    return null
  }
}

const isAuthEndpoint = (url?: string) =>
  !!url && /\/auth\/(login|refresh)(\?|$)/.test(url)

/** Renews the session. Resolves to the new access token, or null when the session is over. */
export const refreshSession = (): Promise<string | null> => {
  if (refreshInFlight) return refreshInFlight

  const refreshToken = localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)
  if (!refreshToken) return Promise.resolve(null)

  refreshInFlight = axios
    .post(`${API_BASE_URL}/auth/refresh`, { refreshToken }, { timeout: 30_000 })
    .then((response) => {
      const data = response.data?.data
      if (!data?.token || !data?.refreshToken) return null
      const tokens = { token: data.token as string, refreshToken: data.refreshToken as string }
      storeSessionTokens(tokens)
      handlers.onTokensRefreshed?.(tokens)
      return tokens.token
    })
    .catch(() => null)
    .finally(() => {
      refreshInFlight = null
    })

  return refreshInFlight
}

/**
 * A token that is valid for at least the next minute, refreshing first if needed. Used by the
 * request interceptor and by the SignalR client, whose WebSocket carries the token at connect.
 */
export const getValidAccessToken = async (): Promise<string | null> => {
  const token = localStorage.getItem(AUTH_TOKEN_STORAGE_KEY)
  if (!token) return null

  const expiresAt = tokenExpiry(token)
  if (expiresAt !== null && expiresAt - Date.now() < REFRESH_MARGIN_MS) {
    return (await refreshSession()) ?? token
  }

  return token
}

apiClient.interceptors.request.use(async (config) => {
  if (!isAuthEndpoint(config.url)) {
    const token = await getValidAccessToken()
    if (token) config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

type RetriableConfig = InternalAxiosRequestConfig & { _authRetried?: boolean; _attempt?: number }

const MAX_GET_RETRIES = 2

/** Network failures, gateway errors and throttling are worth another try; client errors are not. */
const isRetryable = (error: AxiosError) => {
  if (axios.isCancel(error)) return false
  // No response: a dropped connection is worth retrying, a 60-second timeout is not.
  if (!error.response) return error.code !== 'ECONNABORTED'
  const status = error.response.status
  return status === 429 || status === 502 || status === 503 || status === 504
}

const retryDelay = (attempt: number, error: AxiosError) => {
  const retryAfter = Number(error.response?.headers?.['retry-after'])
  if (Number.isFinite(retryAfter) && retryAfter > 0) return Math.min(retryAfter * 1000, 10_000)
  // Exponential backoff with full jitter: 0–500ms, 0–1000ms.
  return Math.random() * 500 * 2 ** (attempt - 1)
}

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const config = error.config as RetriableConfig | undefined
    const status = error.response?.status

    if (config && status === 401 && !config._authRetried && !isAuthEndpoint(config.url)) {
      config._authRetried = true
      const token = await refreshSession()
      if (token) {
        config.headers.Authorization = `Bearer ${token}`
        return apiClient.request(config)
      }

      handlers.onUnauthorized?.()
      return Promise.reject(error)
    }

    if (status === 403) {
      const errors = (error.response?.data as { errors?: string[] } | undefined)?.errors
      if (errors?.includes('PASSWORD_CHANGE_REQUIRED')) handlers.onPasswordChangeRequired?.()
    }

    // Idempotent reads are retried a couple of times on transient failures, so a blip in the
    // network or a rolling deploy does not surface as an error screen.
    if (config && (config.method ?? 'get').toLowerCase() === 'get' && isRetryable(error)) {
      const attempt = (config._attempt ?? 0) + 1
      if (attempt <= MAX_GET_RETRIES && !config.signal?.aborted) {
        config._attempt = attempt
        await new Promise((resolve) => setTimeout(resolve, retryDelay(attempt, error)))
        return apiClient.request(config)
      }
    }

    return Promise.reject(error)
  },
)

/**
 * Thrown when a GET is dropped because the session changed or its caller aborted it. It means
 * "this answer is no longer wanted", not "something went wrong".
 */
export class RequestCancelledError extends Error {
  readonly isRequestCancelled = true

  constructor(url: string) {
    super(`Request cancelled: ${url}`)
    this.name = 'RequestCancelledError'
  }
}

/**
 * True for a request that was deliberately dropped. Callers should return quietly — no error
 * toast, and no state change. Also matches raw axios cancellations.
 */
export const isRequestCancelled = (error: unknown): boolean =>
  (error as { isRequestCancelled?: boolean })?.isRequestCancelled === true ||
  (error as { name?: string })?.name === 'CanceledError' ||
  (error as { name?: string })?.name === 'AbortError' ||
  (error as { message?: string })?.message === 'canceled' ||
  axios.isCancel(error)

/* ------------------------------------------------------------------------------------------------
 * GET de-duplication
 *
 * Identical GETs in flight at the same moment (same URL AND same parameters) share one request —
 * several components mounting together ask for the same thing. Nothing else is shared or
 * cancelled: the previous version aborted any earlier request to the same *path*, so two
 * unrelated callers (the inbox list and the notification poller, say) kept cancelling each other.
 * A caller that wants to abandon its own request passes an AbortSignal, which is honoured.
 * --------------------------------------------------------------------------------------------- */

const inflightRequests = new Map<string, Promise<unknown>>()
const responseCache = new Map<string, { data: unknown; timestamp: number }>()
const GET_CACHE_TTL_MS = 15_000
let sessionController = new AbortController()

// Invalidate client-side read cache on mutations
apiClient.interceptors.request.use((config) => {
  const method = (config.method ?? 'get').toLowerCase()
  if (method !== 'get') {
    responseCache.clear()
  }
  return config
})

/**
 * Aborts every in-flight GET and clears the de-dup map and cache. Called on login and logout: a GET issued
 * before signing in and the same GET after share a key, and must not share a response.
 */
export const resetApiClientCaches = () => {
  sessionController.abort('Session changed')
  sessionController = new AbortController()
  inflightRequests.clear()
  responseCache.clear()
}

const serializeParams = (params: unknown): string => {
  if (!params || typeof params !== 'object') return ''
  return Object.entries(params as Record<string, unknown>)
    .filter(([, value]) => value !== undefined && value !== null)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, value]) => `${key}=${Array.isArray(value) ? value.join(',') : String(value)}`)
    .join('&')
}

/** One signal that fires when either the caller or the session aborts. */
const combineSignals = (a: AbortSignal, b?: AbortSignal | null): AbortSignal => {
  if (!b) return a
  const anyFn = (AbortSignal as unknown as { any?: (signals: AbortSignal[]) => AbortSignal }).any
  if (anyFn) return anyFn([a, b])

  const controller = new AbortController()
  const abort = () => controller.abort()
  if (a.aborted || b.aborted) abort()
  a.addEventListener('abort', abort, { once: true })
  b.addEventListener('abort', abort, { once: true })
  return controller.signal
}

const originalGet = apiClient.get.bind(apiClient)

const isCacheableUrl = (url: string) =>
  !url.includes('/auth/') && !url.includes('/Chat/') && !url.includes('/t/')

apiClient.get = function <T = unknown, R = import('axios').AxiosResponse<T>, D = unknown>(
  url: string,
  config?: AxiosRequestConfig<D>,
): Promise<R> {
  const callerSignal = config?.signal as AbortSignal | undefined
  const key = `${url}?${serializeParams(config?.params)}`

  // Return cached result if available within TTL for cacheable endpoints
  if (!callerSignal && isCacheableUrl(url)) {
    const cached = responseCache.get(key)
    if (cached && Date.now() - cached.timestamp < GET_CACHE_TTL_MS) {
      return Promise.resolve(cached.data as R)
    }
  }

  // A caller with its own signal gets its own request, so aborting it cannot cancel someone
  // else's identical read.
  if (!callerSignal) {
    const existing = inflightRequests.get(key)
    if (existing) return existing as Promise<R>
  }

  const signal = combineSignals(sessionController.signal, callerSignal)

  // axios 1.20 types get() as AxiosResponseResult<…>; at runtime it is the same response this
  // wrapper has always returned, so the declared R is kept for every caller.
  const promise = (originalGet<T, R, D>(url, { ...config, signal }) as unknown as Promise<R>)
    .then((res) => {
      if (!callerSignal && isCacheableUrl(url)) {
        responseCache.set(key, { data: res, timestamp: Date.now() })
      }
      return res
    })
    .catch((err: unknown) => {
      if (axios.isCancel(err)) throw new RequestCancelledError(url)
      throw err
    })
    .finally(() => {
      if (inflightRequests.get(key) === promise) inflightRequests.delete(key)
    })

  if (!callerSignal) inflightRequests.set(key, promise)
  return promise
} as typeof apiClient.get
