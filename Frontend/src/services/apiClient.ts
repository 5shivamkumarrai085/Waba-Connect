import axios from 'axios';

export const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5155/api',
  headers: {
    'Content-Type': 'application/json',
  },
  /**
   * Axios ships with NO timeout — a request whose connection is accepted but never answered
   * waits forever. That is how the app could hang on "Restoring your session…" indefinitely:
   * the boot call to /auth/me had nothing to time it out, so a backend that was still starting
   * (or a dropped connection) left the splash on screen with no error and no way forward.
   *
   * 60s is deliberately generous — some report and template endpoints are genuinely slow — but
   * finite, so every request eventually settles and every caller's catch block eventually runs.
   */
  timeout: 60_000,
});

/** Where the bearer token lives. Read directly here to avoid importing the auth store, which
 *  imports this module — a cycle that would leave `apiClient` undefined at module init. */
export const AUTH_TOKEN_STORAGE_KEY = 'waba_auth_token';

let onUnauthorized: (() => void) | null = null;

/**
 * Registers the callback fired on a 401. The auth store calls this once at startup.
 * Indirection rather than a direct import, again to avoid the store <-> client cycle.
 */
export const setUnauthorizedHandler = (handler: (() => void) | null) => {
  onUnauthorized = handler;
};

apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem(AUTH_TOKEN_STORAGE_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (!axios.isCancel(error) && error?.response?.status === 401) {
      // The token is missing, expired, or was invalidated server-side. Let the auth store
      // clear session state and route to the login screen.
      // Note: a request cancelled by the stale-request logic below resolves to a promise that
      // never settles, so its 401 never arrives here. That is acceptable — the next live
      // request will surface it — and is not worth defeating the cancellation for.
      onUnauthorized?.();
    }
    return Promise.reject(error);
  }
);

/**
 * Thrown when a GET is superseded by a newer request to the same path, or dropped because the
 * session changed. It means "this answer is no longer wanted", not "something went wrong".
 */
export class RequestCancelledError extends Error {
  readonly isRequestCancelled = true;

  constructor(url: string) {
    super(`Request superseded: ${url}`);
    this.name = 'RequestCancelledError';
  }
}

/**
 * True for a request that was deliberately dropped. Callers should return quietly — no error
 * toast, and no state change, because a newer request is already on its way.
 *
 * Also matches raw axios cancellations, so callers that talk to axios directly work too.
 */
export const isRequestCancelled = (error: unknown): boolean =>
  (error as { isRequestCancelled?: boolean })?.isRequestCancelled === true ||
  (error as { name?: string })?.name === 'CanceledError' ||
  (error as { name?: string })?.name === 'AbortError' ||
  (error as { message?: string })?.message === 'canceled' ||
  axios.isCancel(error);

// Map of in-flight promises: key -> Promise
const inflightRequests = new Map<string, Promise<any>>();
// Map of AbortControllers: path -> AbortController
const abortControllers = new Map<string, AbortController>();

/**
 * Clears the dedupe and cancellation maps.
 *
 * Must be called on login and logout. The dedupe key is the URL plus its query params and
 * carries no notion of identity, so a GET issued before signing in and the same GET issued
 * after share a key — without this, the second call silently resolves with the first user's
 * (or the logged-out) response.
 */
export const resetApiClientCaches = () => {
  abortControllers.forEach((controller) => controller.abort('Session changed'));
  abortControllers.clear();
  inflightRequests.clear();
};

const getRequestKey = (url: string, params?: any) => {
  return `${url}?${params ? new URLSearchParams(params).toString() : ''}`;
};

const getPathFromUrl = (url: string) => {
  return url.split('?')[0];
};

const originalGet = apiClient.get;

// Overwrite the get method to support deduplication and cancellation
apiClient.get = function <R = any>(url: string, config?: any): Promise<R> {
  const params = config?.params;
  const key = getRequestKey(url, params);
  const path = getPathFromUrl(url);

  // If a duplicate request (exact same URL and query params) is already running, return its promise
  if (inflightRequests.has(key)) {
    return inflightRequests.get(key) as Promise<R>;
  }

  // Cancel any pending request for the same path (e.g. changing search parameters or route)
  if (abortControllers.has(path)) {
    abortControllers.get(path)?.abort('Stale request cancelled');
  }

  const controller = new AbortController();
  abortControllers.set(path, controller);

  const newConfig = {
    ...config,
    signal: controller.signal,
  };

  const promise = originalGet.call(apiClient, url, newConfig)
    .then((res) => {
      inflightRequests.delete(key);
      if (abortControllers.get(path) === controller) {
        abortControllers.delete(path);
      }
      return res;
    })
    .catch((err) => {
      inflightRequests.delete(key);
      if (abortControllers.get(path) === controller) {
        abortControllers.delete(path);
      }
      if (axios.isCancel(err)) {
        // Reject, never hang.
        //
        // This used to `return new Promise(() => {})` to "ignore" a stale response. A promise
        // that never settles does not ignore anything — it strands every awaiting caller
        // permanently, so the `finally` that clears `isLoading` never runs and the page sits on
        // its skeleton until a manual refresh. resetApiClientCaches() aborts every in-flight
        // GET on sign-in, so this fired on essentially every login.
        //
        // Callers use isRequestCancelled() to tell "superseded, do nothing" apart from a real
        // failure. A caller that forgets now shows an error and stops loading, which is
        // recoverable; hanging forever is not.
        throw new RequestCancelledError(url);
      }
      throw err;
    });

  inflightRequests.set(key, promise);
  return promise as Promise<R>;
};

